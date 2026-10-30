using System.Xml;
using Core.Collections.NodeGraph.Extensions;
using Markdown.Renderer;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Mdu.Commands;

public sealed class PdfCommand(GraphBuilder graphBuilder, PdfRenderer pdfRenderer)
{
    public IReadOnlyCollection<string> GeneratedSitePdfPaths { get; private set; } = Array.Empty<string>();

    public int Execute(RenderCommandSettings settings)
    {
        string rootPath;
        string? singleFilePath = null;
        try
        {
            if (settings.RootPath is { } configuredPath && File.Exists(Path.GetFullPath(configuredPath)))
            {
                if (settings.PdfRootPage is not null)
                    throw new InvalidOperationException("--page requires a documentation folder as PATH, not a single file.");
                singleFilePath = Path.GetFullPath(configuredPath);
                if (!Path.GetExtension(singleFilePath).Equals(".md", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"PDF input must be a Markdown file: {singleFilePath}");
                rootPath = Path.GetDirectoryName(singleFilePath)!;
            }
            else
            {
                rootPath = CommandPathDefaults.ResolveRootPath(settings.RootPath);
            }
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }

        var defaultOutputName = singleFilePath is null ? "documentation.pdf" : $"{Path.GetFileNameWithoutExtension(singleFilePath)}.pdf";
        var outputPath = Path.GetFullPath(settings.OutputPath ?? Path.Combine(Environment.CurrentDirectory, defaultOutputName));
        // Like mdu render, the PDF is not written without the custom icons that its pages use.
        try
        {
            SiteConfigurationReader.UseCustomIcons(rootPath, SiteConfigurationReader.ReadForContent(rootPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or YamlException)
        {
            Console.Error.WriteLine($"Could not load the custom icons: {exception.Message}");
            return 1;
        }
        MarkdownFile[] graph;
        try
        {
            graph = graphBuilder.CreateGraph(new RenderCommandSettings
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
        var files = Flatten(graph);
        if (singleFilePath is not null)
            files = files.Where(item => Path.GetFullPath(item.File.FileInfo.FullName).Equals(singleFilePath, StringComparison.Ordinal));
        try
        {
            if (settings.PdfRootPage is not null)
                files = Flatten([PdfSelection.FindPage(rootPath, graph,
                    OperatingSystem.IsWindows() ? settings.PdfRootPage.Replace('\\', '/') : settings.PdfRootPage)]);
            return WritePdf(rootPath, outputPath, files, includeFrontMatter: singleFilePath is null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Could not render PDF: {exception.Message}");
            return 1;
        }
    }

    public int WriteSitePdf(string rootPath, string outputPath, IEnumerable<MarkdownFile> graph) =>
        WritePdf(rootPath, outputPath, Flatten(graph), includeFrontMatter: true);

    public int WriteSitePdfIfLinked(string rootPath, string outputPath, IEnumerable<MarkdownFile> graph)
    {
        outputPath = Path.GetFullPath(outputPath);
        var roots = graph.ToArray();
        var files = Flatten(roots).ToArray();
        var requests = new Dictionary<string, MarkdownFile?>(MarkdownLinks.PathComparer);
        foreach (var (page, _) in files)
        {
            var markdown = OpenApiMarkdownRenderer.ExpandCodeBlocks(
                RemoveFrontMatter(page.ReadMarkdown(), page.FrontMatter), page.FileInfo.FullName,
                out var specificationPages, page.SourceRootPath);
            var links = pdfRenderer.FindPdfLinks(markdown).Concat(
                specificationPages.SelectMany(specification => pdfRenderer.FindPdfLinks(specification.Markdown)));
            foreach (var url in links.Distinct(StringComparer.Ordinal))
            {
                var path = LinkReplacer.GetPdfRoot(url);
                var selected = path is null ? null : PdfSelection.FindPage(rootPath, roots, path, page);
                var destination = selected is null ? outputPath :
                    Path.GetFullPath(Path.Combine(Path.GetDirectoryName(outputPath)!,
                        PdfSelection.GetOutputPath(rootPath, selected)));
                requests[destination] = selected;
            }
        }

        foreach (var (destination, selected) in requests)
            if (WritePdf(rootPath, destination, selected is null ? files : Flatten([selected]), includeFrontMatter: true) != 0)
                return 1;

        if (!requests.ContainsKey(outputPath) && File.Exists(outputPath)) File.Delete(outputPath);
        var scopedDirectory = Path.Combine(Path.GetDirectoryName(outputPath)!, PdfSelection.OutputDirectory);
        if (Directory.Exists(scopedDirectory))
            foreach (var oldPath in Directory.EnumerateFiles(scopedDirectory, "*.pdf"))
                if (Path.GetFileNameWithoutExtension(oldPath) is { Length: 64 } name &&
                    name.All(char.IsAsciiHexDigit) && !requests.ContainsKey(Path.GetFullPath(oldPath)))
                    File.Delete(oldPath);
        GeneratedSitePdfPaths = requests.Keys.ToArray();
        return 0;
    }

    private int WritePdf(
        string rootPath,
        string outputPath,
        IEnumerable<(MarkdownFile File, int Depth)> files,
        bool includeFrontMatter)
    {
        var sections = files.Where(item => IsPdfVisible(item.File.FrontMatter))
            .Select(item =>
            {
                var markdown = OpenApiMarkdownRenderer.ExpandCodeBlocks(
                    RemoveFrontMatter(item.File.ReadMarkdown(), item.File.FrontMatter),
                    item.File.FileInfo.FullName,
                    item.File.SourceRootPath,
                    includeSpecificationPageLink: false);
                return new PdfSourceSection(
                    GetTitle(item.File),
                    GetRelativeHtmlPath(rootPath, item.File),
                    markdown,
                    item.Depth);
            })
            .ToArray();

        if (sections.Length == 0)
        {
            Console.Error.WriteLine("No Markdown pages selected for PDF output.");
            return 1;
        }

        var outputDirectory = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(outputDirectory);
        var temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
                pdfRenderer.WritePdf(stream, Path.GetFileNameWithoutExtension(outputPath), sections, includeFrontMatter);
            File.Move(temporaryPath, outputPath, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            Console.Error.WriteLine($"Could not write PDF: {exception.Message}");
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            return 1;
        }

        Console.WriteLine($"Wrote {sections.Length} Markdown files to {outputPath}");
        return 0;
    }

    private static IEnumerable<(MarkdownFile File, int Depth)> Flatten(IEnumerable<MarkdownFile> files, int depth = 0)
    {
        foreach (var file in files.OrderBy(file => file.NavigationOrder.HasValue ? 0 : 1)
                     .ThenBy(file => file.NavigationOrder)
                     .ThenBy(GetTitle, StringComparer.OrdinalIgnoreCase))
        {
            yield return (file, depth);
            foreach (var child in Flatten(file.Children, depth + 1)) yield return child;
        }
    }

    private static string GetTitle(MarkdownFile file) =>
        !string.IsNullOrWhiteSpace(file.Title)
            ? file.Title.Trim()
            : file.FileInfo.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase)
                ? file.FileInfo.Directory?.Name ?? "Documentation"
                : Path.GetFileNameWithoutExtension(file.FileInfo.Name);

    private static string GetRelativeHtmlPath(string rootPath, MarkdownFile file)
    {
        var relativePath = Path.ChangeExtension(file.SiteRelativePath ?? Path.GetRelativePath(rootPath, file.FileInfo.FullName), ".html");
        if (Path.GetFileName(relativePath).Equals("README.html", StringComparison.OrdinalIgnoreCase))
            relativePath = Path.Combine(Path.GetDirectoryName(relativePath) ?? string.Empty, "index.html");
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string RemoveFrontMatter(string markdown, string frontMatter) =>
        frontMatter.Length > 0 && markdown.StartsWith(frontMatter, StringComparison.Ordinal)
            ? markdown[frontMatter.Length..].TrimStart('\r', '\n')
            : markdown;

    private static bool IsPdfVisible(string frontMatter)
    {
        if (!frontMatter.Contains("pdf", StringComparison.Ordinal)) return true;
        var lines = frontMatter.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[0].Trim() == "---") lines.RemoveAt(0);
        if (lines.Count > 0 && lines[^1].Trim() == "---") lines.RemoveAt(lines.Count - 1);

        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(string.Join(Environment.NewLine, lines)));
            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode metadata)
                return true;

            YamlNode? visibleNode = null;
            if (metadata.Children.TryGetValue(new YamlScalarNode("pdf"), out var pdfNode) && pdfNode is YamlMappingNode pdf &&
                pdf.Children.TryGetValue(new YamlScalarNode("visible"), out var nestedVisibleNode))
                visibleNode = nestedVisibleNode;
            if (visibleNode is null && metadata.Children.TryGetValue(new YamlScalarNode("pdf.visible"), out var compactVisibleNode))
                visibleNode = compactVisibleNode;

            return visibleNode is not YamlScalarNode scalar || !bool.TryParse(scalar.Value, out var visible) || visible;
        }
        catch (YamlException)
        {
            return true;
        }
    }
}
