using System.Text.RegularExpressions;

namespace Markdown.Renderer;

public static class HtmlFragment
{
    private static readonly Regex StartTagRegex = new(@"<[a-zA-Z][^<>]*>", RegexOptions.Compiled);
    private static readonly Regex BooleanAttributeRegex = new(
        @"""[^""]*""|'[^']*'|\s(?<name>disabled|hidden|checked|selected|required|multiple|controls|playsinline|autoplay|muted|loop|data-media-error)(?=[\s/>])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string NormalizeVoidElements(string html)
    {
        var normalized = Regex.Replace(html, @"<(input|br|hr|img|source)(?<attributes>[^<>]*?)(?<!/)>", "<$1${attributes} />", RegexOptions.IgnoreCase);
        return StartTagRegex.Replace(normalized, tag =>
            BooleanAttributeRegex.Replace(tag.Value, match =>
            {
                var name = match.Groups["name"].Value;
                return name.Length == 0 ? match.Value : $" {name}=\"{name}\"";
            }));
    }
}
