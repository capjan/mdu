using System.Text.RegularExpressions;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Markdown.Renderer;

/// <summary>
/// Points links to Markdown files at the rendered HTML pages. Works on the parsed document,
/// so link syntax shown in code blocks and code spans stays unchanged.
/// </summary>
public class LinkReplacer
{
    private static readonly Regex AbsoluteUrlRegex = new(@"^(?:/|[A-Za-z][A-Za-z0-9+.-]*:)", RegexOptions.Compiled);
    private const string MarkdownExtension = ".md";
    private const string PdfLinkUrl = "mdu:pdf";

    public static bool IsPdfLink(string? url) =>
        string.Equals(url, PdfLinkUrl, StringComparison.OrdinalIgnoreCase);

    public void ReplaceAll(MarkdownDocument document, string? pdfDownloadUrl = null)
    {
        var headingIds = GetHeadingIdsByLowercase(document);
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (IsPdfLink(link.Url))
            {
                if (string.IsNullOrWhiteSpace(pdfDownloadUrl)) continue;
                link.Url = pdfDownloadUrl;
                continue;
            }

            link.Url = MatchHeadingIdCasing(ReplaceUrl(link.Url), headingIds);
        }
    }

    /// <summary>
    /// Heading ids are lowercase, so a same-page link such as <c>#Api.Client</c> is pointed at the heading
    /// <c>api.client</c> when no id matches its exact spelling.
    /// </summary>
    private static string? MatchHeadingIdCasing(string? url, IReadOnlyDictionary<string, string?> headingIds)
    {
        if (url is not { Length: > 1 } || url[0] != '#') return url;
        var fragment = url[1..];
        if (headingIds.TryGetValue(fragment.ToLowerInvariant(), out var id) && id is not null && id != fragment)
            return $"#{id}";
        return url;
    }

    private static IReadOnlyDictionary<string, string?> GetHeadingIdsByLowercase(MarkdownDocument document)
    {
        var ids = document.Descendants<HeadingBlock>()
            .Select(heading => heading.TryGetAttributes()?.Id)
            .OfType<string>()
            .ToArray();
        var exactIds = ids.ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var key = id.ToLowerInvariant();
            // An exact id wins; ids that differ only in casing are ambiguous and stay unmatched.
            if (exactIds.Contains(key)) result[key] = key;
            else if (!result.TryAdd(key, id) && result[key] != id) result[key] = null;
        }
        return result;
    }

    public static string? ReplaceUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || AbsoluteUrlRegex.IsMatch(url)) return url;

        var suffixStart = url.IndexOfAny(['?', '#']);
        var path = suffixStart < 0 ? url : url[..suffixStart];
        var suffix = suffixStart < 0 ? string.Empty : url[suffixStart..];
        if (!path.EndsWith(MarkdownExtension, StringComparison.Ordinal)) return url;

        var fileNameStart = path.LastIndexOf('/') + 1;
        var fileNameWithoutExtension = path[fileNameStart..^MarkdownExtension.Length];
        if (fileNameWithoutExtension.Length == 0) return url;
        if (fileNameWithoutExtension.Equals("readme", StringComparison.InvariantCultureIgnoreCase))
        {
            fileNameWithoutExtension = "index";
        }
        return $"{path[..fileNameStart]}{fileNameWithoutExtension}.html{suffix}";
    }
}
