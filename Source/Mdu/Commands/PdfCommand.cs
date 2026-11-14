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
    /// <summary>The tags of the site, shown below each page title. mdu render and mdu serve set them; mdu pdf loads them.</summary>
    public SiteTags Tags { get; set; } = SiteTags.Empty;
    /// <summary>The site's <c>content.code.annotate</c> setting. mdu render and mdu serve set it; mdu pdf reads it.</summary>
    public bool SiteCodeAnnotations { get; set; } = true;

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
        Markdown.Renderer.Res.Model.SiteConfiguration siteConfiguration;
        try
        {
            siteConfiguration = SiteConfigurationReader.ReadForContent(rootPath);
            SiteCodeAnnotations = siteConfiguration.CodeAnnotations;
            SiteConfigurationReader.UseCustomIcons(rootPath, siteConfiguration);
            SiteConfigurationReader.UseDiagramLimits(siteConfiguration);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or YamlException)
        {
            Console.Error.WriteLine($"Could not read the site configuration or custom icons: {exception.Message}");
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
        try
        {
            Tags = SiteTags.Load(rootPath, siteConfiguration, graph);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Could not read the tag vocabulary: {exception.Message}");
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
            var selected = files.ToArray();
            // Only the tags of the exported pages matter, not those of other pages in the folder.
            if (Tags.Report(rootPath, selected.Select(item => item.File))) return 1;
            return WritePdf(rootPath, outputPath, selected, includeFrontMatter: singleFilePath is null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Could not render PDF: {exception.Message}");
            return 1;
        }
    }

    private string? GetTagNames(MarkdownFile page) =>
        Tags.For(page) is { Count: > 0 } tags ? string.Join('\n', tags.Select(tag => tag.Name)) : null;

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
            var exported = files.Where(item => IsPdfVisible(item.File.FrontMatter)).ToArray();
            var exportedPages = exported.Select(item => item.File).ToHashSet();
            sections = exported
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
                    markdown = ExpandTagLists(rootPath, item.File, markdown, exportedPages);
                    return new PdfSourceSection(
                        PdfSelection.GetPdfTitle(rootPath, item.File, markdown, pdfRenderer.ExcludesTitleHeading),
                        GetRelativeHtmlPath(rootPath, item.File),
                        markdown,
                        item.Depth,
                        GetTagNames(item.File),
                        CodeAnnotations.ReadFrontMatter(item.File.FrontMatter) ?? SiteCodeAnnotations);
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
        internal bool Completed { get; set; }
    }

    public SitePdfJob CreateSitePdfJob(string outputPath, PdfSourceSection[] sections) =>
        new(outputPath, sections, pdfRenderer.CreateSession(Path.GetFileNameWithoutExtension(outputPath), sections));

    public bool AdvanceSitePdfJob(SitePdfJob job)
    {
        if (job.Completed) return true;
        try
        {
            if (job.Session.RenderNextSection()) return false;
            job.Failed = WritePreparedPdf(job.OutputPath, job.Sections, true, job.Session.WritePdf) != 0;
        }
        catch (Exception exception)
        {
            // A background PDF failure must terminate this job rather than retry a partially mutated session.
            job.Failed = true;
            job.Completed = true;
            Console.Error.WriteLine($"Could not write PDF: {exception.Message}");
            DiscardSitePdf(job.OutputPath);
        }
        job.Completed = true;
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

    // In the PDF, a tags block is a plain list. Pages of this export are linked within the document; the others are
    // named without a link, because the PDF does not contain them.
    private string ExpandTagLists(string rootPath, MarkdownFile page, string markdown, IReadOnlySet<MarkdownFile> exportedPages) =>
        TagListBlock.Expand(markdown, block => Tags.Check(block) is { } error
            ? TagListBlock.RenderError(error)
            : TagListBlock.Render(Tags.Select(block.Options!, page), block.Options!,
                TagListStyle.Pdf,
                target => !exportedPages.Contains(target) ? null : MarkdownLinks.GetRelativeUrl(
                    Path.GetDirectoryName(Path.Combine(rootPath, GetRelativeHtmlPath(rootPath, page)))!,
                    Path.Combine(rootPath, GetRelativeHtmlPath(rootPath, target))),
                _ => string.Empty, null));

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
        if (FrontMatterYaml.ReadMapping(frontMatter) is not { } metadata) return true;

        return FrontMatterYaml.FindSetting(metadata, "pdf", "visible") is not YamlScalarNode scalar ||
               !bool.TryParse(scalar.Value, out var visible) || visible;
    }
}
