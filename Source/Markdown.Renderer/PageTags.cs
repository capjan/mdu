using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Markdown.Renderer;

/// <summary>A link to a tag page, relative to the page that shows it.</summary>
public sealed record PageTagLink(string Name, string Url, int PageCount = 0);

/// <summary>Renders the tag links of a page and the page counts of the tag vocabulary.</summary>
public static partial class PageTags
{
    internal const string Icon =
        "<svg viewBox=\"0 0 16 16\" aria-hidden=\"true\" focusable=\"false\"><path d=\"M2 2h5.6l6.4 6.4-5.6 5.6L2 7.6V2z\"></path><circle cx=\"5\" cy=\"5\" r=\"1.1\"></circle></svg>";

    [GeneratedRegex(@"<h(?<level>[12])(?:\s[^>]*)?>(?<content>.*?)</h\k<level>>", RegexOptions.Singleline)]
    private static partial Regex TopHeadingRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    /// <summary>The list of tag links of a page, as an unordered list of links.</summary>
    public static string RenderList(IReadOnlyList<PageTagLink> tags, string className = "page-tags")
    {
        var output = new StringBuilder();
        output.Append($"<ul class=\"{className}\" aria-label=\"Tags\"><li class=\"page-tags-icon\" aria-hidden=\"true\">{Icon}</li>");
        foreach (var tag in tags)
            output.Append($"<li><a class=\"page-tag\" href=\"{HtmlEncoder.Default.Encode(tag.Url)}\">{HtmlEncoder.Default.Encode(tag.Name)}</a></li>");
        output.Append("</ul>\n");
        return output.ToString();
    }

    /// <summary>Puts the tag list right after the level-one heading, or at the start of a page without one.</summary>
    internal static string InsertAfterTitle(string renderedMarkdown, IReadOnlyList<PageTagLink>? tags)
    {
        if (tags is not { Count: > 0 }) return renderedMarkdown;
        var list = RenderList(tags);
        var heading = TopHeadingRegex().Matches(renderedMarkdown).FirstOrDefault(match => match.Groups["level"].Value == "1");
        return heading is null
            ? list + renderedMarkdown
            : renderedMarkdown.Insert(heading.Index + heading.Length, "\n" + list);
    }

    /// <summary>
    /// Ends the section of every second-level heading that names a tag with a link to its tag page, so the vocabulary
    /// page doubles as the overview of all tags.
    /// </summary>
    internal static string InsertSectionLinks(string renderedMarkdown, IReadOnlyList<PageTagLink>? tags)
    {
        if (tags is not { Count: > 0 }) return renderedMarkdown;
        var byName = tags.GroupBy(tag => TagCatalog.Normalize(tag.Name)).ToDictionary(group => group.Key, group => group.First());
        var headings = TopHeadingRegex().Matches(renderedMarkdown).ToArray();
        var output = new StringBuilder(renderedMarkdown);
        for (var index = headings.Length - 1; index >= 0; index--)
        {
            var heading = headings[index];
            if (heading.Groups["level"].Value != "2") continue;
            var name = WebUtility.HtmlDecode(TagRegex().Replace(heading.Groups["content"].Value, string.Empty));
            if (!byName.TryGetValue(TagCatalog.Normalize(name), out var tag)) continue;
            var end = index + 1 < headings.Length ? headings[index + 1].Index : renderedMarkdown.Length;
            var label = tag.PageCount == 1 ? "1 page" : $"{tag.PageCount} pages";
            output.Insert(end,
                $"<p class=\"tag-overview-link\"><a href=\"{HtmlEncoder.Default.Encode(tag.Url)}\">{label}</a></p>\n");
        }
        return output.ToString();
    }
}
