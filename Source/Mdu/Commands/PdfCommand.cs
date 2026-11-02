using System.Xml;
using Mdu.Service.PageTree.Extensions;
using Markdown.Renderer;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Mdu.Commands;

public sealed class PdfCommand(GraphBuilder graphBuilder, PdfRenderer pdfRenderer)
{
    private readonly Dictionary<string, PdfSourceSection[]> _sitePdfSections = new(MarkdownLinks.PathComparer);
    private readonly Dictionary<MarkdownFile, (string Markdown, IReadOnlyList<string> Links)> _pagePdfLinks = new();
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
            var siteConfiguration = SiteConfigurationReader.ReadForContent(rootPath);
            SiteConfigurationReader.UseCustomIcons(rootPath, siteConfiguration);
            SiteConfigurationReader.UseDiagramLimits(siteConfiguration);
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
        var files = Flatten(rootPath, graph);
        if (singleFilePath is not null)
            files = files.Where(item => Path.GetFullPath(item.File.FileInfo.FullName).Equals(singleFilePath, StringComparison.Ordinal));
        try
        {
            if (settings.PdfRootPage is not null)
                files = Flatten(rootPath, [PdfSelection.FindPage(rootPath, graph,
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
        WritePdf(rootPath, outputPath, Flatten(rootPath, graph), includeFrontMatter: true);

    public int WriteSitePdfIfLinked(string rootPath, string outputPath, IEnumerable<MarkdownFile> graph,
        bool reuseUnchanged = false, Action<string, PdfSourceSection[]>? defer = null)
    {
        if (!reuseUnchanged)
        {
            _sitePdfSections.Clear();
            _pagePdfLinks.Clear();
        }
        outputPath = Path.GetFullPath(outputPath);
        var roots = graph.ToArray();
        var files = Flatten(rootPath, roots).ToArray();
        var requests = new Dictionary<string, MarkdownFile?>(MarkdownLinks.PathComparer);
        var failed = false;
        foreach (var (page, _) in files)
        {
            var source = page.ReadMarkdown();
            if (!_pagePdfLinks.TryGetValue(page, out var cached) || !ReferenceEquals(source, cached.Markdown))
            {
                var markdown = OpenApiMarkdownRenderer.ExpandCodeBlocks(
                    RemoveFrontMatter(source, page.FrontMatter), page.FileInfo.FullName,
                    out var specificationPages, page.SourceRootPath);
                var links = pdfRenderer.FindPdfLinks(markdown).Concat(
                    specificationPages.SelectMany(specification => pdfRenderer.FindPdfLinks(specification.Markdown)))
                    .Distinct(StringComparer.Ordinal).ToArray();
                _pagePdfLinks[page] = cached = (source, links);
            }
            foreach (var url in cached.Links)
            {
                MarkdownFile? selected;
                try
                {
                    var path = LinkReplacer.GetPdfRoot(url);
                    selected = path is null ? null : PdfSelection.FindPage(rootPath, roots, path, page);
                }
                catch (InvalidOperationException exception)
                {
                    // Keep collecting, so that the other downloads are still regenerated and none stays outdated.
                    Console.Error.WriteLine(
                        $"Could not write PDF: {PageLabel(PdfSelection.GetTitle(rootPath, page), GetRelativeHtmlPath(rootPath, page))}: {exception.Message}");
                    failed = true;
                    continue;
                }
                var destination = selected is null ? outputPath :
                    Path.GetFullPath(Path.Combine(Path.GetDirectoryName(outputPath)!,
                        PdfSelection.GetOutputPath(rootPath, selected)));
                requests[destination] = selected;
            }
        }

        foreach (var (destination, selected) in requests)
            if (WritePdf(rootPath, destination, selected is null ? files : Flatten(rootPath, [selected]),
                    includeFrontMatter: true, reuseUnchanged: reuseUnchanged, trackSitePdf: true, defer: defer) != 0)
                failed = true;

        if (!requests.ContainsKey(outputPath) && File.Exists(outputPath)) File.Delete(outputPath);
        var scopedDirectory = Path.Combine(Path.GetDirectoryName(outputPath)!, PdfSelection.OutputDirectory);
        if (Directory.Exists(scopedDirectory))
            foreach (var oldPath in Directory.EnumerateFiles(scopedDirectory, "*.pdf"))
                if (Path.GetFileNameWithoutExtension(oldPath) is { Length: 64 } name &&
                    name.All(char.IsAsciiHexDigit) && !requests.ContainsKey(Path.GetFullPath(oldPath)))
                    File.Delete(oldPath);
        foreach (var oldPath in _sitePdfSections.Keys.Where(path => !requests.ContainsKey(path)).ToArray())
            _sitePdfSections.Remove(oldPath);
        GeneratedSitePdfPaths = requests.Keys.ToArray();
        return failed ? 1 : 0;
    }

    private int WritePdf(
        string rootPath,
        string outputPath,
        IEnumerable<(MarkdownFile File, int Depth)> files,
        bool includeFrontMatter,
        bool reuseUnchanged = false,
        bool trackSitePdf = false,
        Action<string, PdfSourceSection[]>? defer = null)
    {
        PdfSourceSection[] sections;
        try
        {
            sections = files.Where(item => IsPdfVisible(item.File.FrontMatter))
                .Select(item =>
                {
                    var source = RemoveFrontMatter(item.File.ReadMarkdown(), item.File.FrontMatter);
                    IReadOnlySet<int> excludedBlocks;
                    try
                    {
                        excludedBlocks = pdfRenderer.FindHiddenOpenApiBlocks(source);
                    }
                    catch (PdfHiddenContentException exception)
                    {
                        throw new PdfHiddenContentException($"{PageLabel(PdfSelection.GetTitle(rootPath, item.File), GetRelativeHtmlPath(rootPath, item.File))}: {exception.Message}");
                    }
                    var markdown = OpenApiMarkdownRenderer.ExpandCodeBlocks(
                        source,
                        item.File.FileInfo.FullName,
                        item.File.SourceRootPath,
                        includeSpecificationPageLink: false,
                        excludedBlockStarts: excludedBlocks);
                    return new PdfSourceSection(
                        PdfSelection.GetPdfTitle(rootPath, item.File, markdown, pdfRenderer.ExcludesTitleHeading),
                        GetRelativeHtmlPath(rootPath, item.File),
                        markdown,
                        item.Depth);
                })
                .ToArray();
        }
        catch (PdfHiddenContentException exception)
        {
            Console.Error.WriteLine($"Could not write PDF: {exception.Message}");
            if (trackSitePdf) DiscardSitePdf(outputPath);
            return 1;
        }

        if (sections.Length == 0)
        {
            Console.Error.WriteLine("No Markdown pages selected for PDF output.");
            if (trackSitePdf) DiscardSitePdf(outputPath);
            return 1;
        }

        // Site PDFs resolve their links in WriteSitePdfIfLinked. Without that step, a malformed link would become a dead link.
        string? malformedLink;
        try
        {
            malformedLink = trackSitePdf ? null : FindMalformedPdfLink(sections);
        }
        catch (PdfHiddenContentException exception)
        {
            Console.Error.WriteLine($"Could not write PDF: {exception.Message}");
            return 1;
        }
        if (malformedLink is not null)
        {
            Console.Error.WriteLine($"Could not write PDF: {malformedLink}");
            return 1;
        }

        if (reuseUnchanged && File.Exists(outputPath) &&
            _sitePdfSections.TryGetValue(outputPath, out var previousSections) &&
            previousSections.SequenceEqual(sections))
            return 0;

        if (defer is not null)
        {
            defer(outputPath, sections);
            return 0;
        }
        return WritePreparedPdf(outputPath, sections, trackSitePdf,
            stream => pdfRenderer.WritePdf(stream, Path.GetFileNameWithoutExtension(outputPath), sections, includeFrontMatter));
    }

    private int WritePreparedPdf(string outputPath, PdfSourceSection[] sections, bool trackSitePdf, Action<Stream> write)
    {
        var outputDirectory = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(outputDirectory);
        var temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
                write(stream);
            File.Move(temporaryPath, outputPath, true);
            if (trackSitePdf) _sitePdfSections[outputPath] = sections;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or PdfHiddenContentException)
        {
            Console.Error.WriteLine($"Could not write PDF: {exception.Message}");
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            if (trackSitePdf) DiscardSitePdf(outputPath);
            return 1;
        }

        Console.WriteLine($"Wrote {sections.Length} Markdown files to {outputPath}");
        return 0;
    }

    public sealed class SitePdfJob(string outputPath, PdfSourceSection[] sections, PdfRenderer.RenderSession session)
    {
        public string OutputPath { get; } = outputPath;
        public PdfSourceSection[] Sections { get; } = sections;
        public PdfRenderer.RenderSession Session { get; } = session;
        public bool Failed { get; internal set; }
    }

    public SitePdfJob CreateSitePdfJob(string outputPath, PdfSourceSection[] sections) =>
        new(outputPath, sections, pdfRenderer.CreateSession(Path.GetFileNameWithoutExtension(outputPath), sections));

    public bool AdvanceSitePdfJob(SitePdfJob job)
    {
        try
        {
            if (job.Session.RenderNextSection()) return false;
            job.Failed = WritePreparedPdf(job.OutputPath, job.Sections, true, job.Session.WritePdf) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or PdfHiddenContentException)
        {
            Console.Error.WriteLine($"Could not write PDF: {exception.Message}");
            DiscardSitePdf(job.OutputPath);
            job.Failed = true;
        }
        return true;
    }

    private string? FindMalformedPdfLink(IEnumerable<PdfSourceSection> sections)
    {
        foreach (var section in sections)
        foreach (var url in FindVisiblePdfLinks(section))
        {
            try
            {
                LinkReplacer.GetPdfRoot(url);
            }
            catch (InvalidOperationException exception)
            {
                return $"{PageLabel(section.Title, section.RelativeHtmlPath)}: {exception.Message}";
            }
        }

        return null;
    }

    private IReadOnlyList<string> FindVisiblePdfLinks(PdfSourceSection section)
    {
        try
        {
            return pdfRenderer.FindVisiblePdfLinks(section.Markdown);
        }
        catch (PdfHiddenContentException exception)
        {
            throw new PdfHiddenContentException($"{PageLabel(section.Title, section.RelativeHtmlPath)}: {exception.Message}");
        }
    }

    // Matches the page label of the errors that PdfRenderer reports.
    private static string PageLabel(string title, string relativeHtmlPath) => $"Page \"{title}\" ({relativeHtmlPath})";

    // A failed site PDF must not stay downloadable, because it may still contain content the author meant to exclude.
    private void DiscardSitePdf(string outputPath)
    {
        _sitePdfSections.Remove(outputPath);
        if (File.Exists(outputPath)) File.Delete(outputPath);
    }

    private static IEnumerable<(MarkdownFile File, int Depth)> Flatten(string rootPath, IEnumerable<MarkdownFile> files, int depth = 0)
    {
        foreach (var file in files.OrderBy(file => file.NavigationOrder.HasValue ? 0 : 1)
                     .ThenBy(file => file.NavigationOrder)
                     .ThenBy(file => PdfSelection.GetTitle(rootPath, file), StringComparer.OrdinalIgnoreCase))
        {
            yield return (file, depth);
            foreach (var child in Flatten(rootPath, file.Children, depth + 1)) yield return child;
        }
    }

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
