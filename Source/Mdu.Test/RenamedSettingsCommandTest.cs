using Markdown.Document.Impl;
using Markdown.Renderer;
using Markdown.Search;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Renderer;
using Mdu.Service.Visitors;

namespace Mdu.Test;

[Collection("Process state")]
public class RenamedSettingsCommandTest : IDisposable
{
    private const string Renamed = "'site_name' is now 'site.name'";
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;
    private readonly string _output = Directory.CreateTempSubdirectory().FullName;
    private readonly GraphBuilder _graphBuilder = new(new DataPreparationVisitor(new MarkdownDocumentReader()));

    public RenamedSettingsCommandTest()
    {
        File.WriteAllText(Path.Combine(_root, "README.md"), "# Docs\n");
        File.WriteAllText(Path.Combine(_root, "mdu.yml"), "site_name: Docs\n");
    }

    public void Dispose()
    {
        Directory.Delete(_root, true);
        Directory.Delete(_output, true);
    }

    [Fact]
    public void RenderStops()
    {
        var command = new RenderCommand(_graphBuilder, new CountNodesVisitor<MarkdownFile>(), new HtmlRenderer(), new SearchIndexBuilder());

        var (exitCode, errors) = Run(() => command.Execute(new RenderCommandSettings { RootPath = _root, OutputPath = _output }));

        Assert.Equal(1, exitCode);
        Assert.Contains(Renamed, errors);
    }

    [Fact]
    public void PdfStops()
    {
        var command = new PdfCommand(_graphBuilder, new PdfRenderer(new HtmlRenderer()));

        var (exitCode, errors) = Run(() => command.Execute(new RenderCommandSettings { RootPath = _root, OutputPath = _output }));

        Assert.Equal(1, exitCode);
        Assert.Contains(Renamed, errors);
    }

    [Fact]
    public void FixStops()
    {
        var command = new FixCommand(_graphBuilder, new LinterVisitor(new ConsoleLintErrorReceiver()));

        var (exitCode, errors) = Run(() => command.Execute(new VisitorSettings { RootPath = _root, SearchPattern = "*.md" }));

        Assert.Equal(1, exitCode);
        Assert.Contains(Renamed, errors);
    }

    [Fact]
    public void LintWarns()
    {
        var receiver = new ConsoleLintErrorReceiver();
        var command = new LintCommand(_graphBuilder, new LinterVisitor(receiver), new LintResultRenderer(receiver));

        var (_, errors) = Run(() => command.Execute(new VisitorSettings { RootPath = _root, SearchPattern = "*.md" }));

        Assert.Contains($"Warning: mdu.yml uses renamed settings: {Renamed}", errors);
    }

    [Fact]
    public void TreeWarns()
    {
        var command = new PrintTreeCommand(_graphBuilder, new PrintTreeVisitor());

        var (exitCode, errors) = Run(() => command.Execute(new VisitorSettings { RootPath = _root, SearchPattern = "*.md" }));

        Assert.Equal(0, exitCode);
        Assert.Contains($"Warning: mdu.yml uses renamed settings: {Renamed}", errors);
    }

    private static (int ExitCode, string Errors) Run(Func<int> command)
    {
        var original = Console.Error;
        using var errors = new StringWriter();
        Console.SetError(errors);
        try
        {
            return (command(), errors.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }
}
