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
        string.Equals(url, PdfLinkUrl, StringComparison.OrdinalIgnoreCase) ||
        (url?.StartsWith(PdfLinkUrl + "?", StringComparison.OrdinalIgnoreCase) ?? false);

    public static string? GetPdfRoot(string url)
    {
        if (string.Equals(url, PdfLinkUrl, StringComparison.OrdinalIgnoreCase)) return null;
        const string prefix = "mdu:pdf?root=";
        if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Invalid PDF link '{url}'. Expected mdu:pdf?root=<Markdown page>.");
        var root = url[prefix.Length..];
        if (root.Length == 0 || root.IndexOfAny(['&', '#', '?']) >= 0 ||
            Regex.IsMatch(root, "%(?![0-9a-fA-F]{2})"))
            throw new InvalidOperationException($"Invalid PDF root in '{url}'.");
        return Uri.UnescapeDataString(root);
    }

    public void ReplaceAll(MarkdownDocument document, string? pdfDownloadUrl = null,
        IReadOnlyDictionary<string, string>? pdfDownloadUrls = null)
    {
        var headingIds = HeadingIds.Read(document);
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!link.IsImage && IsPdfLink(link.Url))
            {
                if (pdfDownloadUrls?.TryGetValue(link.Url!, out var downloadUrl) == true)
                    link.Url = downloadUrl;
                else if (GetPdfRoot(link.Url!) is null && !string.IsNullOrWhiteSpace(pdfDownloadUrl))
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
    private static string? MatchHeadingIdCasing(string? url, HeadingIds headingIds)
    {
        if (url is not { Length: > 1 } || url[0] != '#') return url;
        var fragment = url[1..];
        if (headingIds.Exact.Contains(fragment)) return url;
        return headingIds.ByLowercase.TryGetValue(fragment.ToLowerInvariant(), out var id) && id is not null
            ? $"#{id}"
            : url;
    }

    /// <param name="ByLowercase">The id for each lowercase spelling, or <c>null</c> when several ids share it.</param>
    private sealed record HeadingIds(IReadOnlySet<string> Exact, IReadOnlyDictionary<string, string?> ByLowercase)
    {
        public static HeadingIds Read(MarkdownDocument document)
        {
            var exact = document.Descendants<HeadingBlock>()
                .Select(heading => heading.TryGetAttributes()?.Id)
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
            var byLowercase = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var id in exact)
            {
                var key = id.ToLowerInvariant();
                if (!byLowercase.TryAdd(key, id)) byLowercase[key] = null;
            }
            return new HeadingIds(exact, byLowercase);
        }
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
