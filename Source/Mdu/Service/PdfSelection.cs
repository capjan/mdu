using System.Security.Cryptography;
using System.Text;
using Mdu.Model;

namespace Mdu.Service;

internal static class PdfSelection
{
    internal const string OutputDirectory = "_mdu-pdf";

    internal static MarkdownFile FindPage(string rootPath, IEnumerable<MarkdownFile> graph, string path,
        MarkdownFile? sourcePage = null)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Any(char.IsControl) ||
            !Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"PDF root must be a relative Markdown page path: '{path}'.");

        var basePath = sourcePage is null ? rootPath :
            Path.GetDirectoryName(Path.Combine(rootPath, GetSitePath(rootPath, sourcePage)))!;
        var target = Path.GetFullPath(path, basePath);
        var relative = Path.GetRelativePath(rootPath, target);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"PDF root is outside the documentation: '{path}'.");

        var matches = Flatten(graph).Where(page =>
            GetPaths(rootPath, page).Any(candidate =>
                MarkdownLinks.PathComparer.Equals(Path.GetFullPath(candidate, rootPath), target))).ToArray();
        return matches.Length == 1 ? matches[0] :
            throw new InvalidOperationException($"PDF root page not found or ambiguous: '{path}'.");
    }

    internal static string GetSitePath(string rootPath, MarkdownFile page) =>
        page.SiteRelativePath ?? Path.GetRelativePath(rootPath, page.FileInfo.FullName);

    internal static string GetOutputPath(string rootPath, MarkdownFile page)
    {
        var identity = GetSitePath(rootPath, page).Replace('\\', '/');
        return OutputDirectory + "/" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".pdf";
    }

    private static IEnumerable<string> GetPaths(string rootPath, MarkdownFile page)
    {
        yield return GetSitePath(rootPath, page);
        if (page.ImportMarkerPath is not null) yield return Path.GetRelativePath(rootPath, page.ImportMarkerPath);
    }

    private static IEnumerable<MarkdownFile> Flatten(IEnumerable<MarkdownFile> pages)
    {
        foreach (var page in pages)
        {
            yield return page;
            foreach (var child in Flatten(page.Children)) yield return child;
        }
    }
}
