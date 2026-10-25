using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers.Html;
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
    private static readonly Regex InlineMarkdownLinkRegex = new(
        @"(?<![\p{L}\p{N}_])\[(?<label>[^\[\]]+)\]\((?:[^()]|\([^()]*\))*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly SearchIndexJsonContext JsonContext = new(JsonOptions);

    private readonly List<SearchIndexEntry> _entries = new();
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseGridTables()
        .UsePipeTables()
        .UseAdvancedExtensions()
        .UseHeadingIds()
        .UseGitHubAlerts()
        .UseIconsAndEmojis()
        .UseKeyboardKeys()
        .Build();

    public void Clear() => _entries.Clear();

    public void AddPage(string title, string url, string markdown)
    {
        _entries.RemoveAll(entry => entry.PageUrl.Equals(url, StringComparison.Ordinal));

        var document = Markdig.Markdown.Parse(markdown, _pipeline);
        var codeBySection = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var sectionId = string.Empty;
        foreach (var item in document.Descendants().ToArray())
        {
            if (item is HeadingBlock heading)
                sectionId = heading.GetAttributes().Id ?? string.Empty;

            if (item is CodeInline inline)
            {
                AddCode(codeBySection, sectionId, inline.Content);
                NormalizeMarkdownLinks(inline);
            }
            else if (item is CodeBlock block)
            {
                if (IsSearchableCodeBlock(block))
                    AddCode(codeBySection, sectionId, GetCode(block));
                block.Parent!.Remove(block);
            }
        }

        var html = document.ToHtml(_pipeline);
        var headings = HeadingRegex.Matches(html).Cast<Match>().ToArray();

        if (headings.Length == 0)
        {
            AddEntry(title, string.Empty, url, ExtractText(html), codeBySection.GetValueOrDefault(string.Empty));
            return;
        }

        var introduction = ExtractText(html[..headings[0].Index]);
        var introductionCode = codeBySection.GetValueOrDefault(string.Empty);
        if (!string.IsNullOrWhiteSpace(introduction) || introductionCode is { Count: > 0 })
            AddEntry(title, string.Empty, url, introduction, introductionCode);

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
            AddEntry(title, section, sectionUrl, text, codeBySection.GetValueOrDefault(idMatch.Groups["id"].Value));
        }
    }

    public void WriteIndex(string outputPath)
    {
        Directory.CreateDirectory(outputPath);
        var indexPath = Path.Combine(outputPath, "search-index.json");
        File.WriteAllText(indexPath, JsonSerializer.Serialize(_entries, JsonContext.ListSearchIndexEntry), Encoding.UTF8);
    }

    private void AddEntry(string title, string section, string url, string text, List<string>? code) =>
        _entries.Add(new SearchIndexEntry(title, section, url, url.Split('#')[0], text, string.Join(" ", code ?? [])));

    private static void AddCode(Dictionary<string, List<string>> codeBySection, string sectionId, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        if (!codeBySection.TryGetValue(sectionId, out var sectionCode))
            codeBySection.Add(sectionId, sectionCode = []);
        sectionCode.Add(code.Trim());
    }

    private static string GetCode(CodeBlock block) =>
        block is LeafBlock leaf && leaf.Lines.Lines is not null
            ? string.Join(Environment.NewLine, leaf.Lines.Lines.Select(line => line.Slice.ToString()))
            : string.Empty;

    private static bool IsSearchableCodeBlock(CodeBlock block)
    {
        if (block is not FencedCodeBlock fenced) return true;
        var info = fenced.Info?.Trim() ?? string.Empty;
        var languageEnd = info.IndexOfAny([' ', '\t']);
        var language = languageEnd < 0 ? info : info[..languageEnd];

        return language.ToLowerInvariant() is not (
            "mermaid" or "plantuml" or "puml" or "context" or "isometric" or "hero" or "icon-search" or "openapi");
    }

    private static void NormalizeMarkdownLinks(CodeInline code)
    {
        if (!code.Content.Contains("](", StringComparison.Ordinal)) return;
        code.Content = InlineMarkdownLinkRegex.Replace(code.Content, match =>
            Markdig.Markdown.ToPlainText(match.Groups["label"].Value, LinkLabelPipeline).Trim());
    }

    private static readonly MarkdownPipeline LinkLabelPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

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
    string Text,
    string Code);

[JsonSerializable(typeof(List<SearchIndexEntry>))]
internal partial class SearchIndexJsonContext : JsonSerializerContext
{
}
