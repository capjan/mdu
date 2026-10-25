using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdown.Renderer.MarkdigExtension;

namespace Markdown.Search;

public sealed class SearchIndexBuilder
{
    private static readonly Regex HeadingRegex = new(
        "<h(?<level>[1-6])(?<attributes>[^>]*)>(?<title>.*?)</h\\k<level>>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex IdRegex = new("\\bid=\"(?<id>[^\"]+)\"", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ScriptAndStyleRegex = new("<(script|style)\\b[^>]*>.*?</\\1>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly SearchIndexJsonContext JsonContext = new(JsonOptions);

    private readonly List<SearchIndexEntry> _entries = new();
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseGridTables()
        .UsePipeTables()
        .UseAdvancedExtensions()
        .UseGitHubAlerts()
        .UseIconsAndEmojis()
        .UseKeyboardKeys()
        .Build();

    public void Clear() => _entries.Clear();

    public void AddPage(string title, string url, string markdown)
    {
        _entries.RemoveAll(entry => entry.PageUrl.Equals(url, StringComparison.Ordinal));

        var document = Markdig.Markdown.Parse(markdown, _pipeline);
        foreach (var block in document.Descendants<CodeBlock>().ToArray())
            block.Parent!.Remove(block);

        // Documentation also shows Markdown links inside inline code spans.
        // Keep ordinary inline code literal, but index those examples by their labels.
        foreach (var code in document.Descendants<CodeInline>())
        {
            if (code.Content.Contains('[') &&
                Markdig.Markdown.Parse(code.Content).Descendants<LinkInline>().Any())
                code.Content = Markdig.Markdown.ToPlainText(code.Content).Trim();
        }

        var html = document.ToHtml(_pipeline);
        var headings = HeadingRegex.Matches(html).Cast<Match>().ToArray();

        if (headings.Length == 0)
        {
            AddEntry(title, string.Empty, url, ExtractText(html));
            return;
        }

        var introduction = ExtractText(html[..headings[0].Index]);
        if (!string.IsNullOrWhiteSpace(introduction))
            AddEntry(title, string.Empty, url, introduction);

        for (var index = 0; index < headings.Length; index++)
        {
            var heading = headings[index];
            var idMatch = IdRegex.Match(heading.Groups["attributes"].Value);
            if (!idMatch.Success) continue;

            var section = ExtractText(heading.Groups["title"].Value);
            var contentStart = heading.Index + heading.Length;
            var contentEnd = index + 1 < headings.Length ? headings[index + 1].Index : html.Length;
            var text = ExtractText(html[contentStart..contentEnd]);
            var sectionUrl = $"{url}#{Uri.EscapeDataString(idMatch.Groups["id"].Value)}";
            AddEntry(title, section, sectionUrl, text);
        }
    }

    public void WriteIndex(string outputPath)
    {
        Directory.CreateDirectory(outputPath);
        var indexPath = Path.Combine(outputPath, "search-index.json");
        File.WriteAllText(indexPath, JsonSerializer.Serialize(_entries, JsonContext.ListSearchIndexEntry), Encoding.UTF8);
    }

    private void AddEntry(string title, string section, string url, string text) =>
        _entries.Add(new SearchIndexEntry(title, section, url, url.Split('#')[0], text));

    private static string ExtractText(string html)
    {
        var withoutScripts = ScriptAndStyleRegex.Replace(html, " ");
        var withoutTags = HtmlTagRegex.Replace(withoutScripts, " ");
        return WhitespaceRegex.Replace(WebUtility.HtmlDecode(withoutTags), " ").Trim();
    }
}

internal sealed record SearchIndexEntry(
    string Title,
    string Section,
    string Url,
    [property: JsonIgnore] string PageUrl,
    string Text);

[JsonSerializable(typeof(List<SearchIndexEntry>))]
internal partial class SearchIndexJsonContext : JsonSerializerContext
{
}
