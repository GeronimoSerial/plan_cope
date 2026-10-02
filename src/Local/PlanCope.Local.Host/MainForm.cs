using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PlanCope.Local.Api;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
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
    private bool _clientAppLoaded;
    private Uri? _clientAppUri;
    private readonly Dictionary<ulong, string> _navigationUris = [];
    private readonly HttpClient _localHttp = new();
    private readonly HttpClient _centralHttp = new();
    private readonly DataDirectoryResolver _directories;
    private readonly UpdateHealthTracker _healthTracker;

    private UpdateService? _updateService;
    private string? _updateChannel;
    private string? _updateAccessToken;
    private string _updateState = "idle";
    private string? _updateTargetVersion;
    private string? _updateSha256;
    private string? _updateFileName;
    private string? _updateMessage;
    private int? _updateProgress;
    private bool _restartAvailable;
    private IReadOnlyList<UpdateBlockingSession> _blockingSessions = [];
    private string? _loggedBlockingSessionIds;
    private int _sessionGateFailures;
    private bool _sessionGateLastCheckFailed;
    private bool _updateCheckInProgress;
    private bool _pendingSessionUpdatePrompt;
    private bool _networkRefreshInProgress;
    private bool _closing;
    private readonly System.Windows.Forms.Timer _networkTimer = new() { Interval = 5000 };
    private readonly System.Windows.Forms.Timer _sessionGateTimer = new() { Interval = 30000, Enabled = false };
    private readonly System.Windows.Forms.Timer _updateCheckTimer = new() { Interval = 4 * 60 * 60 * 1000, Enabled = false };
    private string? _updateFeedUrl;

    public MainForm(DataDirectoryResolver directories, UpdateHealthTracker healthTracker)
    {
        _directories = directories;
        _healthTracker = healthTracker;
        InitializeComponent();
        _networkTimer.Tick += async (_, _) => await RefreshLanAddressAsync();
        Controls.Add(_loadingLabel);
        _sessionGateTimer.Tick += (_, _) => _ = EvaluateSessionGateAsync();
        _updateCheckTimer.Tick += (_, _) => _ = HandleCheckForUpdatesAsync(manual: false);
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
        _closing = true;
        _networkTimer.Stop();
        _networkTimer.Dispose();
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
        _lanBaseUrl = $"http://{LanAddressResolver.GetLocalIpAddress()}:{_localPort}";
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
                "--Local:AssetsPath", _directories.AssetsDirectory,
                "--Logging:FilePath", Path.Combine(_directories.LogsDirectory, "local-api.log")
            ]));
        await _api.StartAsync();
        _networkTimer.Start();
        await RefreshPhaseAStatusAsync();
        InitializeUpdateService();
    }

    private void InitializeUpdateService()
    {
        var feedUrl = Environment.GetEnvironmentVariable("PLANCOPE_UPDATE_FEED_URL");
        var centralBaseUrl = _api?.Configuration["Central:BaseUrl"];
        _updateFeedUrl = !string.IsNullOrWhiteSpace(feedUrl)
            ? feedUrl.TrimEnd('/')
            : string.IsNullOrWhiteSpace(centralBaseUrl)
                ? null
                : centralBaseUrl.TrimEnd('/') + "/api/updates";
        if (string.IsNullOrWhiteSpace(_updateFeedUrl)) return;

        var configuredChannel = Environment.GetEnvironmentVariable("PLANCOPE_UPDATE_CHANNEL") is { } c && !string.IsNullOrWhiteSpace(c)
            ? c
            : "stable";
        var channel = configuredChannel.Equals("beta", StringComparison.OrdinalIgnoreCase) ? UpdateChannel.Beta : UpdateChannel.Stable;
        var channelName = channel == UpdateChannel.Beta ? "beta" : "stable";
        var channelFeedUrl = $"{_updateFeedUrl.TrimEnd('/')}/{channelName}";
        var backend = new VelopackUpdateBackend(channelFeedUrl, channelName, () => _updateAccessToken);
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
        _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
        _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        _webView.CoreWebView2.DownloadStarting += OnDownloadStarting;
        _clientAppUri = ResolveClientAppUri(_webView.CoreWebView2);
        _webView.Source = _clientAppUri;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_clientAppUri is null)
        {
            e.Cancel = true;
            return;
        }

        var action = HostNavigationPolicy.Decide(_clientAppUri, e.Uri, isTopFrame: true);
        if (action is HostNavigationAction.Allow)
        {
            _navigationUris[e.NavigationId] = e.Uri;
            return;
        }

        e.Cancel = true;
        if (action is HostNavigationAction.OpenExternal && e.IsUserInitiated)
        {
            try
            {
                OpenUrl(e.Uri);
            }
            catch
            {
                // The host UI stays available if Windows cannot open the default browser.
            }
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (_clientAppUri is null) return;

        var action = HostNavigationPolicy.Decide(_clientAppUri, e.Uri, isTopFrame: true);
        if (action is HostNavigationAction.Allow)
        {
            _webView.CoreWebView2.Navigate(e.Uri);
            return;
        }

        if (action is HostNavigationAction.OpenExternal && e.IsUserInitiated)
        {
            try
            {
                OpenUrl(e.Uri);
            }
            catch
            {
                // The host UI stays available if Windows cannot open the default browser.
            }
        }
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        if (_clientAppUri is null || !HostNavigationPolicy.IsAppDownload(_clientAppUri, e.DownloadOperation.Uri))
        {
            e.Cancel = true;
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var navigationUri = _navigationUris.Remove(e.NavigationId, out var uri) ? uri : null;
        var isClientAppNavigation = _clientAppUri is not null && navigationUri is not null
            && HostNavigationPolicy.IsClientAppOrigin(_clientAppUri, navigationUri);
        var operationCanceled = e.WebErrorStatus is CoreWebView2WebErrorStatus.OperationCanceled;
        if (!e.IsSuccess && HostNavigationPolicy.IsFatalNavigationFailure(_clientAppLoaded, isClientAppNavigation, operationCanceled))
        {
            ShowStartupError("No se pudo cargar la interfaz local del host.");
            return;
        }

        if (e.IsSuccess && isClientAppNavigation)
        {
            _clientAppLoaded = true;
        }

        if (!isClientAppNavigation)
        {
            return;
        }

        PostHostContext();

        var version = GetInstalledAppVersion();
        if (version is not null)
        {
            _healthTracker.MarkHealthy(version);
            _ = ReportHealthAsync(version, healthy: true, detail: null);
        }

        _updateCheckTimer.Start();
        _ = HandleCheckForUpdatesAsync(manual: false);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_clientAppUri is null || !HostNavigationPolicy.IsClientAppOrigin(_clientAppUri, e.Source))
        {
            return;
        }

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
            case "host:activationComplete":
                _ = OnActivationCompleteAsync();
                break;
            case "host:checkForUpdates":
                _ = HandleCheckForUpdatesAsync(manual: true);
                break;
            case "host:downloadUpdate":
                _ = HandleDownloadUpdateAsync();
                break;
            case "host:applyUpdate":
                _ = TryApplyUpdateAndRestartAsync();
                break;
            case "host:deferUpdate":
                _pendingSessionUpdatePrompt = false;
                StopSessionGatePolling();
                _updateState = "idle";
                _updateTargetVersion = null;
                _updateMessage = null;
                _blockingSessions = [];
                PushUpdateStatus();
                break;
            case "host:openStatsReport":
                _ = HandleOpenStatsReportAsync(message);
                break;
        }
    }

    private async Task HandleOpenStatsReportAsync(HostBridgeMessage message)
    {
        string? path = null;
        string? error = null;
        try
        {
            var reportsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PlanCope",
                "reports");
            var service = new LocalStatsReportService(_localHttp, reportsDirectory);
            path = await service.SaveReportAsync(
                $"http://127.0.0.1:{_localPort}",
                message.Cue ?? string.Empty,
                message.SchoolYear,
                message.Course,
                message.Exam);

            LocalStatsReportService.OpenReport(
                reportsDirectory,
                path,
                reportPath => Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true }));
        }
        catch (Exception exception)
        {
            error = $"No se pudo guardar o abrir el informe HTML: {exception.Message}";
        }

        if (_webView.CoreWebView2 is null || string.IsNullOrWhiteSpace(message.RequestId))
        {
            return;
        }

        var reply = new
        {
            type = "host:statsReportResult",
            requestId = message.RequestId,
            success = error is null,
            path,
            message = error ?? $"Informe guardado y abierto en el navegador. Archivo: {path}"
        };
        _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(reply, JsonOptions));
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
                message = _updateMessage,
                progress = _updateProgress,
                restartAvailable = _restartAvailable,
                blockingSessions = _blockingSessions
            }
        };

        _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, JsonOptions));
    }

    internal static string? GetInstalledAppVersion()
        => Velopack.Locators.VelopackLocator.IsCurrentSet
            ? Velopack.Locators.VelopackLocator.Current.CurrentlyInstalledVersion?.ToString()
            : null;

    private async Task HandleCheckForUpdatesAsync(bool manual)
    {
        if (_updateCheckInProgress || _updateState is "updateAvailable" or "downloading" or "readyPendingSessionClose" or "readyToRestart") return;
        if (_updateService is null)
        {
            _updateState = manual ? "error" : "idle";
            _updateTargetVersion = null;
            _updateMessage = manual ? "La búsqueda de actualizaciones no está configurada para este equipo." : null;
            PushUpdateStatus();
            return;
        }

        _updateCheckInProgress = true;
        try
        {
            _updateState = "checking";
            _updateTargetVersion = null;
            _updateMessage = null;
            PushUpdateStatus();

            var result = await UpdateRequestAuth.RunAsync(
                () => _updateService.CheckForUpdatesAsync(),
                RefreshUpdateAccessTokenAsync);
            if (!result.UpdateAvailable)
            {
                _updateState = "upToDate";
                PushUpdateStatus();
                await Task.Delay(TimeSpan.FromSeconds(5));
                if (!IsDisposed && _updateState == "upToDate")
                {
                    _updateState = "idle";
                    PushUpdateStatus();
                }
                return;
            }

            _updateState = "updateAvailable";
            _updateTargetVersion = result.TargetVersion;
            _updateSha256 = result.Sha256;
            _updateFileName = result.FileName;
            if (await HasActiveSessionAsync())
            {
                _pendingSessionUpdatePrompt = true;
                _updateState = "updateAvailablePendingSession";
                StartSessionGatePolling();
            }
            PushUpdateStatus();
        }
        catch (Exception exception)
        {
            LogUpdateFailure("check", exception);
            _updateState = manual ? "error" : "idle";
            _updateMessage = manual
                ? "No se pudo verificar si hay actualizaciones. Revisá la conexión y la configuración de Central."
                : null;
            PushUpdateStatus();
        }
        finally
        {
            _updateCheckInProgress = false;
        }
    }

    private async Task HandleDownloadUpdateAsync()
    {
        if (_updateService is null || string.IsNullOrWhiteSpace(_updateSha256)) return;
        try
        {
            _pendingSessionUpdatePrompt = false;
            _updateState = "downloading";
            _updateMessage = null;
            _updateProgress = 0;
            PushUpdateStatus();
            var progressThrottle = new UpdateProgressThrottle();
            await UpdateRequestAuth.RunAsync(
                () => _updateService.DownloadUpdateAsync(_updateSha256, progress =>
                {
                    var now = DateTimeOffset.UtcNow;
                    if (!progressThrottle.ShouldReport(progress, now)) return;
                    void PublishProgress()
                    {
                        if (IsDisposed || !IsHandleCreated) return;
                        _updateProgress = Math.Clamp(progress, 0, 100);
                        PushUpdateStatus();
                    }
                    if (IsDisposed || !IsHandleCreated) return;
                    if (InvokeRequired) BeginInvoke((Action)PublishProgress); else PublishProgress();
                }),
                RefreshUpdateAccessTokenAsync);
            if (_updateService.LastDownloadIntegrityFailed)
            {
                _updateState = "integrityFailed";
                _updateMessage = "La actualización no superó la verificación SHA-256.";
                PushUpdateStatus();
                return;
            }

            if (!string.IsNullOrEmpty(_updateTargetVersion) &&
                !string.IsNullOrEmpty(_updateSha256) &&
                !string.IsNullOrEmpty(_updateFileName))
            {
                _healthTracker.MarkPendingRestart(
                    _updateTargetVersion,
                    _updateFileName,
                    _updateSha256,
                    _updateFeedUrl ?? string.Empty);
            }

            await EvaluateSessionGateAsync();
        }
        catch (Exception exception)
        {
            LogUpdateFailure("download", exception);
            _updateState = "error";
            _updateMessage = "No se pudo descargar la actualización. Revisá la conexión e intentá buscar de nuevo.";
            PushUpdateStatus();
        }
    }

    private void LogUpdateFailure(string operation, Exception exception)
    {
        UpdateFailureLogger.Log(_directories.LogsDirectory, _updateFeedUrl, _updateAccessToken, operation, exception);
    }

    private async Task EvaluateSessionGateAsync()
    {
        if (await HasActiveSessionAsync())
        {
            _updateState = _pendingSessionUpdatePrompt ? "updateAvailablePendingSession" : "readyPendingSessionClose";
            if (!_sessionGateLastCheckFailed) _updateMessage = null;
            PushUpdateStatus();
            StartSessionGatePolling();
            return;
        }

        if (_pendingSessionUpdatePrompt)
        {
            _pendingSessionUpdatePrompt = false;
            _updateState = "updateAvailable";
            _updateMessage = null;
            PushUpdateStatus();
            StopSessionGatePolling();
            return;
        }

        _updateState = "readyToRestart";
        _updateMessage = null;
        _restartAvailable = false;
        PushUpdateStatus();
        StopSessionGatePolling();
        await TryApplyUpdateAndRestartAsync(automatic: true);
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

    private async Task TryApplyUpdateAndRestartAsync(bool automatic = false)
    {
        try
        {
            var decision = await UpdateRestartGuard.TryStartAsync(
                HasActiveSessionAsync,
                () => _updateService?.TryApplyAndRestart(userConfirmedRestart: true) == true);
            System.Diagnostics.Trace.WriteLine($"Update apply/restart decision (automatic={automatic}): {decision}.");
            if (decision == UpdateRestartDecision.SessionActive)
            {
                _updateState = "readyPendingSessionClose";
                if (!_sessionGateLastCheckFailed) _updateMessage = null;
                _restartAvailable = false;
                PushUpdateStatus();
                StartSessionGatePolling();
                return;
            }

            if (decision == UpdateRestartDecision.RestartUnavailable)
            {
                _restartAvailable = true;
                _updateMessage = "No se pudo iniciar el reinicio automático. Podés volver a intentarlo.";
                PushUpdateStatus();
                return;
            }

            if (automatic)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                if (!IsDisposed && _updateState == "readyToRestart")
                {
                    _restartAvailable = true;
                    PushUpdateStatus();
                }
            }
        }
        catch (Exception exception)
        {
            LogUpdateFailure("apply-restart", exception);
            System.Diagnostics.Trace.WriteLine($"Update apply/restart failed: {exception}");
            _restartAvailable = true;
            _updateMessage = "No se pudo iniciar el reinicio automático. Podés volver a intentarlo.";
            PushUpdateStatus();
        }
    }

    private async Task<bool> HasActiveSessionAsync()
    {
        try
        {
            using var response = await _localHttp.GetAsync($"http://127.0.0.1:{_localPort}/api/sessions/active");
            response.EnsureSuccessStatusCode();
            var sessions = await JsonSerializer.DeserializeAsync<List<LocalDeliverySession>>(response.Content.ReadAsStream(), JsonOptions);
            _sessionGateFailures = 0;
            _sessionGateLastCheckFailed = false;
            _sessionGateTimer.Interval = 30000;
            _blockingSessions = sessions?.Select(session => new UpdateBlockingSession(
                session.Id,
                $"Sesión {session.AccessCode} · {session.SchoolCode} · {session.Status}")).ToArray() ?? [];
            var sessionIds = string.Join(", ", _blockingSessions.Select(session => session.Id));
            if (_blockingSessions.Count > 0 && !string.Equals(sessionIds, _loggedBlockingSessionIds, StringComparison.Ordinal))
            {
                UpdateFailureLogger.LogMessage(_directories.LogsDirectory, "session-gate", $"Update blocked by active session ids: {sessionIds}.");
                System.Diagnostics.Trace.WriteLine($"Update blocked by active sessions: {sessionIds}.");
            }
            _loggedBlockingSessionIds = _blockingSessions.Count > 0 ? sessionIds : null;
            return _blockingSessions.Count > 0;
        }
        catch (Exception exception)
        {
            _sessionGateFailures++;
            _sessionGateLastCheckFailed = true;
            var reason = SessionGateRetryPolicy.DescribeException(exception);
            _updateMessage = reason;
            _blockingSessions = [];
            _sessionGateTimer.Interval = (int)SessionGateRetryPolicy.GetDelay(_sessionGateFailures).TotalMilliseconds;
            LogUpdateFailure("session-gate", exception);
            SessionGateRetryPolicy.LogFailure(message => System.Diagnostics.Trace.WriteLine(message), exception, _sessionGateFailures);
            return true;
        }
    }

    private sealed record UpdateBlockingSession(string Id, string Label);

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

    private async Task RefreshUpdateAccessTokenAsync(bool forceRefresh)
    {
        if (_api is null) return;

        using var scope = _api.Services.CreateScope();
        var services = scope.ServiceProvider;
        var repository = services.GetService<ISyncStateRepository>();
        if (repository is null) return;

        var expiryState = await repository.GetAsync("central_access_token_expires_at");
        var expiryText = ReadSyncStateString(expiryState?.ValueJson);
        var expiresAt = DateTimeOffset.TryParse(expiryText, out var parsedExpiry)
            ? parsedExpiry
            : (DateTimeOffset?)null;
        var refresher = services.GetRequiredService<NodeCredentialRefresher>();
        if (forceRefresh)
        {
            await refresher.TryRefreshAfterUnauthorizedAsync(_updateAccessToken, CancellationToken.None);
        }
        else if (UpdateRequestAuth.ShouldRefresh(expiresAt, DateTimeOffset.UtcNow))
        {
            await refresher.TryRefreshAsync(CancellationToken.None);
        }

        var tokenState = await repository.GetAsync("central_access_token");
        _updateAccessToken = ReadSyncStateString(tokenState?.ValueJson);
    }

    private static string? ReadSyncStateString(string? valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson)) return null;
        using var document = JsonDocument.Parse(valueJson);
        return document.RootElement.ValueKind is JsonValueKind.String
            ? document.RootElement.GetString()
            : document.RootElement.GetRawText();
    }

    /// <summary>
    /// Reports launch health to Central as fire-and-forget telemetry so a bad release is visible
    /// before it reaches the whole fleet. Never throws into the caller and never blocks anything:
    /// a failed report is silently dropped. Returns immediately when Central is not configured.
    /// </summary>
    private async Task ReportHealthAsync(string version, bool healthy, string? detail)
    {
        var feedUrl = _updateFeedUrl;
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

    private async Task OnActivationCompleteAsync()
    {
        await RefreshPhaseAStatusAsync();
        PostHostContext();
        _ = HandleCheckForUpdatesAsync(manual: false);
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

    private async Task RefreshLanAddressAsync()
    {
        if (_networkRefreshInProgress || _closing)
        {
            return;
        }

        _networkRefreshInProgress = true;
        try
        {
            var address = await Task.Run(LanAddressResolver.GetLocalIpAddress);
            var nextUrl = $"http://{address}:{_localPort}";
            if (_closing || nextUrl == _lanBaseUrl)
            {
                return;
            }

            _lanBaseUrl = nextUrl;
            PostHostContext();
        }
        catch (System.Net.NetworkInformation.NetworkInformationException exception)
        {
            // Adapter configuration can change while being enumerated; retry on the next tick.
            Debug.WriteLine($"Could not refresh LAN address: {exception.Message}");
        }
        finally
        {
            _networkRefreshInProgress = false;
        }
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

    private sealed record HostBridgeMessage(
        string Type,
        string? AccessCode,
        string? RequestId = null,
        string? Cue = null,
        string? SchoolYear = null,
        string? Course = null,
        string? Exam = null);
    private sealed record ActivationStatus(bool PhaseAComplete);
}
