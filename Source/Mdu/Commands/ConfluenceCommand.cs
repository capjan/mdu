using System.Text.Json;
using System.Xml;
using Markdown.Confluence;
using Markdown.Renderer;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using YamlDotNet.Core;

namespace Mdu.Commands;

public sealed class ConfluenceCommand(GraphBuilder graphBuilder)
{
    public async Task<int> Execute(ConfluenceCommandSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var parentId = settings.ParentPageId ?? Environment.GetEnvironmentVariable("MDU_CONFLUENCE_PARENT_PAGE_ID");
            var target = new ConfluenceTarget(
                Required(settings.Url, "MDU_CONFLUENCE_URL"),
                Required(settings.SpaceKey, "MDU_CONFLUENCE_SPACE"),
                string.IsNullOrWhiteSpace(parentId) ? null : parentId,
                settings.ExportId);
            var token = Required(null, "MDU_CONFLUENCE_PAT");
            var root = CommandPathDefaults.ResolveRootPath(settings.RootPath);
            var configuration = SiteConfigurationReader.Read(root);
            var glossaryPath = SiteConfigurationReader.ResolveGlossaryPath(root, configuration);
            SiteConfigurationReader.UseCustomIcons(root, configuration);
            var graph = graphBuilder.CreateGraph(new VisitorSettings
            {
                RootPath = root, SearchPattern = settings.SearchPattern, IncludeHidden = settings.IncludeHidden
            }, glossaryPath);
            var sources = PrepareSources(graph, root);
            var glossary = glossaryPath is null ? null : File.ReadAllText(glossaryPath);
            var pdf = PreparePdf(sources, glossary);
            using var client = new ConfluenceClient(target, token);
            await new ConfluencePublisher(client, Console.Out).Publish(sources, settings.DryRun,
                glossary, cancellationToken, pdf);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Confluence publication was cancelled or timed out. A partial export may remain; retry after checking the target.");
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or
                                           ArgumentException or HttpRequestException or JsonException or XmlException or YamlException)
        {
            var message = exception is HttpRequestException ? "Confluence connection failed; check the URL, network and certificate trust." : exception.Message;
            var token = Environment.GetEnvironmentVariable("MDU_CONFLUENCE_PAT");
            if (!string.IsNullOrEmpty(token)) message = message.Replace(token, "[redacted]", StringComparison.Ordinal);
            Console.Error.WriteLine($"Could not publish to Confluence: {message}");
            return 1;
        }
    }

    public static IReadOnlyList<ConfluenceSource> PrepareSources(IEnumerable<MarkdownFile> graph, string root)
    {
        var result = new List<ConfluenceSource>();
        foreach (var page in graph) Add(page, null);
        return result;

        void Add(MarkdownFile page, string? parentPath)
        {
            var path = (page.SiteRelativePath ?? Path.GetRelativePath(root, page.FileInfo.FullName)).Replace('\\', '/');
            var markdown = page.ReadMarkdown();
            if (page.FrontMatter.Length > 0 && markdown.StartsWith(page.FrontMatter, StringComparison.Ordinal))
                markdown = markdown[page.FrontMatter.Length..].TrimStart('\r', '\n');
            if (page.FrontMatter.Contains("dashboard", StringComparison.Ordinal))
                Console.WriteLine($"Warning: {path}: dashboard layout is not exported; its Markdown content is preserved.");
            result.Add(new ConfluenceSource(path, parentPath,
                NavigationPage.RemoveIconShortcodes(string.IsNullOrWhiteSpace(page.Title) ? page.FileInfo.Name : page.Title),
                markdown, page.FileInfo.FullName, page.SourceRootPath ?? root));
            foreach (var child in page.Children) Add(child, path);
        }
    }

    public static ConfluenceAsset? PreparePdf(IReadOnlyList<ConfluenceSource> sources, string? glossary)
    {
        var html = new HtmlRenderer();
        html.SetGlossary(glossary);
        if (!sources.Any(source => html.ContainsPdfLink(source.Markdown))) return null;
        using var buffer = new MemoryStream();
        var sections = sources.Select(source => new PdfSourceSection(source.Title,
            LinkReplacer.ReplaceUrl(source.Path)!,
            OpenApiMarkdownRenderer.ExpandCodeBlocks(source.Markdown, source.FilePath, source.SourceRoot,
                includeSpecificationPageLink: false))).ToArray();
        new PdfRenderer(html).WritePdf(buffer, "Documentation", sections);
        var content = buffer.ToArray();
        return new ConfluenceAsset("mdu-" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content)) + ".pdf", content, false);
    }

    private static string Required(string? value, string variable) =>
        !string.IsNullOrWhiteSpace(value ?? Environment.GetEnvironmentVariable(variable))
            ? (value ?? Environment.GetEnvironmentVariable(variable))!
            : throw new InvalidOperationException($"Set {variable} before publishing to Confluence.");
}
