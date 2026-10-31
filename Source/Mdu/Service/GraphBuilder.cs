using Core.Collections.NodeGraph.Extensions;
using Markdown.Renderer;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service.Visitors;

namespace Mdu.Service;

/// <summary>
/// Main Service to Process Markdown Files
/// </summary>
public class GraphBuilder
{

    private readonly DataPreparationVisitor _dataPreparationVisitor;
    private readonly IGitTreeSourceResolver _sourceResolver;

    public GraphBuilder(DataPreparationVisitor dataPreparationVisitor, IGitTreeSourceResolver? sourceResolver = null)
    {
        _dataPreparationVisitor = dataPreparationVisitor;
        _sourceResolver = sourceResolver ?? new GitTreeSourceResolver();
    }

    public MarkdownFile[] CreateGraph(IVisitorSettings settings, string? excludedMarkdownPath = null)
    {
        var searchOptions = new EnumerationOptions
        {
            AttributesToSkip = settings.IncludeHidden
                ? FileAttributes.System
                : FileAttributes.Hidden | FileAttributes.System,
            RecurseSubdirectories = false
        };

        var rootPath = Path.GetFullPath(settings.RootPath ?? Directory.GetCurrentDirectory());
        var searchPattern = settings.SearchPattern ?? Default.SearchPattern;

        var rootDirectory = new DirectoryInfo(rootPath);
        
        var graph = CreateGraph(rootDirectory, searchPattern, searchOptions, excludedMarkdownPath);
        graph.Visit(_dataPreparationVisitor);
        foreach (var page in Flatten(graph))
            page.SiteRelativePath = Path.GetRelativePath(rootPath, page.FileInfo.FullName);

        graph = ExpandImports(graph, searchPattern, searchOptions);
        ValidateSitePaths(graph);
        return graph;
    }

    private MarkdownFile[] ExpandImports(
        MarkdownFile[] pages,
        string searchPattern,
        EnumerationOptions searchOptions)
    {
        var result = pages.ToArray();
        for (var index = 0; index < result.Length; index++)
        {
            var page = result[index];
            if (!string.IsNullOrWhiteSpace(page.ImportSource))
            {
                result[index] = ImportPages(page, searchPattern, searchOptions);
                continue;
            }

            page.Children = ExpandImports(page.Children, searchPattern, searchOptions);
        }

        return result;
    }

    private MarkdownFile ImportPages(
        MarkdownFile importPage,
        string searchPattern,
        EnumerationOptions searchOptions)
    {
        var importBody = RemoveFrontMatter(importPage.ReadMarkdown(), importPage.FrontMatter).Trim();
        if (!string.IsNullOrWhiteSpace(importBody))
            throw new InvalidOperationException("An mdu.import page must contain front matter only; put its content in the imported README.md or index.md.");

        var source = _sourceResolver.Resolve(importPage.ImportSource!);
        var glossaryPath = ResolveImportedGlossaryPath(source, importPage.ImportSource!);
        var importedPages = GetMarkdownFilesAt(null, new DirectoryInfo(source.SourceFolderPath), searchPattern, searchOptions, glossaryPath).ToArray();
        if (importedPages.Length != 1 ||
            !IsSectionRoot(importedPages[0].FileInfo.Name))
            throw new InvalidOperationException(
                $"Imported folder '{source.SourcePath}' must contain a README.md or index.md at its root.");

        importedPages.Visit(_dataPreparationVisitor);
        if (Flatten(importedPages).Any(page => !string.IsNullOrWhiteSpace(page.ImportSource)))
            throw new InvalidOperationException("Imported documentation cannot contain another mdu.import declaration.");

        var importedRoot = importedPages[0];
        var importedMarkdown = importedRoot.ReadMarkdown();
        var importedFrontMatter = importedRoot.FrontMatter;
        importedRoot.MarkdownContent = importPage.FrontMatter + RemoveFrontMatter(importedMarkdown, importedFrontMatter);

        var mountDirectory = GetMountDirectory(importPage.SiteRelativePath!);
        var rootRelativePath = Path.Combine(mountDirectory, importedRoot.FileInfo.Name);
        importedRoot.ImportMarkerPath = importPage.FileInfo.FullName;
        importedRoot.Parent = importPage.Parent;

        foreach (var page in Flatten(importedPages))
        {
            var sourceRelativePath = Path.GetRelativePath(source.SourceFolderPath, page.FileInfo.FullName);
            page.SiteRelativePath = Path.Combine(mountDirectory, sourceRelativePath);
            page.EditPageLink = GetEditPageLink(source, page.FileInfo);
            page.SourceRootPath = source.RepositoryPath;
            page.GlossaryPath = glossaryPath;
        }

        importedRoot.SiteRelativePath = rootRelativePath;
        importedRoot.AliasRelativePath = IsSectionRoot(Path.GetFileName(importPage.SiteRelativePath!))
            ? null
            : Path.ChangeExtension(importPage.SiteRelativePath, ".html");
        _dataPreparationVisitor.Process(importedRoot, importPage.CountDepth());
        importedRoot.ImportSource = null;

        foreach (var page in Flatten(importedPages))
        {
            foreach (var specPath in OpenApiMarkdownRenderer.FindReferencedSpecPaths(RemoveFrontMatter(page.ReadMarkdown(), page.FrontMatter)))
                OpenApiMarkdownRenderer.ResolveSpecPath(page.FileInfo.FullName, specPath, source.RepositoryPath);
        }

        var localChildren = ExpandImports(importPage.Children, searchPattern, searchOptions);
        foreach (var child in localChildren) child.Parent = importedRoot;
        importedRoot.Children = importedRoot.Children.Concat(localChildren).ToArray();
        return importedRoot;
    }

    private static string? ResolveImportedGlossaryPath(ResolvedGitTreeSource source, string sourceUrl)
    {
        try
        {
            return SiteConfigurationReader.ResolveGlossaryPath(
                source.SourceFolderPath,
                SiteConfigurationReader.ReadForContent(source.SourceFolderPath));
        }
        catch (Exception exception) when (exception is YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Could not read the glossary of imported source '{sourceUrl}': {exception.Message}", exception);
        }
    }

    private static string GetMountDirectory(string importRelativePath)
    {
        var directory = Path.GetDirectoryName(importRelativePath) ?? string.Empty;
        var fileName = Path.GetFileName(importRelativePath);
        return IsSectionRoot(fileName)
            ? directory
            : Path.Combine(directory, Path.GetFileNameWithoutExtension(fileName));
    }

    private static bool IsSectionRoot(string fileName) =>
        fileName.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("index.md", StringComparison.OrdinalIgnoreCase);

    private static string GetEditPageLink(ResolvedGitTreeSource source, FileInfo file)
    {
        var relativePath = Path.GetRelativePath(source.RepositoryPath, file.FullName)
            .Replace(Path.DirectorySeparatorChar, '/');
        var filePath = string.Join('/', relativePath.Split('/').Select(Uri.EscapeDataString));
        return $"{source.EditPageLinkPrefix}/{filePath}";
    }

    private static string RemoveFrontMatter(string markdown, string frontMatter) =>
        !string.IsNullOrEmpty(frontMatter) && markdown.StartsWith(frontMatter, StringComparison.Ordinal)
            ? markdown[frontMatter.Length..].TrimStart('\r', '\n')
            : markdown;

    private static IEnumerable<MarkdownFile> Flatten(IEnumerable<MarkdownFile> pages)
    {
        foreach (var page in pages)
        {
            yield return page;
            foreach (var child in Flatten(page.Children)) yield return child;
        }
    }

    private static void ValidateSitePaths(IEnumerable<MarkdownFile> roots)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in Flatten(roots))
        {
            var relativePath = page.SiteRelativePath!.Replace(Path.DirectorySeparatorChar, '/');
            if (!paths.Add(relativePath))
                throw new InvalidOperationException($"More than one Markdown page maps to '{relativePath}'.");
        }
    }

    private MarkdownFile[] CreateGraph(
        DirectoryInfo directoryInfo,
        string searchPattern,
        EnumerationOptions searchOptions,
        string? excludedMarkdownPath)
    {
        return GetMarkdownFilesAt(null, directoryInfo, searchPattern, searchOptions, excludedMarkdownPath).ToArray();
    }

    private static IEnumerable<MarkdownFile> GetMarkdownFilesAt(
        MarkdownFile? parent,
        DirectoryInfo directoryInfo,
        string searchPattern,
        EnumerationOptions searchOptions,
        string? excludedMarkdownPath)
    {
        var markdownFilesInFolder = directoryInfo
            .GetFiles(searchPattern, searchOptions)
            .Where(fileInfo => excludedMarkdownPath is null || !PathsEqual(fileInfo.FullName, excludedMarkdownPath))
            .Select(fileInfo => new MarkdownFile(fileInfo, parent))
            .ToList();
        var usedParent = markdownFilesInFolder
            .FirstOrDefault(itm => itm.FileInfo.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase)) ??
            markdownFilesInFolder.FirstOrDefault(itm => itm.FileInfo.Name.Equals("index.md", StringComparison.OrdinalIgnoreCase));

        if (usedParent == null)
        {
            foreach (var page in markdownFilesInFolder) page.Parent = parent;
            return markdownFilesInFolder
                .Concat(directoryInfo.GetDirectories("*", searchOptions).SelectMany(child =>
                    GetMarkdownFilesAt(parent, child, searchPattern, searchOptions, excludedMarkdownPath)))
                .ToArray();
        }

        var siblingPages = markdownFilesInFolder
            .Where(file => file != usedParent)
            .ToArray();
        var directoryPages = directoryInfo
            .GetDirectories("*", searchOptions)
            .SelectMany(dirInfoOfChildren =>
                GetMarkdownFilesAt(usedParent, dirInfoOfChildren, searchPattern, searchOptions, excludedMarkdownPath)).ToArray();
        usedParent.IsOnlyReadmeInSection = siblingPages.Length == 0 && directoryPages.Length == 0;
        usedParent.Children = siblingPages.Concat(directoryPages).ToArray();

        foreach (var page in siblingPages)
            page.Parent = usedParent;

        return [usedParent];
    }

    private static bool PathsEqual(string left, string right) =>
        MarkdownLinks.PathComparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));

}
