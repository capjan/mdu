using Core.Collections.NodeGraph.Extensions;
using Markdown.Renderer;
using Markdown.Search;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Commands;

public class RenderCommand
{
    private readonly GraphBuilder _graphBuilder;
    private readonly CountNodesVisitor<MarkdownFile> _countNodesVisitor;
    private readonly HtmlRenderer _renderer;
    private readonly SearchIndexBuilder _searchIndexBuilder;
    private readonly PdfCommand _pdfCommand;

    public RenderCommand(
        GraphBuilder graphBuilder,
        CountNodesVisitor<MarkdownFile> countNodesVisitor,
        HtmlRenderer renderer,
        SearchIndexBuilder searchIndexBuilder,
        PdfCommand? pdfCommand = null)
    {
        _graphBuilder = graphBuilder;
        _countNodesVisitor = countNodesVisitor;
        _renderer = renderer;
        _searchIndexBuilder = searchIndexBuilder;
        _pdfCommand = pdfCommand ?? new PdfCommand(graphBuilder, new PdfRenderer(renderer));
    }

    public int Execute(RenderCommandSettings settings)
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

        var outPath = CommandPathDefaults.ResolveOutputPath(settings.OutputPath);
        var resolvedSettings = new RenderCommandSettings
        {
            RootPath = rootPath,
            OutputPath = outPath,
            SearchPattern = settings.SearchPattern,
            IncludeHidden = settings.IncludeHidden,
            EditPageRoot = settings.EditPageRoot
        };
        
        Markdown.Renderer.Res.Model.SiteConfiguration siteConfiguration;
        string? glossaryPath;
        try
        {
            siteConfiguration = SiteConfigurationReader.Read(rootPath);
            glossaryPath = SiteConfigurationReader.ResolveGlossaryPath(rootPath, siteConfiguration);
            _renderer.SetGlossary(glossaryPath is null ? null : File.ReadAllText(glossaryPath));
        }
        catch (YamlDotNet.Core.YamlException exception)
        {
            Console.Error.WriteLine($"Could not read mdu.yml: {exception.Message}");
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"Could not read site configuration or glossary: {exception.Message}");
            return 1;
        }
        try
        {
            SiteConfigurationReader.UseCustomIcons(rootPath, siteConfiguration);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"Could not load the custom icons: {exception.Message}");
            return 1;
        }
        MarkdownFile[] graph;
        try
        {
            graph = _graphBuilder.CreateGraph(resolvedSettings, glossaryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Could not resolve Markdown sources: {exception.Message}");
            return 1;
        }

        _renderer.PrepareBasePath(outPath, siteConfiguration);
        SearchClientAsset.WriteTo(outPath);
        _searchIndexBuilder.Clear();
        var nodeCount = CountMarkdownNodes(graph);
        
        Console.WriteLine("Preparing Assets");
        CopyAssets(rootPath, outPath, graph);
        SiteConfigurationReader.CopyAssets(rootPath, outPath, siteConfiguration);

        try
        {
            if (_pdfCommand.WriteSitePdfIfLinked(rootPath, Path.Combine(outPath, "documentation.pdf"), graph) != 0)
                return 1;

            Console.WriteLine($"Rendering {nodeCount} Files");
            RenderNodesToHtml(rootPath, outPath, graph, settings.EditPageRoot);
            _searchIndexBuilder.WriteIndex(outPath);

            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Could not render the site: {exception.Message}");
            return 1;
        }
    }

    private void RenderNodesToHtml(
        string rootPath,
        string outPath,
        MarkdownFile[] graph,
        string editPageRoot)
    {
        var rendererVisitor = new MarkdigVisitor(rootPath, outPath, _renderer, editPageRoot, _searchIndexBuilder, graph);
        
        graph.Visit(rendererVisitor);
    }

    private static void CopyAssets(string rootPath, string outPath, MarkdownFile[] graph)
    {
        var assetsVisitor = new LocalAssetsCopy(rootPath, outPath);
        graph.Visit(assetsVisitor);
    }

    private int CountMarkdownNodes(MarkdownFile[] graph)
    {
        graph.Visit(_countNodesVisitor);
        var nodeCount = _countNodesVisitor.NodeCount;
        return nodeCount;
    }
}
