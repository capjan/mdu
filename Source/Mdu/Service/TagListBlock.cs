using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Mdu.Model;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Mdu.Service;

/// <summary>The options of a <c>tags</c> code block: the pages must carry every tag of <c>tags</c> and none of <c>exclude</c>.</summary>
/// <param name="Limit">The number of pages to show, 0 for all; null when the block does not set it.</param>
public sealed record TagListOptions(IReadOnlyList<string> Tags, IReadOnlyList<string> Exclude, int? Limit);

/// <summary>A <c>tags</c> code block of a page, with its options or the reason they cannot be used.</summary>
public sealed record TagListBlockSource(int Start, int Length, int Line, TagListOptions? Options, string? Error);

/// <summary>How a list of tagged pages is written.</summary>
public enum TagListStyle
{
    /// <summary>Cards on an ordinary page.</summary>
    Page,
    /// <summary>A plain link list, which a dashboard turns into tiles.</summary>
    Dashboard,
    /// <summary>A plain link list for the PDF.</summary>
    Pdf
}

/// <summary>
/// Finds and expands <c>tags</c> code blocks, which list the pages that carry given tags, such as on a hub page:
/// <code>
/// ```tags
/// tags: [Security]
/// exclude: [Internal]
/// limit: 6
/// ```
/// </code>
/// </summary>
public static partial class TagListBlock
{
    public const string Language = "tags";
    /// <summary>The number of cards a page shows without <c>limit</c>; a link leads to the tag page for the rest.</summary>
    public const int DefaultLimit = 6;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    // Only a cheap filter: Markdig decides, so fences with arguments, in lists, or in callouts count too.
    [GeneratedRegex(@"(?:`{3,}|~{3,})[ \t]*tags\b")]
    private static partial Regex FenceRegex();

    [GeneratedRegex(@"[\\`*_{}\[\]<>()#+!|~]")]
    private static partial Regex MarkdownPunctuation();

    [GeneratedRegex(@"^(\d*)([.)=-])")]
    private static partial Regex LineStart();

    /// <summary>The <c>tags</c> code blocks of a page; <paramref name="markdown"/> may include the front matter.</summary>
    public static IReadOnlyList<TagListBlockSource> Find(string markdown)
    {
        if (!FenceRegex().IsMatch(markdown)) return [];
        return Markdig.Markdown.Parse(markdown, Pipeline).Descendants<FencedCodeBlock>()
            .Where(block => block.Info == Language)
            .Select(block =>
            {
                var (options, error) = Parse(block.Lines.ToString());
                return new TagListBlockSource(block.Span.Start, block.Span.Length, block.Line + 1, options, error);
            })
            .ToArray();
    }

    /// <summary>Replaces every <c>tags</c> code block with the Markdown that <paramref name="render"/> returns for it.</summary>
    public static string Expand(string markdown, Func<TagListBlockSource, string> render)
    {
        var blocks = Find(markdown);
        if (blocks.Count == 0) return markdown;
        var output = new StringBuilder(markdown);
        foreach (var block in blocks.OrderByDescending(block => block.Start))
        {
            // Inside a callout or a list item, every generated line needs the container's "> " or indentation.
            var lineStart = block.Start == 0 ? 0 : markdown.LastIndexOf('\n', block.Start - 1) + 1;
            var prefix = string.Concat(markdown[lineStart..block.Start].Select(character => character is '>' or '\t' ? character : ' '));
            var replacement = prefix.Length == 0 ? render(block) : render(block).Replace("\n", "\n" + prefix, StringComparison.Ordinal);
            output.Remove(block.Start, block.Length).Insert(block.Start, replacement);
        }
        return output.ToString();
    }

    public static (TagListOptions? Options, string? Error) Parse(string yaml)
    {
        YamlMappingNode? mapping;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            mapping = stream.Documents.FirstOrDefault()?.RootNode as YamlMappingNode;
        }
        catch (YamlException exception)
        {
            return (null, $"The tags block is not valid YAML: {exception.Message}");
        }
        if (mapping is null) return (null, "The tags block needs 'tags' with at least one tag, such as 'tags: [Security]'.");

        IReadOnlyList<string> tags = [];
        IReadOnlyList<string> exclude = [];
        int? limit = null;
        foreach (var (keyNode, value) in mapping.Children)
        {
            switch ((keyNode as YamlScalarNode)?.Value)
            {
                case "tags":
                    if (ReadList(value) is not { } tagList) return (null, "'tags' in the tags block must be one tag or a list of tags.");
                    tags = tagList;
                    break;
                case "exclude":
                    if (ReadList(value) is not { } excludeList) return (null, "'exclude' in the tags block must be one tag or a list of tags.");
                    exclude = excludeList;
                    break;
                case "limit":
                    if (value is not YamlScalarNode { Value: { } text } ||
                        !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                        return (null, "'limit' in the tags block must be a whole number, or 0 for no limit.");
                    limit = number;
                    break;
                case var key:
                    return (null, $"The tags block does not know the option '{key}'. Use 'tags', 'exclude', and 'limit'.");
            }
        }
        return tags.Count == 0
            ? (null, "The tags block needs 'tags' with at least one tag, such as 'tags: [Security]'.")
            : (new TagListOptions(tags, exclude, limit), null);

        static IReadOnlyList<string>? ReadList(YamlNode node) => node switch
        {
            YamlScalarNode { Value: { } value } when !string.IsNullOrWhiteSpace(value) => [value.Trim()],
            YamlSequenceNode sequence when sequence.Children.All(item => item is YamlScalarNode { Value: { } text } && !string.IsNullOrWhiteSpace(text)) =>
                sequence.Children.Cast<YamlScalarNode>().Select(item => item.Value!.Trim()).ToArray(),
            _ => null
        };
    }

    /// <summary>
    /// Writes the pages as Markdown: cards on a page, tiles on a dashboard, or a plain list in the PDF. A page shows
    /// <see cref="DefaultLimit"/> cards unless the block sets <c>limit</c>, and links to the tag page for the rest;
    /// dashboards and the PDF have no such link, so they show all pages unless the block sets a limit.
    /// </summary>
    /// <param name="url">The link to a page, or null to write its title without a link.</param>
    /// <param name="area">The navigation area to show for a page, or an empty text for none.</param>
    public static string Render(
        IReadOnlyList<MarkdownFile> pages,
        TagListOptions options,
        TagListStyle style,
        Func<MarkdownFile, string?> url,
        Func<MarkdownFile, string> area,
        string? allPagesUrl)
    {
        var limit = options.Limit ?? (style == TagListStyle.Page ? DefaultLimit : 0);
        var shown = limit > 0 ? pages.Take(limit).ToArray() : pages.ToArray();
        var output = new StringBuilder("\n");
        if (shown.Length == 0)
        {
            // The page with the block and excluded pages may be the only ones with the tags, so "other" and "without".
            var empty = $"No other pages are tagged {string.Join(" and ", options.Tags)}" +
                        (options.Exclude.Count > 0 ? $" without {string.Join(" or ", options.Exclude)}" : string.Empty) + " yet.";
            return style == TagListStyle.Page
                ? $"\n<p class=\"tag-list-empty\">{HtmlEncoder.Default.Encode(empty)}</p>\n"
                : $"\n{Escape(empty)}\n";
        }

        if (style == TagListStyle.Page) output.Append("<div class=\"tag-list\">\n\n");
        foreach (var page in shown)
        {
            var title = Escape(NavigationPage.RemoveIconShortcodes(NavigationPage.GetTitle(page)).Trim());
            // Without a link, the title starts the list item, so a leading "1." or "-" must not start a nested list.
            var link = url(page) is { } target
                ? $"[{title}](<{target}>)"
                : LineStart().Replace(title, "$1\\$2", 1) + (style == TagListStyle.Pdf ? " (not in this PDF)" : string.Empty);
            if (style == TagListStyle.Page)
            {
                var detail = string.Join(" · ", new[] { area(page), page.Description }.Where(text => !string.IsNullOrWhiteSpace(text)));
                output.Append($"- {link}");
                if (detail.Length > 0) output.Append($"\n  <span class=\"tag-list-detail\">{HtmlEncoder.Default.Encode(detail)}</span>");
            }
            else
            {
                // A dashboard turns the text after the link into the tile's subtitle and an icon before it into its logo.
                var icon = style == TagListStyle.Dashboard && !string.IsNullOrWhiteSpace(page.Icon)
                    ? $":{page.Icon.Trim().Trim(':').Replace('/', '-')}: "
                    : string.Empty;
                output.Append($"- {icon}{link}");
                if (!string.IsNullOrWhiteSpace(page.Description)) output.Append($" — {Escape(page.Description.Trim())}");
            }
            output.Append('\n');
        }

        if (style == TagListStyle.Page)
        {
            output.Append("\n</div>\n");
            if (allPagesUrl is not null && shown.Length < pages.Count)
                output.Append($"\n<p class=\"tag-list-more\"><a href=\"{HtmlEncoder.Default.Encode(allPagesUrl)}\">" +
                              $"All pages tagged {HtmlEncoder.Default.Encode(options.Tags[0])}</a></p>\n");
        }
        return output.ToString();
    }

    /// <summary>The message a page shows in place of a block that cannot be used.</summary>
    public static string RenderError(string message) => $"\n> [!WARNING]\n> {Escape(message)}\n";

    private static string Escape(string text) => MarkdownPunctuation().Replace(text, match => "\\" + match.Value);
}
