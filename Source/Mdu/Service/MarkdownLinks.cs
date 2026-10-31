using System.Text.RegularExpressions;
using Markdown.Renderer;
using Markdig;
using Markdig.Extensions.Footnotes;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using YamlDotNet.RepresentationModel;

namespace Mdu.Service;

/// <summary>
/// Finds the local links of Markdown pages and rewrites them when pages or other files move, so relative links keep
/// pointing at the same files. Links to web addresses, absolute paths, and missing files are left alone.
/// </summary>
public static class MarkdownLinks
{
    public static readonly StringComparer PathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly Regex Scheme = new(@"^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.Compiled);
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseFootnotes().UsePreciseSourceLocation().Build();

    public static bool IsMarkdownFile(string path) =>
        Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".markdown", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lists the Markdown pages of a folder. Symbolic links are skipped, so pages outside the folder are never read
    /// or rewritten through them.
    /// </summary>
    public static IEnumerable<string> EnumerateMarkdownFiles(string rootPath, bool includeHidden) =>
        Directory.EnumerateFiles(rootPath, "*", new EnumerationOptions
            {
                AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint | (includeHidden ? 0 : FileAttributes.Hidden),
                RecurseSubdirectories = true
            })
            .Where(IsMarkdownFile)
            .Select(Path.GetFullPath);

    /// <summary>
    /// Rewrites the local links of the page at <paramref name="sourcePath"/>, which moves to
    /// <paramref name="newSourcePath"/>, for the files in <paramref name="movedPaths"/> that move as well.
    /// </summary>
    public static string Rewrite(
        string content,
        string sourcePath,
        string newSourcePath,
        IReadOnlyDictionary<string, string> movedPaths,
        StringComparer pathComparer,
        out int changedLinkCount)
    {
        var document = Markdig.Markdown.Parse(content, Pipeline);
        var edits = new Dictionary<(int Start, int Length), string>();

        foreach (var link in GetLinkInlines(document))
            AddEdit(link.UrlSpan, link.Url);

        foreach (var definition in document.GetLinkReferenceDefinitions(true).OfType<LinkReferenceDefinition>()
                     .Where(definition => definition is not FootnoteLinkReferenceDefinition))
            AddEdit(definition.UrlSpan, definition.Url);

        foreach (var openApiSpec in OpenApiMarkdownRenderer.FindSpecPathSpans(content))
        {
            string specPath;
            try
            {
                specPath = OpenApiMarkdownRenderer.ResolveSpecPath(sourcePath, openApiSpec.Value);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!File.Exists(specPath) && !movedPaths.ContainsKey(specPath))
                continue;

            var movedSpecPath = movedPaths.GetValueOrDefault(specPath, specPath);
            if (!string.Equals(sourcePath, newSourcePath, StringComparison.Ordinal) ||
                !string.Equals(specPath, movedSpecPath, StringComparison.Ordinal))
            {
                var updatedSpecPath = Path.IsPathRooted(openApiSpec.Value)
                    ? movedSpecPath
                    : Path.GetRelativePath(Path.GetDirectoryName(newSourcePath)!, movedSpecPath).Replace('\\', '/');
                if (updatedSpecPath != openApiSpec.Value)
                    edits[(openApiSpec.Start, openApiSpec.Length)] = FormatYamlPath(updatedSpecPath);
            }
        }

        var rewritten = content;
        foreach (var edit in edits.OrderByDescending(edit => edit.Key.Start))
        {
            rewritten = rewritten.Remove(edit.Key.Start, edit.Key.Length)
                .Insert(edit.Key.Start, edit.Value);
        }

        changedLinkCount = edits.Count;
        return rewritten;

        static string FormatYamlPath(string value)
        {
            var isPlainPath = !string.IsNullOrWhiteSpace(value)
                && !char.IsDigit(value[0])
                && !value.Equals("null", StringComparison.OrdinalIgnoreCase)
                && !value.Equals("true", StringComparison.OrdinalIgnoreCase)
                && !value.Equals("false", StringComparison.OrdinalIgnoreCase)
                && !value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                && !value.Equals("no", StringComparison.OrdinalIgnoreCase)
                && !value.Equals("on", StringComparison.OrdinalIgnoreCase)
                && !value.Equals("off", StringComparison.OrdinalIgnoreCase)
                && value is not ("~" or "..." or "---")
                && value.All(character => char.IsLetterOrDigit(character) || character is '/' or '\\' or '.' or '_' or '-');
            if (isPlainPath) return value;

            var escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
            return $"\"{escaped}\"";
        }

        void AddEdit(SourceSpan span, string? destination)
        {
            if (destination is null || span.Start < 0 || span.Length <= 0 || span.Start + span.Length > content.Length)
                return;

            var newDestination = GetUpdatedDestination(sourcePath, newSourcePath, destination, movedPaths, pathComparer);
            if (newDestination is null) return;

            var original = content.Substring(span.Start, span.Length);
            var replacement = original.StartsWith('<') && original.EndsWith('>')
                ? $"<{newDestination}>"
                : newDestination;
            if (replacement != original) edits[(span.Start, span.Length)] = replacement;
        }
    }

    /// <summary>Returns the local links of a page together with the files they point to.</summary>
    public static IReadOnlyList<LocalLink> Find(string content, string sourcePath)
    {
        var document = Markdig.Markdown.Parse(content, Pipeline);
        var links = new List<LocalLink>();
        foreach (var link in GetLinkInlines(document))
            Add(link.Line, link.Span, link.Url);
        foreach (var definition in document.GetLinkReferenceDefinitions(true).OfType<LinkReferenceDefinition>()
                     .Where(definition => definition is not FootnoteLinkReferenceDefinition))
            Add(definition.Line, definition.Span, definition.Url);
        foreach (var openApiSpec in OpenApiMarkdownRenderer.FindSpecPathSpans(content))
        {
            try
            {
                var targetPath = OpenApiMarkdownRenderer.ResolveSpecPath(sourcePath, openApiSpec.Value);
                if (!File.Exists(targetPath)) continue;
                var line = 1 + content.AsSpan(0, openApiSpec.Start).Count('\n');
                var markdown = content.Substring(openApiSpec.Start, openApiSpec.Length);
                links.Add(new LocalLink(line, markdown, targetPath));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException or PathTooLongException)
            {
            }
        }
        return links.OrderBy(link => link.Line).ToList();

        void Add(int line, SourceSpan span, string? destination)
        {
            if (destination is null || ResolveTarget(sourcePath, destination, null, out _, out _) is not { } targetPath) return;
            var markdown = span.Start >= 0 && span.End < content.Length ? content[span.Start..(span.End + 1)] : destination;
            links.Add(new LocalLink(line + 1, markdown, targetPath));
        }
    }

    private static IEnumerable<LinkInline> GetLinkInlines(Markdig.Syntax.MarkdownDocument document) =>
        document.Descendants<LinkInline>()
            .Concat(document.GetLinkReferenceDefinitions(true)
                .OfType<FootnoteLinkReferenceDefinition>()
                .SelectMany(definition => definition.Footnote.Descendants<LinkInline>()))
            .Distinct();

    private static string? GetUpdatedDestination(
        string sourcePath,
        string newSourcePath,
        string destination,
        IReadOnlyDictionary<string, string> movedPaths,
        StringComparer pathComparer)
    {
        if (ResolveTarget(sourcePath, destination, movedPaths, out var suffixStart, out var viaFolder) is not { } targetPath) return null;

        var targetMoved = movedPaths.TryGetValue(targetPath, out var movedTargetPath);
        var finalTargetPath = targetMoved ? movedTargetPath! : targetPath;
        if (!targetMoved && pathComparer.Equals(sourcePath, newSourcePath)) return null;

        // A link to a folder stays a link to the folder while its README.md is still the folder page.
        var keepFolder = viaFolder && Path.GetFileName(finalTargetPath).Equals("README.md", StringComparison.OrdinalIgnoreCase);
        if (StillPointsTo(destination, suffixStart, newSourcePath, keepFolder ? Path.GetDirectoryName(finalTargetPath)! : finalTargetPath))
            return null;
        var relativeTarget = Path.GetRelativePath(Path.GetDirectoryName(newSourcePath)!,
            keepFolder ? Path.GetDirectoryName(finalTargetPath)! : finalTargetPath);
        var encodedTarget = string.Join('/', relativeTarget
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Select(Uri.EscapeDataString));
        var path = suffixStart < 0 ? destination : destination[..suffixStart];
        if (keepFolder && path.EndsWith('/')) encodedTarget += "/";
        var suffix = suffixStart < 0 ? string.Empty : destination[suffixStart..];
        return encodedTarget + suffix;
    }

    // Links between files that move together, such as pages of a moved folder, often still work unchanged. The
    // comparison respects case even where the file system does not, because web servers usually do.
    private static bool StillPointsTo(string destination, int suffixStart, string newSourcePath, string target)
    {
        var relativePath = Uri.UnescapeDataString(suffixStart < 0 ? destination : destination[..suffixStart]);
        var resolved = Path.GetFullPath(relativePath, Path.GetDirectoryName(newSourcePath)!);
        return string.Equals(Path.TrimEndingDirectorySeparator(resolved), Path.TrimEndingDirectorySeparator(target), StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves a link destination relative to its page. A folder resolves to its <c>README.md</c>. Returns null for
    /// web addresses, absolute paths, and files that neither exist nor move.
    /// </summary>
    private static string? ResolveTarget(
        string sourcePath,
        string destination,
        IReadOnlyDictionary<string, string>? movedPaths,
        out int suffixStart,
        out bool viaFolder)
    {
        suffixStart = -1;
        viaFolder = false;
        if (string.IsNullOrWhiteSpace(destination) ||
            destination.StartsWith("//", StringComparison.Ordinal) ||
            Scheme.IsMatch(destination))
            return null;

        suffixStart = destination.IndexOfAny(['?', '#']);
        var relativePath = suffixStart < 0 ? destination : destination[..suffixStart];
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        try
        {
            relativePath = Uri.UnescapeDataString(relativePath);
            if (Path.IsPathRooted(relativePath)) return null;

            var targetPath = Path.GetFullPath(relativePath, Path.GetDirectoryName(sourcePath)!);
            if (Directory.Exists(targetPath))
            {
                var readmePath = Path.Combine(targetPath, "README.md");
                viaFolder = File.Exists(readmePath);
                return viaFolder ? readmePath : null;
            }

            return File.Exists(targetPath) || movedPaths?.ContainsKey(targetPath) == true ? targetPath : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or UriFormatException)
        {
            return null;
        }
    }
}

/// <param name="Line">The 1-based line of the link in its page.</param>
/// <param name="Markdown">The Markdown of the link, such as <c>[Setup](setup.md)</c>.</param>
/// <param name="TargetPath">The full path of the file the link points to.</param>
public sealed record LocalLink(int Line, string Markdown, string TargetPath);
