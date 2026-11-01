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
        rootPath = Path.GetFullPath(rootPath);
        var comparer = new FileSystemPathComparer(MarkdownLinks.PathComparer, rootPath);
        var sourcePath = Path.GetFullPath(source);
        var isDirectory = Directory.Exists(sourcePath);
        if (!isDirectory && !File.Exists(sourcePath)) throw new InvalidOperationException($"{source} does not exist.");
        sourcePath = ResolvePathWithinRoot(rootPath, sourcePath, comparer, source);
        if (comparer.Equals(rootPath, sourcePath))
            throw new InvalidOperationException($"{source} is not inside the documentation folder {rootPath}.");
        if (!isDirectory && !MarkdownLinks.IsMarkdownFile(sourcePath))
            throw new InvalidOperationException($"{source} is not a Markdown page. Move pages or folders.");
        RejectSymbolicLinks(rootPath, sourcePath, source);

        var targetPath = Path.GetFullPath(target);
        var targetIsSourceDirectory = isDirectory && Directory.Exists(targetPath) &&
            PathsEqualForCaseOnlyRename(targetPath, sourcePath) && comparer.ResolvesToSameEntry(targetPath, sourcePath);
        if (Directory.Exists(targetPath) && !targetIsSourceDirectory ||
            target.EndsWith('/') || target.EndsWith(Path.DirectorySeparatorChar))
            targetPath = Path.Combine(targetPath, Path.GetFileName(sourcePath));
        else if (!isDirectory && !MarkdownLinks.IsMarkdownFile(targetPath))
            targetPath += Path.GetExtension(sourcePath);

        targetPath = ResolvePathWithinRoot(rootPath, targetPath, comparer, target);
        RejectSymbolicLinks(rootPath, targetPath, target);
        if (string.Equals(sourcePath, targetPath, StringComparison.Ordinal))
            throw new InvalidOperationException($"{source} is already at that path.");
        if ((File.Exists(targetPath) || Directory.Exists(targetPath)) &&
            (!PathsEqualForCaseOnlyRename(sourcePath, targetPath) || !comparer.ResolvesToSameEntry(sourcePath, targetPath)))
            throw new InvalidOperationException($"{Path.GetRelativePath(rootPath, targetPath)} already exists.");
        if (isDirectory && IsInside(sourcePath, targetPath) && !PathsEqualForCaseOnlyRename(sourcePath, targetPath))
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
        var caseOnlyRename = PathsEqualForCaseOnlyRename(plan.SourcePath, plan.TargetPath);
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

    private static bool PathsEqualForCaseOnlyRename(string firstPath, string secondPath) =>
        MarkdownLinks.PathComparer.Equals(
            NormalizePath(firstPath),
            NormalizePath(secondPath));

    private static string NormalizePath(string path) =>
        OperatingSystem.IsMacOS() ? path.Normalize(NormalizationForm.FormC) : path;

    private sealed class FileSystemPathComparer(StringComparer comparer, string rootPath) : StringComparer
    {
        private readonly Dictionary<string, DirectoryEntryIndex> _directories = new(StringComparer.Ordinal);

        public override int Compare(string? x, string? y)
        {
            var comparison = comparer.Compare(
                x is null ? null : NormalizePath(x),
                y is null ? null : NormalizePath(y));
            if (comparison != 0 || Equals(x, y)) return comparison;
            return StringComparer.Ordinal.Compare(x, y);
        }

        public override bool Equals(string? x, string? y)
        {
            if (x is null || y is null) return x is null && y is null;
            if (string.Equals(x, y, StringComparison.Ordinal)) return true;
            if (!comparer.Equals(NormalizePath(x), NormalizePath(y))) return false;
            if ((!File.Exists(x) && !Directory.Exists(x)) || (!File.Exists(y) && !Directory.Exists(y))) return false;
            return ResolvesToSameEntry(x, y);
        }

        public override int GetHashCode(string value) => comparer.GetHashCode(NormalizePath(value));

        public bool ResolvesToSameEntry(string firstPath, string secondPath)
        {
            var firstFullPath = Path.GetFullPath(firstPath);
            var secondFullPath = Path.GetFullPath(secondPath);
            if (IsInside(rootPath, firstFullPath) && IsInside(rootPath, secondFullPath))
            {
                if (HasExactPathSpelling(rootPath, firstFullPath) &&
                    HasExactPathSpelling(rootPath, secondFullPath))
                {
                    var firstRelativePath = Path.GetRelativePath(rootPath, firstFullPath);
                    var secondRelativePath = Path.GetRelativePath(rootPath, secondFullPath);
                    return ResolvesToSameEntry(rootPath, firstRelativePath, secondRelativePath, firstPath, secondPath);
                }
            }

            var firstRoot = Path.GetPathRoot(firstFullPath)!;
            var secondRoot = Path.GetPathRoot(secondFullPath)!;
            if (!comparer.Equals(firstRoot, secondRoot)) return false;

            var firstSegments = GetPathSegments(firstFullPath, firstRoot);
            var secondSegments = GetPathSegments(secondFullPath, secondRoot);
            return ResolvesToSameEntry(firstRoot, firstSegments, secondSegments, firstPath, secondPath);
        }

        private bool ResolvesToSameEntry(string basePath, string firstPath, string secondPath, string source, string target) =>
            ResolvesToSameEntry(
                basePath,
                GetPathSegments(firstPath, string.Empty),
                GetPathSegments(secondPath, string.Empty),
                source,
                target);

        private bool ResolvesToSameEntry(
            string basePath,
            string[] firstSegments,
            string[] secondSegments,
            string source,
            string target)
        {
            if (firstSegments.Length != secondSegments.Length) return false;

            var actualParent = basePath;
            for (var index = 0; index < firstSegments.Length; index++)
            {
                var firstSegment = firstSegments[index];
                var secondSegment = secondSegments[index];
                if (string.Equals(firstSegment, secondSegment, StringComparison.Ordinal))
                {
                    actualParent = Path.Combine(actualParent, firstSegment);
                    continue;
                }

                var directory = GetDirectoryIndex(actualParent, target, source);
                var firstEntry = directory.Find(firstSegment);
                var secondEntry = directory.Find(secondSegment);
                if (firstEntry is null || !string.Equals(firstEntry, secondEntry, StringComparison.Ordinal))
                    return false;
                actualParent = firstEntry;
            }

            return true;
        }

        private DirectoryEntryIndex GetDirectoryIndex(string path, string target, string source)
        {
            if (_directories.TryGetValue(path, out var index)) return index;
            try
            {
                index = new DirectoryEntryIndex(
                    Directory.EnumerateFileSystemEntries(path),
                    OperatingSystem.IsMacOS(),
                    OperatingSystem.IsMacOS() || OperatingSystem.IsWindows());
                _directories.Add(path, index);
                return index;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                throw new InvalidOperationException(
                    $"Could not verify whether the existing target {target} is the same file-system entry as {source}.",
                    exception);
            }
        }

    }

    private sealed class DirectoryEntryIndex
    {
        private readonly Dictionary<string, string> _exact = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _caseInsensitive = new(StringComparer.OrdinalIgnoreCase);
        private readonly bool _normalizeUnicode;
        private readonly bool _caseInsensitiveFileSystem;

        public DirectoryEntryIndex(IEnumerable<string> entries, bool normalizeUnicode, bool caseInsensitiveFileSystem)
        {
            _normalizeUnicode = normalizeUnicode;
            _caseInsensitiveFileSystem = caseInsensitiveFileSystem;
            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                _exact.TryAdd(name, entry);
                var key = normalizeUnicode ? NormalizePath(name) : name;
                if (!_caseInsensitive.TryGetValue(key, out var matches))
                    _caseInsensitive[key] = matches = [];
                matches.Add(entry);
            }
        }

        public string? Find(string segment)
        {
            if (_exact.TryGetValue(segment, out var exact)) return exact;
            if (!_caseInsensitiveFileSystem) return null;

            var key = _normalizeUnicode ? NormalizePath(segment) : segment;
            if (!_caseInsensitive.TryGetValue(key, out var matches)) return null;
            if (matches.Count > 1)
                throw new InvalidOperationException($"Could not unambiguously resolve the existing path segment {segment}.");
            return matches[0];
        }
    }

    private static bool IsInside(string directory, string path)
    {
        var relativePath = Path.GetRelativePath(directory, path);
        return relativePath == "." ||
               !Path.IsPathRooted(relativePath) &&
               relativePath != ".." &&
               !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool HasExactPathSpelling(string directory, string path)
    {
        var relativePath = Path.GetRelativePath(directory, path);
        return string.Equals(
            Path.GetFullPath(Path.Combine(directory, relativePath)),
            path,
            StringComparison.Ordinal);
    }

    private static string ResolvePathWithinRoot(
        string rootPath,
        string path,
        FileSystemPathComparer comparer,
        string displayPath)
    {
        var fullPath = Path.GetFullPath(path);
        if (IsInside(rootPath, fullPath) && HasExactPathSpelling(rootPath, fullPath))
            return Path.GetFullPath(Path.Combine(rootPath, Path.GetRelativePath(rootPath, fullPath)));

        var rootFileSystemRoot = Path.GetPathRoot(rootPath)!;
        var pathFileSystemRoot = Path.GetPathRoot(fullPath)!;
        if (!comparer.Equals(rootFileSystemRoot, pathFileSystemRoot))
            throw new InvalidOperationException($"{displayPath} is not inside the documentation folder {rootPath}.");

        var rootSegments = GetPathSegments(rootPath, rootFileSystemRoot);
        var pathSegments = GetPathSegments(fullPath, pathFileSystemRoot);
        if (pathSegments.Length < rootSegments.Length)
            throw new InvalidOperationException($"{displayPath} is not inside the documentation folder {rootPath}.");

        var candidateRoot = pathFileSystemRoot;
        for (var index = 0; index < rootSegments.Length; index++)
            candidateRoot = Path.Combine(candidateRoot, pathSegments[index]);
        if (!comparer.ResolvesToSameEntry(rootPath, candidateRoot))
            throw new InvalidOperationException($"{displayPath} is not inside the documentation folder {rootPath}.");

        var relativePath = Path.GetRelativePath(candidateRoot, fullPath);
        if (!IsInside(candidateRoot, fullPath))
            throw new InvalidOperationException($"{displayPath} is not inside the documentation folder {rootPath}.");

        return Path.GetFullPath(Path.Combine(rootPath, relativePath));
    }

    private static string[] GetPathSegments(string path, string root) =>
        (root.Length == 0 ? path : path[root.Length..])
        .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
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
