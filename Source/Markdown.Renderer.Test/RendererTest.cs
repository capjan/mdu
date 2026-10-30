using System.Text;
using Markdown.Renderer.Res.Model;

namespace Markdown.Renderer.Test;

public class RendererTest
{
    [Fact]
    public async Task InitializesHeaderAndNavigationBeforeRenderingThePageContent()
    {
        using var writer = new StringWriter();
        await new HtmlRenderer().WriteHtml(writer, "test", "", 0, "# Page\n\n## Section\n\nText", "");
        var html = writer.ToString();
        var css = Resources.CssContent();

        Assert.Contains("<header class=\"site-header has-wide-sidebar\"", html);
        Assert.DoesNotContain("%%HAS_TOC%%", html);
        Assert.DoesNotContain("%%HEADER_LAYOUT_CLASS%%", html);
        Assert.Contains("class=\"navigation-toggle panel-toggle\"", html);
        Assert.Contains("class=\"toc-toggle panel-toggle\"", html);
        Assert.Equal(2, html.Split("class=\"panel-fill\"").Length - 1);
        Assert.True(html.IndexOf("navigationToggle.setAttribute(\"aria-expanded\"", StringComparison.Ordinal) <
                    html.IndexOf("<nav class=\"top-navigation", StringComparison.Ordinal));
        Assert.True(html.IndexOf("children.hidden = !open;", StringComparison.Ordinal) <
                    html.IndexOf("<main class=\"site-main", StringComparison.Ordinal));
        Assert.Contains(".panel-toggle[aria-expanded=\"false\"] .panel-fill", css);
        Assert.Contains(".panel-toggle:hover", css);
        Assert.Contains("document.documentElement.classList.remove(\"panels-initializing\")", html);
    }

    [Fact]
    public void RendersAnAccessibleSidebarResizer()
    {
        var html = Resources.HtmlTemplateContent();
        var css = Resources.CssContent();

        Assert.Contains("id=\"sidebar-resizer\" role=\"separator\" aria-label=\"Resize sidebar\"", html);
        Assert.Contains("aria-orientation=\"vertical\" tabindex=\"0\"", html);
        Assert.Contains("panel.resizer.setAttribute(\"aria-valuemin\", String(panel.min));", html);
        Assert.Contains("grid-template-columns: var(--sidebar-width) minmax(0, 1fr);", css);
        Assert.Contains("resizer.addEventListener(\"pointerdown\"", html);
        Assert.Contains("resizer.addEventListener(\"keydown\"", html);
        Assert.Contains("bindPanelResizer(sidebarPanel, 1);", html);
        var focusStyles = BlockAfter(css, ".sidebar-resizer:focus-visible,\n.toc-resizer:focus-visible {");
        Assert.Contains("border-radius: 2px;", focusStyles);
        Assert.Contains("outline-offset: -2px;", focusStyles);
    }

    [Fact]
    public void PreservesThePreferredSidebarWidthAcrossViewportChanges()
    {
        var html = Resources.HtmlTemplateContent();
        Assert.Contains("observedWidths.get(entry.target) !== Math.round(entry.borderBoxSize[0].inlineSize)", html);
        Assert.DoesNotContain("window.addEventListener(\"resize\", () => {", html);
        Assert.Contains("panel.preferred = clampedWidth;", html);
        Assert.Contains("pendingWidth = pointerStartWidth + direction * (event.clientX - pointerStartX);", html);
        Assert.Contains("if (dragFrame === null) dragFrame = requestAnimationFrame(applyDrag);", html);
        Assert.Contains("if (clampedWidth !== currentWidth) setPanelWidth(panel, clampedWidth);", html);
        Assert.Contains("const step = event.shiftKey ? 50 : 10;", html);
    }

    [Fact]
    public void KeepsSidebarAndContentInTheirOwnColumnsOnTheSameRow()
    {
        var css = Resources.CssContent();
        var sidebarStyles = BlockAfter(css, ".site-sidebar {");
        var mainStyles = BlockAfter(css, "\n.site-main {");
        var collapsedStyles = BlockAfter(css, ".site-layout.sidebar-collapsed .site-main {");

        Assert.Contains("grid-column: 1;", sidebarStyles);
        Assert.Contains("grid-row: 1;", sidebarStyles);
        Assert.Contains("grid-column: 2;", mainStyles);
        Assert.Contains("grid-row: 1;", mainStyles);
        Assert.Contains("grid-column: 1;", collapsedStyles);
    }

    [Fact]
    public void MatchesTheResizerLineToTheSidebarGuide()
    {
        var css = Resources.CssContent();
        var resizerStyles = BlockAfter(css, ".sidebar-resizer::before {");
        var guideStyles = BlockAfter(css, ".site-nav-row:has(> .site-nav-toggle) + .site-nav-children::before {");

        foreach (var styles in new[] { resizerStyles, guideStyles })
        {
            Assert.Contains("width: 1px;", styles);
            Assert.Contains("background: var(--border-subtle);", styles);
        }
    }

    [Fact]
    public void UsesRemainingContentWidthAndReservesSpaceForTheTableOfContents()
    {
        var css = Resources.CssContent();
        var html = Resources.HtmlTemplateContent();

        Assert.DoesNotContain("--content-max-width", css);
        Assert.Contains("grid-template-columns: minmax(0, 1fr);", css);
        Assert.Contains("@container (min-width: 600px)", css);
        Assert.Contains("grid-template-columns: minmax(0, 1fr) var(--toc-width);", css);
        Assert.Contains("width - (sidebarVisible ? panels.sidebar.min : 0) - padding >= panels.tocThreshold", html);
        Assert.Contains(": panels.contentMin) + padding;", html);
        Assert.Contains("width - reserve", html);
        Assert.Contains(":root.toc-space-available .site-layout:has(.on-this-page)", css);
        Assert.DoesNotContain("\n.site-layout:has(.on-this-page) {", css);
    }

    [Fact]
    public void RestoresLayoutPreferencesBeforeRenderingTheBody()
    {
        var html = Resources.HtmlTemplateContent();
        var body = html.IndexOf("<body", StringComparison.Ordinal);
        Assert.True(html.IndexOf("for (const panel of [panels.sidebar, panels.toc])", StringComparison.Ordinal) < body);
        Assert.True(html.IndexOf("mdu-toc-collapsed", StringComparison.Ordinal) < body);
        Assert.Contains("aria-label=\"Resize table of contents\"", html);
        Assert.Contains("bindPanelResizer(tocPanel, -1);", html);
        Assert.Contains("tocToggle.setAttribute(\"aria-expanded\"", html);
    }

    [Fact]
    public async Task OmitsTocControlsReferenceWhenThePageHasNoHeadings()
    {
        using var writer = new StringWriter();
        await new HtmlRenderer().WriteHtml(writer, "test", "", 0, "Plain text", "");
        var html = writer.ToString();

        Assert.DoesNotContain("aria-controls=\"on-this-page\"", html);
        Assert.DoesNotContain("%%IS_DASHBOARD%%", html);
        Assert.DoesNotContain("%%TOC_CONTROLS%%", html);
    }

    [Fact]
    public void InitializesNavigationGroupsOnlyOnceAndPreservesBrowserShortcuts()
    {
        var html = Resources.HtmlTemplateContent();
        Assert.Equal(1, html.Split("querySelectorAll(\".site-nav-toggle\")").Length - 1);
        Assert.Contains("if (event.altKey || event.metaKey || event.ctrlKey) return;", html);
        Assert.Contains("if (moved) {", html);
        Assert.Contains("else restorePanelWidth(panel);", html);
        Assert.DoesNotContain("window.mduPanels", html);
        Assert.DoesNotContain("header.classList.toggle(\"has-wide-sidebar\"", html);
        Assert.Contains("setOpen(!breakpoint.matches && ((event.matches && !isDashboard) || !startsCollapsed()))", html);
        Assert.Equal(1, html.Split("`mdu-nav-expanded:${new URL(link.href).pathname}`").Length - 1);
        Assert.DoesNotContain("layout.style.removeProperty(\"--sidebar-width\")", html);
        Assert.DoesNotContain("\"%%SITE_LAYOUT_CLASS%%\".includes", html);
    }

    [Fact]
    public void UsesSharedDashboardPaddingAndLimitsTitleTruncationToTheTitle()
    {
        var css = Resources.CssContent();
        Assert.Contains("#site-brand-title {", css);
        Assert.DoesNotContain(".site-brand > span", css);
        Assert.Contains("padding: 1rem var(--main-padding) 3rem;", css);
        Assert.Contains("--main-padding: clamp(1rem, 2.5vw, 2.5rem);", Dashboard.Styles);
        Assert.Contains("padding-inline: var(--main-padding);", Dashboard.Styles);
    }

    private static string BlockAfter(string text, string marker)
    {
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected block marker: {marker}");
        start += marker.Length;
        var end = text.IndexOf('}', start);
        Assert.True(end >= start, $"Expected closing brace after: {marker}");
        return text[start..end];
    }

    [Fact]
    public void AppliesImageLightboxGridLayoutOnlyWhileOpen()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            new HtmlRenderer().PrepareBasePath(outputPath);
            var css = File.ReadAllText(Path.Combine(outputPath, "css", "main.css"));
            var lightboxStyles = css.Split(".image-lightbox {", 2)[1].Split('}', 2)[0];

            Assert.DoesNotContain("display:", lightboxStyles);
            Assert.Matches(@"\.image-lightbox\[open\]\s*\{\s*display:\s*grid;\s*\}", css);
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public async Task BasicTest()
    {
        var markdown = Res.Resources.MarkdownWithCode;
        var renderer = new HtmlRenderer(); 
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        await renderer.WriteHtml(sw, "test", "", 0, markdown, "");
        var renderedHtml = sb.ToString();
        
        Assert.Contains("<title>test</title>", renderedHtml);
        Assert.Contains("<h1", renderedHtml);
        Assert.Contains("Markdown with Syntax Highlighting", renderedHtml);
        Assert.Contains("<span class=\"string\">", renderedHtml);
        Assert.Contains("Hello World", renderedHtml);
    }

    [Fact]
    public void RendersLocalAbbreviationsWithoutReplacingCode()
    {
        var renderer = new HtmlRenderer();

        var html = renderer.RenderMarkdownFragment(
            "*[HTML]: Hypertext Markup Language\n\nHTML and `HTML`; **bold** and *italic*.\n\n```text\nHTML\n```\n");

        Assert.Contains("<abbr title=\"Hypertext Markup Language\">HTML</abbr> and <code>HTML</code>", html);
        Assert.Contains("<strong>bold</strong> and <em>italic</em>", html);
        Assert.Contains("<pre><code>HTML</code></pre>", html);
    }

    [Fact]
    public void SharedAbbreviationsAreAddedOnceAndLocalDefinitionsTakePrecedencePerDocument()
    {
        var renderer = new HtmlRenderer();
        renderer.SetGlossary("*[CPU]: Central processing unit\n*[GPU]: Graphics processing unit\n");

        var localOverride = renderer.RenderMarkdownFragment(
            "*[CPU]: Local processor\n\nCPU and GPU.\n");
        var anotherPage = renderer.RenderMarkdownFragment("CPU.\n");

        Assert.Contains("<abbr title=\"Local processor\">CPU</abbr>", localOverride);
        Assert.Contains("<abbr title=\"Graphics processing unit\">GPU</abbr>", localOverride);
        Assert.Contains("<abbr title=\"Central processing unit\">CPU</abbr>", anotherPage);
    }

    [Fact]
    public void InvalidGlossaryLeavesTheLastValidGlossaryActive()
    {
        var renderer = new HtmlRenderer();
        renderer.SetGlossary("*[CPU]: Central processing unit\n");

        Assert.Throws<InvalidOperationException>(() => renderer.SetGlossary("No abbreviation definitions here."));

        Assert.Contains(
            "<abbr title=\"Central processing unit\">CPU</abbr>",
            renderer.RenderMarkdownFragment("CPU."));
    }

    [Fact]
    public async Task AppliesConfiguredBackgroundToHeaderSurfacesForLightAndDarkThemes()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var renderer = new HtmlRenderer();
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);

        try
        {
            renderer.PrepareBasePath(outputPath, new SiteConfiguration
            {
                Light = new SitePalette { Background = "#123456" },
                Dark = new SitePalette { Background = "#654321" }
            });
            await renderer.WriteHtml(sw, "test", "", 0, "# Test", "");

            var html = sb.ToString();
            Assert.Contains(":root,:root[data-theme=\"light\"]{--page-background:#123456;--surface-header:#123456;", html);
            Assert.Contains(":root[data-theme=\"dark\"]{--page-background:#654321;--surface-header:#654321;", html);
            Assert.Contains("@media(prefers-color-scheme:dark){:root[data-theme=\"auto\"]{--page-background:#654321;--surface-header:#654321;", html);
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public async Task DerivesSecondaryColorsFromConfiguredPalette()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var renderer = new HtmlRenderer();
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);

        try
        {
            renderer.PrepareBasePath(outputPath, new SiteConfiguration
            {
                Dark = new SitePalette { Background = "#1b1b1d", Text = "#d4d4d8", Primary = "#5b9bf0" }
            });
            await renderer.WriteHtml(sw, "test", "", 0, "# Test", "");

            var html = sb.ToString();
            Assert.Contains("--text-secondary:color-mix(in oklab,var(--text-primary) 63%,var(--page-background))", html);
            Assert.Contains("--border:color-mix(in oklab,var(--text-primary) 16%,var(--page-background))", html);
            Assert.Contains("--nav-hover:color-mix(in oklab,var(--link) 16%,var(--page-background))", html);
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public async Task RendersTwoTrailingSpacesAsHardLineBreak()
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        await new HtmlRenderer().WriteHtml(sw, "test", "", 0, "First line  \nSecond line", "");

        Assert.Contains("<p>First line<br />\nSecond line</p>", sb.ToString());
    }

    [Fact]
    public async Task OmitsFootnoteReferencesFromTableOfContentsTitles()
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        await new HtmlRenderer().WriteHtml(sw, "test", "", 0,
            "# Dashboard\n\n## Produkte[^1]\n\nText\n\n[^1]: Note", "");

        var toc = sb.ToString().Split("class=\"toc-list\"", 2)[1].Split("</ul>", 2)[0];
        Assert.Contains("class=\"toc-link\" href=\"#produkte\">Produkte</a>", toc);
        Assert.DoesNotContain("<sup>", toc);
    }

    [Fact]
    public async Task AddsPermalinksToSectionHeadingsOnly()
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        await new HtmlRenderer().WriteHtml(sw, "test", "", 0, "# Dashboard\n\n## Produkte", "");

        var html = sb.ToString();
        Assert.Matches("<h1[^>]*>Dashboard</h1>", html);
        Assert.Contains("Produkte<a class=\"headerlink\" href=\"#produkte\" title=\"Permanent link\" aria-label=\"Permanent link\"><svg viewBox=\"0 0 20 20\" aria-hidden=\"true\"", html);
    }

    [Fact]
    public async Task OmitsBreadcrumbsThatOnlyNameTheCurrentPage()
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        await new HtmlRenderer().WriteHtml(sw, "test", "", 0, "# Dashboard", "",
            breadcrumbs: [new BreadcrumbItem("Dashboard", null)]);

        Assert.DoesNotContain("class=\"breadcrumbs\"", sb.ToString());
    }

    [Fact]
    public async Task RendersBreadcrumbsWithAncestors()
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        await new HtmlRenderer().WriteHtml(sw, "test", "", 1, "# Guide", "",
            breadcrumbs: [new BreadcrumbItem("Dashboard", "../index.html"), new BreadcrumbItem("Guide", null)]);

        Assert.Contains("<nav class=\"breadcrumbs\" aria-label=\"Breadcrumb\"><a href=\"../index.html\">Dashboard</a></nav>", sb.ToString());
    }

    [Fact]
    public async Task RendersFooterWithNeighborPagesAndDateWithoutEditLink()
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        await new HtmlRenderer().WriteHtml(sw, "test", "", 0, "# Guide", "https://example.com/edit/Guide.md",
            navigationItems:
            [
                new NavigationItem { Title = "Home", Url = "index.html" },
                new NavigationItem { Title = "Guide", Url = "Guide.html", IsCurrent = true },
                new NavigationItem { Title = "Tips & Tricks", Url = "Tips.html" }
            ],
            lastModified: new DateTimeOffset(2026, 10, 3, 14, 0, 0, TimeSpan.FromHours(2)));

        var footer = sb.ToString().Split("<footer class=\"page-footer\">", 2)[1].Split("</footer>", 2)[0];
        var heading = sb.ToString().Split("<h1", 2)[1].Split("</h1>", 2)[0];
        Assert.Contains("Last updated on <time datetime=\"2026-10-03\">October 3, 2026</time>", footer);
        Assert.DoesNotContain("Edit this page", footer);
        Assert.Contains("class=\"page-edit-link\"", heading);
        Assert.Contains("<a class=\"page-pagination-link is-previous\" href=\"index.html\" rel=\"prev\"><span class=\"page-pagination-label\">Previous</span><span class=\"page-pagination-title\">Home</span></a>", footer);
        Assert.Contains("<a class=\"page-pagination-link is-next\" href=\"Tips.html\" rel=\"next\"><span class=\"page-pagination-label\">Next</span><span class=\"page-pagination-title\">Tips &amp; Tricks</span></a>", footer);
    }

    [Fact]
    public async Task RendersFooterEditLinkWhenPageHasNoH1()
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        await new HtmlRenderer().WriteHtml(sw, "test", "", 0, "## Details", "https://example.com/edit/Guide.md");

        var html = sb.ToString();
        var footer = html.Split("<footer class=\"page-footer\">", 2)[1].Split("</footer>", 2)[0];
        Assert.Contains("<a href=\"https://example.com/edit/Guide.md\" target=\"_blank\" rel=\"noopener noreferrer\">Edit this page</a>", footer);
        Assert.DoesNotContain("class=\"page-edit-link\"", html);
    }

    [Fact]
    public async Task OmitsFooterWithoutNeighborPagesOrDate()
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        await new HtmlRenderer().WriteHtml(sw, "test", "", 0, "# Only page", "",
            navigationItems: [new NavigationItem { Title = "Only page", Url = "index.html", IsCurrent = true }]);

        Assert.DoesNotContain("page-footer", sb.ToString());
    }

    [Fact]
    public async Task UnclassifiedNavigationItemsDoNotExpandATopNavigationSidebar()
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        await new HtmlRenderer().WriteHtml(sw, "test", "", 0, "# Page", "",
            navigationItems:
            [
                new NavigationItem
                {
                    Title = "Page",
                    Url = "page.html",
                    IsTopNavigationItem = true,
                    IsInCurrentTopSection = true
                }
            ],
            topNavigationItems: [new NavigationItem { Title = "Page", Url = "page.html", IsCurrent = true }]);

        var html = sb.ToString();
        Assert.Contains("class=\"site-layout has-top-navigation no-wide-sidebar\"", html);
        Assert.DoesNotContain("is-outside-top-navigation-section", html);
    }

    [Fact]
    public async Task AddsUnlistedTitleToFootnotes()
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        await new HtmlRenderer().WriteHtml(sw, "test", "", 0, "# Page\n\nText[^1]\n\n[^1]: Note", "");

        var html = sb.ToString();
        Assert.Contains("<div class=\"footnotes\">\n<hr />\n<h2 class=\"footnotes-title\">Footnotes</h2>", html);
        Assert.DoesNotContain(">Footnotes</a>", html.Split("class=\"toc-list\"", 2).ElementAtOrDefault(1) ?? string.Empty);
    }

    [Theory]
    [InlineData("## 6. Sicherheitsinvarianten", "6-sicherheitsinvarianten")]
    [InlineData("## 3.1 Überblick", "31-uberblick")]
    [InlineData("## 2024", "2024")]
    [InlineData("## 10 `mdu render`", "10-mdu-render")]
    [InlineData("## Overview", "overview")]
    [InlineData("## app.Client", "app.client")]
    [InlineData("## 6. Security {#custom}", "custom")]
    [InlineData("## 2FA Setup", "2fa-setup")]
    [InlineData("## 3D View", "3d-view")]
    [InlineData("## 1. 2FA Overview", "1-2fa-overview")]
    [InlineData("3.1 Overview\n------------", "31-overview")]
    public void KeepsLeadingNumbersInHeadingIds(string heading, string expectedId)
    {
        var html = new HtmlRenderer().RenderMarkdownFragment(heading);

        Assert.Contains($"id=\"{expectedId}\"", html);
    }

    [Fact]
    public void NumbersDuplicateHeadingIdsWithLeadingNumbers()
    {
        var html = new HtmlRenderer().RenderMarkdownFragment("## 1. Setup\n\n## 1. Setup\n\n## Setup");

        Assert.Contains("id=\"1-setup\"", html);
        Assert.Contains("id=\"1-setup-1\"", html);
        Assert.Contains("id=\"setup\"", html);
    }

    [Fact]
    public void DoesNotReuseExplicitIdsForHeadingsWithLeadingNumbers()
    {
        var html = new HtmlRenderer().RenderMarkdownFragment("## Custom {#6-security}\n\n## 6. Security");

        Assert.Contains("id=\"6-security\"", html);
        Assert.Contains("id=\"6-security-1\"", html);
    }

    [Fact]
    public void ResolvesSamePageFragmentsWithDifferentCasing()
    {
        var html = new HtmlRenderer().RenderMarkdownFragment(
            "## app.authorization.AuthorizedAuditContext\n\nSee [context](#app.authorization.AuthorizedAuditContext) and [missing](#Missing).");

        Assert.Contains("id=\"app.authorization.authorizedauditcontext\"", html);
        Assert.Contains("href=\"#app.authorization.authorizedauditcontext\"", html);
        Assert.Contains("href=\"#Missing\"", html);
    }

    [Fact]
    public void KeepsFragmentsThatMatchAnExplicitIdExactly()
    {
        var html = new HtmlRenderer().RenderMarkdownFragment(
            "## Client {#Client}\n\n## Other\n\nSee [client](#Client).");

        Assert.Contains("id=\"Client\"", html);
        Assert.Contains("href=\"#Client\"", html);
    }

    [Fact]
    public void KeepsExactAndAmbiguousFragmentsNextToIdsThatDifferInCasing()
    {
        var html = new HtmlRenderer().RenderMarkdownFragment(
            "## Custom {#Client}\n\n## Client\n\nSee [custom](#Client), [auto](#client) and [ambiguous](#CLIENT).");

        Assert.Contains("href=\"#Client\">custom", html);
        Assert.Contains("href=\"#client\">auto", html);
        Assert.Contains("href=\"#CLIENT\">ambiguous", html);
    }
}
