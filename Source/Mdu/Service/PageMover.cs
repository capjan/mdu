using System.Text;

namespace Mdu.Service;

/// <summary>
/// Moves a Markdown page or a folder inside the documentation and rewrites the local links of every page, so links
/// to the moved files and the links inside them keep working.
/// </summary>
public static class PageMover
{
    /// <summary>Works out what a move changes without touching any file.</summary>
    /// <param name="target">The new path, or an existing folder to move into.</param>
    /// <exception cref="InvalidOperationException">The move is not possible; the message says why.</exception>
    public static PageMovePlan Plan(string rootPath, string source, string target, bool includeHidden)
    {
        var comparer = MarkdownLinks.PathComparer;
        rootPath = Path.GetFullPath(rootPath);
        var sourcePath = Path.GetFullPath(source);
        var isDirectory = Directory.Exists(sourcePath);
        if (!isDirectory && !File.Exists(sourcePath)) throw new InvalidOperationException($"{source} does not exist.");
        if (!IsInside(rootPath, sourcePath) || comparer.Equals(rootPath, sourcePath))
            throw new InvalidOperationException($"{source} is not inside the documentation folder {rootPath}.");
        if (!isDirectory && !MarkdownLinks.IsMarkdownFile(sourcePath))
            throw new InvalidOperationException($"{source} is not a Markdown page. Move pages or folders.");
        RejectSymbolicLinks(rootPath, sourcePath, source);

        var targetPath = Path.GetFullPath(target);
        if (Directory.Exists(targetPath) && !comparer.Equals(targetPath, sourcePath) ||
            target.EndsWith('/') || target.EndsWith(Path.DirectorySeparatorChar))
            targetPath = Path.Combine(targetPath, Path.GetFileName(sourcePath));
        else if (!isDirectory && !MarkdownLinks.IsMarkdownFile(targetPath))
            targetPath += Path.GetExtension(sourcePath);

        if (!IsInside(rootPath, targetPath))
            throw new InvalidOperationException($"{target} is not inside the documentation folder {rootPath}.");
        RejectSymbolicLinks(rootPath, targetPath, target);
        if (string.Equals(sourcePath, targetPath, StringComparison.Ordinal))
            throw new InvalidOperationException($"{source} is already at that path.");
        if ((File.Exists(targetPath) || Directory.Exists(targetPath)) &&
            (!comparer.Equals(sourcePath, targetPath) || !ResolvesToSameEntry(sourcePath, targetPath)))
            throw new InvalidOperationException($"{Path.GetRelativePath(rootPath, targetPath)} already exists.");
        if (isDirectory && IsInside(sourcePath, targetPath) && !comparer.Equals(sourcePath, targetPath))
            throw new InvalidOperationException("A folder cannot move into itself.");

        // A moved folder takes its images and other files along, so links to them change as well.
        var movedPaths = new Dictionary<string, string>(comparer);
        if (isDirectory)
        {
            RejectRedirectedLinks(rootPath, sourcePath, targetPath);
            // Symbolic links inside the folder move as links; the files they point to stay where they are.
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in Directory.EnumerateFiles(sourcePath, "*", options))
                movedPaths[Path.GetFullPath(file)] = Path.Combine(targetPath, Path.GetRelativePath(sourcePath, file));
        }
        else
        {
            movedPaths[sourcePath] = targetPath;
        }

        var pages = MarkdownLinks.EnumerateMarkdownFiles(rootPath, includeHidden)
            .Concat(movedPaths.Keys.Where(MarkdownLinks.IsMarkdownFile))
            .Distinct(comparer);
        var changedPages = new List<ChangedPage>();
        foreach (var page in pages)
        {
            var content = File.ReadAllText(page);
            var newPath = movedPaths.GetValueOrDefault(page, page);
            var rewritten = MarkdownLinks.Rewrite(content, page, newPath, movedPaths, comparer, out var linkCount);
            if (rewritten != content) changedPages.Add(new ChangedPage(page, newPath, rewritten, linkCount));
        }

        return new PageMovePlan(rootPath, sourcePath, targetPath, isDirectory, movedPaths.Count,
            changedPages.OrderBy(page => page.NewPath, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Moves the files and writes the pages with rewritten links.</summary>
    public static void Apply(PageMovePlan plan)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(plan.TargetPath)!);
        var caseOnlyRename = MarkdownLinks.PathComparer.Equals(plan.SourcePath, plan.TargetPath);
        // On file systems that ignore case, a rename that only changes case goes through a temporary name.
        var from = plan.SourcePath;
        if (caseOnlyRename)
        {
            from = plan.SourcePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Move(plan.SourcePath, from, plan.IsDirectory);
        }

        Move(from, plan.TargetPath, plan.IsDirectory);
        foreach (var page in plan.ChangedPages)
            WritePreservingEncoding(page.NewPath, page.Content);

        var sourceDirectory = Path.GetDirectoryName(plan.SourcePath)!;
        if (!plan.IsDirectory && !caseOnlyRename &&
            !MarkdownLinks.PathComparer.Equals(sourceDirectory, plan.RootPath) &&
            Directory.Exists(sourceDirectory) && !Directory.EnumerateFileSystemEntries(sourceDirectory).Any())
            Directory.Delete(sourceDirectory);
    }

    // Path checks compare names, so a symbolic link could lead a move outside the documentation folder. Moves through
    // links are refused instead of resolved.
    private static void RejectSymbolicLinks(string rootPath, string path, string displayPath)
    {
        var current = rootPath;
        foreach (var part in Path.GetRelativePath(rootPath, path).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null)
                throw new InvalidOperationException($"{displayPath} goes through the symbolic link {Path.GetRelativePath(rootPath, current)}; move the files it points to instead.");
            if (!info.Exists) return;
        }
    }

    // A moved folder takes its symbolic links along unchanged: a relative link then resolves from its new place, and an
    // absolute link keeps its old path. The move is refused when either changes what a link points to, for example a
    // relative link out of a folder that moves one level deeper, or an absolute link to a file inside the moved folder.
    private static void RejectRedirectedLinks(string rootPath, string folder, string newFolder)
    {
        foreach (var link in EnumerateLinks(new DirectoryInfo(folder)))
        {
            var target = link.LinkTarget!;
            var linkDirectory = Path.GetDirectoryName(link.FullName)!;
            var newLinkDirectory = Path.Combine(newFolder, Path.GetRelativePath(folder, linkDirectory));
            var before = Path.GetFullPath(target, linkDirectory);
            var after = Path.GetFullPath(target, newLinkDirectory);
            var expected = IsInside(folder, before) ? Path.Combine(newFolder, Path.GetRelativePath(folder, before)) : before;
            // Git stores the link text, so a link that only works because this file system ignores case would break in
            // checkouts on Linux; the comparison respects case like the rewriting of Markdown links.
            if (string.Equals(Path.TrimEndingDirectorySeparator(after), Path.TrimEndingDirectorySeparator(expected), StringComparison.Ordinal))
                continue;

            throw new InvalidOperationException(
                $"{Path.GetRelativePath(rootPath, link.FullName)} is a symbolic link to {target}, which would point somewhere else after the move. " +
                "Remove or change the link, then move the folder.");
        }
    }

    // Walks the folder without following symbolic links, so linked folders are neither entered nor looped through.
    private static IEnumerable<FileSystemInfo> EnumerateLinks(DirectoryInfo directory)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }))
        {
            if (entry.LinkTarget is not null) yield return entry;
            else if (entry is DirectoryInfo child)
                foreach (var link in EnumerateLinks(child))
                    yield return link;
        }
    }

    private static void Move(string from, string to, bool isDirectory)
    {
        if (isDirectory) Directory.Move(from, to);
        else File.Move(from, to);
    }

    private static void WritePreservingEncoding(string path, string content)
    {
        var hasBom = File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.Preamble);
        File.WriteAllText(path, content, new UTF8Encoding(hasBom));
    }

    private static bool ResolvesToSameEntry(string firstPath, string secondPath)
    {
        var firstActualPath = ResolveExistingPath(firstPath);
        var secondActualPath = ResolveExistingPath(secondPath);
        var firstRoot = Path.GetPathRoot(firstActualPath)!;
        var secondRoot = Path.GetPathRoot(secondActualPath)!;
        return string.Equals(firstRoot, secondRoot, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(firstActualPath[firstRoot.Length..], secondActualPath[secondRoot.Length..], StringComparison.Ordinal);
    }

    private static string ResolveExistingPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var actualPath = root;
        foreach (var segment in fullPath[root.Length..]
                     .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            var entries = Directory.EnumerateFileSystemEntries(actualPath).ToArray();
            var entry = entries.FirstOrDefault(candidate =>
                           string.Equals(Path.GetFileName(candidate), segment, StringComparison.Ordinal))
                       ?? entries.FirstOrDefault(candidate =>
                           string.Equals(Path.GetFileName(candidate), segment, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                throw new IOException($"Could not resolve the existing path {path}.");
            actualPath = entry;
        }

        return actualPath;
    }

    private static bool IsInside(string directory, string path)
    {
        var relativePath = Path.GetRelativePath(directory, path);
        return relativePath == "." ||
               !Path.IsPathRooted(relativePath) &&
               relativePath != ".." &&
               !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}

/// <param name="MovedFileCount">The number of files that move, including images and other files of a moved folder.</param>
/// <param name="ChangedPages">The pages whose links change, at their paths after the move.</param>
public sealed record PageMovePlan(
    string RootPath,
    string SourcePath,
    string TargetPath,
    bool IsDirectory,
    int MovedFileCount,
    IReadOnlyList<ChangedPage> ChangedPages);

/// <param name="Path">The page before the move.</param>
/// <param name="NewPath">The page after the move.</param>
/// <param name="LinkCount">The number of links that change in the page.</param>
public sealed record ChangedPage(string Path, string NewPath, string Content, int LinkCount);
