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

    public static IOrderedEnumerable<MarkdownFile> OrderPages(IEnumerable<MarkdownFile> pages) => pages
        .OrderBy(page => page.NavigationOrder.HasValue ? 0 : 1)
        .ThenBy(page => page.NavigationOrder)
        .ThenBy(GetTitle, StringComparer.OrdinalIgnoreCase);
}
