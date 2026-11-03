using Mdu.Service.PageTree.Extensions;
using Mdu.Commands.Settings;
using Mdu.Service;
using Mdu.Service.Renderer;
using Mdu.Service.Visitors;

namespace Mdu.Commands;

public class LintCommand
{
    private readonly GraphBuilder _graphBuilder;
    private readonly LinterVisitor _linterVisitor;
    private readonly LintResultRenderer _lintResultRenderer;

    public LintCommand(GraphBuilder graphBuilder, LinterVisitor linterVisitor, LintResultRenderer lintResultRenderer)
    {
        _linterVisitor = linterVisitor;
        _lintResultRenderer = lintResultRenderer;
        _graphBuilder = graphBuilder;
    }

    public int Execute(VisitorSettings settings, bool pretty = false, bool color = false, LintFormat format = LintFormat.Text)
    {
        string rootPath;
        try
        {
            rootPath = CommandPathDefaults.ResolveRootPath(settings.RootPath);
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }

        // JSON output must stay a single document.
        if (format != LintFormat.Json) Console.WriteLine("Running Linter");
        SiteConfigurationReader.WarnAboutRenamedSettings(rootPath);
        Mdu.Model.MarkdownFile[] graph;
        try
        {
            graph = _graphBuilder.CreateGraph(new VisitorSettings
            {
                RootPath = rootPath,
                SearchPattern = settings.SearchPattern,
                IncludeHidden = settings.IncludeHidden
            }, SiteConfigurationReader.TryResolveGlossaryPath(rootPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"Could not resolve Markdown sources: {exception.Message}");
            return 1;
        }
        SiteConfigurationReader.TryUseDiagramLimits(rootPath);
        _linterVisitor.Prepare(graph, rootPath);
        graph.Visit(_linterVisitor);
        return format == LintFormat.Text
            ? _lintResultRenderer.Render(_linterVisitor.OrphanedPages, rootPath, pretty, color)
            : _lintResultRenderer.Render(format, _linterVisitor.OrphanedPages, rootPath);
    }
}
