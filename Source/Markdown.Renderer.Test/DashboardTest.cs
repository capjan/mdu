using System.Text;
using System.Text.RegularExpressions;
using Markdown.Renderer.Res.Model;

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
        Assert.Equal(new DashboardOptions("https://duckduckgo.com/?q={query}", true, false, false), defaults);

        var options = Dashboard.ReadFrontMatter("""
            ---
            layout: dashboard
            dashboard:
              search: https://search.example.org/?q={query}
              clock: false
              status: true
              open: new-tab
            ---
            """);
        Assert.Equal(new DashboardOptions("https://search.example.org/?q={query}", false, true, true), options);

        Assert.Null(Dashboard.ReadFrontMatter("---\nlayout: dashboard\ndashboard:\n  search: none\n---")!.SearchUrl);
        Assert.Equal("https://www.google.com/search?q={query}",
            Dashboard.ReadFrontMatter("---\nlayout: dashboard\ndashboard:\n  search: Google\n---")!.SearchUrl);
        Assert.Equal("https://duckduckgo.com/?q={query}",
            Dashboard.ReadFrontMatter("---\nlayout: dashboard\ndashboard:\n  search: javascript:alert({query})\n---")!.SearchUrl);
    }

    [Fact]
    public void ReadsTheLimitAndTheCollapsedGroups()
    {
        var options = Dashboard.ReadFrontMatter("""
            ---
            layout: dashboard
            dashboard:
              limit: 6
              collapsed: [AI, " Social "]
            ---
            """)!;
        Assert.Equal(6, options.Limit);
        Assert.Equal(["AI", "Social"], options.Collapsed);
        Assert.True(options.StartsCollapsed("ai"));
        Assert.False(options.StartsCollapsed("Developer"));

        var all = Dashboard.ReadFrontMatter("---\nlayout: dashboard\ndashboard:\n  collapsed: true\n  limit: 0\n---")!;
        Assert.Null(all.Limit);
        Assert.True(all.StartsCollapsed("Anything"));

        Assert.Equal(["Social"], Dashboard.ReadFrontMatter("---\nlayout: dashboard\ndashboard:\n  collapsed: Social\n---")!.Collapsed);
        var none = Dashboard.ReadFrontMatter("---\nlayout: dashboard\ndashboard:\n  collapsed: false\n  limit: many\n---")!;
        Assert.Null(none.Collapsed);
        Assert.Null(none.Limit);
    }

    [Fact]
    public async Task FoldsGroupsAndLimitsTheirTiles()
    {
        var html = await RenderPage("""
            ## Office

            - [Mail](https://mail.example.org)
            - [Drive](https://drive.example.org)

            ### Archive

            - [Old Wiki](https://wiki.example.org)
            - [Photos](https://photos.example.org)
            - [ ](https://task.example.org)
            - ![Logo](logo.png)
            - Moved to the archive drive

            ## Tools[^1]

            - [Editor](https://editor.example.org)
            - [Shell](https://shell.example.org)
            - [Notes](https://notes.example.org)

            ## [AI](https://ai.example.org)

            - [Claude](https://claude.ai)

            [^1]: Installed locally.
            """, Options with { Limit = 2, Collapsed = ["ai"] });

        Assert.Contains("<section class=\"dashboard-group\" role=\"rowgroup\">", html);
        Assert.Contains("<section class=\"dashboard-group is-collapsed\" role=\"rowgroup\">", html);
        Assert.Contains("<h2 id=\"office\"><button class=\"dashboard-group-toggle\" type=\"button\" aria-expanded=\"true\" disabled><span class=\"dashboard-group-title\">Office</span><span class=\"dashboard-group-count\" data-count=\"5\" aria-hidden=\"true\"></span>", html);
        // One tile past the limit shows instead of a button for it.
        Assert.Contains("data-count=\"3\"", html);
        // A link in the heading keeps only its text inside the button. Until the fold script runs, every group is open.
        Assert.Contains("aria-expanded=\"true\" disabled><span class=\"dashboard-group-title\">AI</span><span class=\"dashboard-group-count\" data-count=\"1\"", html);
        // A task-list link counts as a tile; an item with only an image is a note.
        Assert.Equal(3, CountOf(html, "<li class=\"dashboard-tile is-overflow\" role=\"row\">"));
        Assert.Equal(2, CountOf(html, "<li class=\"dashboard-note is-overflow\""));
        Assert.Equal(1, CountOf(html, "class=\"dashboard-more\""));
        Assert.Contains("aria-expanded=\"false\" data-more=\"3\">Show 3 more</button>", html);
        // A note past the limit folds with the tiles around it.
        Assert.Contains("<li class=\"dashboard-note is-overflow\" role=\"row\"><div role=\"gridcell\">Moved to the archive drive</div></li>", html);
        // A footnote reference follows the button, so it still links to its footnote.
        Assert.Matches("<span class=\"dashboard-group-title\">Tools</span>.*?</button><a [^>]*class=\"footnote-ref\"", html);
        // Folding starts in the script that can undo it.
        Assert.Equal(1, CountOf(html, "dashboard.classList.add(\"is-folding\")"));
    }

    [Fact]
    public void ReadsTheFrequentlyUsedLinks()
    {
        DashboardOptions Read(string value) => Dashboard.ReadFrontMatter($"---\nlayout: dashboard\ndashboard:\n  frequent: {value}\n---")!;
        Assert.Equal(4, Read("4").Frequent);
        Assert.Equal(6, Read("true").Frequent);
        Assert.Null(Read("false").Frequent);
        Assert.Null(Read("0").Frequent);
        Assert.Null(Read("often").Frequent);
        Assert.Null(Dashboard.ReadFrontMatter("---\nlayout: dashboard\n---")!.Frequent);
    }

    [Fact]
    public async Task LeavesAHiddenRowForTheFrequentlyUsedLinks()
    {
        const string markdown = "## Office\n\n- [Mail](https://mail.example.org)\n";
        var html = await RenderPage(markdown, new DashboardOptions(null, false, false, false, Frequent: 4));
        // The row comes before the groups and stays hidden until the script fills it.
        Assert.Matches(
            "<div id=\"dashboard-results\"[^>]*>\\s*<div class=\"dashboard-row dashboard-frequent-row\" role=\"presentation\" hidden>\\s*" +
            "<section class=\"dashboard-group dashboard-frequent\" role=\"rowgroup\" data-frequent=\"4\">",
            html);
        Assert.Contains("<ul class=\"dashboard-tiles\" role=\"presentation\"></ul>", html);
        Assert.Contains("aria-label=\"Clear frequently used links\"", html);

        Assert.DoesNotContain("<section class=\"dashboard-group dashboard-frequent\"", await RenderPage(markdown, new DashboardOptions(null, false, false, false)));
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
        Assert.Equal(2, CountOf(html, "<div class=\"dashboard-row\" role=\"presentation\">"));
        Assert.Equal(2, CountOf(html, "<section class=\"dashboard-group\" role=\"rowgroup\">"));

        Assert.Contains("<ul class=\"dashboard-tiles has-descriptions\" role=\"presentation\">", html);
        Assert.Contains("<a class=\"dashboard-link\" href=\"https://mail.google.com\" target=\"_blank\" rel=\"noopener noreferrer\"><span class=\"dashboard-icon\"><span class=\"icon\">", html);
        Assert.Contains("<span class=\"dashboard-name\">Mail</span><span class=\"dashboard-description\">Inbox &amp; calendar</span>", html);
        Assert.Contains("<a class=\"dashboard-link\" href=\"Guide/Handbook.html\">", html);
        Assert.Contains("<span class=\"dashboard-description\">Team <em>rules</em></span>", html);

        Assert.Contains("<ul class=\"dashboard-tiles\" role=\"presentation\">", html);
        Assert.Contains("<span class=\"dashboard-name\">GitHub</span></span>", html);
        Assert.Contains("<span class=\"dashboard-name\">Christians GitLab</span><span class=\"dashboard-description\">gitlab.example.org</span>", html);
        Assert.Contains(">JD</span><span class=\"dashboard-text\"><span class=\"dashboard-name\">Jan@Dataport</span>", html);
        Assert.Contains("<li class=\"dashboard-note\" role=\"row\"><div role=\"gridcell\">Plain note without a link</div></li>", html);
        Assert.Equal(4, CountOf(html, "<span class=\"dashboard-status\""));
        Assert.Equal(4, CountOf(html, "<span class=\"visually-hidden\"> (opens in a new tab)</span>"));
        Assert.Equal(4, CountOf(html, "<path d=\"M5 11 11 5M6 5h5v5\"></path>"));
        Assert.Contains("<span class=\"dashboard-go\" aria-hidden=\"true\"><svg viewBox=\"0 0 16 16\" focusable=\"false\"><path d=\"M3.5 8h9M9 4.5 12.5 8 9 11.5\"></path></svg></span></a>", html);
        Assert.Contains("id=\"dashboard-search-clear\" type=\"button\" tabindex=\"-1\"", html);

        Assert.Contains("<style>/* Dashboard pages", html);
        Assert.DoesNotContain("class=\"on-this-page\"", html);
    }

    [Fact]
    public async Task RendersFootnotesBelowLastUpdatedWithoutASeparator()
    {
        var html = await RenderPage("""
            ## Products[^1]

            - [Project](https://example.org)

            [^1]: Finished project.
            """, Options, new DateTimeOffset(2026, 10, 3, 14, 0, 0, TimeSpan.FromHours(2)));

        var lastUpdatedIndex = html.IndexOf("Last updated on <time", StringComparison.Ordinal);
        var footnotesIndex = html.IndexOf("<div class=\"footnotes\">", StringComparison.Ordinal);
        Assert.True(lastUpdatedIndex >= 0);
        Assert.True(footnotesIndex > lastUpdatedIndex);

        var footnotes = html[footnotesIndex..].Split("</div>", 2)[0];
        Assert.Contains("<h2 class=\"footnotes-title\">Footnotes</h2>", footnotes);
        Assert.DoesNotContain("<hr />", footnotes);
    }

    [Theory]
    [InlineData("GitHub", "github.com", true)]
    [InlineData("YouTube", "www.youtube.com", true)]
    [InlineData("Prime Video", "www.primevideo.com", true)]
    [InlineData("Disney+", "www.disneyplus.com", true)]
    [InlineData("Stack Overflow", "stackoverflow.com", true)]
    [InlineData("Cloudflare", "dash.cloudflare.com", false)]
    [InlineData("Grafana", "prod.grafana.example", false)]
    [InlineData("Grafana", "staging.grafana.example", false)]
    [InlineData("Spotify", "open.spotify.com", false)]
    [InlineData("Grafana", "grafana.example", true)]
    [InlineData("Grafana", "grafana", true)]
    [InlineData("Grafana", "grafana.example.org", false)]
    [InlineData("GitLab (Privat)", "git.ip-co.de", false)]
    [InlineData("ChatGPT", "chat.openai.com", false)]
    [InlineData("GitHub Docs", "docs.github.com", false)]
    [InlineData("Amazon", "amazon.co.uk", false)]
    [InlineData("MDN", "developer.mozilla.org", false)]
    [InlineData("Portal", "", false)]
    [InlineData("…", "example.org", false)]
    public void RecognizesAHostThatOnlyRepeatsTheName(string name, string host, bool repeats) =>
        Assert.Equal(repeats, Dashboard.HostRepeatsName(name, host));

    [Fact]
    public async Task ShowsTheHostOnlyWhereItAddsToTheName()
    {
        var html = await RenderPage("""
            ## Compact

            - [Netflix](https://www.netflix.com)
            - [Grafana](https://grafana.example.org)

            ## Described

            - [GitHub](https://github.com)
            - [Wiki](https://wiki.example.org) — Team knowledge
            """, Options);

        Assert.Contains("<span class=\"dashboard-name\">Netflix</span></span>", html);
        Assert.Contains("<span class=\"dashboard-name\">Grafana</span><span class=\"dashboard-description\">grafana.example.org</span>", html);
        // Described tiles keep two lines, so the host still fills the second one.
        Assert.Contains("<span class=\"dashboard-name\">GitHub</span><span class=\"dashboard-description\">github.com</span>", html);
        Assert.Contains("href=\"https://www.netflix.com\"", html);
    }

    [Fact]
    public async Task LeavesOutThePreviousAndNextPageButKeepsThePageMeta()
    {
        var html = await RenderPage(
            "## Work\n\n- [Mail](https://mail.example.org)\n",
            Options,
            new DateTimeOffset(2026, 10, 3, 14, 0, 0, TimeSpan.Zero),
            Neighbors);

        Assert.Contains("Last updated on <time", html);
        Assert.DoesNotContain("page-pagination", html);

        var regular = await RenderPage(
            "# Start\n",
            null,
            navigationItems: Neighbors);
        Assert.Contains("class=\"page-pagination\"", regular);
    }

    [Fact]
    public async Task TreatsLowerLevelHeadingsAsSubheadingsOfAGroup()
    {
        var html = await RenderPage("## Network\n\n#### Bookmarks\n\n- [Shop](https://shop.example.org)\n", Options);

        Assert.Contains("<section class=\"dashboard-group\" role=\"rowgroup\">", html);
        Assert.Contains("<div role=\"row\"><div role=\"gridcell\">\n<h4", html);
        Assert.Contains(".markdown-content .dashboard-group :is(h3, h4, h5, h6) {", Dashboard.Styles);
        Assert.Contains(":scope > [role='gridcell'] > :is(h3, h4, h5, h6)", Dashboard.Script);
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

        Assert.Contains("<input id=\"dashboard-search-input\" type=\"search\" role=\"combobox\" aria-controls=\"dashboard-results\" aria-haspopup=\"grid\" aria-expanded=\"false\" aria-autocomplete=\"list\"", html);
        Assert.Contains("<div id=\"dashboard-results\" role=\"grid\" aria-label=\"Links and pages\" aria-readonly=\"true\" hidden>", html);
        Assert.Contains("<p class=\"dashboard-search-hint\" id=\"dashboard-search-hint\"></p>", html);
        Assert.Contains("<p class=\"visually-hidden\" id=\"dashboard-search-status\" role=\"status\" aria-live=\"polite\" aria-atomic=\"true\"></p>", html);
        Assert.Contains("<div class=\"dashboard-pages\" id=\"dashboard-pages\" role=\"rowgroup\" aria-label=\"Pages\" hidden>", html);
        Assert.DoesNotContain("id=\"dashboard-pages\" aria-labelledby", html);
        Assert.DoesNotContain("aria-activedescendant=", html);
    }

    [Fact]
    public async Task GivesTilesStableUniqueCellsAcrossListsAndGroupsWhileKeepingLinks()
    {
        const string markdown = """
            # Start

            ## Work

            ### Dashboard Tile 0

            > ### Dashboard Tile 1

            - [Mail & calendar](https://mail.example.org)
            - A note

            Another list:

            - [Mail & calendar](https://mail.example.org)

            ---

            ## More

            - [Handbook](Guide/Handbook.md)
            """;
        var html = await RenderPage(markdown, Options);
        var ids = Regex.Matches(html, "<div class=\"dashboard-cell\" id=\"(dashboard-tile-[0-9]+)\"")
            .Select(match => match.Groups[1].Value).ToArray();

        Assert.Equal(new[] { "dashboard-tile-2", "dashboard-tile-3", "dashboard-tile-4" }, ids);
        Assert.Contains("<h3 id=\"dashboard-tile-0\"", html);
        Assert.Equal(1, CountOf(html, "id=\"dashboard-tile-0\""));
        Assert.Contains("<blockquote>\n<h3 id=\"dashboard-tile-1\"", html);
        Assert.Equal(1, CountOf(html, "id=\"dashboard-tile-1\""));
        var allIds = Regex.Matches(html, "\\bid=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(allIds.Length, allIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(html, await RenderPage(markdown, Options));
        Assert.Equal(3, CountOf(html, "<li class=\"dashboard-tile\" role=\"row\"><div class=\"dashboard-cell\""));
        Assert.Equal(3, CountOf(html, "role=\"gridcell\" aria-selected=\"false\""));
        Assert.Contains("<div class=\"dashboard-cell\" id=\"dashboard-tile-2\" role=\"gridcell\" aria-selected=\"false\"><a class=\"dashboard-link\" href=\"https://mail.example.org\" target=\"_blank\" rel=\"noopener noreferrer\">", html);
        Assert.Contains("<span class=\"dashboard-name\">Mail &amp; calendar</span><span class=\"dashboard-description\">mail.example.org</span>", html);
        Assert.Contains("<div class=\"dashboard-cell\" id=\"dashboard-tile-4\" role=\"gridcell\" aria-selected=\"false\"><a class=\"dashboard-link\" href=\"Guide/Handbook.html\">", html);
    }

    [Fact]
    public async Task KeepsTheDashboardGridRowsInsideRowGroups()
    {
        var html = await RenderPage("""
            # Start

            ## Work

            A group description.

            - [Mail](https://mail.example.org) — Inbox

            ---

            Content between rows.

            ## Reference

            - A note
            """, new DashboardOptions(null, false, true, true));

        Assert.Contains("<section class=\"dashboard-group\" role=\"rowgroup\">\n<div role=\"row\"><div role=\"gridcell\">\n<h2", html);
        Assert.Contains("<div role=\"row\"><div role=\"gridcell\"><ul>\n<li>A note</li>", html);
        Assert.Contains("<div role=\"rowgroup\">\n<div role=\"row\"><div role=\"gridcell\">\n<p>Content between rows.</p>", html);
        Assert.Contains("<span class=\"dashboard-status\" role=\"img\" aria-label=\"Status unknown\"></span>", html);
        Assert.Contains("class=\"dashboard-description\">Inbox</span>", html);
        Assert.DoesNotContain("role=\"region\"", html);
        Assert.DoesNotContain("role=\"grid\" aria-label=\"Links and pages\" aria-readonly=\"true\">\n<section", html);
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
    [InlineData("NuGet", "www.nuget.org", "simple-nuget")]
    [InlineData("Docker Hub", "hub.docker.com", "dashboard-docker")]
    [InlineData("Markdig", "github.com", "dashboard-github")]
    [InlineData("Mermaid", "mermaid.js.org", "dashboard-mermaid")]
    [InlineData("Visual Studio Code", "code.visualstudio.com", "dashboard-visual-studio-code")]
    [InlineData("Docker", "www.docker.com", "dashboard-docker")]
    [InlineData("Repository", "gitlab.com", "dashboard-gitlab")]
    [InlineData("Repositories", "bitbucket.org", "dashboard-bitbucket")]
    [InlineData("Repositories", "codeberg.org", "dashboard-codeberg")]
    [InlineData("Snippets", "gist.github.com", "dashboard-github")]
    [InlineData("Home Grafana", "grafana", "dashboard-grafana")]
    [InlineData("Home Grafana", "GRAFANA.", "dashboard-grafana")]
    [InlineData("Inbox", "gmail.com", "dashboard-gmail")]
    [InlineData("Christians GitLab", "gitlab.example.org", "dashboard-gitlab")]
    [InlineData("Xq Zw", "github.com", "dashboard-github")]
    [InlineData("Material Design Icons", "pictogrammers.com", "dashboard-material-design-icons")]
    [InlineData("Anthropic", "anthropic.com", "dashboard-anthropic")]
    [InlineData("Astro", "astro.build", "dashboard-astro")]
    [InlineData("Framer", "framer.com", "dashboard-framer")]
    [InlineData("Railway", "railway.app", "dashboard-railway")]
    [InlineData("GitLab", "github.com", "dashboard-github")]
    [InlineData("Inbox", "mail.google.com", "dashboard-gmail")]
    [InlineData("Documents", "docs.google.com", "dashboard-google-docs")]
    [InlineData("Files", "drive.google.com", "dashboard-google-drive")]
    [InlineData("Schedule", "calendar.google.com", "dashboard-google-calendar")]
    [InlineData("Assistant", "gemini.google.com", "dashboard-google-gemini")]
    [InlineData("Packages", "www.nuget.org", "simple-nuget")]
    [InlineData("Package Home", "nuget.org", "simple-nuget")]
    [InlineData("Music", "open.spotify.com", "dashboard-spotify")]
    [InlineData("GitHub Docs", "docs.github.com", "dashboard-github")]
    [InlineData("Claude", "claude.ai", "dashboard-claude-ai")]
    [InlineData("Home Assistant", "192.168.1.2", "dashboard-home-assistant")]
    [InlineData("Nextcloud", "cloud.example.org", "dashboard-nextcloud")]
    [InlineData("Grafana", "grafana.example.org", "dashboard-grafana")]
    [InlineData("Portainer", "portainer.local", "dashboard-portainer")]
    [InlineData("Proxmox", "192.168.1.3", "dashboard-proxmox")]
    [InlineData("Jellyfin", "media.example.org", "dashboard-jellyfin")]
    [InlineData("YouTube", "www.youtube.com", "dashboard-youtube")]
    [InlineData("Perplexity", "perplexity.ai", "dashboard-perplexity")]
    [InlineData("ChatGPT", "chatgpt.com", "dashboard-chatgpt")]
    [InlineData("Stack Overflow", "stackoverflow.com", "simple-stackoverflow")]
    [InlineData("Dashboard Icons", "dashboardicons.com", "dashboard-dashboard-icons")]
    [InlineData("Simple Icons", "simpleicons.org", "simple-simpleicons")]
    [InlineData("GitHub", "", "dashboard-github")]
    [InlineData("Inbox", "MAIL.GOOGLE.COM.", "dashboard-gmail")]
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

    [Theory]
    [InlineData("Apple Storage", "storage.example.org", "AS")]
    [InlineData("Password Vault", "vault.example.org", "PV")]
    [InlineData("Shared Files", "files.example.org", "SF")]
    [InlineData("My Music", "music.example.org", "MM")]
    [InlineData("Team Code", "code.example.org", "TC")]
    [InlineData("Files", "files.example.org", "Fi")]
    [InlineData("Files", "example.org", "Fi")]
    [InlineData("Code", "example.org", "Co")]
    [InlineData("Music", "example.org", "Mu")]
    [InlineData("Vault", "example.org", "Va")]
    [InlineData("Password VAULT", "VAULT.example.org", "PV")]
    [InlineData("Home Files", "files", "HF")]
    [InlineData("Home Grafana", "localhost", "HG")]
    [InlineData("Inbox", "www.www.gmail.com", "In")]
    [InlineData("Repositories", "bitbucket.org.example.org", "Re")]
    [InlineData("Repositories", "team.codeberg.org", "Re")]
    [InlineData("Amazon Home", "home.example.org", "AH")]
    [InlineData("My GitHub", "example.org", "MG")]
    [InlineData("My GitHub", "github-mirror.example.org", "MG")]
    [InlineData("My GitHub", "", "MG")]
    [InlineData("Inbox", "mail.google.com.example.org", "In")]
    [InlineData("Portal", "team.github.io", "Po")]
    [InlineData("Portal", "amazon.co.uk", "Po")]
    [InlineData("DevDocs", "devdocs.io", "De")]
    [InlineData("Artificial Analysis", "artificialanalysis.ai", "AA")]
    [InlineData("CommonMark", "commonmark.org", "Co")]
    [InlineData("Typora", "typora.io", "Ty")]
    [InlineData("Octicons", "primer.style", "Oc")]
    public void UsesInitialsInsteadOfGuessingAnUnrelatedLogo(string name, string host, string initials)
    {
        var html = Dashboard.RenderAutomaticIcon(name, host);

        Assert.Contains("dashboard-monogram", html);
        Assert.Contains($">{initials}</span>", html);
        Assert.DoesNotContain("<svg", html);
    }

    [Fact]
    public async Task ExplicitIconsOverrideKnownHosts()
    {
        var html = await RenderPage("## Tools\n\n- :simple-rider: [IDE](https://github.com)\n", Options);

        Assert.True(StaticCode.Icons.IconCatalog.TryGetIcon("simple-rider", out var icon));
        Assert.Contains($"<span class=\"dashboard-icon\"><span class=\"icon\">{icon.ToSvg()}", html);
    }

    [Fact]
    public async Task AppliesAnExplicitIconColorInsideDashboardTiles()
    {
        var html = await RenderPage("## Tools\n\n- :dashboard-files:{ style=\"color: #16803c\" } [Files](https://files.example.org)\n", Options);

        Assert.Contains("<span class=\"dashboard-icon\"><span class=\"icon\" style=\"color: #16803c\"><svg", html);
        Assert.Contains("fill=\"#16803c\"", html);
    }

    [Fact]
    public async Task RendersAnIconForASingleLabelHostWithAPort()
    {
        var html = await RenderPage("## Tools\n\n- [Home Grafana](http://grafana:3000)\n", Options);

        Assert.True(StaticCode.Icons.IconCatalog.TryGetIcon("dashboard-grafana", out var icon));
        Assert.Contains($"<span class=\"dashboard-icon\"><span class=\"icon\">{icon.ToSvg()}", html);
        Assert.Contains("href=\"http://grafana:3000\"", html);
    }

    [Fact]
    public async Task NormalizesTheHostOnceForIconsAndSubtitles()
    {
        var html = await RenderPage("## Tools\n\n- [Inbox](https://www.www.gmail.com)\n", Options);

        Assert.Contains("class=\"dashboard-icon dashboard-monogram\"", html);
        Assert.Contains("<span class=\"dashboard-description\">www.gmail.com</span>", html);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("files")]
    [InlineData("music")]
    [InlineData("vault")]
    public async Task ExplicitIconsCanStillSelectAmbiguousNames(string name)
    {
        var html = await RenderPage($"## Tools\n\n- :dashboard-{name}: [{name}](https://example.org)\n", Options);

        Assert.True(StaticCode.Icons.IconCatalog.TryGetIcon("dashboard-" + name, out var icon));
        Assert.Contains($"<span class=\"dashboard-icon\"><span class=\"icon\">{icon.ToSvg()}", html);
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

    private static readonly NavigationItem[] Neighbors =
    [
        new() { Title = "Start", Url = "index.html", IsCurrent = true },
        new() { Title = "Guide", Url = "Guide.html" }
    ];

    private static async Task<string> RenderPage(
        string markdown,
        DashboardOptions? dashboard,
        DateTimeOffset? lastModified = null,
        IReadOnlyList<NavigationItem>? navigationItems = null)
    {
        var html = new StringBuilder();
        await using var writer = new StringWriter(html);
        await new HtmlRenderer().WriteHtml(writer, "Start", "", 0, markdown, "", navigationItems, lastModified: lastModified, dashboard: dashboard);
        return html.ToString();
    }

    private static int CountOf(string text, string value) =>
        (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;
}
