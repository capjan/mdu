using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Markdown.Renderer;
using Markdown.Renderer.MarkdigExtension;
using Markdown.Renderer.Res.Model;
using Markdown.Search;
using Mdu.Model;
using Mdu.Service.PageTree;
using Mdu.Service.PageTree.Extensions;

namespace Mdu.Service.Visitors;

public class MarkdigVisitor : BaseVisitor
{
    public MarkdigVisitor(
        string rootPath, 
        string outPath, 
        HtmlRenderer htmlFoundation, 
        string editPageRoot,
        SearchIndexBuilder searchIndexBuilder,
        MarkdownFile[]? navigationRoots = null,
        IReadOnlyCollection<string>? siteMediaProviders = null,
        IReadOnlyDictionary<string, DateTimeOffset>? lastModifiedDates = null) : base(rootPath, outPath, ".html")
    {
        _rootPath = rootPath;
        _outPath = outPath;
        _htmlFoundation = htmlFoundation;
        _editPageRoot = editPageRoot;
        _searchIndexBuilder = searchIndexBuilder;
        _navigationRoots = navigationRoots ?? Array.Empty<MarkdownFile>();
        _topNavigationItems = new Dictionary<MarkdownFile, MarkdownFile?>();
        _pageOutputs = Flatten(_navigationRoots)
            .Select(page => (Page: page, OutputPath: GetOutputPagePath(GetRenderPath(page))))
            .ToArray();
        _pageOutputPaths = _pageOutputs
            .Select(item => item.OutputPath)
            .ToHashSet(PathComparer);
        foreach (var (page, _) in _pageOutputs)
        {
            _topNavigationItems.Add(page, FindTopNavigationItem(page));
        }
        _navigationEntries = CreateNavigationEntries(_navigationRoots);
        _siteMediaProviders = siteMediaProviders ?? FindSiteMediaProviders();
        _lastModifiedDates = lastModifiedDates ?? GitLastModified.Read(rootPath);
    }
    
    private readonly HtmlRenderer _htmlFoundation;
    private readonly string _editPageRoot;
    private readonly SearchIndexBuilder _searchIndexBuilder;
    private readonly string _rootPath;
    private readonly string _outPath;
    private readonly MarkdownFile[] _navigationRoots;
    private readonly IReadOnlyList<NavigationEntry> _navigationEntries;
    private readonly Dictionary<MarkdownFile, MarkdownFile?> _topNavigationItems;
    private readonly (MarkdownFile Page, string OutputPath)[] _pageOutputs;
    private readonly HashSet<string> _pageOutputPaths;
    private readonly IReadOnlyCollection<string> _siteMediaProviders;
    private readonly IReadOnlyDictionary<string, DateTimeOffset> _lastModifiedDates;
    private readonly Dictionary<MarkdownFile, IReadOnlyList<string>> _openApiSpecPagePaths = new();
    private readonly Dictionary<MarkdownFile, PreparedPage> _preparedPages = new();

    public IReadOnlyCollection<string> SiteMediaProviders => _siteMediaProviders;
    public IReadOnlyDictionary<MarkdownFile, IReadOnlyList<string>> OpenApiSpecPagePaths => _openApiSpecPagePaths;

    public void PrepareSearchIndex() => _navigationRoots.Visit(new SearchPreparationVisitor(this));

    public void PrepareNavigation()
    {
        // Generated navigation, scripts and styles live in _mdu; site pages must not be written there.
        var collision = _pageOutputPaths.FirstOrDefault(path => ReservedOutput.Contains(_outPath, path));
        if (collision is not null) throw ReservedOutput.Collision(_outPath, collision);
        var first = _navigationRoots.FirstOrDefault();
        if (first is null)
        {
            _htmlFoundation.PrepareNavigation([]);
            return;
        }
        // Built from the site root, so item URLs are site-root relative and match the entries by position.
        var items = BuildNavigation(first, Path.Combine(_outPath, "index.html"));
        var urls = new Dictionary<MarkdownFile, string>();
        for (var i = 0; i < items.Count; i++) urls.TryAdd(_navigationEntries[i].Page, items[i].Url);
        for (var i = 0; i < items.Count; i++)
            if (_topNavigationItems[_navigationEntries[i].Page] is { } owner && urls.TryGetValue(owner, out var ownerUrl))
                items[i].TopNavigationOwnerUrl = ownerUrl;
        _htmlFoundation.PrepareNavigation(items);
    }

    public void InvalidatePreparedPage(MarkdownFile page) => _preparedPages.Remove(page);

    public IReadOnlyList<string> GetPreparedSpecificationPagePaths(MarkdownFile page) =>
        _preparedPages.TryGetValue(page, out var prepared)
            ? prepared.Specifications.Select(spec => Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(GetOutputPagePath(GetRenderPath(page)))!, spec.FileName)))
                .Where(path => !_pageOutputPaths.Contains(path)).ToArray()
            : Array.Empty<string>();

    private PreparedPage PreparePage(MarkdownFile page, string outputFileName)
    {
        var markdown = OpenApiMarkdownRenderer.ExpandCodeBlocks(
            RemoveFrontMatter(page.ReadMarkdown(), page.FrontMatter), page.FileInfo.FullName,
            out var specifications, page.SourceRootPath, Path.GetFileName(outputFileName),
            GetOutputSiblings(_pageOutputs, Path.GetDirectoryName(outputFileName)!));
        _searchIndexBuilder.AddPage(NavigationPage.GetTitle(page), GetSiteRelativeUrl(outputFileName), markdown);
        return new PreparedPage(markdown, specifications);
    }

    private sealed record PreparedPage(string Markdown, IReadOnlyList<OpenApiSpecificationPage> Specifications);

    private sealed class SearchPreparationVisitor(MarkdigVisitor owner) : IVisitor<MarkdownFile>
    {
        public TraversalAlgorithm Algorithm => TraversalAlgorithm.Default;

        public bool Process(MarkdownFile page, int depth)
        {
            owner._preparedPages[page] = owner.PreparePage(page, GetOutputPagePath(owner.GetRenderPath(page)));
            return true;
        }
    }

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static IEnumerable<OpenApiSiblingPage> GetOutputSiblings(
        IEnumerable<(MarkdownFile Page, string OutputPath)> pageOutputs,
        string outputDirectory) =>
        pageOutputs
            .Where(item => string.Equals(Path.GetDirectoryName(item.OutputPath), outputDirectory, PathComparison))
            .Select(item => new OpenApiSiblingPage(
                item.Page.FileInfo.FullName,
                Path.GetFileName(item.OutputPath),
                item.Page.SourceRootPath));

    protected override bool ProcessFile(MarkdownFile entity, int _, FilePathInfo io)
    {
        Console.WriteLine($"{io.InputRelativeFilePathToRootDirectory} => {io.OutputRelativeFilePathToRootDirectory}");

        io.CreateOutputDirectory();    // ensures that the output directory exits

        var editPageLink = entity.EditPageLink ?? (string.IsNullOrEmpty(_editPageRoot)
            ? string.Empty
            : GetEditPageLink(io.InputRelativeFilePathToRootDirectory));
        var nestingDeep = Path.GetDirectoryName(io.OutputRelativeFilePathToRootDirectory)?
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length ?? 0;
        
        var outputFileName = io.OutputFullFilePath;
   
        if (Path.GetFileName(outputFileName).Equals("readme.html", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(outputFileName)) File.Delete(outputFileName);
            outputFileName = outputFileName.Substring(0, outputFileName.Length - 11) + "index.html";
        }

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputFileName))!;
        var prepared = _preparedPages.Remove(entity, out var pending) ? pending : PreparePage(entity, outputFileName);
        var markdown = prepared.Markdown;
        var specificationPages = prepared.Specifications;

        var navigation = BuildNavigation(entity, outputFileName);
        var breadcrumbs = BuildBreadcrumbs(entity, outputFileName);
        var topNavigation = BuildTopNavigation(entity, outputFileName);
        var pdfUrl = GetRelativeUrl(
            GetSiteRelativeSegments(Path.GetDirectoryName(Path.GetFullPath(outputFileName))!),
            ["documentation.pdf"]);
        var (pdfDownloadUrls, pdfDownloadNames) = GetPdfDownloads(markdown, entity, outputDirectory, pdfUrl);

        var temporaryPath = outputFileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var fs = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
            using (var sw = new StreamWriter(fs))
                _htmlFoundation.WriteHtml(
                    sw,
                    NavigationPage.RemoveIconShortcodes(entity.Title),
                    entity.Description,
                    nestingDeep,
                    markdown,
                    editPageLink,
                    navigation,
                    breadcrumbs,
                    _siteMediaProviders,
                    entity.Icon,
                    topNavigation,
                    GetLastModified(entity),
                    pdfUrl,
                    Dashboard.ReadFrontMatter(entity.FrontMatter),
                    pdfDownloadUrls,
                    pdfDownloadNames,
                    GetGlossaryScope(entity),
                    ImageZoom.ReadFrontMatter(entity.FrontMatter),
                    pageNavigation: PageNavigation.ReadFrontMatter(entity.FrontMatter)).GetAwaiter().GetResult();

            File.Move(temporaryPath, outputFileName, true);
            WriteAliasPage(entity, outputFileName);
            _openApiSpecPagePaths[entity] = WriteSpecificationPages(
                entity,
                outputFileName,
                nestingDeep,
                navigation,
                breadcrumbs,
                topNavigation,
                pdfUrl,
                specificationPages);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }

        return true;
    }

    private (IReadOnlyDictionary<string, string> Urls, IReadOnlyDictionary<string, string> Names) GetPdfDownloads(
        string markdown, MarkdownFile entity, string outputDirectory, string pdfUrl)
    {
        var urls = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var url in _htmlFoundation.FindPdfLinks(markdown))
        {
            string? path;
            MarkdownFile selected;
            try
            {
                path = LinkReplacer.GetPdfRoot(url);
                if (path is null)
                {
                    urls[url] = pdfUrl;
                    names[url] = "documentation";
                    continue;
                }
                selected = PdfSelection.FindPage(_rootPath, _navigationRoots, path, entity);
            }
            catch (InvalidOperationException)
            {
                // The PDF step reports invalid roots and fails mdu render; mdu serve keeps the page with the link unresolved.
                continue;
            }
            urls[url] = GetRelativeUrl(
                GetSiteRelativeSegments(outputDirectory),
                PdfSelection.GetOutputPath(_rootPath, selected).Split('/'));
            var pdfTitle = PdfSelection.GetPdfTitle(_rootPath, selected,
                RemoveFrontMatter(selected.ReadMarkdown(), selected.FrontMatter), _htmlFoundation.ExcludesTitleHeadingFromPdf);
            var title = Regex.Replace(NavigationPage.RemoveIconShortcodes(pdfTitle),
                @":[\w+-]+:", match => ShortcodeExtension.Emojis.ContainsKey(match.Value) ? string.Empty : match.Value)
                .Trim();
            title = Regex.Replace(title, @"\s+", " ");
            names[url] = title.Length > 0 ? title : PdfSelection.GetFallbackTitle(_rootPath, selected);
        }
        return (urls, names);
    }

    private GlossaryScope? GetGlossaryScope(MarkdownFile page) =>
        page.Glossary is null ? null : _htmlFoundation.GetGlossaryScope(page.Glossary);

    private IReadOnlyList<string> WriteSpecificationPages(
        MarkdownFile entity,
        string outputFileName,
        int nestingDeep,
        IReadOnlyList<NavigationItem> navigation,
        IReadOnlyList<BreadcrumbItem> pageBreadcrumbs,
        IReadOnlyList<NavigationItem> topNavigation,
        string pdfUrl,
        IReadOnlyList<OpenApiSpecificationPage> specificationPages)
    {
        if (specificationPages.Count == 0) return Array.Empty<string>();

        var parentDirectory = Path.GetDirectoryName(outputFileName)!;
        var parentFile = Path.GetFileName(outputFileName);
        var generatedPagePaths = new List<string>(specificationPages.Count);
        var specBreadcrumbs = pageBreadcrumbs
            .Where(item => item.Url is not null)
            .Append(new BreadcrumbItem(NavigationPage.GetTitle(entity), parentFile))
            .Append(new BreadcrumbItem("OpenAPI Specification", null))
            .ToArray();

        foreach (var specPage in specificationPages)
        {
            var (pdfDownloadUrls, pdfDownloadNames) = GetPdfDownloads(specPage.Markdown, entity, parentDirectory, pdfUrl);
            var specOutputPath = Path.GetFullPath(Path.Combine(parentDirectory, specPage.FileName));
            if (_pageOutputPaths.Contains(specOutputPath)) continue;
            var temporaryPath = specOutputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var fs = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
                using (var sw = new StreamWriter(fs))
                    _htmlFoundation.WriteHtml(
                        sw,
                        specPage.Title,
                        string.Empty,
                        nestingDeep,
                        specPage.Markdown,
                        string.Empty,
                        navigation,
                        specBreadcrumbs,
                        _siteMediaProviders,
                        string.Empty,
                        topNavigation,
                        GetLastModified(entity),
                        pdfUrl,
                        pdfDownloadUrls: pdfDownloadUrls,
                        pdfDownloadNames: pdfDownloadNames,
                        glossaryScope: GetGlossaryScope(entity),
                        imageZoom: ImageZoom.ReadFrontMatter(entity.FrontMatter)).GetAwaiter().GetResult();

                File.Move(temporaryPath, specOutputPath, true);
                generatedPagePaths.Add(specOutputPath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        return generatedPagePaths;
    }

    private void WriteAliasPage(MarkdownFile page, string outputFileName)
    {
        if (!string.IsNullOrWhiteSpace(page.AliasRelativePath))
        {
            var importAliasPath = Path.GetFullPath(Path.Combine(_outPath, page.AliasRelativePath));
            WriteRedirect(importAliasPath, outputFileName);
            return;
        }

        if (page.FileInfo.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase)) return;
        var aliasDirectory = Path.Combine(page.FileInfo.DirectoryName!, Path.GetFileNameWithoutExtension(page.FileInfo.Name));
        if (Directory.Exists(aliasDirectory) && Directory.EnumerateFiles(aliasDirectory).Any(filePath =>
                Path.GetFileName(filePath).Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(filePath).Equals("index.md", StringComparison.OrdinalIgnoreCase))) return;

        var aliasPath = Path.Combine(Path.GetDirectoryName(outputFileName)!, Path.GetFileNameWithoutExtension(outputFileName), "index.html");
        WriteRedirect(aliasPath, outputFileName);
    }

    private static void WriteRedirect(string aliasPath, string outputFileName)
    {
        var relativeTarget = Path.GetRelativePath(Path.GetDirectoryName(aliasPath)!, outputFileName)
            .Replace(Path.DirectorySeparatorChar, '/');
        relativeTarget = string.Join('/', relativeTarget.Split('/').Select(segment => segment == ".." ? segment : Uri.EscapeDataString(segment)));
        var encodedTarget = HtmlEncoder.Default.Encode(relativeTarget);
        Directory.CreateDirectory(Path.GetDirectoryName(aliasPath)!);
        File.WriteAllText(aliasPath,
            $"<!doctype html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"refresh\" content=\"0; url={encodedTarget}\"><link rel=\"canonical\" href=\"{encodedTarget}\"><title>Moved</title></head><body><a href=\"{encodedTarget}\">Continue to the page</a></body></html>");
    }

    private DateTimeOffset? GetLastModified(MarkdownFile page)
    {
        if (page.SourceRootPath is not null) return null;
        var relativePath = Path.GetRelativePath(_rootPath, page.FileInfo.FullName).Replace(Path.DirectorySeparatorChar, '/');
        return _lastModifiedDates.TryGetValue(relativePath, out var date) ? date : null;
    }

    private IReadOnlyCollection<string> FindSiteMediaProviders()
    {
        var providers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in Flatten(_navigationRoots))
        {
            var markdown = RemoveFrontMatter(page.ReadMarkdown(), page.FrontMatter);
            providers.UnionWith(_htmlFoundation.FindMediaProviders(markdown));
        }

        return new[] { "YouTube", "Vimeo" }.Where(providers.Contains).ToArray();
    }

    private static string RemoveFrontMatter(string markdown, string frontMatter)
    {
        if (!string.IsNullOrEmpty(frontMatter) && markdown.StartsWith(frontMatter, StringComparison.Ordinal))
            return markdown[frontMatter.Length..].TrimStart('\r', '\n');

        return markdown;
    }

    private static IEnumerable<MarkdownFile> Flatten(IEnumerable<MarkdownFile> pages)
    {
        foreach (var page in pages)
        {
            yield return page;
            foreach (var child in Flatten(page.Children))
                yield return child;
        }
    }

    private IReadOnlyList<NavigationItem> BuildNavigation(MarkdownFile page, string currentOutputPath)
    {
        var currentPath = Path.GetFullPath(currentOutputPath);
        var currentDirectorySegments = GetSiteRelativeSegments(Path.GetDirectoryName(currentPath)!);
        var entries = _navigationEntries;
        var activeTopNavigationItem = _topNavigationItems.GetValueOrDefault(page);
        var activeTopNavigationDepth = activeTopNavigationItem is null
            ? -1
            : entries.FirstOrDefault(entry => ReferenceEquals(entry.Page, activeTopNavigationItem))?.Depth ?? -1;
        if (activeTopNavigationDepth < 0) activeTopNavigationItem = null;
        return entries
            .Select(entry => new NavigationItem
            {
                Title = entry.Title,
                Url = GetRelativeUrl(currentDirectorySegments, entry.UrlSegments),
                Depth = entry.Depth,
                IsCurrent = string.Equals(entry.OutputPath, currentPath, PathComparison),
                IsCurrentSection = ReferenceEquals(page, entry.Page) || IsDescendant(page, entry.Page),
                IsExpandedByDefault = entry.IsExpandedByDefault,
                IsTopNavigationItem = IsTopNavigationPage(entry.Page),
                IsOutsideTopNavigationSection = _topNavigationItems[entry.Page] is null,
                IsInCurrentTopSection = activeTopNavigationItem is not null &&
                                        ReferenceEquals(_topNavigationItems[entry.Page], activeTopNavigationItem),
                IsAncestorOfCurrentTopSection = activeTopNavigationItem is not null &&
                                                IsDescendant(activeTopNavigationItem, entry.Page),
                TopNavigationDepth = activeTopNavigationDepth < 0
                    ? 0
                    : entry.Depth - activeTopNavigationDepth - 1
            })
            .ToArray();
    }

    private IReadOnlyList<NavigationItem> BuildTopNavigation(MarkdownFile page, string currentOutputPath)
    {
        var currentPath = Path.GetFullPath(currentOutputPath);
        var currentDirectorySegments = GetSiteRelativeSegments(Path.GetDirectoryName(currentPath)!);
        var activeTab = _topNavigationItems.GetValueOrDefault(page);
        return NavigationPage.OrderPages(Flatten(_navigationRoots).Where(IsTopNavigationPage))
            .Select(candidate =>
            {
                var candidatePath = GetOutputPagePath(GetRenderPath(candidate));
                var isCurrentPage = string.Equals(candidatePath, currentPath, PathComparison);
                return new NavigationItem
                {
                    Title = NavigationPage.GetTitle(candidate),
                    Url = GetRelativeUrl(currentDirectorySegments, GetSiteRelativeSegments(candidatePath)),
                    IsCurrent = isCurrentPage,
                    IsCurrentSection = ReferenceEquals(candidate, activeTab)
                };
            })
            .ToArray();
    }

    private IReadOnlyList<NavigationEntry> CreateNavigationEntries(IEnumerable<MarkdownFile> roots)
    {
        var entries = new List<NavigationEntry>();
        AddPages(roots, 0);
        return entries;

        void AddPages(IEnumerable<MarkdownFile> pages, int depth)
        {
            foreach (var page in NavigationPage.OrderPages(pages))
            {
                if (page.NavigationVisible)
                {
                    var pagePath = GetOutputPagePath(GetRenderPath(page));
                    entries.Add(new NavigationEntry(page, NavigationPage.GetTitle(page), pagePath, GetSiteRelativeSegments(pagePath), depth,
                        page.NavigationExpanded == true));
                }

                AddPages(page.Children, page.NavigationVisible ? depth + 1 : depth);
            }
        }
    }

    private string[] GetSiteRelativeSegments(string path)
    {
        var relativePath = Path.GetRelativePath(_outPath, path);
        return relativePath == "." ? Array.Empty<string>() : relativePath.Split(Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Returns the URL of a page relative to a directory, both given as path segments relative to the site root;
    /// the same result as <see cref="Path.GetRelativePath"/> with escaped segments, without touching the file system.
    /// </summary>
    private static string GetRelativeUrl(string[] fromDirectory, string[] toFile)
    {
        var common = 0;
        while (common < fromDirectory.Length && common < toFile.Length - 1 &&
               string.Equals(fromDirectory[common], toFile[common], PathComparison))
            common++;

        return string.Join('/', Enumerable.Repeat("..", fromDirectory.Length - common)
            .Concat(toFile.Skip(common).Select(Uri.EscapeDataString)));
    }

    private sealed record NavigationEntry(MarkdownFile Page, string Title, string OutputPath, string[] UrlSegments, int Depth, bool IsExpandedByDefault);

    private static bool IsDescendant(MarkdownFile page, MarkdownFile ancestor)
    {
        for (var current = page.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor)) return true;
        }

        return false;
    }

    private MarkdownFile? FindTopNavigationItem(MarkdownFile page)
    {
        for (var current = page; current is not null; current = current.Parent)
        {
            if (IsTopNavigationPage(current)) return current;
        }

        return null;
    }

    private bool IsTopNavigationPage(MarkdownFile page) =>
        page.NavigationVisible &&
        page.NavigationPlacement == NavigationPlacement.Top &&
        NavigationPage.IsTopLevel(page, _rootPath);

    private IReadOnlyList<BreadcrumbItem> BuildBreadcrumbs(MarkdownFile page, string currentOutputPath)
    {
        var breadcrumbs = new List<BreadcrumbItem>();
        var ancestors = new List<MarkdownFile>();
        for (var parent = page.Parent; parent is not null; parent = parent.Parent)
            ancestors.Add(parent);
        ancestors.Reverse();

        var currentDirectory = Path.GetDirectoryName(Path.GetFullPath(currentOutputPath))!;
        foreach (var ancestor in ancestors.Where(ancestor => ancestor.NavigationVisible))
        {
            var ancestorPath = GetRenderPath(ancestor);
            breadcrumbs.Add(new BreadcrumbItem(
                NavigationPage.GetTitle(ancestor),
                GetRelativeUrl(currentDirectory, GetOutputPagePath(ancestorPath))));
        }

        if (page.NavigationVisible)
            breadcrumbs.Add(new BreadcrumbItem(NavigationPage.GetTitle(page), null));
        return breadcrumbs;
    }

    private static string GetOutputPagePath(string pagePath)
    {
        if (Path.GetFileName(pagePath).Equals("readme.html", StringComparison.OrdinalIgnoreCase))
            pagePath = pagePath[..^"readme.html".Length] + "index.html";

        return Path.GetFullPath(pagePath);
    }

    private string GetRenderPath(MarkdownFile page) =>
        FilePathInfo.CreateRenderFileInfo(_rootPath, _outPath, page.FileInfo, ".html", page.SiteRelativePath)
            .OutputFullFilePath;

    private static string GetRelativeUrl(string currentDirectory, string pagePath)
    {
        var relativeUrl = Path.GetRelativePath(currentDirectory, pagePath)
            .Replace(Path.DirectorySeparatorChar, '/');
        return string.Join('/', relativeUrl.Split('/').Select(Uri.EscapeDataString));
    }

    private string GetSiteRelativeUrl(string pagePath)
    {
        var relativePath = Path.GetRelativePath(_outPath, pagePath)
            .Replace(Path.DirectorySeparatorChar, '/');
        return string.Join('/', relativePath.Split('/').Select(Uri.EscapeDataString));
    }

    private string GetEditPageLink(string relativePath)
    {
        var segments = relativePath.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString);
        return $"{_editPageRoot.TrimEnd('/')}/{string.Join('/', segments)}";
    }

}
