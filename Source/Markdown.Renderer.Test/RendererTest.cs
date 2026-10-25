using System.Text;
using Markdown.Renderer.Res.Model;

namespace Markdown.Renderer.Test;

public class RendererTest
{
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
}
