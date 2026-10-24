using Core.Collections.NodeGraph;
using Markdown.Document;
using Markdown.Document.Model;
using Markdown.Linter;
using Markdown.Linter.Rules.Rule2;
using Markdown.Linter.Rules.Rule3;
using Markdown.Linter.Rules.Rule4;
using Markdig;
using Markdig.Extensions.Footnotes;
using Mdu.Model;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Mdu.Service.Visitors;

public class LinterVisitor: IVisitor<MarkdownFile>
{
    private static readonly MarkdownPipeline FootnotePipeline =
        new MarkdownPipelineBuilder().UseFootnotes().UsePreciseSourceLocation().Build();

    public readonly ILinterErrorReceiver ErrorReceiver;
    private readonly Rule2Validator _rule2Validator = new();
    private readonly Rule3Validator _rule3Validator = new();
    private readonly Rule4Validator _rule4Validator = new();
    private readonly List<MarkdownFile> _pages = new();
    private IReadOnlyList<MarkdownFile> _allPages = Array.Empty<MarkdownFile>();
    private readonly HashSet<string> _linkedPages = new(PathComparer);
    private string _rootPath = Directory.GetCurrentDirectory();

    private static StringComparer PathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public IReadOnlyList<MarkdownFile> OrphanedPages => _pages
        .Where(page => !page.NavigationVisible && !_linkedPages.Contains(Path.GetFullPath(page.FileInfo.FullName)))
        .OrderBy(page => page.FileInfo.FullName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public LinterVisitor(ILinterErrorReceiver errorReceiver)
    {
        ErrorReceiver = errorReceiver;
    }

    public void Prepare(MarkdownFile[] graph, string? rootPath = null)
    {
        _rootPath = Path.GetFullPath(rootPath ?? graph.FirstOrDefault()?.FileInfo.DirectoryName ?? Directory.GetCurrentDirectory());
        var pages = new List<MarkdownFile>();
        var visited = new HashSet<string>(PathComparer);
        Visit(graph);
        _allPages = pages;
        ReportMissingSectionReadmes(pages);
        ReportMkDocsPagesFiles(pages);
        return;

        void Visit(IEnumerable<MarkdownFile> currentPages)
        {
            foreach (var page in currentPages)
            {
                if (!visited.Add(Path.GetFullPath(page.FileInfo.FullName))) continue;
                pages.Add(page);
                Visit(page.Children);
            }
        }
    }

    private void ReportMissingSectionReadmes(IReadOnlyCollection<MarkdownFile> pages)
    {
        var pagePaths = pages.Select(page => Path.GetFullPath(page.FileInfo.FullName)).ToHashSet(PathComparer);
        var directories = pages.Select(page => page.FileInfo.Directory)
            .Where(directory => directory is not null)
            .Cast<DirectoryInfo>()
            .DistinctBy(directory => Path.GetFullPath(directory.FullName), PathComparer);

        foreach (var directory in directories)
        {
            var directMarkdown = Directory.EnumerateFiles(directory.FullName)
                .Where(path => IsMarkdownFile(Path.GetExtension(path))).ToArray();
            var childSections = Directory.GetDirectories(directory.FullName)
                .Any(child => pagePaths.Any(path => SamePath(Path.GetDirectoryName(path), child)));
            if (!childSections && directMarkdown.Length < 2) continue;
            if (directMarkdown.Any(path => Path.GetFileName(path).Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
                                           Path.GetFileName(path).Equals("index.md", StringComparison.OrdinalIgnoreCase))) continue;

            ErrorReceiver.AddWarning(directory.FullName, 1,
                $"{RuleCodes.MissingSectionReadme}: Section '{directory.Name}' needs a README.md page; run 'mdu fix'.");
        }
    }

    private void ReportMkDocsPagesFiles(IReadOnlyCollection<MarkdownFile> pages)
    {
        var directories = pages.Select(page => page.FileInfo.Directory).Where(directory => directory is not null)
            .Cast<DirectoryInfo>().DistinctBy(directory => Path.GetFullPath(directory.FullName), PathComparer);
        foreach (var file in directories.SelectMany(directory => Directory.EnumerateFiles(directory.FullName, ".pages")))
            ErrorReceiver.Add(file, 1, $"{RuleCodes.MkDocsPagesFile}: MkDocs .pages configuration must be migrated; run 'mdu fix'.");
    }

    public TraversalAlgorithm Algorithm { get; }
    
    public bool Process(MarkdownFile entity, int graphDepth)
    {
        _pages.Add(entity);
        var markdownFilePath = entity.FileInfo.FullName;
        var markdownContent = entity.ReadMarkdown();
        ReportUnusedFootnotes(entity.FileInfo.FullName, markdownContent);
        ReportLegacySidebarMetadata(entity);
        ReportInvalidNavigationMetadata(entity);
        foreach (var issue in MarkdownCompatibility.FindIssues(markdownContent))
            ErrorReceiver.Add(markdownFilePath, issue.LineNumber, $"{issue.Code}: {issue.Message}");

        if (entity.InvalidNavigationPlacement is not null)
        {
            ErrorReceiver.Add(markdownFilePath, 1,
                $"{RuleCodes.InvalidNavigationPlacement}: navigation.placement must be 'sidebar' or 'top', not '{entity.InvalidNavigationPlacement}'.");
        }
        else if (entity.NavigationPlacement == NavigationPlacement.Top)
        {
            if (!IsTopLevelNavigationPage(entity))
                ErrorReceiver.Add(markdownFilePath, 1,
                    $"{RuleCodes.NavigationPlacementLevel}: navigation.placement: top is only valid on a top-level page or a top-level section README.md/index.md.");
            if (entity.NavigationExpanded.HasValue)
                ErrorReceiver.Add(markdownFilePath, 1,
                    $"{RuleCodes.ExpandedTopNavigationRoot}: navigation.expanded has no effect on a top navigation root, which is always expanded.");
        }

        if (entity.FileInfo.Name.Equals("index.md", StringComparison.OrdinalIgnoreCase))
            ErrorReceiver.AddWarning(markdownFilePath, 1, $"{RuleCodes.MkDocsIndexPage}: MkDocs index.md pages should be renamed to README.md; run 'mdu fix'.");

        var title = string.IsNullOrWhiteSpace(entity.HeadingTitle) ? entity.Title : entity.HeadingTitle;
        if (string.IsNullOrWhiteSpace(title))
        {
            ErrorReceiver.Add(markdownFilePath, 1, $"{RuleCodes.HeadingRequired}: Page must start with a level-one heading that defines its title.");
        }

        // Rule 2: The title of a markdown file must match the folder name 
        if (graphDepth > 0 && entity.FileInfo.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(entity.NavigationTitle))
        {
            var directoryName = entity.FileInfo.Directory?.Name ?? "";
            var rule2Input = new Rule2Input(title, directoryName);
            var result = _rule2Validator.Validate(rule2Input);
            ProcessValidationResult(markdownFilePath, 1, result);

            if (entity.IsOnlyReadmeInSection && !HasMatchingMarkdownFileBeside(entity.FileInfo.Directory))
            {
                ErrorReceiver.AddWarning(
                    markdownFilePath,
                    1,
                    $"{RuleCodes.ReadmeWithoutSubpages}: This section has no Markdown subpages; '{directoryName}.md' would be sufficient.");
            }
        }
        else if (!entity.FileInfo.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase))
        {
            var readmePath = FindReadmeInMatchingDirectory(entity.FileInfo);
            if (readmePath is not null)
            {
                ErrorReceiver.Add(
                    markdownFilePath,
                    1,
                    $"{RuleCodes.DuplicatePageRepresentation}: '{entity.FileInfo.Name}' and '{Path.GetRelativePath(entity.FileInfo.Directory!.FullName, readmePath)}' represent the same page. Keep only one.");
            }
        }
        
        // Rule 3: casing of links must match the casing on the current machine
        var linksOfFile = MarkdownDocument.ReadLinks(markdownContent);
        foreach (var link in linksOfFile)
        {
            if (entity.NavigationVisible && !link.IsImageLink && TryResolvePageLink(entity, link.Destination, out var targetPath))
                _linkedPages.Add(targetPath);

            var rule3Input = new Rule3Input(entity.FileInfo.FullName, link.Destination);
            var casingResult = _rule3Validator.Validate(rule3Input);
            ProcessValidationResult(markdownFilePath, link.LineNumber, casingResult);

            // A casing mismatch already explains why the target cannot be resolved on this machine.
            if (!casingResult.IsValid) continue;

            var linkResult = _rule4Validator.Validate(new Rule4Input(entity.FileInfo.FullName, link.Destination));
            ProcessValidationResult(markdownFilePath, link.LineNumber, linkResult);
        }
        
        return true;
    }

    private void ReportUnusedFootnotes(string filePath, string markdownContent)
    {
        var document = Markdig.Markdown.Parse(markdownContent, FootnotePipeline);
        var definitions = Markdig.Syntax.LinkReferenceDefinitionExtensions
            .GetLinkReferenceDefinitions(document, addGroup: false);
        if (definitions is null) return;

        foreach (var definition in definitions.Links.Values.OfType<FootnoteLinkReferenceDefinition>())
        {
            var footnote = definition.Footnote;
            if (footnote.Links.Count > 0) continue;

            ErrorReceiver.AddWarning(filePath, footnote.Line + 1,
                $"{RuleCodes.UnusedFootnote}: Footnote '[^{footnote.Label}]' is defined but never referenced.");
        }
    }

    private void ReportLegacySidebarMetadata(MarkdownFile page)
    {
        if (string.IsNullOrWhiteSpace(page.FrontMatter)) return;
        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(StripFrontMatterDelimiters(page.FrontMatter)));
            if (yaml.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode metadata) return;

            var hasSidebarKey = metadata.Children.Keys.OfType<YamlScalarNode>().Any(key =>
                key.Value?.Equals("sidebar", StringComparison.Ordinal) == true ||
                key.Value?.StartsWith("sidebar.", StringComparison.Ordinal) == true);
            if (hasSidebarKey)
                ErrorReceiver.Add(page.FileInfo.FullName, 1,
                    $"{RuleCodes.LegacySidebarMetadata}: replace the old sidebar front matter with navigation metadata.");
        }
        catch (YamlException)
        {
        }
    }

    private void ReportInvalidNavigationMetadata(MarkdownFile page)
    {
        if (string.IsNullOrWhiteSpace(page.FrontMatter)) return;
        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(StripFrontMatterDelimiters(page.FrontMatter)));
            if (yaml.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode metadata ||
                !metadata.Children.TryGetValue(new YamlScalarNode("navigation"), out var navigationNode) ||
                navigationNode is YamlMappingNode)
                return;

            ErrorReceiver.Add(page.FileInfo.FullName, 1,
                $"{RuleCodes.InvalidNavigationPlacement}: navigation front matter must be a mapping.");
        }
        catch (YamlException)
        {
        }
    }

    private static string StripFrontMatterDelimiters(string frontMatter)
    {
        var lines = frontMatter.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[0].Trim() == "---") lines.RemoveAt(0);
        if (lines.Count > 0 && lines[^1].Trim() == "---") lines.RemoveAt(lines.Count - 1);
        return string.Join(Environment.NewLine, lines);
    }

    private bool IsTopLevelNavigationPage(MarkdownFile page)
    {
        var file = page.FileInfo;
        var relativePath = page.SiteRelativePath ?? Path.GetRelativePath(_rootPath, file.FullName);
        if (Path.IsPathRooted(relativePath) || relativePath == ".." ||
            relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return false;

        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Length == 1) return true;
        return segments.Length == 2 &&
               (file.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
                file.Name.Equals("index.md", StringComparison.OrdinalIgnoreCase));
    }

    private bool HasMatchingMarkdownFileBeside(DirectoryInfo? sectionDirectory)
    {
        var parentDirectory = sectionDirectory?.Parent;
        if (sectionDirectory is null || parentDirectory is null) return false;

        return _allPages.Any(page =>
            IsMarkdownFile(page.FileInfo.Extension) &&
            !page.FileInfo.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase) &&
            SamePath(page.FileInfo.DirectoryName, parentDirectory.FullName) &&
            Path.GetFileNameWithoutExtension(page.FileInfo.Name).Equals(sectionDirectory.Name, StringComparison.OrdinalIgnoreCase));
    }

    private string? FindReadmeInMatchingDirectory(FileInfo pageFile)
    {
        var parentDirectory = pageFile.Directory;
        if (parentDirectory is null || !IsMarkdownFile(pageFile.Extension)) return null;

        var pageName = Path.GetFileNameWithoutExtension(pageFile.Name);
        return _allPages
            .Where(page => page.FileInfo.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(page =>
                SamePath(page.FileInfo.Directory?.Parent?.FullName, parentDirectory.FullName) &&
                page.FileInfo.Directory?.Name.Equals(pageName, StringComparison.OrdinalIgnoreCase) == true)
            ?.FileInfo.FullName;
    }

    private static bool SamePath(string? left, string right) =>
        left is not null && Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsMarkdownFile(string extension) =>
        extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase);

    private static bool TryResolvePageLink(MarkdownFile source, string destination, out string targetPath)
    {
        targetPath = string.Empty;
        if (string.IsNullOrWhiteSpace(destination) || destination.StartsWith('#') ||
            destination.StartsWith("//", StringComparison.Ordinal) ||
            System.Text.RegularExpressions.Regex.IsMatch(destination, @"^[A-Za-z][A-Za-z0-9+.-]*:"))
            return false;

        var suffixStart = destination.IndexOfAny(['?', '#']);
        var relativePath = suffixStart < 0 ? destination : destination[..suffixStart];
        if (string.IsNullOrWhiteSpace(relativePath)) return false;

        try
        {
            relativePath = Uri.UnescapeDataString(relativePath);
            if (Path.IsPathRooted(relativePath)) return false;

            var sourceDirectory = source.FileInfo.DirectoryName;
            if (string.IsNullOrEmpty(sourceDirectory)) return false;

            targetPath = Path.GetFullPath(relativePath, sourceDirectory);
            if (Directory.Exists(targetPath))
                targetPath = Path.Combine(targetPath, "README.md");

            return File.Exists(targetPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or UriFormatException)
        {
            return false;
        }
    }

    private void ProcessValidationResult(
        string markdownFilePath,
        int lineNumber,
        FluentValidation.Results.ValidationResult result)
    {
        if (!result.IsValid)
        {
            ErrorReceiver.Add(markdownFilePath, lineNumber, result.ToString());
        }
    }
}
