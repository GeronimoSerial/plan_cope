using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PlanCope.Local.Api;

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

    public MainForm()
    {
        InitializeComponent();
        Controls.Add(_loadingLabel);
    }

    private async void MainForm_Load(object? sender, EventArgs e)
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
        _api = LocalApiApplication.Build(["--urls", $"http://0.0.0.0:{_localPort}"]);
        await _api.StartAsync();
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
                port = _localPort
            }
        };

        _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, JsonOptions));
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

    private sealed record HostBridgeMessage(string Type, string? AccessCode);
}
