using Mdu.Service.PageTree.Extensions;
using Mdu.Commands.Settings;
using Mdu.Service;
using Mdu.Service.Renderer;
using Mdu.Service.Visitors;
using Mdu.Service.Assets;
using Markdown.Linter;

namespace Mdu.Commands;

public class LintCommand
{
    private readonly GraphBuilder _graphBuilder;
    private readonly LinterVisitor _linterVisitor;
    private readonly LintResultRenderer _lintResultRenderer;
    private readonly AssetServerClient? _assets;

    public LintCommand(GraphBuilder graphBuilder, LinterVisitor linterVisitor, LintResultRenderer lintResultRenderer, AssetServerClient? assets = null)
    {
        _assets = assets;
        _linterVisitor = linterVisitor;
        _lintResultRenderer = lintResultRenderer;
        _graphBuilder = graphBuilder;
    }

    public int Execute(VisitorSettings settings, bool pretty = false, bool color = false, LintFormat format = LintFormat.Text, bool offline = false)
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
        if (!offline) CheckAssetLinks(rootPath);
        return format == LintFormat.Text
            ? _lintResultRenderer.Render(_linterVisitor.OrphanedPages, rootPath, pretty, color)
            : _lintResultRenderer.Render(format, _linterVisitor.OrphanedPages, rootPath);
    }

    /// <summary>
    /// MDU032: images and links on the public host of the site's asset server must exist and must not be in the trash. The
    /// server checks them in batches through its public check endpoint; when it cannot be reached, lint warns and goes on.
    /// </summary>
    private void CheckAssetLinks(string rootPath)
    {
        if (_assets is null) return;
        var receiver = _linterVisitor.ErrorReceiver;
        var configPath = Path.Combine(rootPath, "mdu.yml");
        AssetsSettings? settings;
        try
        {
            settings = SiteConfigurationReader.ReadAssets(rootPath);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or YamlDotNet.Core.YamlException)
        {
            receiver.AddWarning(configPath, 1, $"{RuleCodes.BrokenAssetLink}: Asset links are not checked: {exception.Message}");
            return;
        }
        if (settings is null) return;

        var links = _linterVisitor.WebLinks.Where(link => settings.IsPublicLink(link.Url)).ToList();
        if (links.Count == 0) return;
        IReadOnlyList<LinkStatus> statuses;
        try
        {
            statuses = _assets.CheckAsync(settings, links.Select(link => link.Url).Distinct(StringComparer.Ordinal).ToList()).GetAwaiter().GetResult();
        }
        catch (AssetServerException exception)
        {
            receiver.AddWarning(configPath, 1,
                $"{RuleCodes.BrokenAssetLink}: {links.Count} asset links were not checked. {exception.Message} Use --offline to skip the check.");
            return;
        }

        var byUrl = statuses.GroupBy(status => status.Url, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Status, StringComparer.Ordinal);
        foreach (var (filePath, lineNumber, url) in links)
        {
            var problem = byUrl.GetValueOrDefault(url) switch
            {
                "ok" or null => null,
                "trash" => "is in the trash of the asset server; restore the image or change the link",
                "deleted" => "was deleted from the asset server for good",
                "invalid" => "is no image link of the asset server",
                _ => "points to no image on the asset server"
            };
            if (problem is not null)
                receiver.Add(filePath, lineNumber, $"{RuleCodes.BrokenAssetLink}: '{url}' {problem}.");
        }
    }
}
