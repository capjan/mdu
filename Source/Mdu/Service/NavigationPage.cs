using System.Text.RegularExpressions;
using Mdu.Model;

namespace Mdu.Service;

internal static class NavigationPage
{
    private static readonly Regex IconShortcodes = new(
        @"(?::(?:material|simple|octicons|fontawesome|dashboard)-[\w-]+:\s*)+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string GetTitle(MarkdownFile page)
    {
        if (!string.IsNullOrWhiteSpace(page.NavigationTitle)) return RemoveIconShortcodes(page.NavigationTitle).Trim();
        if (!string.IsNullOrWhiteSpace(page.Title)) return RemoveIconShortcodes(page.Title).Trim();

        return page.FileInfo.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase)
            ? page.FileInfo.Directory?.Name ?? "Documentation"
            : Path.GetFileNameWithoutExtension(page.FileInfo.Name);
    }

    public static string RemoveIconShortcodes(string title) => IconShortcodes.Replace(title, string.Empty);

    public static bool IsTopLevel(MarkdownFile page, string rootPath)
    {
        var relativePath = page.ImportedNavigationRelativePath ?? page.SiteRelativePath ??
                           Path.GetRelativePath(rootPath, page.FileInfo.FullName);
        if (Path.IsPathRooted(relativePath) || relativePath == ".." ||
            relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return false;

        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Length == 1) return true;
        return segments.Length == 2 &&
               (page.FileInfo.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
                page.FileInfo.Name.Equals("index.md", StringComparison.OrdinalIgnoreCase));
    }

    public static IOrderedEnumerable<MarkdownFile> OrderPages(IEnumerable<MarkdownFile> pages) => pages
        .OrderBy(page => page.NavigationOrder.HasValue ? 0 : 1)
        .ThenBy(page => page.NavigationOrder)
        .ThenBy(GetTitle, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The first navigation level a page belongs to, such as "Operations" for Operations/Backups.md. With a single root
    /// page, that is the level below it; the root page is its own area.
    /// </summary>
    public static MarkdownFile GetArea(MarkdownFile page, IReadOnlyCollection<MarkdownFile> roots)
    {
        var chain = page.Parents().Append(page).ToArray();
        var depth = roots.Count == 1 ? 1 : 0;
        return chain.Length > depth ? chain[depth] : chain[0];
    }

    /// <summary>All pages below <paramref name="pages"/>, depth first in navigation order, hidden pages included.</summary>
    public static IEnumerable<MarkdownFile> Flatten(IEnumerable<MarkdownFile> pages)
    {
        foreach (var page in OrderPages(pages))
        {
            yield return page;
            foreach (var child in Flatten(page.Children)) yield return child;
        }
    }
}
