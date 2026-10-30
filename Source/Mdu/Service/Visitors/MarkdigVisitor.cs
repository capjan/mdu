using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Markdown.Renderer;
using Markdown.Renderer.MarkdigExtension;
using Markdown.Renderer.Res.Model;
using Markdown.Search;
using Mdu.Model;

namespace Mdu.Service.Visitors;

public class MarkdigVisitor : BaseVisitor
{
    public MarkdigVisitor(
        string rootPath, 
        string outPath, 
        HtmlRenderer htmlFoundation, 
        string editPageRoot,
        SearchIndexBuilder searchIndexBuilder,
        MarkdownFile[]? navigationRoots = null) : base(rootPath, outPath, ".html")
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
        _siteMediaProviders = FindSiteMediaProviders();
        _lastModifiedDates = GitLastModified.Read(rootPath);
    }
    
    private readonly HtmlRenderer _htmlFoundation;
    private readonly string _editPageRoot;
    private readonly SearchIndexBuilder _searchIndexBuilder;
    private readonly string _rootPath;
    private readonly string _outPath;
    private readonly MarkdownFile[] _navigationRoots;
    private readonly Dictionary<MarkdownFile, MarkdownFile?> _topNavigationItems;
    private readonly (MarkdownFile Page, string OutputPath)[] _pageOutputs;
    private readonly HashSet<string> _pageOutputPaths;
    private readonly IReadOnlyCollection<string> _siteMediaProviders;
    private readonly IReadOnlyDictionary<string, DateTimeOffset> _lastModifiedDates;
    private readonly Dictionary<MarkdownFile, IReadOnlyList<string>> _openApiSpecPagePaths = new();

    public IReadOnlyCollection<string> SiteMediaProviders => _siteMediaProviders;
    public IReadOnlyDictionary<MarkdownFile, IReadOnlyList<string>> OpenApiSpecPagePaths => _openApiSpecPagePaths;

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
   
        if (outputFileName.EndsWith("readme.html", StringComparison.InvariantCultureIgnoreCase))
        {
            if (File.Exists(outputFileName)) File.Delete(outputFileName);
            outputFileName = outputFileName.Substring(0, outputFileName.Length - 11) + "index.html";
        }

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputFileName))!;
        var outputSiblings = GetOutputSiblings(_pageOutputs, outputDirectory);
        var markdown = OpenApiMarkdownRenderer.ExpandCodeBlocks(
            RemoveFrontMatter(entity.ReadMarkdown(), entity.FrontMatter),
            entity.FileInfo.FullName,
            out var specificationPages,
            entity.SourceRootPath,
            Path.GetFileName(outputFileName),
            outputSiblings);

        _searchIndexBuilder.AddPage(
            NavigationPage.GetTitle(entity),
            GetSiteRelativeUrl(outputFileName),
            markdown);

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
                    pdfDownloadNames).GetAwaiter().GetResult();

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
            var path = LinkReplacer.GetPdfRoot(url);
            if (path is null)
            {
                urls[url] = pdfUrl;
                names[url] = "documentation";
                continue;
            }
            var selected = PdfSelection.FindPage(_rootPath, _navigationRoots, path, entity);
            urls[url] = GetRelativeUrl(
                GetSiteRelativeSegments(outputDirectory),
                PdfSelection.GetOutputPath(_rootPath, selected).Split('/'));
            var title = Regex.Replace(NavigationPage.RemoveIconShortcodes(selected.Title),
                @":[\w+-]+:", match => ShortcodeExtension.Emojis.ContainsKey(match.Value) ? string.Empty : match.Value)
                .Trim();
            if (title.Length == 0)
            {
                title = Path.GetFileNameWithoutExtension(selected.FileInfo.Name);
                if (selected.FileInfo.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase))
                {
                    var directory = Path.GetDirectoryName(PdfSelection.GetSitePath(_rootPath, selected));
                    title = string.IsNullOrEmpty(directory) ? new DirectoryInfo(_rootPath).Name : Path.GetFileName(directory);
                }
            }
            names[url] = title;
        }
        return (urls, names);
    }

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
                        pdfDownloadNames: pdfDownloadNames).GetAwaiter().GetResult();

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
        var entries = CreateNavigationEntries(_navigationRoots);
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
        IsTopLevelNavigationPage(page);

    private bool IsTopLevelNavigationPage(MarkdownFile page)
    {
        var relativePath = page.SiteRelativePath ?? Path.GetRelativePath(_rootPath, page.FileInfo.FullName);
        if (Path.IsPathRooted(relativePath) || relativePath == ".." ||
            relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return false;

        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Length == 1) return true;
        return segments.Length == 2 &&
               (page.FileInfo.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
                page.FileInfo.Name.Equals("index.md", StringComparison.OrdinalIgnoreCase));
    }

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
        if (pagePath.EndsWith("readme.html", StringComparison.OrdinalIgnoreCase))
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
