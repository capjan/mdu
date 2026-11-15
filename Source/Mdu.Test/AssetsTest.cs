using System.Net;
using System.Text;
using System.Text.Json;
using Markdown.Document.Impl;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Service;
using Mdu.Service.Assets;
using Mdu.Service.Renderer;
using Mdu.Service.Visitors;

namespace Mdu.Test;

/// <summary>The asset server of a site: the assets section, mdu assets upload and the link check MDU032 (mdu-assets M10).</summary>
[Collection("Process state")]
public sealed class AssetsTest : IDisposable
{
    private const string Server = "https://assets.example.com/";
    private const string PublicHost = "https://img.example.com";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mdu-assets-test-{Guid.NewGuid():N}");
    private readonly FakeServer _server = new();
    private readonly string? _originalToken = Environment.GetEnvironmentVariable(AssetsSettings.TokenVariable);

    public AssetsTest()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "README.md"), "# Docs\n");
        WriteSettings($"assets:\n  server: {Server}\n  public: {PublicHost}\n  project: docs\n");
        Environment.SetEnvironmentVariable(AssetsSettings.TokenVariable, "mdua_test-token");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AssetsSettings.TokenVariable, _originalToken);
        Directory.Delete(_root, recursive: true);
    }

    private void WriteSettings(string yaml) => File.WriteAllText(Path.Combine(_root, "mdu.yml"), yaml);

    private AssetsCommand Assets() => new(new AssetServerClient(new HttpClient(_server)));

    private static (int ExitCode, string Output, string Errors) Run(Func<int> command)
    {
        var (originalOut, originalError) = (Console.Out, Console.Error);
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Console.SetOut(output);
        Console.SetError(errors);
        try
        {
            return (command(), output.ToString(), errors.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static string AssetJson(string name) =>
        $$"""{"fileName":"{{name}}","url":"{{PublicHost}}/i/AbCdEfGhIj/{{name}}","markdown":"![Shot]({{PublicHost}}/i/AbCdEfGhIj/{{name}})","html":"<img src=\"x\" alt=\"Shot\">"}""";

    [Fact]
    public void ReadsTheAssetsSection()
    {
        var settings = SiteConfigurationReader.ReadAssets(_root)!;

        Assert.Equal((Server, PublicHost + "/", "docs"), (settings.Server.ToString(), settings.Public.ToString(), settings.Project));
        Assert.True(settings.IsPublicLink($"{PublicHost}/i/AbCdEfGhIj/x.png?w=800"));
        Assert.False(settings.IsPublicLink("https://assets.example.com/i/AbCdEfGhIj/x.png"));
        Assert.False(settings.IsPublicLink("https://example.org/i/AbCdEfGhIj/x.png"));
    }

    [Theory]
    [InlineData("assets.server: https://assets.example.com/base", "https://assets.example.com/base/", "https://assets.example.com/base/")]
    [InlineData("assets:\n  server: http://127.0.0.1:5180", "http://127.0.0.1:5180/", "http://127.0.0.1:5180/")]
    public void UsesTheServerForPublicLinksWhenNoOtherAddressIsGiven(string yaml, string server, string publicBase)
    {
        WriteSettings(yaml);

        var settings = SiteConfigurationReader.ReadAssets(_root)!;

        Assert.Equal((server, publicBase, (string?)null), (settings.Server.ToString(), settings.Public.ToString(), settings.Project));
    }

    [Theory]
    [InlineData("assets.server: assets.example.com", "must be an http or https address")]
    [InlineData("assets.server: ftp://assets.example.com", "must be an http or https address")]
    [InlineData("assets.project: docs", "need 'assets.server'")]
    public void ExplainsWrongAssetSettings(string yaml, string message)
    {
        WriteSettings(yaml);

        Assert.Contains(message, Assert.Throws<InvalidOperationException>(() => SiteConfigurationReader.ReadAssets(_root)).Message);
        Assert.Contains(message, Assert.Throws<InvalidOperationException>(() => SiteConfigurationReader.Read(_root)).Message);
    }

    [Fact]
    public void UploadsAndPrintsTheSnippet()
    {
        var image = Path.Combine(_root, "shot.png");
        File.WriteAllBytes(image, [1, 2, 3]);
        _server.Respond(HttpStatusCode.Created, AssetJson("shot.png"));

        var (exitCode, output, _) = Run(() => Assets().Upload(_root, [image], null, ["Setup", "macOS"], "Settings dialog", null, "markdown"));

        Assert.Equal(0, exitCode);
        Assert.Equal($"![Shot]({PublicHost}/i/AbCdEfGhIj/shot.png)", output.Trim());
        var request = Assert.Single(_server.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("Bearer mdua_test-token", request.Authorization);
        Assert.Equal(
            "https://assets.example.com/api/v1/projects/docs/assets?name=shot.png&duplicates=reject&alt=Settings%20dialog&tag=Setup&tag=macOS",
            request.Url);
        Assert.Equal([1, 2, 3], request.Body);
    }

    [Fact]
    public void UploadsStandardInputAndPrintsTheChosenFormat()
    {
        _server.Respond(HttpStatusCode.Created, AssetJson("clipboard.png"));

        var (exitCode, output, _) = Run(() =>
            Assets().Upload(_root, ["-"], "other", [], null, "clipboard.png", "url", new MemoryStream([9, 9])));

        Assert.Equal(0, exitCode);
        Assert.Equal($"{PublicHost}/i/AbCdEfGhIj/clipboard.png", output.Trim());
        Assert.StartsWith("https://assets.example.com/api/v1/projects/other/assets?name=clipboard.png", _server.Requests[0].Url);
    }

    [Fact]
    public void PrintsTheExistingImageForADuplicate()
    {
        var image = Path.Combine(_root, "again.png");
        File.WriteAllBytes(image, [1]);
        _server.Respond(HttpStatusCode.Conflict, $$"""{"title":"Already uploaded","detail":"…","existing":{{AssetJson("first.png")}}}""");

        var (exitCode, output, errors) = Run(() => Assets().Upload(_root, [image], null, [], null, null, "markdown"));

        Assert.Equal(0, exitCode);
        Assert.Contains("first.png", output);
        Assert.Contains("is already in the project as first.png", errors);
    }

    [Fact]
    public void ExplainsRefusalsAndGoesOnWithTheNextFile()
    {
        var first = Path.Combine(_root, "a.png");
        var second = Path.Combine(_root, "b.png");
        File.WriteAllBytes(first, [1]);
        File.WriteAllBytes(second, [2]);
        _server.Respond(HttpStatusCode.UnprocessableEntity, """{"title":"Quota exceeded","detail":"The project uses 10 of 10 bytes."}""");
        _server.Respond(HttpStatusCode.Created, AssetJson("b.png"));

        var (exitCode, output, errors) = Run(() => Assets().Upload(_root, [first, second], null, [], null, null, "markdown"));

        Assert.Equal(1, exitCode);
        Assert.Contains($"{first}: The project uses 10 of 10 bytes. (422", errors);
        Assert.Contains("b.png", output);
    }

    [Fact]
    public void NamesWhatIsMissingBeforeUploading()
    {
        Environment.SetEnvironmentVariable(AssetsSettings.TokenVariable, null);
        Assert.Contains("set MDU_ASSETS_TOKEN", Run(() => Assets().Upload(_root, ["x.png"], null, [], null, null, "markdown")).Errors);

        Environment.SetEnvironmentVariable(AssetsSettings.TokenVariable, "mdua_test-token");
        WriteSettings("site.name: Docs\n");
        Assert.Contains("Add 'assets.server' to mdu.yml", Run(() => Assets().Upload(_root, ["x.png"], null, [], null, null, "markdown")).Errors);

        WriteSettings($"assets.server: {Server}\n");
        Assert.Contains("Use --project", Run(() => Assets().Upload(_root, ["x.png"], null, [], null, null, "markdown")).Errors);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public void NeverSendsTheTokenOverPlainHttpToAnotherMachine()
    {
        WriteSettings("assets.server: http://assets.example.com\nassets.project: docs\n");
        var image = Path.Combine(_root, "a.png");
        File.WriteAllBytes(image, [1]);

        var (exitCode, _, errors) = Run(() => Assets().Upload(_root, [image], null, [], null, null, "markdown"));

        Assert.Equal(1, exitCode);
        Assert.Contains("only over https", errors);
        Assert.Empty(_server.Requests);
    }

    private (int ExitCode, IReadOnlyList<LintDiagnostic> Diagnostics) Lint(string page, bool offline = false)
    {
        File.WriteAllText(Path.Combine(_root, "README.md"), page);
        var receiver = new ConsoleLintErrorReceiver();
        var command = new LintCommand(new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())), new LinterVisitor(receiver),
            new LintResultRenderer(receiver), new AssetServerClient(new HttpClient(_server)));
        var (exitCode, _, _) = Run(() => command.Execute(new VisitorSettings { RootPath = _root, SearchPattern = "*.md" }, offline: offline));
        return (exitCode, receiver.Diagnostics);
    }

    [Fact]
    public void ReportsMissingAndTrashedImagesOnTheAssetServer()
    {
        _server.Respond(HttpStatusCode.OK, $$"""
            {"results":[
              {"url":"{{PublicHost}}/i/Ok00000000/ok.png","status":"ok"},
              {"url":"{{PublicHost}}/i/Trash00000/old.png","status":"trash"},
              {"url":"{{PublicHost}}/i/Gone000000/x.png?w=800","status":"unknown"}]}
            """);

        var (exitCode, diagnostics) = Lint($"""
            # Docs

            ![OK]({PublicHost}/i/Ok00000000/ok.png)
            ![Old]({PublicHost}/i/Trash00000/old.png)
            [Gone]({PublicHost}/i/Gone000000/x.png?w=800)
            ![Elsewhere](https://example.org/i/Elsewhere0/x.png)
            """);

        Assert.Equal(1, exitCode);
        var problems = diagnostics.Where(d => d.Message.StartsWith("MDU032", StringComparison.Ordinal)).ToList();
        Assert.Equal([(4, "is in the trash"), (5, "points to no image")],
            problems.Select(d => (d.LineNumber, d.Message.Contains("trash") ? "is in the trash" : "points to no image")));
        Assert.All(problems, d => Assert.False(d.IsWarning));
        using var body = JsonDocument.Parse(Encoding.UTF8.GetString(Assert.Single(_server.Requests).Body));
        Assert.Equal(3, body.RootElement.GetProperty("urls").GetArrayLength());
        Assert.Equal("https://assets.example.com/api/v1/public/check", _server.Requests[0].Url);
    }

    [Fact]
    public void WarnsWhenTheAssetServerCannotBeReached()
    {
        _server.Fail();

        var (exitCode, diagnostics) = Lint($"# Docs\n\n![Shot]({PublicHost}/i/AbCdEfGhIj/x.png)\n");

        Assert.Equal(0, exitCode);
        var warning = Assert.Single(diagnostics, d => d.Message.StartsWith("MDU032", StringComparison.Ordinal));
        Assert.True(warning.IsWarning);
        Assert.Contains("cannot be reached", warning.Message);
    }

    [Fact]
    public void SkipsTheCheckOfflineAndWithoutAssetLinks()
    {
        Assert.Equal(0, Lint($"# Docs\n\n![Shot]({PublicHost}/i/AbCdEfGhIj/x.png)\n", offline: true).ExitCode);
        Assert.Equal(0, Lint("# Docs\n\n![Shot](https://example.org/x.png)\n").ExitCode);
        Assert.Empty(_server.Requests);
    }

    private sealed record RecordedRequest(HttpMethod Method, string Url, string? Authorization, byte[] Body);

    /// <summary>Answers requests from a queue and records them, so no test needs the network.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _answers = new();
        private bool _failing;

        public List<RecordedRequest> Requests { get; } = [];

        public void Respond(HttpStatusCode status, string body) => _answers.Enqueue((status, body));

        public void Fail() => _failing = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_failing) throw new HttpRequestException("Connection refused");
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), body));
            var (status, json) = _answers.Count > 0 ? _answers.Dequeue() : (HttpStatusCode.NotFound, "{}");
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
