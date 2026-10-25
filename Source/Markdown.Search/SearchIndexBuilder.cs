using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdown.Renderer;
using Markdown.Renderer.MarkdigExtension;

namespace Markdown.Search;

public sealed class SearchIndexBuilder
{
    private static readonly Regex HeadingRegex = new(
        "<h(?<level>[1-6])(?<attributes>[^>]*)>(?<title>.*?)</h\\k<level>>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex IdRegex = new("\\bid=\"(?<id>[^\"]+)\"", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex OperationClassRegex = new("\\bclass=\"(?:[^\"]*\\s)?openapi-operation(?:\\s[^\"]*)?\"", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    // Headings that start a new part of an API reference instead of continuing the current operation.
    private static readonly Regex OpenApiSectionClassRegex = new("\\bclass=\"(?:[^\"]*\\s)?openapi-(?:operation|tag|overview)(?:\\s[^\"]*)?\"", RegexOptions.Compiled | RegexOptions.CultureInvariant);
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
        var sectionIdBeforeOpenApiBlock = string.Empty;
        foreach (var item in document.Descendants().ToArray())
        {
            if (item is HeadingBlock heading)
                sectionId = heading.GetAttributes().Id ?? string.Empty;
            else if (item is HtmlBlock htmlBlock)
            {
                // Code after an API reference belongs to the section that contains the reference, not to its last operation.
                var blockHtml = htmlBlock.Lines.ToString();
                if (blockHtml.Contains(OpenApiMarkdownRenderer.BlockStartMarker, StringComparison.Ordinal))
                    sectionIdBeforeOpenApiBlock = sectionId;
                if (blockHtml.Contains(OpenApiMarkdownRenderer.BlockEndMarker, StringComparison.Ordinal))
                    sectionId = sectionIdBeforeOpenApiBlock;
            }

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
        var entryOutsideOpenApi = -1;
        if (!string.IsNullOrWhiteSpace(introduction) || introductionCode is { Count: > 0 })
        {
            AddEntry(title, string.Empty, url, introduction, introductionCode);
            entryOutsideOpenApi = _entries.Count - 1;
        }

        // Parameters, responses, examples, schemas and description headings of an API operation belong to the
        // operation's entry, so a search finds the operation once instead of each of its sections.
        var openApiBlocks = GetOpenApiBlockRanges(html, headings);
        var operationEntry = -1;
        for (var index = 0; index < headings.Length; index++)
        {
            var heading = headings[index];
            var attributes = heading.Groups["attributes"].Value;
            var contentStart = heading.Index + heading.Length;
            var contentEnd = index + 1 < headings.Length ? headings[index + 1].Index : html.Length;
            var openApiBlock = openApiBlocks.FirstOrDefault(block => heading.Index >= block.Start && heading.Index < block.End);
            var inOpenApiBlock = openApiBlock != default;
            if (inOpenApiBlock && contentEnd > openApiBlock.End)
            {
                // Text after the reference and before the next heading belongs to the section that contains the reference.
                var afterBlock = ExtractText(html[(openApiBlock.End + OpenApiMarkdownRenderer.BlockEndMarker.Length)..contentEnd]);
                if (entryOutsideOpenApi >= 0) AppendToEntry(entryOutsideOpenApi, afterBlock, null);
                else if (!string.IsNullOrWhiteSpace(afterBlock)) AddEntry(title, string.Empty, url, afterBlock, null);
                contentEnd = openApiBlock.End;
            }

            if (operationEntry >= 0 && inOpenApiBlock && !OpenApiSectionClassRegex.IsMatch(attributes))
            {
                var sectionText = $"{ExtractText(heading.Groups["title"].Value)} {ExtractText(html[contentStart..contentEnd])}";
                var subsectionId = IdRegex.Match(heading.Groups["attributes"].Value);
                AppendToEntry(operationEntry, sectionText,
                    subsectionId.Success ? codeBySection.GetValueOrDefault(subsectionId.Groups["id"].Value) : null);
                continue;
            }
            operationEntry = -1;

            var idMatch = IdRegex.Match(heading.Groups["attributes"].Value);
            if (!idMatch.Success) continue;

            var section = ExtractText(heading.Groups["title"].Value);
            var text = ExtractText(html[contentStart..contentEnd]);
            var sectionUrl = $"{url}#{Uri.EscapeDataString(idMatch.Groups["id"].Value)}";
            AddEntry(title, section, sectionUrl, text, codeBySection.GetValueOrDefault(idMatch.Groups["id"].Value));
            if (inOpenApiBlock && OperationClassRegex.IsMatch(attributes))
                operationEntry = _entries.Count - 1;
            else if (!inOpenApiBlock)
                entryOutsideOpenApi = _entries.Count - 1;
        }
    }

    /// <summary>
    /// The ranges between the markers around each expanded <c>openapi</c> block. Markdown that contains operations
    /// without markers, such as a specification page, counts as one block, as in the renderer.
    /// </summary>
    private static IReadOnlyList<(int Start, int End)> GetOpenApiBlockRanges(string html, IReadOnlyList<Match> headings)
    {
        var ranges = new List<(int Start, int End)>();
        var position = 0;
        while (true)
        {
            var start = html.IndexOf(OpenApiMarkdownRenderer.BlockStartMarker, position, StringComparison.Ordinal);
            if (start < 0) break;
            var end = html.IndexOf(OpenApiMarkdownRenderer.BlockEndMarker, start, StringComparison.Ordinal);
            if (end < 0) break;
            ranges.Add((start, end));
            position = end + OpenApiMarkdownRenderer.BlockEndMarker.Length;
        }

        if (ranges.Count == 0 && headings.Any(heading => OperationClassRegex.IsMatch(heading.Groups["attributes"].Value)))
            ranges.Add((0, html.Length));
        return ranges;
    }

    private void AppendToEntry(int index, string text, List<string>? code)
    {
        var entry = _entries[index];
        var joinedText = string.IsNullOrWhiteSpace(text) ? entry.Text : $"{entry.Text} {text}".Trim();
        var joinedCode = code is { Count: > 0 } ? $"{entry.Code} {string.Join(" ", code)}".Trim() : entry.Code;
        _entries[index] = entry with { Text = joinedText, Code = joinedCode };
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
