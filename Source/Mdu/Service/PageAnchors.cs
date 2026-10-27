using System.Net;
using System.Text.RegularExpressions;
using Markdown.Renderer;

namespace Mdu.Service;

/// <summary>
/// Reads the element ids of a rendered page, so links such as <c>page.md#setup</c> can be checked against the
/// anchors readers actually get: heading ids, explicit ids such as <c>{#custom}</c>, footnotes, ids in raw HTML,
/// and the operation anchors of embedded OpenAPI specifications.
/// </summary>
public sealed class PageAnchors
{
    private static readonly Regex StartTagRegex = new(
        "<(?<tag>[a-zA-Z][a-zA-Z0-9-]*)(?<attributes>(?:\"[^\"]*\"|'[^']*'|[^'\">])*)>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    // HTML attribute names ignore letter case, and values may be unquoted, as in <a ID=setup>.
    private static readonly Regex AttributeRegex = new(
        "(?<name>[^\\s\"'>/=]+)(?:\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s\"'=<>`]+)))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HtmlRenderer _renderer = new();
    private readonly Dictionary<string, Anchors?> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the anchors of the Markdown page at <paramref name="filePath"/>, or <c>null</c> when the page
    /// cannot be rendered, in which case its links are not checked.
    /// </summary>
    public Anchors? Read(string filePath, string markdown, string? sourceRootPath = null)
    {
        var key = Path.GetFullPath(filePath);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        Anchors? anchors;
        try
        {
            var expanded = OpenApiMarkdownRenderer.ExpandCodeBlocks(markdown, filePath, sourceRootPath);
            var html = _renderer.RenderMarkdownFragment(expanded);
            anchors = ReadAnchors(html);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            anchors = null;
        }

        _cache[key] = anchors;
        return anchors;
    }

    private static Anchors ReadAnchors(string html)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var headingIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match tag in StartTagRegex.Matches(html))
        {
            var tagName = tag.Groups["tag"].Value;
            var isHeading = tagName.Length == 2 && char.ToLowerInvariant(tagName[0]) == 'h' && tagName[1] is >= '1' and <= '6';
            // Browsers also scroll to <a name="...">, but the name of a form field is no anchor.
            var isLink = tagName.Equals("a", StringComparison.OrdinalIgnoreCase);
            foreach (Match attribute in AttributeRegex.Matches(tag.Groups["attributes"].Value))
            {
                var name = attribute.Groups["name"].Value;
                var isId = name.Equals("id", StringComparison.OrdinalIgnoreCase);
                if (!isId && !(isLink && name.Equals("name", StringComparison.OrdinalIgnoreCase))) continue;

                var value = WebUtility.HtmlDecode(attribute.Groups["value"].Value);
                if (value.Length == 0) continue;
                ids.Add(value);
                if (isId && isHeading) headingIds.Add(value);
            }
        }

        return new Anchors(ids, headingIds);
    }

    /// <param name="Ids">Every id and anchor name on the page.</param>
    /// <param name="HeadingIds">The heading ids, which same-page links reach in any letter case.</param>
    public sealed record Anchors(IReadOnlySet<string> Ids, IReadOnlySet<string> HeadingIds)
    {
        /// <summary>Finds the one heading id that matches <paramref name="fragment"/> when letter case is ignored.</summary>
        public string? FindHeadingIgnoringCase(string fragment)
        {
            var matches = HeadingIds.Where(id => id.Equals(fragment, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
    }
}
