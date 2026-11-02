using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace Mdu.Service;

public sealed class PreviewServer : IDisposable
{
    private const string LiveReloadEndpoint = "/__mdu/events";

    private static readonly IReadOnlyDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".bmp"] = "image/bmp",
            [".css"] = "text/css; charset=utf-8",
            [".gif"] = "image/gif",
            [".html"] = "text/html; charset=utf-8",
            [".json"] = "application/json; charset=utf-8",
            [".jpeg"] = "image/jpeg",
            [".jpg"] = "image/jpeg",
            [".js"] = "text/javascript; charset=utf-8",
            [".pdf"] = "application/pdf",
            [".png"] = "image/png",
            [".svg"] = "image/svg+xml",
            [".txt"] = "text/plain; charset=utf-8"
        };

    private readonly string _outputPath;
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<Guid, EventClient> _eventClients = new();
    private long _revision;
    private int _building;
    private readonly Func<string, CancellationToken, Task>? _prepareOutput;

    public PreviewServer(string outputPath, int port, Func<string, CancellationToken, Task>? prepareOutput = null)
    {
        _outputPath = Path.GetFullPath(outputPath);
        _prepareOutput = prepareOutput;
        BaseUrl = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(BaseUrl);
    }

    public string BaseUrl { get; }

    public void SetBuilding(bool building) => Volatile.Write(ref _building, building ? 1 : 0);

    public void Start() => _listener.Start();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
                }
                catch (HttpListenerException) when (!cancellationToken.IsCancellationRequested && _listener.IsListening)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                    continue;
                }
                catch (SocketException) when (!cancellationToken.IsCancellationRequested && _listener.IsListening)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                    continue;
                }

                _ = HandleRequestAsync(context, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            foreach (var client in _eventClients.Values)
                client.Messages.Writer.TryComplete();
        }
    }

    public void Publish(IEnumerable<string>? paths = null, bool reloadAll = false)
    {
        var revision = Interlocked.Increment(ref _revision);
        var message = JsonSerializer.Serialize(new LiveReloadMessage(
            revision,
            reloadAll,
            paths?.Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>()),
            PreviewServerJsonContext.Default.LiveReloadMessage);

        foreach (var client in _eventClients.Values)
            client.Messages.Writer.TryWrite(message);
    }

    public static string ToUrlPath(string outputRoot, string outputFile)
    {
        var relativePath = Path.GetRelativePath(outputRoot, outputFile)
            .Replace(Path.DirectorySeparatorChar, '/');
        var escapedPath = string.Join('/', relativePath.Split('/').Select(Uri.EscapeDataString));
        return "/" + escapedPath;
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        if (context.Request.Url?.AbsolutePath == "/__mdu/status")
        {
            try
            {
                await WriteTextAsync(context.Response, HttpStatusCode.OK,
                    JsonSerializer.Serialize(new PreviewStatus(Volatile.Read(ref _building) != 0),
                        PreviewServerJsonContext.Default.PreviewStatus));
            }
            finally
            {
                context.Response.Close();
            }
            return;
        }
        if (context.Request.Url?.AbsolutePath == LiveReloadEndpoint)
        {
            var revision = long.TryParse(context.Request.QueryString["revision"], out var parsedRevision)
                ? parsedRevision
                : (long?)null;
            StartEventStream(context.Response, cancellationToken, revision);
            return;
        }

        try
        {
            var requestedPath = context.Request.Url?.AbsolutePath ?? "/";
            var filePath = ResolveFilePath(requestedPath);
            if (filePath is null)
            {
                await WriteTextAsync(context.Response, HttpStatusCode.Forbidden, "Forbidden");
                return;
            }

            if (_prepareOutput is not null)
            {
                await _prepareOutput(filePath, cancellationToken);
                // Materializing a page can also create its directory alias.
                filePath = ResolveFilePath(requestedPath)!;
            }
            if (!File.Exists(filePath))
            {
                await WriteTextAsync(context.Response, HttpStatusCode.NotFound, "Not found");
                return;
            }

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.Headers[HttpResponseHeader.CacheControl] = "no-cache";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.ContentType = ContentTypes.TryGetValue(Path.GetExtension(filePath), out var contentType)
                ? contentType
                : "application/octet-stream";

            if (Path.GetExtension(filePath).Equals(".html", StringComparison.OrdinalIgnoreCase))
            {
                var html = await File.ReadAllTextAsync(filePath, cancellationToken);
                html = InjectLiveReload(html, ToUrlPath(_outputPath, filePath));
                var bytes = Encoding.UTF8.GetBytes(html);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
            }
            else
            {
                var file = new FileInfo(filePath);
                context.Response.ContentLength64 = file.Length;
                await using var input = file.OpenRead();
                await input.CopyToAsync(context.Response.OutputStream, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or HttpListenerException or SocketException or InvalidOperationException or YamlDotNet.Core.YamlException or OperationCanceledException)
        {
            try
            {
                if (!cancellationToken.IsCancellationRequested && context.Response.OutputStream.CanWrite)
                    await WriteTextAsync(context.Response, HttpStatusCode.InternalServerError, "Could not read the requested file");
            }
            catch (Exception writeException) when (writeException is IOException or HttpListenerException or SocketException or ObjectDisposedException)
            {
            }
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (HttpListenerException)
            {
            }
        }
    }

    private string? ResolveFilePath(string requestedPath)
    {
        var decodedPath = Uri.UnescapeDataString(requestedPath);
        var relativePath = decodedPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var filePath = Path.GetFullPath(Path.Combine(_outputPath, relativePath));
        if (!IsInsideOutput(filePath)) return null;

        if (decodedPath == "/")
        {
            var indexPath = Path.Combine(_outputPath, "index.html");
            if (File.Exists(indexPath)) return indexPath;
            return Directory.EnumerateFiles(_outputPath, "*.html", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault() ?? indexPath;
        }

        if (decodedPath.EndsWith('/') || Directory.Exists(filePath))
            filePath = Path.Combine(filePath, "index.html");
        return IsInsideOutput(filePath) ? filePath : null;
    }

    private bool IsInsideOutput(string path)
    {
        var relativePath = Path.GetRelativePath(_outputPath, path);
        return !Path.IsPathRooted(relativePath) &&
               relativePath != ".." &&
               !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private void StartEventStream(
        HttpListenerResponse response,
        CancellationToken cancellationToken,
        long? clientRevision)
    {
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.SendChunked = true;
        response.KeepAlive = true;
        response.Headers[HttpResponseHeader.CacheControl] = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        var id = Guid.NewGuid();
        var client = new EventClient(response);
        _eventClients[id] = client;
        _ = SendEventsAsync(id, client, clientRevision, cancellationToken);
    }

    private async Task SendEventsAsync(
        Guid id,
        EventClient client,
        long? clientRevision,
        CancellationToken cancellationToken)
    {
        try
        {
            var connected = Encoding.UTF8.GetBytes(": connected\n\n");
            await client.Response.OutputStream.WriteAsync(connected, cancellationToken);
            await client.Response.OutputStream.FlushAsync(cancellationToken);

            var revision = Interlocked.Read(ref _revision);
            var initialMessage = JsonSerializer.Serialize(new LiveReloadMessage(
                revision,
                clientRevision.HasValue && clientRevision.Value != revision,
                Array.Empty<string>()),
                PreviewServerJsonContext.Default.LiveReloadMessage);
            var initialEvent = Encoding.UTF8.GetBytes($"data: {initialMessage}\n\n");
            await client.Response.OutputStream.WriteAsync(initialEvent, cancellationToken);
            await client.Response.OutputStream.FlushAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                readTimeout.CancelAfter(TimeSpan.FromSeconds(3));

                string? message = null;
                try
                {
                    message = await client.Messages.Reader.ReadAsync(readTimeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Keep idle event streams open and detect disconnected browsers.
                }

                var payload = message is null ? ": keep-alive\n\n" : $"data: {message}\n\n";
                var bytes = Encoding.UTF8.GetBytes(payload);
                await client.Response.OutputStream.WriteAsync(bytes, cancellationToken);
                await client.Response.OutputStream.FlushAsync(cancellationToken);
            }
        }
        catch (Exception exception) when (exception is IOException or HttpListenerException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
        finally
        {
            _eventClients.TryRemove(id, out _);
            client.Messages.Writer.TryComplete();
            client.Response.Close();
        }
    }

    private static string InjectLiveReload(string html, string servedPath)
    {
        var script = $$"""
            <script>
            (() => {
              const currentPath = {{JsonSerializer.Serialize(servedPath, PreviewServerJsonContext.Default.String)}};
              let events;
              let revision;
              const disconnect = () => {
                events?.close();
                events = undefined;
              };
              const connect = () => {
                if (events || document.visibilityState === "hidden") return;
                const url = new URL("{{LiveReloadEndpoint}}", location.href);
                if (revision !== undefined) url.searchParams.set("revision", revision);
                events = new EventSource(url);
                events.onmessage = message => {
                  const change = JSON.parse(message.data);
                  revision = change.revision;
                  if (change.reloadAll || change.paths.includes(currentPath)) location.reload();
                };
              };
              connect();
              document.addEventListener("visibilitychange", () => {
                if (document.visibilityState === "hidden") disconnect();
                else if (!events) connect();
              });
              document.addEventListener("click", event => {
                if (!(event.target instanceof Element) || event.button !== 0 ||
                    event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;

                const link = event.target.closest("a[href]");
                if (!link || link.hasAttribute("download") ||
                    link.target && link.target !== "_self") return;

                const destination = new URL(link.href, location.href);
                if (destination.origin === location.origin &&
                    (destination.pathname !== location.pathname || destination.search !== location.search))
                    disconnect();
              });
              window.addEventListener("pagehide", disconnect);
              window.addEventListener("pageshow", event => { if (event.persisted) connect(); });
            })();
            </script>
            """;

        var bodyEnd = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return bodyEnd < 0 ? html + script : html.Insert(bodyEnd, script);
    }

    private static async Task WriteTextAsync(HttpListenerResponse response, HttpStatusCode statusCode, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        response.StatusCode = (int)statusCode;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }

    public void Dispose()
    {
        foreach (var client in _eventClients.Values)
            client.Messages.Writer.TryComplete();
        _listener.Close();
    }

    private sealed record EventClient(HttpListenerResponse Response)
    {
        public Channel<string> Messages { get; } = Channel.CreateUnbounded<string>();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LiveReloadMessage))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(PreviewStatus))]
internal partial class PreviewServerJsonContext : JsonSerializerContext
{
}

internal sealed record LiveReloadMessage(long Revision, bool ReloadAll, string[] Paths);
internal sealed record PreviewStatus(bool Building);
