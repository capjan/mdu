using System.Text;
using System.Text.Encodings.Web;
using Markdown.Renderer;
using Markdown.Renderer.Res.Model;
using Mdu.Model;

namespace Mdu.Service.Visitors;

/// <summary>The tag pages under <c>tags/</c>: one page per tag and an overview of all tags.</summary>
public partial class MarkdigVisitor
{
    private const int RelatedTagLimit = 5;

    /// <summary>Adds one search entry per tag, so that a query naming a tag finds its tag page first.</summary>
    public void AddTagsToSearchIndex()
    {
        foreach (var tag in _siteTags.Tags)
            _searchIndexBuilder.AddTag(tag.Name, $"{SiteTags.Folder}/{Uri.EscapeDataString(tag.Slug)}.html",
                tag.Definition?.Description ?? string.Empty, tag.Definition?.Aliases ?? [], tag.Pages.Count);
    }

    /// <summary>
    /// Writes the tag pages and <c>tags/index.html</c>. With a vocabulary page, the index forwards to it, because the
    /// vocabulary lists the tags with their descriptions. Returns the written files.
    /// </summary>
    public IReadOnlyList<string> WriteTagPages()
    {
        if (_siteTags.Tags.Count == 0) return [];
        var folder = Path.Combine(_outPath, SiteTags.Folder);
        Directory.CreateDirectory(folder);
        var written = new List<string>();
        var anchorPage = _siteTags.VocabularyPage ?? _navigationRoots.FirstOrDefault();
        var indexPath = Path.Combine(folder, "index.html");
        var overviewPath = _siteTags.VocabularyPage is { } vocabulary ? GetOutputPagePath(GetRenderPath(vocabulary)) : indexPath;
        var overviewUrl = GetRelativeUrl(GetSiteRelativeSegments(folder), GetSiteRelativeSegments(overviewPath));

        foreach (var tag in _siteTags.Tags)
        {
            var outputPath = Path.Combine(folder, tag.Slug + ".html");
            WriteGeneratedPage(outputPath, tag.Name, tag.Definition?.Description ?? string.Empty, RenderTagPage(tag, folder),
                [new BreadcrumbItem("Tags", overviewUrl), new BreadcrumbItem(tag.Name, null)], anchorPage);
            written.Add(outputPath);
        }

        if (_siteTags.VocabularyPage is not null)
            WriteRedirect(indexPath, overviewPath);
        else
            WriteGeneratedPage(indexPath, "Tags", "All tags of the documentation.", RenderOverview(), [], anchorPage);
        written.Add(indexPath);
        return written;
    }

    /// <summary>Replaces the <c>tags</c> code blocks of a page with the list of the pages they select.</summary>
    private string ExpandTagLists(MarkdownFile page, string markdown, string outputDirectory, bool dashboard)
    {
        var segments = GetSiteRelativeSegments(outputDirectory);
        return TagListBlock.Expand(markdown, block =>
        {
            if (_siteTags.Check(block) is { } error) return TagListBlock.RenderError(error);
            var options = block.Options!;
            var allPages = options.Tags.Count == 1 && options.Exclude.Count == 0 && _siteTags.Find(options.Tags[0]) is { } tag
                ? GetRelativeUrl(segments, [SiteTags.Folder, tag.Slug + ".html"])
                : null;
            return TagListBlock.Render(_siteTags.Select(options, page), options,
                dashboard ? TagListStyle.Dashboard : TagListStyle.Page,
                target => GetRelativeUrl(segments, GetSiteRelativeSegments(GetOutputPagePath(GetRenderPath(target)))),
                // A section page is its own area; repeating its title would add nothing.
                target => NavigationPage.GetArea(target, _navigationRoots) is var area && !ReferenceEquals(area, target)
                    ? NavigationPage.RemoveIconShortcodes(NavigationPage.GetTitle(area)).Trim()
                    : string.Empty,
                allPages);
        });
    }

    private IReadOnlyList<PageTagLink>? GetTagLinks(IReadOnlyList<SiteTag> tags, string fromDirectory)
    {
        if (tags.Count == 0) return null;
        var segments = GetSiteRelativeSegments(fromDirectory);
        return tags.Select(tag => new PageTagLink(tag.Name,
            GetRelativeUrl(segments, [SiteTags.Folder, tag.Slug + ".html"]), tag.Pages.Count)).ToArray();
    }

    private bool IsInTagFolder(string outputPath) =>
        Path.GetFullPath(outputPath).StartsWith(
            Path.GetFullPath(Path.Combine(_outPath, SiteTags.Folder)) + Path.DirectorySeparatorChar, PathComparison);

    // The pages of a tag are already in navigation order, so the groups and their entries follow the sidebar.
    private string RenderTagPage(SiteTag tag, string folder)
    {
        var folderSegments = GetSiteRelativeSegments(folder);
        var groups = tag.Pages
            .GroupBy(page => NavigationPage.GetArea(page, _navigationRoots))
            .ToArray();
        var output = new StringBuilder();
        output.Append($"<h1>{Encode(tag.Name)}</h1>\n\n");
        if (!string.IsNullOrWhiteSpace(tag.Definition?.DescriptionMarkdown))
            output.Append(RelocateLinks(tag.Definition.DescriptionMarkdown.Trim(), tag)).Append("\n\n");

        var summary = Count(tag.Pages.Count, "page") + (groups.Length > 1 ? $" in {groups.Length} areas" : string.Empty);
        output.Append($"<p class=\"tag-page-summary\">{summary}</p>\n\n");

        var related = tag.Pages
            .SelectMany(_siteTags.For)
            .Where(other => !ReferenceEquals(other, tag))
            .GroupBy(other => other)
            .Select(group => (Tag: group.Key, Shared: group.Count()))
            .OrderByDescending(item => item.Shared)
            .ThenBy(item => item.Tag.Name, StringComparer.OrdinalIgnoreCase)
            .Take(RelatedTagLimit)
            .ToArray();
        if (related.Length > 0)
        {
            output.Append("<div class=\"tag-page-related\"><span class=\"tag-page-related-label\">Often tagged with</span><ul class=\"page-tags\" aria-label=\"Often tagged with\">");
            foreach (var (other, shared) in related)
                output.Append($"<li><a class=\"page-tag\" href=\"{Encode(Uri.EscapeDataString(other.Slug))}.html\">{Encode(other.Name)} <span class=\"page-tag-count\">{shared}</span></a></li>");
            output.Append("</ul></div>\n\n");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var title = group.Key is null ? "Pages" : NavigationPage.GetTitle(group.Key);
            var id = TagCatalog.Slug(title);
            for (var suffix = 2; !ids.Add(id); suffix++) id = $"{TagCatalog.Slug(title)}-{suffix}";
            output.Append($"<h2 id=\"{Encode(id)}\">{Encode(NavigationPage.RemoveIconShortcodes(title))}</h2>\n\n<ul class=\"tag-page-list\">\n");
            foreach (var page in group)
            {
                var url = GetRelativeUrl(folderSegments, GetSiteRelativeSegments(GetOutputPagePath(GetRenderPath(page))));
                var detail = string.IsNullOrWhiteSpace(page.Description) ? GetPath(page) : page.Description;
                output.Append($"<li><a href=\"{Encode(url)}\">{Encode(NavigationPage.RemoveIconShortcodes(NavigationPage.GetTitle(page)))}</a>");
                if (detail.Length > 0) output.Append($"<span class=\"tag-page-detail\">{Encode(detail)}</span>");
                output.Append("</li>\n");
            }
            output.Append("</ul>\n\n");
        }
        return output.ToString();
    }

    private string RenderOverview()
    {
        var output = new StringBuilder("<h1>Tags</h1>\n\n<ul class=\"tag-overview-list\">\n");
        foreach (var tag in _siteTags.Tags)
        {
            output.Append($"<li><a href=\"{Encode(Uri.EscapeDataString(tag.Slug))}.html\">{Encode(tag.Name)}</a>" +
                          $"<span class=\"tag-overview-count\">{Count(tag.Pages.Count, "page")}</span>");
            if (!string.IsNullOrWhiteSpace(tag.Definition?.Description))
                output.Append($"<span class=\"tag-overview-description\">{Encode(tag.Definition.Description)}</span>");
            output.Append("</li>\n");
        }
        output.Append("</ul>\n");
        return output.ToString();
    }

    private void WriteGeneratedPage(string outputPath, string title, string description, string markdown,
        IReadOnlyList<BreadcrumbItem> breadcrumbs, MarkdownFile? anchorPage)
    {
        var navigation = anchorPage is null ? [] : BuildNavigation(anchorPage, outputPath);
        var topNavigation = anchorPage is null ? [] : BuildTopNavigation(anchorPage, outputPath);
        var pdfUrl = GetRelativeUrl(GetSiteRelativeSegments(Path.GetDirectoryName(outputPath)!), ["documentation.pdf"]);
        var temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream))
                _htmlFoundation.WriteHtml(writer, title, description, 1, markdown, string.Empty, navigation, breadcrumbs,
                    _siteMediaProviders, topNavigationItems: topNavigation, pdfDownloadUrl: pdfUrl).GetAwaiter().GetResult();
            File.Move(temporaryPath, outputPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    // The trail below the root page, such as "Operations › Backups"; every page is below the root page anyway.
    private string GetPath(MarkdownFile page) =>
        string.Join(" › ", page.Parents().Where(parent => parent.NavigationVisible && !(_navigationRoots.Length == 1 && parent.Parent is null))
            .Select(parent => NavigationPage.RemoveIconShortcodes(NavigationPage.GetTitle(parent))));

    // The description is written for the vocabulary page; its relative links must work from the tag page as well.
    private string RelocateLinks(string markdown, SiteTag tag) =>
        _siteTags.VocabularyPath is not { } vocabularyPath
            ? markdown
            : MarkdownLinks.Rewrite(markdown, vocabularyPath, Path.Combine(_rootPath, SiteTags.Folder, tag.Slug + ".md"),
                new Dictionary<string, string>(), MarkdownLinks.PathComparer, out _);

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static string Encode(string value) => HtmlEncoder.Default.Encode(value);
}
