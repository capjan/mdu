using System.Text;
using System.Text.Encodings.Web;
using Markdown.Renderer.Res.Model;

namespace Markdown.Renderer;

internal static class SiteNavigation
{
    /// <summary>Publishes the shared navigation script and the static sitemap, whose head carries the site theme.</summary>
    internal static void WriteTo(SiteAssets assets, IReadOnlyList<NavigationItem> items, string sitemapHead)
    {
        // URLs are relative to the site root; page-specific state stays in the referencing HTML.
        var neutral = items.Select(item => new NavigationItem
        {
            Title = item.Title, Url = item.Url, Depth = item.Depth,
            IsExpandedByDefault = item.IsExpandedByDefault,
            IsTopNavigationItem = item.IsTopNavigationItem,
            IsOutsideTopNavigationSection = item.IsOutsideTopNavigationSection,
            TopNavigationOwnerUrl = item.TopNavigationOwnerUrl
        }).ToArray();
        var markup = RenderItems(neutral, shared: true);
        var script = Resources.ScriptContent("navigation")
            .Replace("%%NAVIGATION_HTML%%", Quote(markup), StringComparison.Ordinal);
        assets.Replace(SiteAssets.NavigationPath, script);

        foreach (var item in neutral) item.Url = "../" + item.Url;
        var sitemap = "<!DOCTYPE html><html lang=\"en\" data-theme=\"auto\"><head><meta charset=\"utf-8\">" +
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
            sitemapHead + "</head><body><main class=\"markdown-content\"><h1>Sitemap</h1>" +
            "<nav aria-label=\"All documentation pages\"><ul class=\"site-nav-list\">" +
            RenderItems(neutral, sitemap: true) + "</ul></nav></main></body></html>";
        assets.Replace(SiteAssets.SitemapPath, sitemap);
    }

    internal static string Reference(IReadOnlyList<NavigationItem> items, string prefix, SiteAssets assets)
    {
        var current = items.FirstOrDefault(item => item.IsCurrent)?.Url ?? string.Empty;
        var sectionUrls = "[" + string.Join(',', items.Where(item => item.IsCurrentSection).Select(item => Quote(item.Url))) + "]";
        var top = items.FirstOrDefault(item => item.IsTopNavigationItem && item.IsInCurrentTopSection)?.Url ?? string.Empty;
        return $"<li class=\"site-nav-item is-outside-top-navigation-section\"><a class=\"site-nav-link\" href=\"{HtmlEncoder.Default.Encode(prefix + SiteAssets.SitemapPath)}\">Sitemap</a></li>" +
            $"<script src=\"{HtmlEncoder.Default.Encode(prefix + assets.Url(SiteAssets.NavigationPath))}\" data-prefix=\"{HtmlEncoder.Default.Encode(prefix)}\"" +
            $" data-current=\"{HtmlEncoder.Default.Encode(current)}\" data-sections=\"{HtmlEncoder.Default.Encode(sectionUrls)}\" data-top=\"{HtmlEncoder.Default.Encode(top)}\"></script>";
    }

    private static string Quote(string value) => "\"" + JavaScriptEncoder.Default.Encode(value) + "\"";

    internal static string RenderItems(IReadOnlyList<NavigationItem> navigationItems, bool sitemap = false, bool shared = false)
    {
        var output = new StringBuilder();
        var index = 0;
        RenderItems(0);
        return output.ToString();

        void RenderItems(int depth)
        {
            while (index < navigationItems.Count && navigationItems[index].Depth == depth)
            {
                var itemIndex = index++;
                var item = navigationItems[itemIndex];
                var hasChildren = index < navigationItems.Count && navigationItems[index].Depth > depth;
                var hasToggle = hasChildren && depth > 0 && !sitemap;
                var indentDepth = Math.Max(0, item.Depth - 1);
                var encodedTitle = HtmlEncoder.Default.Encode(item.Title);
                var encodedUrl = HtmlEncoder.Default.Encode(item.Url);
                var childrenId = $"site-nav-children-{itemIndex}";
                var expanded = !hasToggle || item.IsExpandedByDefault;

                var itemClass = item.IsTopNavigationItem ? "site-nav-item top-navigation-entry" : "site-nav-item";
                if (item.IsCurrentSection) itemClass += " is-current-section";
                if (item.IsOutsideTopNavigationSection) itemClass += " is-outside-top-navigation-section";
                if (item.IsInCurrentTopSection) itemClass += " is-in-current-top-section";
                if (item.IsAncestorOfCurrentTopSection) itemClass += " is-top-navigation-ancestor";
                var owner = shared && item.TopNavigationOwnerUrl is { } ownerUrl
                    ? $" data-top-owner=\"{HtmlEncoder.Default.Encode(ownerUrl)}\"" : string.Empty;
                output.AppendLine($"<li class=\"{itemClass}\" data-depth=\"{depth}\"{owner} style=\"--indent-depth: {indentDepth}; --wide-indent-depth: {Math.Max(0, item.TopNavigationDepth)}\">");
                var rowClass = item.IsCurrent ? " is-current" : item.IsCurrentSection ? " is-current-section" : string.Empty;
                var currentValue = item.IsCurrent ? "page" : item.IsCurrentSection ? "location" : "false";
                output.AppendLine($"<div class=\"site-nav-row{rowClass}\">");
                if (hasToggle)
                {
                    var label = expanded ? "Collapse" : "Expand";
                    var expandedValue = expanded ? "true" : "false";
                    output.AppendLine($"<button class=\"site-nav-toggle\" type=\"button\" aria-expanded=\"{expandedValue}\" aria-controls=\"{childrenId}\" aria-label=\"{label} {encodedTitle}\"><svg viewBox=\"0 0 12 12\" aria-hidden=\"true\" focusable=\"false\"><path d=\"M3 2l4 4-4 4\"></path></svg></button>");
                }
                else if (depth > 0)
                {
                    output.AppendLine("<span class=\"site-nav-toggle-spacer\" aria-hidden=\"true\"></span>");
                }

                output.AppendLine($"<a class=\"site-nav-link{(item.IsCurrent ? " is-current" : item.IsCurrentSection ? " is-current-section" : string.Empty)}\" href=\"{encodedUrl}\" aria-current=\"{currentValue}\">{encodedTitle}</a>");
                output.AppendLine("</div>");

                if (hasChildren)
                {
                    var childClass = item.IsTopNavigationItem ? "site-nav-list site-nav-children top-navigation-children" : "site-nav-list site-nav-children";
                    output.AppendLine($"<ul class=\"{childClass}\" id=\"{childrenId}\"{(expanded ? string.Empty : " hidden")}>");
                    RenderItems(depth + 1);
                    output.AppendLine("</ul>");
                }

                output.AppendLine("</li>");
            }
        }
    }

}
