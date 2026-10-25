using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdown.Renderer.MarkdigExtension;
using StaticCode.Icons;
using YamlDotNet.RepresentationModel;

namespace Markdown.Renderer;

/// <summary>
/// The options of a page with <c>layout: dashboard</c> in its front matter.
/// </summary>
public sealed record DashboardOptions(string? SearchUrl, bool Clock, bool Status, bool NewTab);

/// <summary>
/// Renders a page with <c>layout: dashboard</c> as a start page: every second-level heading becomes a group and every
/// list item with a link becomes a tile. A thematic break starts a new row of groups. The source stays an ordinary
/// Markdown link list.
/// </summary>
public static class Dashboard
{
    public const string Layout = "dashboard";

    private static readonly Dictionary<string, string> SearchEngines = new(StringComparer.OrdinalIgnoreCase)
    {
        ["duckduckgo"] = "https://duckduckgo.com/?q={query}",
        ["google"] = "https://www.google.com/search?q={query}",
        ["bing"] = "https://www.bing.com/search?q={query}",
        ["startpage"] = "https://www.startpage.com/do/search?q={query}",
        ["ecosia"] = "https://www.ecosia.org/search?q={query}"
    };
    private static readonly Regex CamelCaseBoundaryRegex = new("(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);
    private static readonly Regex NonSlugCharactersRegex = new("[^a-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex LeadingSeparatorRegex = new(@"\A\s*(?:[—–:|·-]\s*)?", RegexOptions.Compiled);
    private static readonly Regex AnchorTagRegex = new("</?a\\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HeadingIdRegex = new("<h[1-6][^>]*\\bid=\"(?<id>[^\"]+)\"", RegexOptions.Compiled);
    private static readonly char[] WordSeparators = [' ', '@', '-', '_', '.', '/', '&', '+'];

    public static string Styles => Resources.DashboardCssContent();
    public static string Script => Resources.DashboardScriptContent();

    /// <summary>
    /// Reads the dashboard options from a page's front matter, or returns null when the page has no dashboard layout.
    /// </summary>
    public static DashboardOptions? ReadFrontMatter(string? frontMatter)
    {
        if (string.IsNullOrWhiteSpace(frontMatter)) return null;

        var lines = frontMatter.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[0].Trim() == "---") lines.RemoveAt(0);
        if (lines.Count > 0 && lines[^1].Trim() == "---") lines.RemoveAt(lines.Count - 1);

        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(string.Join('\n', lines)));
            root = stream.Documents.FirstOrDefault()?.RootNode as YamlMappingNode;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return null;
        }

        if (root is null || !string.Equals(ReadScalar(root, "layout")?.Trim(), Layout, StringComparison.OrdinalIgnoreCase))
            return null;

        var options = root.Children.TryGetValue(new YamlScalarNode("dashboard"), out var node) ? node as YamlMappingNode : null;
        return new DashboardOptions(
            ReadSearchUrl(options is null ? null : ReadScalar(options, "search")),
            ReadBoolean(options, "clock", true),
            ReadBoolean(options, "status", false),
            !string.Equals(options is null ? null : ReadScalar(options, "open")?.Trim(), "same-tab", StringComparison.OrdinalIgnoreCase));
    }

    public static string Render(MarkdownDocument document, MarkdownPipeline pipeline, DashboardOptions options)
    {
        var html = new StringBuilder();
        html.Append("<div class=\"dashboard\"");
        if (options.SearchUrl is not null) html.Append($" data-dashboard-search=\"{HtmlEncoder.Default.Encode(options.SearchUrl)}\"");
        if (options.Status) html.Append(" data-dashboard-status=\"true\"");
        html.AppendLine(">");

        var blocks = document.ToList();
        var firstGroup = blocks.FindIndex(block => block is HeadingBlock { Level: 2 });
        var usedIds = ReadHeadingIds(blocks.OfType<HeadingBlock>(), pipeline);
        var introduction = firstGroup < 0 ? blocks : blocks.Take(firstGroup).ToList();
        foreach (var block in introduction) html.Append(RenderBlock(block, pipeline));
        html.AppendLine(RenderToolbar(options, firstGroup >= 0));
        html.AppendLine($"<div id=\"dashboard-results\" role=\"grid\" aria-label=\"Links and pages\" aria-readonly=\"true\"{(firstGroup < 0 ? " hidden" : string.Empty)}>");
        if (firstGroup >= 0) html.Append(RenderGroups(blocks.Skip(firstGroup), pipeline, options, usedIds));

        // The filter script fills these containers, so they exist even on a dashboard without groups.
        html.AppendLine("<div class=\"dashboard-pages\" id=\"dashboard-pages\" role=\"rowgroup\" aria-label=\"Pages\" hidden>");
        html.AppendLine("<div role=\"row\"><p class=\"dashboard-pages-title\" id=\"dashboard-pages-title\" role=\"gridcell\">Pages</p></div>");
        html.AppendLine("<ul class=\"dashboard-pages-list\" id=\"dashboard-pages-list\" role=\"presentation\"></ul>");
        html.AppendLine("</div>");
        html.AppendLine("</div>");
        html.AppendLine("<p class=\"dashboard-empty\" id=\"dashboard-empty\" hidden></p>");
        html.AppendLine("</div>");
        return html.ToString();
    }

    private static string RenderGroups(IEnumerable<Block> blocks, MarkdownPipeline pipeline, DashboardOptions options, HashSet<string> usedIds)
    {
        var html = new StringBuilder();
        html.AppendLine("<div class=\"dashboard-row\" role=\"presentation\">");
        var groupOpen = false;
        var tileIndex = 0;
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ThematicBreakBlock:
                    if (groupOpen) html.AppendLine("</section>");
                    groupOpen = false;
                    html.AppendLine("</div>");
                    html.AppendLine("<div class=\"dashboard-row\" role=\"presentation\">");
                    break;
                case HeadingBlock { Level: 2 } heading:
                    if (groupOpen) html.AppendLine("</section>");
                    groupOpen = true;
                    html.AppendLine("<section class=\"dashboard-group\" role=\"rowgroup\">");
                    html.AppendLine("<div role=\"row\"><div role=\"gridcell\">");
                    html.Append(RenderBlock(heading, pipeline));
                    html.AppendLine("</div></div>");
                    break;
                case ListBlock list:
                    if (groupOpen) html.Append(RenderTiles(list, pipeline, options, usedIds, ref tileIndex));
                    else
                    {
                        html.AppendLine("<div role=\"rowgroup\">");
                        html.Append(RenderTiles(list, pipeline, options, usedIds, ref tileIndex));
                        html.AppendLine("</div>");
                    }
                    break;
                default:
                    if (!groupOpen) html.AppendLine("<div role=\"rowgroup\">");
                    html.AppendLine("<div role=\"row\"><div role=\"gridcell\">");
                    html.Append(RenderBlock(block, pipeline));
                    html.AppendLine("</div></div>");
                    if (!groupOpen) html.AppendLine("</div>");
                    break;
            }
        }

        if (groupOpen) html.AppendLine("</section>");
        html.AppendLine("</div>");
        return html.ToString();
    }

    private static HashSet<string> ReadHeadingIds(IEnumerable<HeadingBlock> headings, MarkdownPipeline pipeline)
    {
        var rendered = Render(renderer =>
        {
            foreach (var heading in headings) renderer.Render(heading);
        }, pipeline);
        return HeadingIdRegex.Matches(rendered).Select(match => match.Groups["id"].Value).ToHashSet(StringComparer.Ordinal);
    }

    private static string RenderToolbar(DashboardOptions options, bool hasGroups)
    {
        var html = new StringBuilder();
        html.AppendLine("<div class=\"dashboard-toolbar\">");
        html.AppendLine("<div class=\"dashboard-search\">");
        html.AppendLine("<svg viewBox=\"0 0 20 20\" aria-hidden=\"true\" focusable=\"false\"><circle cx=\"8.5\" cy=\"8.5\" r=\"5.5\"></circle><path d=\"m12.5 12.5 5 5\"></path></svg>");
        html.AppendLine("<label class=\"visually-hidden\" for=\"dashboard-search-input\">Filter links</label>");
        var placeholder = options.SearchUrl is null ? "Filter links and pages…" : "Filter links, pages, or search the web…";
        html.AppendLine($"<input id=\"dashboard-search-input\" type=\"search\" role=\"combobox\" aria-controls=\"dashboard-results\" aria-haspopup=\"grid\" aria-expanded=\"{hasGroups.ToString().ToLowerInvariant()}\" aria-autocomplete=\"list\" autocomplete=\"off\" spellcheck=\"false\" placeholder=\"{placeholder}\">");
        html.AppendLine("<span class=\"keys dashboard-search-key\" aria-hidden=\"true\"><kbd class=\"key-slash\"><span class=\"key-label\">/</span></kbd></span>");
        html.AppendLine("<button class=\"dashboard-search-clear\" id=\"dashboard-search-clear\" type=\"button\" aria-label=\"Clear filter\" title=\"Clear filter\"><svg viewBox=\"0 0 16 16\" aria-hidden=\"true\" focusable=\"false\"><path d=\"m4.5 4.5 7 7m0-7-7 7\"></path></svg></button>");
        html.AppendLine("</div>");
        html.AppendLine("<p class=\"dashboard-search-hint\" id=\"dashboard-search-hint\"></p>");
        html.AppendLine("<p class=\"visually-hidden\" id=\"dashboard-search-status\" role=\"status\" aria-live=\"polite\" aria-atomic=\"true\"></p>");
        if (options.Clock) html.AppendLine("<time class=\"dashboard-clock\" id=\"dashboard-clock\"></time>");
        html.Append("</div>");
        return html.ToString();
    }

    private static string RenderTiles(ListBlock list, MarkdownPipeline pipeline, DashboardOptions options, HashSet<string> usedIds, ref int tileIndex)
    {
        var tiles = list.OfType<ListItemBlock>().Select(item => ReadTile(item, pipeline)).ToList();
        if (tiles.Count == 0 || tiles.All(tile => tile is null))
            return $"<div role=\"row\"><div role=\"gridcell\">{RenderBlock(list, pipeline)}</div></div>";

        var hasDescriptions = tiles.Any(tile => tile is not null && tile.Description.Length > 0);
        var html = new StringBuilder();
        html.AppendLine($"<ul class=\"dashboard-tiles{(hasDescriptions ? " has-descriptions" : string.Empty)}\" role=\"presentation\">");
        for (var index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            if (tile is null)
            {
                html.AppendLine($"<li class=\"dashboard-note\" role=\"row\"><div role=\"gridcell\">{RenderChildren((ListItemBlock)list[index], pipeline)}</div></li>");
                continue;
            }

            var external = Uri.TryCreate(tile.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
            var host = external ? uri!.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host : string.Empty;
            var subtitle = tile.Description.Length > 0
                ? tile.Description
                : HtmlEncoder.Default.Encode(host);

            string id;
            do id = $"dashboard-tile-{tileIndex++}";
            while (!usedIds.Add(id));

            html.Append("<li class=\"dashboard-tile\" role=\"row\">");
            html.Append($"<div class=\"dashboard-cell\" id=\"{id}\" role=\"gridcell\" aria-selected=\"false\">");
            html.Append($"<a class=\"dashboard-link\" href=\"{HtmlEncoder.Default.Encode(tile.Url)}\"");
            if (!string.IsNullOrEmpty(tile.Title)) html.Append($" title=\"{HtmlEncoder.Default.Encode(tile.Title)}\"");
            if (external && options.NewTab) html.Append(" target=\"_blank\" rel=\"noopener noreferrer\"");
            html.Append('>');
            html.Append(tile.IconHtml ?? RenderAutomaticIcon(tile.PlainName, host));
            html.Append("<span class=\"dashboard-text\">");
            html.Append($"<span class=\"dashboard-name\">{tile.NameHtml}</span>");
            if (subtitle.Length > 0) html.Append($"<span class=\"dashboard-description\">{subtitle}</span>");
            html.Append("</span>");
            if (options.Status && external) html.Append("<span class=\"dashboard-status\" role=\"img\" aria-label=\"Status unknown\"></span>");
            html.Append("</a>");
            // Further paragraphs or nested lists of the item stay below the tile, outside its link.
            if (tile.MoreHtml.Length > 0) html.Append($"<div class=\"dashboard-tile-more\">{tile.MoreHtml}</div>");
            html.AppendLine("</div></li>");
        }

        html.AppendLine("</ul>");
        return html.ToString();
    }

    private static DashboardTile? ReadTile(ListItemBlock item, MarkdownPipeline pipeline)
    {
        if (item.FirstOrDefault() is not ParagraphBlock { Inline: { } inlines }) return null;

        var parts = inlines.ToList();
        var more = string.Concat(item.Skip(1).Select(block => RenderBlock(block, pipeline)));
        if (ReadTaskListLink(parts, pipeline) is { } taskListTile) return taskListTile with { MoreHtml = more };
        var linkIndex = parts.FindIndex(inline => inline is LinkInline { IsImage: false });
        if (linkIndex < 0) return null;
        var link = (LinkInline)parts[linkIndex];
        if (string.IsNullOrWhiteSpace(link.Url)) return null;

        var before = parts.Take(linkIndex).Where(inline => inline is not LiteralInline literal || !literal.Content.IsEmptyOrWhitespace()).ToList();
        var linkChildren = link.ToList();
        var linkIcons = linkChildren.Where(IsIcon).ToList();
        var iconParts = before.Where(IsIcon).Concat(linkIcons).Take(1).ToList();
        var nameParts = linkChildren.Except(linkIcons).ToList();

        var plainName = PlainText(nameParts).Trim();
        return new DashboardTile(
            link.Url,
            link.Title,
            RenderInlines(nameParts, pipeline).Trim(),
            plainName,
            RenderDescription(string.Empty, parts.Skip(linkIndex + 1), pipeline),
            iconParts.Count == 0 ? null : $"<span class=\"dashboard-icon\">{RenderInlines(iconParts, pipeline).Trim()}</span>",
            more);
    }

    /// <summary>
    /// Renders the subtitle after a tile's link. A link cannot contain another link, so links in the subtitle keep only
    /// their text.
    /// </summary>
    private static string RenderDescription(string leadingText, IEnumerable<Inline> inlines, MarkdownPipeline pipeline)
    {
        // Escapes like Markdig does, so a leading dash stays a character that the separator pattern recognizes.
        var html = leadingText.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
                       .Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal)
                   + RenderInlines(inlines, pipeline);
        return LeadingSeparatorRegex.Replace(AnchorTagRegex.Replace(html, string.Empty), string.Empty, 1).Trim();
    }

    /// <summary>
    /// Reads <c>- [X](https://x.com)</c>, which Markdown parses as a checked task followed by an automatic link.
    /// </summary>
    private static DashboardTile? ReadTaskListLink(IReadOnlyList<Inline> parts, MarkdownPipeline pipeline)
    {
        if (parts is not [TaskList task, LiteralInline open, LinkInline { IsAutoLink: true } link, LiteralInline close, ..] ||
            open.Content.ToString() != "(" || !close.Content.ToString().StartsWith(')') || string.IsNullOrWhiteSpace(link.Url))
            return null;

        var name = task.Checked ? "X" : " ";
        var description = RenderDescription(close.Content.ToString()[1..], parts.Skip(4), pipeline);
        return new DashboardTile(link.Url, null, name, name, description, null, string.Empty);
    }

    private static bool IsIcon(Inline inline) =>
        inline is IconInline or EmojiShortcodeInline or LinkInline { IsImage: true };

    /// <summary>
    /// Picks a Dashboard Icon from the tile name or the host name, for example <c>dashboard-github</c> for
    /// <c>github.com</c>, and falls back to the initials of the name.
    /// </summary>
    internal static string RenderAutomaticIcon(string name, string host)
    {
        // Colored Dashboard Icons come first; the monochrome Simple Icons cover many developer services they lack.
        var candidates = IconCandidates(name, host).ToList();
        var shortcodes = candidates.Select(candidate => "dashboard-" + candidate)
            .Concat(candidates.Select(candidate => "simple-" + candidate.Replace("-", string.Empty, StringComparison.Ordinal)));
        foreach (var shortcode in shortcodes)
        {
            if (IconCatalog.TryGetIcon(shortcode, out var icon))
                return $"<span class=\"dashboard-icon\"><span class=\"icon\">{icon.ToSvg()}</span></span>";
        }

        var words = name.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(char.IsLetterOrDigit).ToArray()))
            .Where(word => word.Length > 0)
            .ToArray();
        var initials = words.Length switch
        {
            0 => "?",
            1 => words[0].Length > 1 ? $"{char.ToUpperInvariant(words[0][0])}{char.ToLowerInvariant(words[0][1])}" : words[0].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}"
        };
        var hue = name.Aggregate(17, (hash, character) => unchecked(hash * 31 + character)) & 0x7fffffff;
        return $"<span class=\"dashboard-icon dashboard-monogram\" style=\"--dashboard-hue: {hue % 360}\" aria-hidden=\"true\">{HtmlEncoder.Default.Encode(initials)}</span>";
    }

    internal static IEnumerable<string> IconCandidates(string name, string host)
    {
        var candidates = new List<string>();
        void Add(string value)
        {
            var slug = NonSlugCharactersRegex.Replace(value.ToLowerInvariant(), "-").Trim('-');
            if (slug.Length > 0 && !candidates.Contains(slug)) candidates.Add(slug);
        }

        Add(name.Replace("+", " plus", StringComparison.Ordinal));
        Add(CamelCaseBoundaryRegex.Replace(name, "-"));
        Add(name.Replace(" ", string.Empty, StringComparison.Ordinal));
        if (!name.Contains(' ', StringComparison.Ordinal)) Add(name + "-ai");

        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length >= 2)
        {
            var domain = labels[^2];
            if (labels.Length >= 3) Add($"{domain}-{labels[^3]}");
            Add(domain);
        }

        foreach (var word in name.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries).OrderByDescending(word => word.Length))
            if (word.Length > 3) Add(word);

        return candidates;
    }

    private static string RenderBlock(Block block, MarkdownPipeline pipeline) =>
        Render(renderer => renderer.Render(block), pipeline);

    private static string RenderChildren(ContainerBlock block, MarkdownPipeline pipeline) =>
        Render(renderer =>
        {
            foreach (var child in block)
            {
                if (child is ParagraphBlock paragraph && paragraph.Inline is not null) renderer.WriteChildren(paragraph.Inline);
                else renderer.Render(child);
            }
        }, pipeline);

    private static string RenderInlines(IEnumerable<Inline> inlines, MarkdownPipeline pipeline) =>
        Render(renderer =>
        {
            foreach (var inline in inlines) renderer.Render(inline);
        }, pipeline);

    private static string Render(Action<Markdig.Renderers.HtmlRenderer> write, MarkdownPipeline pipeline)
    {
        using var writer = new StringWriter();
        var renderer = new Markdig.Renderers.HtmlRenderer(writer);
        pipeline.Setup(renderer);
        write(renderer);
        renderer.Writer.Flush();
        return writer.ToString();
    }

    private static string PlainText(IEnumerable<Inline> inlines)
    {
        var text = new StringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    text.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    text.Append(code.Content);
                    break;
                case ContainerInline container:
                    text.Append(PlainText(container));
                    break;
            }
        }

        return text.ToString();
    }

    private static string? ReadSearchUrl(string? value)
    {
        var search = value?.Trim();
        if (string.IsNullOrEmpty(search)) return SearchEngines["duckduckgo"];
        if (search.Equals("none", StringComparison.OrdinalIgnoreCase) || search.Equals("false", StringComparison.OrdinalIgnoreCase)) return null;
        if (SearchEngines.TryGetValue(search, out var url)) return url;
        return search.Contains("{query}", StringComparison.Ordinal) &&
               Uri.TryCreate(search.Replace("{query}", "q", StringComparison.Ordinal), UriKind.Absolute, out var uri) &&
               uri.Scheme is "http" or "https"
            ? search
            : SearchEngines["duckduckgo"];
    }

    private static bool ReadBoolean(YamlMappingNode? mapping, string key, bool fallback) =>
        mapping is not null && bool.TryParse(ReadScalar(mapping, key)?.Trim(), out var value) ? value : fallback;

    private static string? ReadScalar(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar
            ? scalar.Value
            : null;

    private sealed record DashboardTile(
        string Url,
        string? Title,
        string NameHtml,
        string PlainName,
        string Description,
        string? IconHtml,
        string MoreHtml);
}
