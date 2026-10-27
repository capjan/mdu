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
    private static readonly Regex IdAttributeRegex = new(
        "<[a-zA-Z][^>]*?\\s(?:id|name)\\s*=\\s*(?:\"(?<double>[^\"]*)\"|'(?<single>[^']*)')",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HeadingIdRegex = new(
        "<h[1-6]\\b[^>]*?\\sid=\"(?<id>[^\"]*)\"",
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
            anchors = new Anchors(
                ReadIds(IdAttributeRegex, html, "double", "single"),
                ReadIds(HeadingIdRegex, html, "id"));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            anchors = null;
        }

        _cache[key] = anchors;
        return anchors;
    }

    private static HashSet<string> ReadIds(Regex regex, string html, params string[] groups) =>
        regex.Matches(html)
            .Select(match => groups.Select(group => match.Groups[group]).First(group => group.Success).Value)
            .Select(WebUtility.HtmlDecode)
            .Where(id => id.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

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
