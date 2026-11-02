using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Mdu.Performance;

// A disposable benchmark browser, isolated from the user's normal browser profile.
// CDP is driven directly by .NET; no Node driver, npm, or JavaScript tooling is used.
internal sealed class Browser : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _profile;
    private readonly ClientWebSocket _socket = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly SemaphoreSlim _send = new(1);
    private readonly CancellationTokenSource _stop = new();
    private Task _reader = Task.CompletedTask;
    private readonly Task<string> _stdout, _stderr;
    private string? _session;
    private int _nextId;
    internal JsonObject Version { get; private set; } = new();

    private Browser(string executable)
    {
        _profile = Path.Combine(Path.GetTempPath(), "mdu-browser-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo(executable) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "--headless=new", "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--remote-debugging-port=0", "--user-data-dir=" + _profile, "about:blank" })
            start.ArgumentList.Add(arg);
        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start benchmark browser.");
        _stdout = _process.StandardOutput.ReadToEndAsync(_stop.Token);
        _stderr = _process.StandardError.ReadToEndAsync(_stop.Token);
    }

    internal static async Task<Browser> Start()
    {
        var executable = Environment.GetEnvironmentVariable("MDU_BENCHMARK_BROWSER") ??
            (OperatingSystem.IsMacOS() ? "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" : "chromium");
        var browser = new Browser(executable);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var portFile = Path.Combine(browser._profile, "DevToolsActivePort");
            while (!File.Exists(portFile))
            {
                if (browser._process.HasExited) throw new InvalidOperationException(await browser._stderr);
                await Task.Delay(10, timeout.Token);
            }
            var lines = await File.ReadAllLinesAsync(portFile, timeout.Token);
            await browser._socket.ConnectAsync(new Uri($"ws://127.0.0.1:{lines[0]}{lines[1]}"), timeout.Token);
            browser._reader = browser.Receive();
            browser.Version = await browser.Command("Browser.getVersion");
            var target = await browser.Command("Target.createTarget", new JsonObject { ["url"] = "about:blank" });
            var attached = await browser.Command("Target.attachToTarget", new JsonObject { ["targetId"] = target["targetId"]!.GetValue<string>(), ["flatten"] = true });
            browser._session = attached["sessionId"]!.GetValue<string>();
            await browser.Command("Page.enable");
            await browser.Command("Runtime.enable");
            await browser.Command("Page.addScriptToEvaluateOnNewDocument", new JsonObject
            {
                ["source"] = "document.addEventListener('DOMContentLoaded',()=>document.querySelectorAll('details.openapi-tag-group').forEach(group=>group.open=true));"
            });
            return browser;
        }
        catch { await browser.DisposeAsync(); throw; }
    }

    internal async Task Navigate(string url)
    {
        await Command("Page.navigate", new JsonObject { ["url"] = url });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var literal = System.Text.Json.JsonSerializer.Serialize(url);
        while (true)
        {
            try
            {
                var state = await Command("Runtime.evaluate", new JsonObject { ["expression"] = $"location.href === {literal} && document.readyState === 'complete'", ["returnByValue"] = true });
                if (state["result"]?["value"]?.GetValue<bool>() == true) return;
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("context", StringComparison.OrdinalIgnoreCase)) { }
            await Task.Delay(5, timeout.Token);
        }
    }

    // Resolves only after the expected content has survived two animation frames.
    // This measures DOM/frame readiness, not physical display-panel presentation.
    internal async Task WaitDisplayed(string marker)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var literal = System.Text.Json.JsonSerializer.Serialize(marker);
        var expression = $"new Promise(resolve => {{ if (!document.body?.innerText.includes({literal})) return resolve(false); requestAnimationFrame(() => requestAnimationFrame(() => resolve(document.body?.innerText.includes({literal}) ?? false))); }})";
        while (true)
        {
            try
            {
                var result = await Command("Runtime.evaluate", new JsonObject { ["expression"] = expression, ["awaitPromise"] = true, ["returnByValue"] = true });
                if (result["result"]?["value"]?.GetValue<bool>() == true) return;
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("context", StringComparison.OrdinalIgnoreCase) || exception.Message.Contains("navigat", StringComparison.OrdinalIgnoreCase)) { }
            await Task.Delay(5, timeout.Token);
        }
    }

    internal async Task<JsonObject> Inspect()
    {
        var result = await Command("Runtime.evaluate", new JsonObject
        {
            ["expression"] = "({title:document.title,headings:[...document.querySelectorAll('h1,h2')].map(x=>x.textContent),iframes:document.querySelectorAll('iframe').length,localScripts:[...document.scripts].every(s=>!s.src||new URL(s.src).origin===location.origin)})",
            ["returnByValue"] = true
        });
        return result["result"]!["value"]!.AsObject().DeepClone().AsObject();
    }

    internal async Task CheckPdfMessage(bool hidden, string screenshot)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var result = await Command("Runtime.evaluate", new JsonObject
            {
                ["expression"] = $"document.getElementById('mdu-download-status')?.hidden === {(hidden ? "true" : "false")}",
                ["returnByValue"] = true
            });
            if (result["result"]?["value"]?.GetValue<bool>() == true) break;
            await Task.Delay(10, timeout.Token);
        }
        var capture = await Command("Page.captureScreenshot", new JsonObject { ["format"] = "png" });
        await File.WriteAllBytesAsync(screenshot, Convert.FromBase64String(capture["data"]!.GetValue<string>()));
    }

    internal async Task VerifyInteractions(string url, string screenshot)
    {
        await Navigate(url + "measure.html");
        await WaitDisplayed("baseline-search-sentinel");
        async Task Check(string expression, string description)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                var result = await Command("Runtime.evaluate", new JsonObject { ["expression"] = expression, ["returnByValue"] = true });
                if (result["result"]?["value"]?.GetValue<bool>() == true) return;
                try { await Task.Delay(20, timeout.Token); }
                catch (OperationCanceledException) { throw new InvalidOperationException("Browser check failed: " + description); }
            }
        }
        async Task Act(string expression) => await Command("Runtime.evaluate", new JsonObject { ["expression"] = expression, ["returnByValue"] = true });
        await Check("document.querySelectorAll('.media-embed iframe').length===0", "consent before player load");
        await Act("document.querySelector('[data-media-consent=once]').click()");
        await Check("document.querySelectorAll('.media-embed iframe').length===1", "load once");
        await Act("document.getElementById('media-settings-open').click();document.getElementById('media-settings-revoke').click();document.getElementById('media-settings-close').click()");
        await Check("document.querySelectorAll('.media-embed iframe').length===0", "revoke unloads player");
        await Act("document.querySelector('[data-media-consent=site]').click()");
        await Check("document.querySelectorAll('.media-embed iframe').length===1", "allow provider");
        await Navigate(url + "measure.html");
        await Check("document.querySelectorAll('.media-embed iframe').length===1", "saved consent survives navigation");
        await Act("document.getElementById('media-settings-open').click();document.querySelector('[data-media-provider=YouTube]').checked=false;document.getElementById('media-settings-save').click()");
        await Check("document.querySelectorAll('.media-embed iframe').length===0", "save revocation unloads player");
        await Act("document.getElementById('search-button').click();const input=document.getElementById('search-input');input.value='baseline-search-sentinel';input.dispatchEvent(new Event('input',{bubbles:true}))");
        await Check("[...document.querySelectorAll('#search-results a')].some(a=>a.getAttribute('href').endsWith('measure.html#stable-anchor'))", "search section link");
        await Act("document.getElementById('search-close').click();document.activeElement?.blur()");
        await Command("Input.dispatchKeyEvent", new JsonObject { ["type"] = "keyDown", ["key"] = "Tab", ["code"] = "Tab", ["windowsVirtualKeyCode"] = 9 });
        await Command("Input.dispatchKeyEvent", new JsonObject { ["type"] = "keyUp", ["key"] = "Tab", ["code"] = "Tab", ["windowsVirtualKeyCode"] = 9 });
        await Check("document.activeElement.matches(':focus-visible')&&getComputedStyle(document.activeElement).outlineStyle!=='none'", "visible keyboard focus");
        await Check("!!document.getElementById('stable-anchor')&&[...document.querySelectorAll('.markdown-content a')].some(a=>a.getAttribute('href')==='index.html')", "anchor and relative navigation");
        var capture = await Command("Page.captureScreenshot", new JsonObject { ["format"] = "png" });
        await File.WriteAllBytesAsync(screenshot, Convert.FromBase64String(capture["data"]!.GetValue<string>()));
    }

    private async Task<JsonObject> Command(string method, JsonObject? parameters = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        var request = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters ?? new JsonObject() };
        if (_session is not null) request["sessionId"] = _session;
        var bytes = Encoding.UTF8.GetBytes(request.ToJsonString());
        await _send.WaitAsync(_stop.Token);
        try { await _socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, endOfMessage: true, _stop.Token); }
        finally { _send.Release(); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { return await completion.Task.WaitAsync(timeout.Token); }
        finally { _pending.TryRemove(id, out _); }
    }

    private async Task Receive()
    {
        var buffer = new byte[32768];
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult received;
                do
                {
                    received = await _socket.ReceiveAsync(buffer, _stop.Token);
                    if (received.MessageType == WebSocketMessageType.Close) return;
                    message.Write(buffer, 0, received.Count);
                } while (!received.EndOfMessage);
                var response = JsonNode.Parse(message.ToArray())!.AsObject();
                if (response["id"] is null || !_pending.TryGetValue(response["id"]!.GetValue<int>(), out var completion)) continue;
                if (response["error"] is { } error) completion.TrySetException(new InvalidOperationException(error.ToJsonString()));
                else if (response["result"]?["exceptionDetails"] is { } details) completion.TrySetException(new InvalidOperationException(details.ToJsonString()));
                else completion.TrySetResult(response["result"]!.AsObject());
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception exception) { foreach (var pending in _pending.Values) pending.TrySetException(exception); }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _socket.Abort();
        await _reader;
        _socket.Dispose();
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync();
        try { await Task.WhenAll(_stdout, _stderr); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        _process.Dispose();
        _send.Dispose();
        _stop.Dispose();
        Directory.Delete(_profile, recursive: true);
    }
}
