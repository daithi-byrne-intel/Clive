using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CombinedLevelsViewer.TorchTabular;

internal sealed class GridEvent
{
    public GridEvent(string name, JsonElement args)
    {
        Name = name;
        Args = args;
    }

    public string Name { get; }
    public JsonElement Args { get; }
}

/// <summary>
/// Hosts Torch's tabular web grid in a WebView2 and speaks its protocol:
/// commands go in through window.runCommand(name, argsJson) and events come back as
/// { EventName, EventArgs } web messages.
/// Torch's own Web folder is served read-only; the LevelsComponents folder of this project
/// is overlaid next to Torch's component folders so its relative imports resolve.
/// </summary>
internal sealed class TorchGridBridge
{
    private const string Origin = "https://torch.tabular";
    private const string OverlayPrefix = "/Html/LevelsComponents/";

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".ico"] = "image/x-icon",
        [".png"] = "image/png",
        [".svg"] = "image/svg+xml",
    };

    private readonly WebView2 webView;
    private readonly string torchWebRoot;
    private readonly string overlayRoot;

    public TorchGridBridge(WebView2 webView, string torchWebRoot, string overlayRoot)
    {
        this.webView = webView;
        this.torchWebRoot = Path.GetFullPath(torchWebRoot);
        this.overlayRoot = Path.GetFullPath(overlayRoot);
    }

    public event EventHandler<GridEvent>? EventReceived;

    public async Task InitializeAsync()
    {
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CombinedLevelsViewer", "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(null, userData);
        await webView.EnsureCoreWebView2Async(environment);

        var core = webView.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.AddWebResourceRequestedFilter(Origin + "/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        core.WebMessageReceived += OnWebMessageReceived;

        var navigation = new TaskCompletionSource<bool>();
        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            core.NavigationCompleted -= OnNavigationCompleted;
            if (args.IsSuccess) navigation.TrySetResult(true);
            else navigation.TrySetException(new InvalidOperationException("The Torch tabular page failed to load: " + args.WebErrorStatus));
        }

        core.NavigationCompleted += OnNavigationCompleted;
        core.Navigate(Origin + OverlayPrefix + "index.html");
        await navigation.Task;

        // Torch's Bootstrapper publishes window.runCommand once its modules have run.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await core.ExecuteScriptAsync("typeof window.runCommand") == "\"function\"") return;
            await Task.Delay(100);
        }

        throw new InvalidOperationException("The Torch tabular page loaded but did not publish runCommand. The installed Torch web components may be incompatible.");
    }

    public Task RunCommandAsync(string name, object args)
    {
        // Torch's CommandRunner expects the arguments as a JSON string and parses it itself.
        var argsJson = JsonSerializer.Serialize(args);
        var script = "window.runCommand(" + JsonSerializer.Serialize(name) + ", " + JsonSerializer.Serialize(argsJson) + ")";
        return webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    public Task<string> EvaluateAsync(string script) => webView.CoreWebView2.ExecuteScriptAsync(script);

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var document = JsonDocument.Parse(args.TryGetWebMessageAsString());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("EventName", out var name)) return;
            var eventArgs = root.TryGetProperty("EventArgs", out var value) ? value.Clone() : default;
            EventReceived?.Invoke(this, new GridEvent(name.GetString() ?? string.Empty, eventArgs));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            // Not a Torch grid event; ignore.
        }
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        var environment = webView.CoreWebView2.Environment;
        var path = Uri.UnescapeDataString(new Uri(args.Request.Uri).AbsolutePath);
        var isOverlay = path.StartsWith(OverlayPrefix, StringComparison.OrdinalIgnoreCase);
        var root = isOverlay ? overlayRoot : torchWebRoot;
        var relative = isOverlay ? path.Substring(OverlayPrefix.Length) : path.TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

        var insideRoot = fullPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!insideRoot || !File.Exists(fullPath))
        {
            args.Response = environment.CreateWebResourceResponse(null, 404, "Not Found", string.Empty);
            return;
        }

        if (!ContentTypes.TryGetValue(Path.GetExtension(fullPath), out var contentType)) contentType = "application/octet-stream";
        var content = new MemoryStream(File.ReadAllBytes(fullPath));
        args.Response = environment.CreateWebResourceResponse(content, 200, "OK", "Content-Type: " + contentType);
    }
}
