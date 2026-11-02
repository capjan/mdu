using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Mdu.Service;

namespace Mdu.Test;

[Collection("Process state")]
public class PreviewServerLoggingTest
{
    [Fact]
    public async Task ClientDisconnectDuringTheResponseDoesNotLogAServerFailure()
    {
        var error = await Request(abortResponse: true);
        Assert.Equal(string.Empty, error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenderFailuresAreStillLogged(bool unexpected)
    {
        var failure = unexpected ? (Exception)new NullReferenceException("Render failure") : new IOException("Render failure");
        var error = await Request(abortResponse: false, (_, _) => Task.FromException(failure));
        Assert.Contains("Render failure", error);
    }

    private static async Task<string> Request(bool abortResponse, Func<string, CancellationToken, Task>? prepare = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-request-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "large.bin");
        // A sparse file larger than the socket buffers keeps delivery in flight when the client resets it.
        using (var file = File.Create(path)) file.SetLength(64 * 1024 * 1024);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var server = new PreviewServer(root, port, prepare);
        server.Start();
        var http = (HttpListener)typeof(PreviewServer).GetField("_listener", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server)!;
        var handle = typeof(PreviewServer).GetMethod("HandleRequestAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<HttpListenerContext, CancellationToken, Task>>(server);
        var originalError = Console.Error;
        using var errors = new StringWriter();
        Console.SetError(errors);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var accepted = http.GetContextAsync();
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"GET /large.bin HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nConnection: close\r\n\r\n"), deadline.Token);
            var context = await accepted.WaitAsync(deadline.Token);
            var serving = handle(context, CancellationToken.None);
            using var reader = new StreamReader(client.GetStream());
            var status = await reader.ReadLineAsync(deadline.Token);
            Assert.Contains(abortResponse ? "200" : "500", status);
            while (await reader.ReadLineAsync(deadline.Token) is { Length: > 0 }) { }
            if (abortResponse)
            {
                client.Client.LingerState = new LingerOption(true, 0);
                client.Close();
            }
            else await reader.ReadToEndAsync(deadline.Token);
            await serving.WaitAsync(deadline.Token);
            return errors.ToString();
        }
        finally
        {
            Console.SetError(originalError);
            Directory.Delete(root, recursive: true);
        }
    }
}
