using System.Text;
using System.Text.RegularExpressions;

namespace Markdown.Renderer.Test;

public class DashboardTest
{
    [Fact]
    public void ReadsTheOptionsOnlyForADashboardLayout()
    {
        Assert.Null(Dashboard.ReadFrontMatter(null));
        Assert.Null(Dashboard.ReadFrontMatter("---\ntitle: Start\n---"));
        Assert.Null(Dashboard.ReadFrontMatter("---\nlayout: [unclosed\n---"));

        var defaults = Dashboard.ReadFrontMatter("---\nlayout: dashboard\n---");
        Assert.Equal(new DashboardOptions("https://duckduckgo.com/?q={query}", true, false, true), defaults);

        var options = Dashboard.ReadFrontMatter("""
            ---
            layout: dashboard
            dashboard:
              search: https://search.example.org/?q={query}
              clock: false
              status: true
              open: same-tab
            ---
            """);
        Assert.Equal(new DashboardOptions("https://search.example.org/?q={query}", false, true, false), options);

        Assert.Null(Dashboard.ReadFrontMatter("---\nlayout: dashboard\ndashboard:\n  search: none\n---")!.SearchUrl);
        Assert.Equal("https://www.google.com/search?q={query}",
            Dashboard.ReadFrontMatter("---\nlayout: dashboard\ndashboard:\n  search: Google\n---")!.SearchUrl);
        Assert.Equal("https://duckduckgo.com/?q={query}",
            Dashboard.ReadFrontMatter("---\nlayout: dashboard\ndashboard:\n  search: javascript:alert({query})\n---")!.SearchUrl);
    }

    [Fact]
    public async Task RendersGroupsOfLinkTiles()
    {
        var html = await RenderPage("""
            # Start

            Everything in one place.

            ## Office

            - :dashboard-gmail: [Mail](https://mail.google.com) — Inbox & calendar
            - [Handbook](Guide/Handbook.md) - Team *rules*

            ---

            ## Developer

            - [GitHub](https://github.com)
            - [Christians GitLab](https://gitlab.example.org)
            - [Jan@Dataport](https://intranet.example.org)
            - Plain note without a link
            """, new DashboardOptions("https://duckduckgo.com/?q={query}", true, true, true));

        Assert.Contains("<div class=\"site-layout has-wide-sidebar is-dashboard\" id=\"site-layout\">", html);
        Assert.Contains("<div class=\"dashboard\" data-dashboard-search=\"https://duckduckgo.com/?q={query}\" data-dashboard-status=\"true\">", html);
        Assert.Contains("<p>Everything in one place.</p>", html);
        Assert.Contains("<input id=\"dashboard-search-input\"", html);
        Assert.Contains("<time class=\"dashboard-clock\"", html);
        Assert.Equal(2, CountOf(html, "<div class=\"dashboard-row\">"));
        Assert.Equal(2, CountOf(html, "<section class=\"dashboard-group\">"));

        Assert.Contains("<ul class=\"dashboard-tiles has-descriptions\" role=\"presentation\">", html);
        Assert.Contains("<a class=\"dashboard-link\" href=\"https://mail.google.com\" target=\"_blank\" rel=\"noopener noreferrer\"><span class=\"dashboard-icon\"><span class=\"icon\">", html);
        Assert.Contains("<span class=\"dashboard-name\">Mail</span><span class=\"dashboard-description\">Inbox &amp; calendar</span>", html);
        Assert.Contains("<a class=\"dashboard-link\" href=\"Guide/Handbook.html\">", html);
        Assert.Contains("<span class=\"dashboard-description\">Team <em>rules</em></span>", html);

        Assert.Contains("<ul class=\"dashboard-tiles\" role=\"presentation\">", html);
        Assert.Contains("<span class=\"dashboard-name\">GitHub</span><span class=\"dashboard-description\">github.com</span>", html);
        Assert.Contains("<span class=\"dashboard-name\">Christians GitLab</span><span class=\"dashboard-description\">gitlab.example.org</span>", html);
        Assert.Contains(">JD</span><span class=\"dashboard-text\"><span class=\"dashboard-name\">Jan@Dataport</span>", html);
        Assert.Contains("<li class=\"dashboard-note\" role=\"row\"><div role=\"gridcell\">Plain note without a link</div></li>", html);
        Assert.Equal(4, CountOf(html, "<span class=\"dashboard-status\""));

        Assert.Contains("<style>/* Dashboard pages", html);
        Assert.DoesNotContain("class=\"on-this-page\"", html);
    }

    [Fact]
    public async Task RendersTheResultContainersWithoutGroups()
    {
        var html = await RenderPage("# Start\n\nOnly an introduction.\n", Options);

        Assert.Contains("<input id=\"dashboard-search-input\"", html);
        Assert.Contains("<ul class=\"dashboard-pages-list\" id=\"dashboard-pages-list\" role=\"presentation\"></ul>", html);
        Assert.Contains("<p class=\"dashboard-empty\" id=\"dashboard-empty\" hidden></p>", html);
        Assert.DoesNotContain("<div class=\"dashboard-row\">", html);
    }

    [Fact]
    public async Task ConnectsTheFilterToTheResultsAndKeepsHintsOutOfTheLiveRegion()
    {
        var html = await RenderPage("# Start", Options);

        Assert.Contains("<input id=\"dashboard-search-input\" type=\"search\" role=\"combobox\" aria-controls=\"dashboard-results\" aria-haspopup=\"grid\" aria-expanded=\"true\" aria-autocomplete=\"list\"", html);
        Assert.Contains("<div id=\"dashboard-results\" role=\"grid\" aria-label=\"Links and pages\">", html);
        Assert.Contains("<p class=\"dashboard-search-hint\" id=\"dashboard-search-hint\"></p>", html);
        Assert.Contains("<p class=\"visually-hidden\" id=\"dashboard-search-status\" role=\"status\" aria-live=\"polite\" aria-atomic=\"true\"></p>", html);
        Assert.DoesNotContain("aria-activedescendant=", html);
    }

    [Fact]
    public async Task GivesTilesStableUniqueCellsAcrossListsAndGroupsWhileKeepingLinks()
    {
        const string markdown = """
            # Start

            ## Work

            - [Mail & calendar](https://mail.example.org)
            - A note

            Another list:

            - [Mail & calendar](https://mail.example.org)

            ---

            ## More

            - [Handbook](Guide/Handbook.md)
            """;
        var html = await RenderPage(markdown, Options);
        var ids = Regex.Matches(html, "id=\"(dashboard-tile-[0-9]+)\"")
            .Select(match => match.Groups[1].Value).ToArray();

        Assert.Equal(new[] { "dashboard-tile-0", "dashboard-tile-1", "dashboard-tile-2" }, ids);
        Assert.Equal(html, await RenderPage(markdown, Options));
        Assert.Equal(3, CountOf(html, "<li class=\"dashboard-tile\" role=\"row\"><div class=\"dashboard-cell\""));
        Assert.Equal(3, CountOf(html, "role=\"gridcell\" aria-selected=\"false\""));
        Assert.Contains("aria-label=\"Mail &amp; calendar\"><a class=\"dashboard-link\" href=\"https://mail.example.org\" target=\"_blank\" rel=\"noopener noreferrer\">", html);
        Assert.Contains("aria-label=\"Handbook\"><a class=\"dashboard-link\" href=\"Guide/Handbook.html\">", html);
    }

    [Fact]
    public async Task KeepsOnlyTheTextOfLinksInASubtitle()
    {
        var html = await RenderPage(
            "# Start\n\n## Work\n\n- [Portal](https://portal.example.org) — [Help](https://help.example.org) and *more*\n",
            Options);

        Assert.Contains("<span class=\"dashboard-description\">Help and <em>more</em></span>", html);
        Assert.DoesNotContain("help.example.org", html);
    }

    [Fact]
    public async Task KeepsFurtherBlocksOfATileBelowItsLink()
    {
        var html = await RenderPage("""
            # Start

            ## Work

            - [Portal](https://portal.example.org) — Intranet

              Opens with the company account.

              - [Status](https://status.example.org)
            """, Options);

        Assert.Contains("</a><div class=\"dashboard-tile-more\"><p>Opens with the company account.</p>", html);
        Assert.Contains("<a href=\"https://status.example.org\">Status</a>", html);
    }

    [Fact]
    public async Task KeepsTheSubtitleOfAnXTile()
    {
        var html = await RenderPage("# Start\n\n## Social\n\n- [X](https://x.com) — Social media\n", Options);

        Assert.Contains("<span class=\"dashboard-name\">X</span><span class=\"dashboard-description\">Social media</span>", html);
    }

    [Theory]
    [InlineData("GitHub", "github.com", "dashboard-github")]
    [InlineData("Christians GitLab", "gitlab.example.org", "dashboard-gitlab")]
    [InlineData("Xq Zw", "github.com", "dashboard-github")]
    public void PicksADashboardIconFromTheNameOrHost(string name, string host, string shortcode)
    {
        var html = Dashboard.RenderAutomaticIcon(name, host);

        Assert.True(StaticCode.Icons.IconCatalog.TryGetIcon(shortcode, out var icon));
        Assert.Contains(icon.ToSvg(), html);
    }

    [Fact]
    public void FallsBackToInitialsWhenNoIconMatches()
    {
        Assert.Contains(">QX</span>", Dashboard.RenderAutomaticIcon("Qwrt@Xyzzy", "xyzzy.invalid"));
        Assert.Contains(">Qz</span>", Dashboard.RenderAutomaticIcon("Qzxv", string.Empty));
    }

    [Fact]
    public async Task LeavesRegularPagesUnchanged()
    {
        var html = await RenderPage("# Start\n\n## Office\n\n- [Mail](https://mail.google.com)\n", null);

        Assert.DoesNotContain("class=\"dashboard", html);
        Assert.DoesNotContain(" is-dashboard\"", html);
        Assert.Contains("<li><a href=\"https://mail.google.com\">Mail</a></li>", html);
    }

    private static readonly DashboardOptions Options = new("https://duckduckgo.com/?q={query}", true, false, true);

    private static async Task<string> RenderPage(string markdown, DashboardOptions? dashboard)
    {
        var html = new StringBuilder();
        await using var writer = new StringWriter(html);
        await new HtmlRenderer().WriteHtml(writer, "Start", "", 0, markdown, "", dashboard: dashboard);
        return html.ToString();
    }

    private static int CountOf(string text, string value) =>
        (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;
}
