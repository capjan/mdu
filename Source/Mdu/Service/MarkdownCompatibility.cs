using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdown.Linter;
using StaticCode.Diagrams;

namespace Mdu.Service;

public sealed record CompatibilityIssue(int LineNumber, string Code, string Message);

public static class MarkdownCompatibility
{
    private static readonly Regex AdmonitionHeader = new(
        @"^(?<indent> *)(?<marker>!!!|\?\?\?\+?)[ \t]*(?<type>[^ \t]+)?(?:[ \t]+(?<title>.*))?$",
        RegexOptions.Compiled);
    private static readonly Regex TabHeader = new(
        "^(?<indent> *)===[ \\t]+[\\\"'](?<title>.*?)[\\\"'][ \\t]*$",
        RegexOptions.Compiled);
    private static readonly Regex CardGridOpening = new(
        """<div\b(?=[^>]*\bclass=["'][^"']*(?:\bgrid\b[^"']*\bcards\b|\bcards\b[^"']*\bgrid\b)[^"']*["'])(?=[^>]*\bmarkdown\b)[^>]*>""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DivTag = new("</?div\\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Fence = new(@"^\s*(```+|~~~+)", RegexOptions.Compiled);
    private static readonly Regex BlockquotePrefix = new(@"^\s*(?:>\s*)*", RegexOptions.Compiled);
    private static readonly Regex AnnotationMarker = new(@"^\s*(?:>\s*)*\{ \.annotate \}\s*$", RegexOptions.Compiled);
    private static readonly Regex AttributeFence = new(
        @"^(?<prefix>\s*(?:>\s*)*)(?<fence>```+|~~~+)[ \t]*\{(?<attributes>[^}]*)\}[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex FenceAttribute = new(
        """\.(?<class>[A-Za-z0-9_+#-]+)|title=(?:"(?<title>[^"]*)"|'(?<title>[^']*)')|\S+""", RegexOptions.Compiled);

    public static IReadOnlyList<CompatibilityIssue> FindIssues(string content)
    {
        var issues = new List<CompatibilityIssue>();
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        var inFence = false;
        string fence = string.Empty;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var fenceMatch = Fence.Match(BlockquotePrefix.Replace(line, string.Empty));
            if (fenceMatch.Success)
            {
                if (!inFence)
                {
                    inFence = true;
                    fence = fenceMatch.Groups[1].Value;
                    if (ConvertAttributeFence(line) != line)
                        issues.Add(new CompatibilityIssue(index + 1, RuleCodes.CodeAnnotations, "Material code fence attributes such as '{ .yaml .annotate }' are unsupported; run 'mdu fix' to write the language after the fence."));
                }
                else if (IsClosingFence(fenceMatch.Groups[1].Value, fence))
                {
                    inFence = false;
                }

                continue;
            }

            if (inFence) continue;
            if (line.TrimStart().StartsWith("!!!", StringComparison.Ordinal) ||
                line.TrimStart().StartsWith("???", StringComparison.Ordinal))
                issues.Add(new CompatibilityIssue(index + 1, RuleCodes.MaterialAdmonition, "Material for MkDocs admonition syntax is unsupported; run 'mdu fix' to convert it to a GitHub Alert."));
            else if (line.TrimStart().StartsWith("=== ", StringComparison.Ordinal))
                issues.Add(new CompatibilityIssue(index + 1, RuleCodes.MaterialTabs, "Material for MkDocs tab syntax is unsupported; run 'mdu fix' to convert tabs to headings."));
            else if (AnnotationMarker.IsMatch(line))
                issues.Add(new CompatibilityIssue(index + 1, RuleCodes.CodeAnnotations, "Material annotations outside code blocks are unsupported; code annotations work without '{ .annotate }'. Run 'mdu fix' to remove the marker."));
        }

        foreach (var cardGrid in FindCardGrids(content))
        {
            issues.Add(new CompatibilityIssue(cardGrid.StartLine + 1, RuleCodes.MaterialCards, "Material card grids are unsupported; run 'mdu fix' to convert them to Markdown lists."));
        }

        var document = Markdig.Markdown.Parse(content, new MarkdownPipelineBuilder().Build());
        foreach (var block in document.Descendants<FencedCodeBlock>())
        {
            if (!string.Equals(block.Info?.Trim(), "mermaid", StringComparison.OrdinalIgnoreCase)) continue;
            var source = GetCode(block);
            if (!DiagramRenderer.TryRenderSvg("mermaid", source, out _))
                issues.Add(new CompatibilityIssue(block.Line + 1, RuleCodes.UnsupportedMermaid, "This Mermaid diagram uses syntax that mdu cannot render yet."));
        }

        return issues;
    }

    public static string FixMarkdown(string content)
    {
        var original = content;
        var normalized = content.ReplaceLineEndings("\n");
        content = ConvertCardGrids(normalized);
        content = ConvertBlockSyntax(content, convertTabs: true);
        content = ConvertBlockSyntax(content, convertTabs: false);
        content = RemoveMaterialAnnotationMarkers(content);
        if (content == normalized) return original;
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : original.Contains('\r') ? "\r" : "\n";
        return newline == "\n" ? content : content.Replace("\n", newline, StringComparison.Ordinal);
    }

    private static string ConvertCardGrids(string content)
    {
        var lines = content.Split('\n');
        var blocks = FindCardGrids(content).ToDictionary(block => block.StartLine);
        var output = new List<string>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (!blocks.TryGetValue(index, out var block))
            {
                output.Add(lines[index]);
                continue;
            }

            output.AddRange(ConvertCardGrid(block.BodyLines));
            index = block.EndLine;
        }

        return string.Join('\n', output);
    }

    private static IReadOnlyList<CardGridBlock> FindCardGrids(string content)
    {
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        var blocks = new List<CardGridBlock>();
        for (var index = 0; index < lines.Length; index++)
        {
            var fenceMatch = Fence.Match(BlockquotePrefix.Replace(lines[index], string.Empty));
            if (fenceMatch.Success)
            {
                var fence = fenceMatch.Groups[1].Value;
                while (++index < lines.Length)
                {
                    var close = Fence.Match(BlockquotePrefix.Replace(lines[index], string.Empty));
                    if (close.Success && IsClosingFence(close.Groups[1].Value, fence)) break;
                }
                continue;
            }

            if (!CardGridOpening.IsMatch(lines[index])) continue;
            var depth = 1;
            var bodyLines = new List<string>();
            var inFence = false;
            var fenceCharacter = string.Empty;
            for (var end = index + 1; end < lines.Length; end++)
            {
                var currentFence = Fence.Match(BlockquotePrefix.Replace(lines[end], string.Empty));
                if (currentFence.Success)
                {
                    if (!inFence)
                    {
                        inFence = true;
                        fenceCharacter = currentFence.Groups[1].Value;
                    }
                    else if (IsClosingFence(currentFence.Groups[1].Value, fenceCharacter))
                    {
                        inFence = false;
                    }
                }

                if (!inFence)
                {
                    foreach (Match tag in DivTag.Matches(lines[end]))
                    {
                        if (tag.Value.StartsWith("</", StringComparison.Ordinal)) depth--;
                        else if (!tag.Value.EndsWith("/>", StringComparison.Ordinal)) depth++;
                        if (depth == 0) break;
                    }
                }

                if (depth == 0)
                {
                    blocks.Add(new CardGridBlock(index, end, bodyLines));
                    index = end;
                    break;
                }

                bodyLines.Add(lines[end]);
            }
        }

        return blocks;
    }

    private static string RemoveMaterialAnnotationMarkers(string content)
    {
        var output = new List<string>();
        var inFence = false;
        string fence = string.Empty;
        foreach (var line in content.ReplaceLineEndings("\n").Split('\n'))
        {
            var fenceMatch = Fence.Match(BlockquotePrefix.Replace(line, string.Empty));
            if (fenceMatch.Success)
            {
                if (!inFence)
                {
                    inFence = true;
                    fence = fenceMatch.Groups[1].Value;
                    output.Add(ConvertAttributeFence(line));
                    continue;
                }
                if (IsClosingFence(fenceMatch.Groups[1].Value, fence))
                {
                    inFence = false;
                }
                output.Add(line);
                continue;
            }

            if (!inFence && AnnotationMarker.IsMatch(line)) continue;
            output.Add(line);
        }

        return string.Join('\n', output);
    }

    // A fence closes with the same character, at least as many times as it opened, so a ``` line inside a ```` block is content.
    private static bool IsClosingFence(string run, string opening) =>
        opening.Length > 0 && run[0] == opening[0] && run.Length >= opening.Length;

    // Material writes "``` { .yaml .annotate title="mdu.yml" }"; MDU reads "```yaml title="mdu.yml"" and annotates without a class.
    private static string ConvertAttributeFence(string line)
    {
        var match = AttributeFence.Match(line);
        if (!match.Success) return line;
        var language = string.Empty;
        string? title = null;
        foreach (Match attribute in FenceAttribute.Matches(match.Groups["attributes"].Value))
        {
            if (attribute.Groups["class"].Success)
            {
                var name = attribute.Groups["class"].Value;
                if (name == "annotate") continue;
                if (language.Length > 0) return line;
                language = name;
            }
            else if (attribute.Groups["title"].Success && title is null)
            {
                title = attribute.Groups["title"].Value;
            }
            else
            {
                return line;
            }
        }

        var titleArgument = title is null ? string.Empty : $" title=\"{title}\"";
        return $"{match.Groups["prefix"].Value}{match.Groups["fence"].Value}{language}{titleArgument}";
    }

    private static string[] ConvertCardGrid(IEnumerable<string> bodyLines)
    {
        var result = new List<string>();
        List<string>? current = null;
        foreach (var rawLine in bodyLines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                if (current is not null) AddItem(current);
                current = [line[2..].TrimEnd()];
            }
            else if (current is not null && line.Length > 0)
            {
                current.Add(DivTag.Replace(line, string.Empty).Trim());
            }
        }

        if (current is not null) AddItem(current);
        return result.ToArray();

        void AddItem(List<string> item)
        {
            var text = string.Join(" ", item).Trim();
            result.Add($"- {text}");
        }
    }

    private static string ConvertBlockSyntax(string content, bool convertTabs) =>
        ProcessLines(content.ReplaceLineEndings("\n").Split('\n'), convertTabs, 0);

    private static string ProcessLines(string[] lines, bool convertTabs, int startingIndent)
    {
        var output = new List<string>();
        var inFence = false;
        string fence = string.Empty;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var fenceMatch = Fence.Match(BlockquotePrefix.Replace(line, string.Empty));
            if (fenceMatch.Success)
            {
                if (!inFence)
                {
                    inFence = true;
                    fence = fenceMatch.Groups[1].Value;
                }
                else if (IsClosingFence(fenceMatch.Groups[1].Value, fence))
                {
                    inFence = false;
                }
            }

            var match = !inFence
                ? (convertTabs ? TabHeader.Match(line) : AdmonitionHeader.Match(line))
                : Match.Empty;
            if (!match.Success)
            {
                output.Add(line);
                continue;
            }

            var indent = match.Groups["indent"].Value.Length;
            if (indent < startingIndent)
            {
                output.Add(line);
                continue;
            }

            var body = new List<string>();
            var bodyEnd = index + 1;
            var sawBody = false;
            while (bodyEnd < lines.Length)
            {
                var candidate = lines[bodyEnd];
                var candidateIndent = candidate.Length - candidate.TrimStart(' ').Length;
                if (candidate.Trim().Length > 0 && candidateIndent <= indent) break;
                if (candidate.Trim().Length > 0) sawBody = true;
                body.Add(candidate);
                bodyEnd++;
            }

            var dedent = indent + (sawBody ? 4 : 0);
            var bodyLines = body.Select(candidate => candidate.Trim().Length == 0
                ? string.Empty
                : candidate[Math.Min(dedent, candidate.Length)..]).ToArray();
            var convertedBody = ProcessLines(bodyLines, convertTabs, 0).TrimEnd('\n');
            var title = match.Groups["title"].Success ? match.Groups["title"].Value.Trim() : string.Empty;
            if (title.Length >= 2 && title[0] == title[^1] && title[0] is '\'' or '"')
                title = title[1..^1];

            if (convertTabs)
            {
                output.Add(new string(' ', indent) + "### " + title);
                if (convertedBody.Length > 0) output.AddRange(convertedBody.Split('\n'));
            }
            else
            {
                var kind = match.Groups["type"].Value.Trim().TrimEnd(':').ToLowerInvariant();
                var alert = kind switch
                {
                    "tip" => "TIP",
                    "warning" => "WARNING",
                    "danger" or "bug" => "CAUTION",
                    _ => "NOTE"
                };
                output.Add(new string(' ', indent) + $"> [!{alert}]");
                if (title.Length > 0) output.Add(new string(' ', indent) + $"> **{title}**");
                if (convertedBody.Length > 0)
                    output.AddRange(convertedBody.Split('\n').Select(bodyLine => new string(' ', indent) + (bodyLine.Length == 0 ? ">" : $"> {bodyLine}")));
            }

            if (bodyEnd < lines.Length && lines[bodyEnd].Trim().Length > 0)
                output.Add(string.Empty);

            index = bodyEnd - 1;
        }

        return string.Join('\n', output);
    }

    private static string GetCode(FencedCodeBlock block)
    {
        if (block.Lines.Lines is null) return string.Empty;
        return string.Join('\n', block.Lines.Lines.Select(line => line.Slice.ToString()));
    }

    private sealed record CardGridBlock(int StartLine, int EndLine, IReadOnlyList<string> BodyLines);
}
