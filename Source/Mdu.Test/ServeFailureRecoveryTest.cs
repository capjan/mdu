using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Markdown.Document.Impl;
using Markdown.Renderer;
using Markdown.Search;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Test;

public class ServeFailureRecoveryTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedBatchesInvalidateEveryChangedSnapshotAndRecoverUnprocessedPages(bool hideSecondPage)
    {
        await using var preview = new Preview(new()
        {
            ["README.md"] = "# Home\n\n[PDF](mdu:pdf)",
            ["A.md"] = "# A\n\nOriginalA",
            ["B.md"] = "# B\n\nOriginalB",
            ["C.md"] = "# C\n\nOriginalC"
        });
        preview.StartProcessing();
        await preview.WaitFor(async () => (await preview.Status()).Contains("\"building\":false"));
        var second = preview.Pages[Path.Combine(preview.Docs, "B.md")];
        Assert.Contains("OriginalB", second.ReadMarkdown());
        var changed = hideSecondPage ? "---\npdf.visible: false\n---\n# B\n\nLatestB" : "# B\n\nLatestB";
        preview.Write("A.md", "---\nmdu:\n  import: invalid\n---\n# A");
        preview.Write("B.md", changed);
        preview.Change("A.md", "B.md");
        await preview.WaitFor(async () => (await preview.Status()).Contains("\"downloads\":\"failed\""));
        Assert.Equal(changed, second.ReadMarkdown());
        using (var unavailable = await preview.Client.GetAsync("B.html"))
            Assert.Equal(HttpStatusCode.InternalServerError, unavailable.StatusCode);

        // Another save while A is still invalid must invalidate its own inputs before the rebuild fails again.
        var third = preview.Pages[Path.Combine(preview.Docs, "C.md")];
        preview.Write("C.md", "# C\n\nLatestC");
        preview.Change("C.md");
        await preview.WaitFor(() => Task.FromResult(third.ReadMarkdown().Contains("LatestC", StringComparison.Ordinal)));

        preview.Write("A.md", "# A\n\nRepairedA");
        preview.Change("A.md");
        await preview.WaitFor(async () => (await preview.Status()).Contains("\"downloads\":\"ready\""));
        Assert.Contains("LatestB", await preview.Client.GetStringAsync("B.html"));
        Assert.Contains("LatestB", await preview.Client.GetStringAsync("search-index.json"));
        var pdf = System.Text.Encoding.Latin1.GetString(await preview.Client.GetByteArrayAsync("documentation.pdf"));
        Assert.DoesNotContain(Hex("OriginalB"), pdf);
        Assert.Equal(!hideSecondPage, pdf.Contains(Hex("LatestB"), StringComparison.Ordinal));
        Assert.Contains(Hex("LatestC"), pdf);
        Assert.DoesNotContain(Hex("OriginalC"), pdf);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailedPendingPagesReturnErrorsAndRetryWithoutPublishingOldHtml(bool unexpected, bool background)
    {
        await using var preview = new Preview(new() { ["README.md"] = "# Home", ["Guide.md"] = "# Guide\n\nLatestGuide" });
        File.WriteAllText(Path.Combine(preview.Site, "Guide.html"), "OldGuide");
        var visitor = preview.FailPendingPage(unexpected ? new NullReferenceException("Injected render failure") : new IOException("Injected render failure"));
        if (background)
        {
            preview.StartProcessing();
            await visitor.Attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        using (var first = await preview.Client.GetAsync("Guide.html"))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);
            Assert.DoesNotContain("OldGuide", await first.Content.ReadAsStringAsync());
        }
        using (var retry = await preview.Client.GetAsync("Guide/index.html"))
            Assert.Equal(HttpStatusCode.InternalServerError, retry.StatusCode);
        Assert.Contains("\"building\":true", await preview.Status());
        Assert.True(preview.Pending.ContainsKey(Path.Combine(preview.Docs, "Guide.md")));

        visitor.Failure = null;
        Assert.Contains("LatestGuide", await preview.Client.GetStringAsync("Guide.html"));
        Assert.Contains("\"building\":false", await preview.Status());
        Assert.Contains("Guide.html", await preview.Client.GetStringAsync("Guide/index.html"));
    }

    [Fact]
    public async Task InvalidConfigurationKeepsThePreviousPendingPagesUsable()
    {
        await using var preview = new Preview(new() { ["README.md"] = "# Home", ["Guide.md"] = "# Guide\n\nPendingGuide" });
        preview.Write("mdu.yml", "site_name: [");
        Assert.ThrowsAny<YamlDotNet.Core.YamlException>(() => preview.Invoke("RebuildSite"));
        Assert.True(preview.Pending.ContainsKey(Path.Combine(preview.Docs, "Guide.md")));
        Assert.Contains("PendingGuide", await preview.Client.GetStringAsync("Guide.html"));
    }

    [Fact]
    public async Task SiblingRenderedForNewSpecificationNamesCannotBeOverwrittenByTheOldQueue()
    {
        const string specification = "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"index-orders\",\"version\":\"1\"},\"paths\":{}}";
        await using var preview = new Preview(new()
        {
            ["README.md"] = "# Orders\n\nPlain home",
            ["orders.json"] = specification,
            ["index-orders.md"] = "# Sibling\n\n```openapi\n" + specification + "\n```"
        });
        Assert.True(preview.Pending.ContainsKey(Path.Combine(preview.Docs, "index-orders.md")));
        preview.Write("README.md", "# Orders\n\n```openapi\norders.json\n```");
        preview.ProcessBatch("README.md");
        Assert.Contains("href=\"index-orders-spec-2.html\"", File.ReadAllText(Path.Combine(preview.Site, "index-orders.html")));
        Assert.False(preview.Pending.ContainsKey(Path.Combine(preview.Docs, "index-orders.md")));
        preview.StartProcessing();
        await preview.WaitFor(async () => (await preview.Status()).Contains("\"building\":false"));
        Assert.Contains("href=\"index-orders-spec-2.html\"", await preview.Client.GetStringAsync("index-orders.html"));
    }

    private static string Hex(string value) => Convert.ToHexString(System.Text.Encoding.ASCII.GetBytes(value));

    // Control the real watcher channel directly so both edits enter one batch without filesystem timing assumptions.
    private sealed class Preview : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"mdu-serve-recovery-{Guid.NewGuid():N}");
        private readonly ServeCommand _command;
        private readonly HtmlRenderer _renderer = new();
        private readonly SearchIndexBuilder _search = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly PreviewServer _server;
        private readonly object _reader, _writer;
        private readonly Type _change = typeof(ServeCommand).GetNestedType("FileChange", BindingFlags.NonPublic)!;
        private readonly Task _requests;
        private Task? _processing;
        public string Docs => Path.Combine(_root, "docs");
        public string Site => Path.Combine(_root, "site");
        public HttpClient Client { get; }
        public Dictionary<string, MarkdownFile> Pages => (Dictionary<string, MarkdownFile>)typeof(ServeCommand)
            .GetProperty("_pages", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_command)!;
        public Dictionary<string, MarkdownFile> Pending => Get<Dictionary<string, MarkdownFile>>("_pendingPages");

        public Preview(Dictionary<string, string> sources)
        {
            Directory.CreateDirectory(Docs);
            foreach (var (path, content) in sources) Write(path, content);
            var preparation = new DataPreparationVisitor(new MarkdownDocumentReader());
            _command = new ServeCommand(new GraphBuilder(preparation), preparation, _renderer, _search);
            Set("_rootPath", Docs);
            Set("_outputPath", Site);
            Set("_settings", new RenderCommandSettings { RootPath = Docs, OutputPath = Site });
            var channel = typeof(Channel).GetMethods().Single(method => method.Name == "CreateUnbounded" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
                .MakeGenericMethod(_change).Invoke(null, null)!;
            _reader = channel.GetType().GetProperty("Reader")!.GetValue(channel)!;
            _writer = channel.GetType().GetProperty("Writer")!.GetValue(channel)!;
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var prepare = typeof(ServeCommand).GetMethod("PrepareOutputAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Func<string, CancellationToken, Task>>(_command);
            _server = new PreviewServer(Site, port, prepare);
            Set("_server", _server);
            Invoke("RebuildSite");
            _server.Start();
            _requests = _server.RunAsync(_stop.Token);
            Client = new HttpClient { BaseAddress = new Uri(_server.BaseUrl), Timeout = TimeSpan.FromSeconds(5) };
        }

        public void Write(string path, string content) => File.WriteAllText(Path.Combine(Docs, path), content);
        public Task<string> Status() => Client.GetStringAsync("__mdu/status");
        public void StartProcessing() => _processing = (Task)Invoke("ProcessChangesAsync", _reader, _server, _stop.Token)!;
        public void Change(params string[] paths)
        {
            foreach (var change in Changes(paths))
                Assert.True((bool)_writer.GetType().GetMethod("TryWrite")!.Invoke(_writer, [change])!);
        }
        public void ProcessBatch(params string[] paths) => Invoke("ProcessChangeBatch", Changes(paths), _server);
        private Array Changes(string[] paths)
        {
            var changes = Array.CreateInstance(_change, paths.Length);
            var kind = Enum.Parse(typeof(ServeCommand).GetNestedType("FileChangeKind", BindingFlags.NonPublic)!, "Changed");
            for (var index = 0; index < paths.Length; index++)
                changes.SetValue(Activator.CreateInstance(_change, Path.Combine(Docs, paths[index]), null, kind), index);
            return changes;
        }
        public FailingVisitor FailPendingPage(Exception failure)
        {
            var visitor = new FailingVisitor(Docs, Site, _renderer, _search, Get<MarkdownFile[]>("_navigationRoots"), failure);
            Set("_pendingVisitor", visitor);
            return visitor;
        }
        public async Task WaitFor(Func<Task<bool>> condition)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await condition())
            {
                if (_processing?.IsFaulted == true) await _processing;
                await Task.Delay(10, deadline.Token);
            }
        }
        private T Get<T>(string name) => (T)typeof(ServeCommand).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_command)!;
        private void Set(string name, object value) => typeof(ServeCommand).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_command, value);
        public object? Invoke(string name, params object[] arguments)
        {
            try { return typeof(ServeCommand).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_command, arguments); }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            try { await Task.WhenAll(_requests, _processing ?? Task.CompletedTask); }
            catch (OperationCanceledException) { }
            finally
            {
                Client.Dispose();
                _server.Dispose();
                _stop.Dispose();
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private sealed class FailingVisitor(string root, string output, HtmlRenderer renderer, SearchIndexBuilder search, MarkdownFile[] roots, Exception failure)
        : MarkdigVisitor(root, output, renderer, string.Empty, search, roots, [], new Dictionary<string, DateTimeOffset>())
    {
        public Exception? Failure = failure;
        public TaskCompletionSource<bool> Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override bool ProcessFile(MarkdownFile entity, int depth, FilePathInfo io)
        {
            if (Failure is { } exception)
            {
                Attempted.TrySetResult(true);
                throw exception;
            }
            return base.ProcessFile(entity, depth, io);
        }
    }
}
