using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Footnotes;
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
/// <param name="Limit">The number of tiles a group shows before a button reveals the rest, or null for all tiles.</param>
/// <param name="Collapsed">The groups that start collapsed: the text of their headings, or <c>*</c> for every group.</param>
/// <param name="Frequent">The number of tiles the row of frequently used links shows, or null for no such row.</param>
public sealed record DashboardOptions(
    string? SearchUrl,
    bool Clock,
    bool Status,
    bool NewTab,
    int? Limit = null,
    IReadOnlyList<string>? Collapsed = null,
    int? Frequent = null)
{
    internal bool StartsCollapsed(string heading) =>
        Collapsed is not null && Collapsed.Any(name => name == "*" || string.Equals(name, heading, StringComparison.OrdinalIgnoreCase));
}

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
    // Exact hosts only: shared hosting domains and unrelated subdomains must not inherit a service logo.
    private static readonly Dictionary<string, string> HostIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["github.com"] = "github",
        ["gist.github.com"] = "github",
        ["bitbucket.org"] = "bitbucket",
        ["codeberg.org"] = "codeberg",
        ["gitlab.com"] = "gitlab",
        ["nuget.org"] = "nuget",
        ["docker.com"] = "docker",
        ["hub.docker.com"] = "docker",
        ["code.visualstudio.com"] = "visual-studio-code",
        ["mermaid.js.org"] = "mermaid",
        ["mail.google.com"] = "gmail",
        ["gmail.com"] = "gmail",
        ["docs.google.com"] = "google-docs",
        ["drive.google.com"] = "google-drive",
        ["calendar.google.com"] = "google-calendar",
        ["gemini.google.com"] = "google-gemini",
        ["open.spotify.com"] = "spotify",
        ["claude.ai"] = "claude-ai"
    };
    // These icon names also describe general service categories, even on matching self-hosted subdomains.
    private static readonly HashSet<string> AmbiguousIconNames = new(StringComparer.Ordinal)
    {
        "code", "files", "music", "vault"
    };
    private static readonly Regex CamelCaseBoundaryRegex = new("(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);
    private static readonly Regex NonSlugCharactersRegex = new("[^a-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex LeadingSeparatorRegex = new(@"\A\s*(?:[—–:|·-]\s*)?", RegexOptions.Compiled);
    private static readonly Regex AnchorTagRegex = new("</?a\\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FootnoteReferenceRegex = new("<a\\b[^>]*\\bclass=\"footnote-ref\"[^>]*>.*?</a>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex HeadingIdRegex = new("<h[1-6][^>]*\\bid=\"(?<id>[^\"]+)\"", RegexOptions.Compiled);
    private static readonly char[] WordSeparators = [' ', '@', '-', '_', '.', '/', '&', '+'];
    // The arrow that fades in on hover and focus shows whether a tile leaves the site.
    private const string ExternalArrow = "<span class=\"dashboard-go\" aria-hidden=\"true\"><svg viewBox=\"0 0 16 16\" focusable=\"false\"><path d=\"M5 11 11 5M6 5h5v5\"></path></svg></span>";
    private const string InternalArrow = "<span class=\"dashboard-go\" aria-hidden=\"true\"><svg viewBox=\"0 0 16 16\" focusable=\"false\"><path d=\"M3.5 8h9M9 4.5 12.5 8 9 11.5\"></path></svg></span>";

    public static string Styles => Resources.DashboardCssContent();
    public static string Script => Resources.DashboardScriptContent();

    /// <summary>
    /// Reads the dashboard options from a page's front matter, or returns null when the page has no dashboard layout.
    /// </summary>
    public static DashboardOptions? ReadFrontMatter(string? frontMatter)
    {
        var root = FrontMatterYaml.ReadMapping(frontMatter);
        if (root is null || !string.Equals(ReadScalar(root, "layout")?.Trim(), Layout, StringComparison.OrdinalIgnoreCase))
            return null;

        var options = root.Children.TryGetValue(new YamlScalarNode("dashboard"), out var node) ? node as YamlMappingNode : null;
        return new DashboardOptions(
            ReadSearchUrl(options is null ? null : ReadScalar(options, "search")),
            ReadBoolean(options, "clock", true),
            ReadBoolean(options, "status", false),
            string.Equals(options is null ? null : ReadScalar(options, "open")?.Trim(), "new-tab", StringComparison.OrdinalIgnoreCase),
            int.TryParse(options is null ? null : ReadScalar(options, "limit")?.Trim(), out var limit) && limit > 0 ? limit : null,
            ReadCollapsed(options),
            ReadFrequent(options));
    }

    /// <summary>
    /// Reads <c>collapsed</c>: <c>true</c> for every group, one heading, or a list of headings.
    /// </summary>
    private static IReadOnlyList<string>? ReadCollapsed(YamlMappingNode? options)
    {
        if (options is null || !options.Children.TryGetValue(new YamlScalarNode("collapsed"), out var node)) return null;
        var names = node switch
        {
            YamlScalarNode { Value: { } value } when bool.TryParse(value.Trim(), out var all) => all ? ["*"] : [],
            YamlScalarNode { Value: { } value } => [value],
            YamlSequenceNode sequence => sequence.Children.OfType<YamlScalarNode>().Select(item => item.Value ?? string.Empty).ToList(),
            _ => new List<string>()
        };
        var trimmed = names.Select(name => name.Trim()).Where(name => name.Length > 0).ToList();
        return trimmed.Count == 0 ? null : trimmed;
    }

    /// <summary>
    /// Reads <c>frequent</c>: the number of frequently used links to show, or <c>true</c> for <see cref="FrequentDefault"/>.
    /// </summary>
    private static int? ReadFrequent(YamlMappingNode? options)
    {
        var value = options is null ? null : ReadScalar(options, "frequent")?.Trim();
        if (int.TryParse(value, out var count)) return count > 0 ? count : null;
        return bool.TryParse(value, out var enabled) && enabled ? FrequentDefault : null;
    }

    private const int FrequentDefault = 6;

    public static string Render(MarkdownDocument document, MarkdownPipeline pipeline, DashboardOptions options)
    {
        var html = new StringBuilder();
        html.Append("<div class=\"dashboard\"");
        if (options.SearchUrl is not null) html.Append($" data-dashboard-search=\"{HtmlEncoder.Default.Encode(options.SearchUrl)}\"");
        if (options.Status) html.Append(" data-dashboard-status=\"true\"");
        html.AppendLine(">");

        var blocks = document.ToList();
        var footnotes = blocks.OfType<FootnoteGroup>().FirstOrDefault();
        if (footnotes is not null) blocks.Remove(footnotes);
        var firstGroup = blocks.FindIndex(block => block is HeadingBlock { Level: 2 });
        var usedIds = ReadHeadingIds(FindHeadingBlocks(blocks), pipeline);
        var introduction = firstGroup < 0 ? blocks : blocks.Take(firstGroup).ToList();
        foreach (var block in introduction) html.Append(RenderBlock(block, pipeline));
        html.AppendLine(RenderToolbar(options, firstGroup >= 0));
        html.AppendLine($"<div id=\"dashboard-results\" role=\"grid\" aria-label=\"Links and pages\" aria-readonly=\"true\"{(firstGroup < 0 ? " hidden" : string.Empty)}>");
        if (firstGroup >= 0)
        {
            // dashboard-folds.js fills the row with copies of the tiles the reader opens most, and shows it once it has any.
            if (options.Frequent is { } frequent)
                html.AppendLine("<div class=\"dashboard-row dashboard-frequent-row\" role=\"presentation\" hidden>")
                    .AppendLine($"<section class=\"dashboard-group dashboard-frequent\" role=\"rowgroup\" data-frequent=\"{frequent}\">")
                    .AppendLine("<div role=\"row\"><div role=\"gridcell\"><h2><span class=\"dashboard-group-title\">Frequently used</span>" +
                                "<button class=\"dashboard-frequent-clear\" type=\"button\" aria-label=\"Clear frequently used links\">Clear</button></h2></div></div>")
                    .AppendLine("<ul class=\"dashboard-tiles\" role=\"presentation\"></ul>")
                    .AppendLine("</section>")
                    .AppendLine("</div>");
            html.Append(RenderGroups(blocks.Skip(firstGroup).ToList(), pipeline, options, usedIds));
            // Inline, so folded groups and saved choices apply before the first paint, and only where a script can undo them.
            html.AppendLine($"<script>{Resources.DashboardFoldsScriptContent().Trim()}</script>");
        }

        // The filter script fills these containers, so they exist even on a dashboard without groups.
        html.AppendLine("<div class=\"dashboard-pages\" id=\"dashboard-pages\" role=\"rowgroup\" aria-label=\"Pages\" hidden>");
        html.AppendLine("<div role=\"row\"><p class=\"dashboard-pages-title\" id=\"dashboard-pages-title\" role=\"gridcell\">Pages</p></div>");
        html.AppendLine("<ul class=\"dashboard-pages-list\" id=\"dashboard-pages-list\" role=\"presentation\"></ul>");
        html.AppendLine("</div>");
        html.AppendLine("</div>");
        html.AppendLine("<p class=\"dashboard-empty\" id=\"dashboard-empty\" hidden></p>");
        html.AppendLine("</div>");
        if (footnotes is not null) html.Append(RenderBlock(footnotes, pipeline));
        return html.ToString();
    }

    private static string RenderGroups(IReadOnlyList<Block> blocks, MarkdownPipeline pipeline, DashboardOptions options, HashSet<string> usedIds)
    {
        var html = new StringBuilder();
        html.AppendLine("<div class=\"dashboard-row\" role=\"presentation\">");
        GroupState? group = null;
        var tileIndex = 0;

        void CloseGroup()
        {
            if (group is null) return;
            var more = group.Total - group.Shown;
            if (more > 0)
                html.AppendLine($"<div class=\"dashboard-more-row\" role=\"row\"><div role=\"gridcell\"><button class=\"dashboard-more\" type=\"button\" aria-expanded=\"false\" data-more=\"{more}\">Show {more} more</button></div></div>");
            html.AppendLine("</section>");
            group = null;
        }

        for (var index = 0; index < blocks.Count; index++)
        {
            switch (blocks[index])
            {
                case ThematicBreakBlock:
                    CloseGroup();
                    html.AppendLine("</div>");
                    html.AppendLine("<div class=\"dashboard-row\" role=\"presentation\">");
                    break;
                case HeadingBlock { Level: 2 } heading:
                    CloseGroup();
                    var collapsed = options.StartsCollapsed(PlainText(heading.Inline ?? Enumerable.Empty<Inline>()).Trim());
                    group = new GroupState(CountGroupTiles(blocks, index + 1), options.Limit);
                    html.AppendLine($"<section class=\"dashboard-group{(collapsed ? " is-collapsed" : string.Empty)}\" role=\"rowgroup\">");
                    html.AppendLine("<div role=\"row\"><div role=\"gridcell\">");
                    html.Append(RenderGroupHeading(RenderBlock(heading, pipeline), group.Total));
                    html.AppendLine("</div></div>");
                    break;
                case ListBlock list:
                    if (group is not null) html.Append(RenderTiles(list, pipeline, options, usedIds, ref tileIndex, group));
                    else
                    {
                        html.AppendLine("<div role=\"rowgroup\">");
                        html.Append(RenderTiles(list, pipeline, options, usedIds, ref tileIndex, null));
                        html.AppendLine("</div>");
                    }
                    break;
                case var block:
                    if (group is null) html.AppendLine("<div role=\"rowgroup\">");
                    html.AppendLine("<div role=\"row\"><div role=\"gridcell\">");
                    html.Append(RenderBlock(block, pipeline));
                    html.AppendLine("</div></div>");
                    if (group is null) html.AppendLine("</div>");
                    break;
            }
        }

        CloseGroup();
        html.AppendLine("</div>");
        return html.ToString();
    }

    /// <summary>Counts the tiles of the group whose blocks start at <paramref name="start"/>.</summary>
    private static int CountGroupTiles(IReadOnlyList<Block> blocks, int start) =>
        blocks.Skip(start)
            .TakeWhile(block => block is not (ThematicBreakBlock or HeadingBlock { Level: 2 }))
            .OfType<ListBlock>()
            .Sum(list => list.OfType<ListItemBlock>().Count(IsTile));

    /// <summary>
    /// Turns the text of a group heading into a button that folds the group. The heading keeps its id, so links to it
    /// still work. A button cannot contain a link, so links in the heading keep only their text, and footnote references
    /// follow the button.
    /// </summary>
    private static string RenderGroupHeading(string headingHtml, int tiles)
    {
        var start = headingHtml.IndexOf('>') + 1;
        var end = headingHtml.LastIndexOf("</h2>", StringComparison.Ordinal);
        if (start <= 0 || end < start) return headingHtml;
        var content = headingHtml[start..end];
        var footnotes = string.Concat(FootnoteReferenceRegex.Matches(content).Select(match => match.Value));
        content = AnchorTagRegex.Replace(FootnoteReferenceRegex.Replace(content, string.Empty), string.Empty).Trim();
        return headingHtml[..start]
               + $"<button class=\"dashboard-group-toggle\" type=\"button\" aria-expanded=\"true\" disabled>"
               + $"<span class=\"dashboard-group-title\">{content}</span>"
               + $"<span class=\"dashboard-group-count\" data-count=\"{tiles}\" aria-hidden=\"true\"></span>"
               + "<svg class=\"dashboard-group-chevron\" viewBox=\"0 0 16 16\" aria-hidden=\"true\" focusable=\"false\"><path d=\"m4 6 4 4 4-4\"></path></svg>"
               + "</button>"
               + footnotes
               + headingHtml[end..];
    }

    /// <summary>
    /// The group being written: how many tiles it has, how many show before Show more, and how many are written so far.
    /// A button for a single tile would take the tile's place, so a group with one tile past the limit shows them all.
    /// </summary>
    private sealed class GroupState(int total, int? limit)
    {
        public int Total { get; } = total;
        public int Shown { get; } = limit is { } value && total - value > 1 ? value : total;
        public int Written { get; set; }
        public bool IsFull => Written >= Shown && Shown < Total;
    }

    private static HashSet<string> ReadHeadingIds(IEnumerable<HeadingBlock> headings, MarkdownPipeline pipeline)
    {
        var rendered = Render(renderer =>
        {
            foreach (var heading in headings) renderer.Render(heading);
        }, pipeline);
        return HeadingIdRegex.Matches(rendered).Select(match => match.Groups["id"].Value).ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<HeadingBlock> FindHeadingBlocks(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            if (block is HeadingBlock heading) yield return heading;
            if (block is ContainerBlock container)
                foreach (var nestedHeading in FindHeadingBlocks(container)) yield return nestedHeading;
        }
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
        // Escape clears the filter, so Tab moves straight from the filter to its best match.
        html.AppendLine("<button class=\"dashboard-search-clear\" id=\"dashboard-search-clear\" type=\"button\" tabindex=\"-1\" aria-label=\"Clear filter\" title=\"Clear filter (Esc)\"><svg viewBox=\"0 0 16 16\" aria-hidden=\"true\" focusable=\"false\"><path d=\"m4.5 4.5 7 7m0-7-7 7\"></path></svg></button>");
        html.AppendLine("</div>");
        html.AppendLine("<p class=\"dashboard-search-hint\" id=\"dashboard-search-hint\"></p>");
        html.AppendLine("<p class=\"visually-hidden\" id=\"dashboard-search-status\" role=\"status\" aria-live=\"polite\" aria-atomic=\"true\"></p>");
        if (options.Clock) html.AppendLine("<time class=\"dashboard-clock\" id=\"dashboard-clock\"></time>");
        html.Append("</div>");
        return html.ToString();
    }

    private static string RenderTiles(ListBlock list, MarkdownPipeline pipeline, DashboardOptions options, HashSet<string> usedIds, ref int tileIndex, GroupState? group)
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
                html.AppendLine($"<li class=\"dashboard-note{(group is { IsFull: true } ? " is-overflow" : string.Empty)}\" role=\"row\"><div role=\"gridcell\">{RenderChildren((ListItemBlock)list[index], pipeline)}</div></li>");
                continue;
            }

            var external = Uri.TryCreate(tile.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
            var host = external ? uri!.Host : string.Empty;
            // In a compact list, a host that only repeats the name (GitHub, github.com) would take the name's space.
            var subtitle = tile.Description.Length > 0
                ? tile.Description
                : !hasDescriptions && HostRepeatsName(tile.PlainName, host)
                    ? string.Empty
                    : HtmlEncoder.Default.Encode(NormalizeHost(host));

            string id;
            do id = $"dashboard-tile-{tileIndex++}";
            while (!usedIds.Add(id));

            // Tiles past the limit stay in the page for the filter and appear when the reader asks for them.
            var overflow = group is not null && ++group.Written > group.Shown;
            html.Append($"<li class=\"dashboard-tile{(overflow ? " is-overflow" : string.Empty)}\" role=\"row\">");
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
            var newTab = external && options.NewTab;
            if (newTab) html.Append("<span class=\"visually-hidden\"> (opens in a new tab)</span>");
            if (options.Status && external) html.Append("<span class=\"dashboard-status\" role=\"img\" aria-label=\"Status unknown\"></span>");
            html.Append(external ? ExternalArrow : InternalArrow);
            html.Append("</a>");
            // Further paragraphs or nested lists of the item stay below the tile, outside its link.
            if (tile.MoreHtml.Length > 0) html.Append($"<div class=\"dashboard-tile-more\">{tile.MoreHtml}</div>");
            html.AppendLine("</div></li>");
        }

        html.AppendLine("</ul>");
        return html.ToString();
    }

    /// <summary>Tells whether a list item becomes a tile, without rendering it.</summary>
    private static bool IsTile(ListItemBlock item) =>
        item.FirstOrDefault() is ParagraphBlock { Inline: { } inlines } &&
        inlines.ToList() is var parts && (IsTaskListLink(parts) || FindTileLink(parts) >= 0);

    /// <summary>The index of the link that makes an item a tile: its first link that is not an image, if it has a URL.</summary>
    private static int FindTileLink(List<Inline> parts)
    {
        var index = parts.FindIndex(inline => inline is LinkInline { IsImage: false });
        return index >= 0 && !string.IsNullOrWhiteSpace(((LinkInline)parts[index]).Url) ? index : -1;
    }

    private static DashboardTile? ReadTile(ListItemBlock item, MarkdownPipeline pipeline)
    {
        if (item.FirstOrDefault() is not ParagraphBlock { Inline: { } inlines }) return null;

        var parts = inlines.ToList();
        var more = string.Concat(item.Skip(1).Select(block => RenderBlock(block, pipeline)));
        if (ReadTaskListLink(parts, pipeline) is { } taskListTile) return taskListTile with { MoreHtml = more };
        var linkIndex = FindTileLink(parts);
        if (linkIndex < 0) return null;
        var link = (LinkInline)parts[linkIndex];

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
    private static bool IsTaskListLink(IReadOnlyList<Inline> parts) =>
        parts is [TaskList, LiteralInline open, LinkInline { IsAutoLink: true } link, LiteralInline close, ..] &&
        open.Content.ToString() == "(" && close.Content.ToString().StartsWith(')') && !string.IsNullOrWhiteSpace(link.Url);

    private static DashboardTile? ReadTaskListLink(IReadOnlyList<Inline> parts, MarkdownPipeline pipeline)
    {
        if (!IsTaskListLink(parts)) return null;
        var task = (TaskList)parts[0];
        var link = (LinkInline)parts[2];
        var close = (LiteralInline)parts[3];

        var name = task.Checked ? "X" : " ";
        var description = RenderDescription(close.Content.ToString()[1..], parts.Skip(4), pipeline);
        return new DashboardTile(link.Url, null, name, name, description, null, string.Empty);
    }

    private static bool IsIcon(Inline inline) =>
        inline is IconInline or EmojiShortcodeInline or LinkInline { IsImage: true };

    /// <summary>
    /// Picks an icon from a known host, the full tile name, or a name word confirmed by a host label,
    /// and falls back to the initials of the name.
    /// </summary>
    internal static string RenderAutomaticIcon(string name, string host)
    {
        // Keep candidate priority across both sets: a weaker colored match must not beat a known host.
        var shortcodes = IconCandidates(name, host).SelectMany(candidate => new[]
        {
            "dashboard-" + candidate,
            "simple-" + candidate.Replace("-", string.Empty, StringComparison.Ordinal)
        });
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
            if (slug.Length > 0 && !AmbiguousIconNames.Contains(slug) && !candidates.Contains(slug)) candidates.Add(slug);
        }

        host = NormalizeHost(host);
        if (HostIcons.TryGetValue(host, out var hostIcon)) candidates.Add(hostIcon);

        Add(name.Replace("+", " plus", StringComparison.Ordinal));
        Add(CamelCaseBoundaryRegex.Replace(name, "-"));
        Add(name.Replace(" ", string.Empty, StringComparison.Ordinal));

        // A host label corroborates a word such as GitLab in "Christians GitLab". Do not guess from an
        // arbitrary word or the penultimate domain label (which may be a public suffix or shared host).
        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length > 1) labels = labels[..^1];
        foreach (var word in name.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries).OrderByDescending(word => word.Length))
            if (word.Length > 3 && labels.Contains(word, StringComparer.OrdinalIgnoreCase)) Add(word);

        return candidates;
    }

    /// <summary>
    /// Tells whether a host adds nothing to a tile's name: apart from a leading <c>www.</c>, the host is the name followed
    /// by a top-level domain, or the name alone, such as <c>Prime Video</c> at <c>www.primevideo.com</c>. Any further
    /// label keeps the host, because it may be all that tells two tiles of the same name apart, such as <c>Grafana</c> at
    /// <c>prod.grafana.example</c> and <c>staging.grafana.example</c>.
    /// </summary>
    internal static bool HostRepeatsName(string name, string host)
    {
        var labels = NormalizeHost(host).Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length is 0 or > 2) return false;
        var label = labels[0];
        return Compact(name.Replace("+", "plus", StringComparison.Ordinal)) is { Length: > 0 } compactName &&
               compactName == Compact(label);

        static string Compact(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static string NormalizeHost(string host)
    {
        host = host.TrimEnd('.');
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
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
