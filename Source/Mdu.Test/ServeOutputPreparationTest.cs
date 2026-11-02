using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Markdown.Document.Impl;
using Markdown.Renderer;
using Markdown.Search;
using Mdu.Commands;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Test;

public class ServeOutputPreparationTest
{
    [Fact]
    public async Task AssetsAndPendingDownloadsRemainAvailableWhileRenderingHoldsTheLock()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-assets-during-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        foreach (var name in new[] { "main.css", "image.svg", "main.js", "search-index.json", "index.html" })
            File.WriteAllText(Path.Combine(root, name), name == "index.html" ? "<html><body>Home</body></html>" : "Asset");
        var preparation = new DataPreparationVisitor(new MarkdownDocumentReader());
        var command = new ServeCommand(new GraphBuilder(preparation), preparation, new HtmlRenderer(), new SearchIndexBuilder());
        var prepare = typeof(ServeCommand).GetMethod("PrepareOutputAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string, CancellationToken, Task>>(command);
        var renderLock = (SemaphoreSlim)typeof(ServeCommand).GetField("_renderLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(command)!;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var server = new PreviewServer(root, port, prepare);
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        server.SetDownloads([Path.Combine(root, "documentation.pdf")], "pending");
        server.Start();
        var running = server.RunAsync(cancellation.Token);
        await renderLock.WaitAsync();
        try
        {
            foreach (var name in new[] { "main.css", "image.svg", "main.js", "search-index.json" })
                Assert.Equal("Asset", await client.GetStringAsync(server.BaseUrl + name));
            using var pdf = await client.GetAsync(server.BaseUrl + "documentation.pdf");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, pdf.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(1), pdf.Headers.RetryAfter?.Delta);
        }
        finally
        {
            renderLock.Release();
            cancellation.Cancel();
            await running;
            Directory.Delete(root, recursive: true);
        }
    }
}
