using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PlanCope.Local.Api;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Host.Services;
using PlanCope.Shared.Domain.Local;
using Microsoft.Data.Sqlite;

namespace PlanCope.Local.Host;

public partial class MainForm : Form
{
    private const int PreferredLocalPort = 5055;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly Label _loadingLabel = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "Iniciando Plan Cope Local...",
        Font = new Font("Segoe UI", 12F),
        ForeColor = Color.FromArgb(55, 71, 79),
        BackColor = Color.FromArgb(242, 246, 244)
    };

    private WebApplication? _api;
    private string _lanBaseUrl = string.Empty;
    private int _localPort = PreferredLocalPort;
    private bool _phaseAComplete;
    private readonly HttpClient _localHttp = new();
    private readonly HttpClient _centralHttp = new();
    private readonly DataDirectoryResolver _directories;
    private readonly ActivationKeyStore _activationKeyStore;
    private readonly UpdateHealthTracker _healthTracker;

    private UpdateService? _updateService;
    private string? _updateChannel;
    private string? _updateAccessToken;
    private string _updateState = "idle";
    private string? _updateTargetVersion;
    private string? _updateMessage;
    private readonly System.Windows.Forms.Timer _sessionGateTimer = new() { Interval = 30000, Enabled = false };

    public MainForm(DataDirectoryResolver directories, ActivationKeyStore activationKeyStore, UpdateHealthTracker healthTracker)
    {
        _directories = directories;
        _activationKeyStore = activationKeyStore;
        _healthTracker = healthTracker;
        InitializeComponent();
        Controls.Add(_loadingLabel);
        _sessionGateTimer.Tick += (_, _) => _ = EvaluateSessionGateAsync();
    }

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        try
        {
            await StartLocalApiAsync();
            await StartWebViewAsync();
        }
        catch (Exception exception)
        {
            ShowStartupError(exception.Message);
        }
    }

    private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_api is null)
        {
            return;
        }

        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _api.StopAsync(shutdown.Token);
        await _api.DisposeAsync();
    }

    private async Task StartLocalApiAsync()
    {
        _localPort = FindAvailablePort(PreferredLocalPort);
        _lanBaseUrl = $"http://{GetLocalIpAddress()}:{_localPort}";
        // Build initializes SQLite and seeds local data. Run it outside the
        // WinForms synchronization context to avoid blocking the UI thread
        // while repositories complete asynchronous database operations.
        _api = await Task.Run(() =>
            LocalApiApplication.Build([
                "--urls", $"http://0.0.0.0:{_localPort}",
                "--ConnectionStrings:LocalDatabase", new SqliteConnectionStringBuilder
                {
                    DataSource = _directories.DatabasePath,
                    Cache = SqliteCacheMode.Shared
                }.ToString(),
                "--Local:AssetsPath", _directories.AssetsDirectory
            ]));
        await _api.StartAsync();
        await RefreshPhaseAStatusAsync();
        InitializeUpdateService();
    }

    private void InitializeUpdateService()
    {
        var feedUrl = Environment.GetEnvironmentVariable("PLANCOPE_UPDATE_FEED_URL");
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            return;
        }

        var channelName = Environment.GetEnvironmentVariable("PLANCOPE_UPDATE_CHANNEL") is { } c && !string.IsNullOrWhiteSpace(c)
            ? c
            : "stable";
        var channel = channelName.Equals("beta", StringComparison.OrdinalIgnoreCase) ? UpdateChannel.Beta : UpdateChannel.Stable;
        var backend = new VelopackUpdateBackend(feedUrl, channelName, () => _updateAccessToken);
        _updateService = new UpdateService(backend, channel);
        _updateChannel = channelName;
    }

    private async Task RefreshPhaseAStatusAsync()
    {
        try
        {
            using var response = await _localHttp.GetAsync($"http://127.0.0.1:{_localPort}/api/activation/status");
            response.EnsureSuccessStatusCode();
            var status = await JsonSerializer.DeserializeAsync<ActivationStatus>(response.Content.ReadAsStream(), JsonOptions);
            _phaseAComplete = status?.PhaseAComplete ?? false;
        }
        catch
        {
            _phaseAComplete = false;
        }
    }

    private async Task StartWebViewAsync()
    {
        Controls.Add(_webView);
        _webView.BringToFront();

        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        _webView.Source = ResolveClientAppUri(_webView.CoreWebView2);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            ShowStartupError("No se pudo cargar la interfaz local del host.");
            return;
        }

        PostHostContext();

        var version = GetInstalledAppVersion();
        if (version is not null)
        {
            _healthTracker.MarkHealthy(version);
            _ = ReportHealthAsync(version, healthy: true, detail: null);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = JsonSerializer.Deserialize<HostBridgeMessage>(e.WebMessageAsJson, JsonOptions);
        if (message is null)
        {
            return;
        }

        switch (message.Type)
        {
            case "host:ready":
                PostHostContext();
                break;
            case "host:openStudentView":
                OpenLocalStudentView(message.AccessCode);
                break;
            case "host:getStoredPassphrase":
                SendStoredPassphrase();
                break;
            case "host:activationComplete":
                _ = OnActivationCompleteAsync(message.Passphrase);
                break;
            case "host:checkForUpdates":
                _ = HandleCheckForUpdatesAsync();
                break;
            case "host:confirmRestart":
                _ = HandleConfirmRestartAsync();
                break;
        }
    }

    private void PostHostContext()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        var payload = new
        {
            type = "host:context",
            context = new
            {
                apiBaseUrl = $"http://127.0.0.1:{_localPort}",
                lanBaseUrl = _lanBaseUrl,
                operatorName = Environment.UserName,
                port = _localPort,
                isActivated = _phaseAComplete,
                appVersion = GetInstalledAppVersion(),
                updateChannel = _updateChannel
            }
        };

        _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private void PushUpdateStatus()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        var payload = new
        {
            type = "host:updateStatus",
            status = new
            {
                state = _updateState,
                targetVersion = _updateTargetVersion,
                message = _updateMessage
            }
        };

        _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, JsonOptions));
    }

    internal static string? GetInstalledAppVersion()
        => Velopack.Locators.VelopackLocator.IsCurrentSet
            ? Velopack.Locators.VelopackLocator.Current.CurrentlyInstalledVersion?.ToString()
            : null;

    private async Task HandleCheckForUpdatesAsync()
    {
        if (_updateService is null)
        {
            _updateState = "notConfigured";
            _updateTargetVersion = null;
            _updateMessage = null;
            PushUpdateStatus();
            return;
        }

        try
        {
            _updateState = "checking";
            _updateTargetVersion = null;
            _updateMessage = null;
            PushUpdateStatus();

            // Read the node access token once for this check+download sequence. Velopack's
            // downloader invokes the Func<string?> provider on every HTTP request; re-reading
            // the persisted token from SQLite there would block a background thread repeatedly
            // for no benefit within one short-lived check+download. Deliberate tradeoff.
            _updateAccessToken = ReadCentralAccessToken();

            var result = await _updateService.CheckForUpdatesAsync();
            if (!result.UpdateAvailable)
            {
                _updateState = "upToDate";
                PushUpdateStatus();
                return;
            }

            _updateState = "downloading";
            _updateTargetVersion = result.TargetVersion;
            PushUpdateStatus();

            await _updateService.DownloadUpdateAsync(result.Sha256 ?? string.Empty);
            if (_updateService.LastDownloadIntegrityFailed)
            {
                _updateState = "integrityFailed";
                _updateMessage = "La actualizacion no paso la verificacion SHA-256.";
                PushUpdateStatus();
                return;
            }

            if (!string.IsNullOrEmpty(result.TargetVersion) &&
                !string.IsNullOrEmpty(result.Sha256) &&
                !string.IsNullOrEmpty(result.FileName))
            {
                _healthTracker.MarkPendingRestart(
                    result.TargetVersion,
                    result.FileName,
                    result.Sha256,
                    Environment.GetEnvironmentVariable("PLANCOPE_UPDATE_FEED_URL") ?? string.Empty);
            }

            await EvaluateSessionGateAsync();
        }
        catch (Exception exception)
        {
            _updateState = "error";
            _updateMessage = exception.Message;
            PushUpdateStatus();
        }
    }

    private async Task HandleConfirmRestartAsync()
    {
        if (_updateService is null)
        {
            _updateState = "notConfigured";
            _updateTargetVersion = null;
            _updateMessage = null;
            PushUpdateStatus();
            return;
        }

        // Re-check the gate now: a session may have started since the confirm control appeared.
        if (await HasActiveSessionAsync())
        {
            _updateState = "readyPendingSessionClose";
            _updateMessage = null;
            PushUpdateStatus();
            StartSessionGatePolling();
            return;
        }

        if (!_updateService.TryApplyAndRestart(userConfirmedRestart: true))
        {
            _updateState = "error";
            _updateMessage = "No se pudo aplicar la actualizacion.";
            PushUpdateStatus();
        }
    }

    private async Task EvaluateSessionGateAsync()
    {
        if (await HasActiveSessionAsync())
        {
            _updateState = "readyPendingSessionClose";
            _updateMessage = null;
            PushUpdateStatus();
            StartSessionGatePolling();
            return;
        }

        _updateState = "readyToApply";
        _updateMessage = null;
        PushUpdateStatus();
        StopSessionGatePolling();
    }

    private void StartSessionGatePolling()
    {
        if (!_sessionGateTimer.Enabled)
        {
            _sessionGateTimer.Start();
        }
    }

    private void StopSessionGatePolling()
    {
        if (_sessionGateTimer.Enabled)
        {
            _sessionGateTimer.Stop();
        }
    }

    private async Task<bool> HasActiveSessionAsync()
    {
        try
        {
            using var response = await _localHttp.GetAsync($"http://127.0.0.1:{_localPort}/api/sessions/active");
            response.EnsureSuccessStatusCode();
            var sessions = await JsonSerializer.DeserializeAsync<List<LocalDeliverySession>>(response.Content.ReadAsStream(), JsonOptions);
            return sessions is { Count: > 0 };
        }
        catch
        {
            // Fail safe: when the session state cannot be determined, assume a session is
            // active so an update is never applied during a possibly-running exam.
            return true;
        }
    }

    private string? ReadCentralAccessToken()
    {
        if (_api is null)
        {
            return null;
        }

        try
        {
            using var scope = _api.Services.CreateScope();
            var repository = scope.ServiceProvider.GetService<ISyncStateRepository>();
            var state = repository?.GetAsync("central_access_token").GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(state?.ValueJson))
            {
                return null;
            }

            using var document = JsonDocument.Parse(state.ValueJson);
            return document.RootElement.ValueKind is JsonValueKind.String
                ? document.RootElement.GetString()
                : document.RootElement.GetRawText();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reports launch health to Central as fire-and-forget telemetry so a bad release is visible
    /// before it reaches the whole fleet. Never throws into the caller and never blocks anything:
    /// a failed report is silently dropped. Returns immediately when no feed is configured —
    /// there is nothing to report to. The feed URL is the same <c>PLANCOPE_UPDATE_FEED_URL</c>
    /// <see cref="InitializeUpdateService"/> reads; Central exposes the endpoint at
    /// <c>{feedUrl}/health</c>.
    /// </summary>
    private async Task ReportHealthAsync(string version, bool healthy, string? detail)
    {
        var feedUrl = Environment.GetEnvironmentVariable("PLANCOPE_UPDATE_FEED_URL");
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            return;
        }

        try
        {
            var token = ReadCentralAccessToken();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{feedUrl.TrimEnd('/')}/health")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { version, healthy, detail }, JsonOptions),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };

            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await _centralHttp.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
        catch
        {
            // Fire-and-forget: never surface a failed health report.
        }
    }

    private void SendStoredPassphrase()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        string? passphrase = null;
        try
        {
            if (_activationKeyStore.HasStoredKey)
            {
                passphrase = System.Text.Encoding.UTF8.GetString(_activationKeyStore.Load());
            }
        }
        catch
        {
            passphrase = null;
        }

        var payload = new
        {
            type = "host:storedPassphrase",
            passphrase
        };

        _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private async Task OnActivationCompleteAsync(string? passphrase)
    {
        if (!string.IsNullOrWhiteSpace(passphrase))
        {
            try
            {
                _activationKeyStore.Store(System.Text.Encoding.UTF8.GetBytes(passphrase));
            }
            catch
            {
                // Storing the passphrase for later convenience must never
                // block the user past an activation that already succeeded.
            }
        }

        await RefreshPhaseAStatusAsync();
        PostHostContext();
    }

    private static Uri ResolveClientAppUri(CoreWebView2 coreWebView)
    {
        var devServerUrl = Environment.GetEnvironmentVariable("PLANCOPE_HOST_UI_URL");
        if (!string.IsNullOrWhiteSpace(devServerUrl))
        {
            return new Uri(devServerUrl);
        }

        var distPath = Path.Combine(AppContext.BaseDirectory, "ClientApp", "dist");
        var indexPath = Path.Combine(distPath, "index.html");
        if (!File.Exists(indexPath))
        {
            throw new InvalidOperationException($"No se encontro la interfaz React compilada en {indexPath}. Ejecuta npm run build en ClientApp.");
        }

        coreWebView.SetVirtualHostNameToFolderMapping(
            "host.plancope.local",
            distPath,
            CoreWebView2HostResourceAccessKind.DenyCors);

        return new Uri("https://host.plancope.local/index.html");
    }

    private void OpenLocalStudentView(string? accessCode)
    {
        if (string.IsNullOrWhiteSpace(accessCode))
        {
            return;
        }

        OpenUrl($"{_lanBaseUrl}/examen/{Uri.EscapeDataString(accessCode)}");
    }

    private void ShowStartupError(string message)
    {
        _webView.Visible = false;
        _loadingLabel.BringToFront();
        _loadingLabel.Text = $"No se pudo iniciar Plan Cope Local.{Environment.NewLine}{message}";
    }

    private static string GetLocalIpAddress()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(static network => network.OperationalStatus is OperationalStatus.Up)
            .Where(static network => network.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .Select(static network => new
            {
                Network = network,
                Properties = network.GetIPProperties()
            })
            .Where(static item => item.Properties.GatewayAddresses.Any(static gateway => gateway.Address.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork))
            .SelectMany(static item => item.Properties.UnicastAddresses
                .Where(static address => address.Address.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork)
                .Where(static address => !IPAddress.IsLoopback(address.Address))
                .Where(static address => !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .Select(address => new { item.Network.NetworkInterfaceType, Address = address.Address.ToString() }))
            .OrderBy(static item => item.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 ? 0 : 1)
            .ThenBy(static item => item.NetworkInterfaceType is NetworkInterfaceType.Ethernet ? 0 : 1)
            .ToList();

        return candidates.FirstOrDefault()?.Address ?? "127.0.0.1";
    }

    private static int FindAvailablePort(int preferredPort)
    {
        for (var port = preferredPort; port < preferredPort + 20; port++)
        {
            if (IsPortAvailable(port))
            {
                return port;
            }
        }

        throw new InvalidOperationException($"No se encontro un puerto disponible entre {preferredPort} y {preferredPort + 19}.");
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private sealed record HostBridgeMessage(string Type, string? AccessCode, string? Passphrase);
    private sealed record ActivationStatus(bool PhaseAComplete);
}
