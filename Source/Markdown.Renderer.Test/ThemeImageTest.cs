namespace Markdown.Renderer.Test;

public class ThemeImageTest
{
    [Fact]
    public void HtmlPreservesThemeMarkersOnImages()
    {
        var html = new HtmlRenderer().RenderMarkdownFragment("""
            ![Light logo](light.svg#only-light)
            ![Dark logo](dark.svg#only-dark)
            ![GitHub light logo](github-light.svg#gh-light-mode-only)
            ![GitHub dark logo](github-dark.svg#gh-dark-mode-only)
            ![Regular logo](regular.svg)
            """);

        Assert.Contains("src=\"light.svg#only-light\"", html);
        Assert.Contains("src=\"dark.svg#only-dark\"", html);
        Assert.Contains("src=\"github-light.svg#gh-light-mode-only\"", html);
        Assert.Contains("src=\"github-dark.svg#gh-dark-mode-only\"", html);
        Assert.Contains("src=\"regular.svg\"", html);
    }

    [Fact]
    public void CssSelectsThemeVariantsAndHidesTheirImageLinks()
    {
        var css = Resources.CssContent();

        Assert.Contains(":root[data-color-scheme=\"light\"]", css);
        Assert.Contains(":root[data-color-scheme=\"dark\"]", css);
        Assert.Contains(":root[data-theme=\"auto\"]", css);
        Assert.Contains(":root[data-theme=\"auto\"]:not([data-color-scheme])", css);
        Assert.Contains("@media (prefers-color-scheme: light)", css);
        Assert.Contains("@media (prefers-color-scheme: dark)", css);
        Assert.DoesNotContain("display: initial", css);
        foreach (var marker in new[] { "only-light", "only-dark", "gh-light-mode-only", "gh-dark-mode-only" })
            Assert.Contains($"img[src$=\"#{marker}\"]", css);
        Assert.Contains(":is(a, .dashboard-icon):has(> img[src$=\"#only-light\"], > img[src$=\"#gh-light-mode-only\"])", css);
        Assert.Contains(":is(a, .dashboard-icon):has(> img[src$=\"#only-dark\"], > img[src$=\"#gh-dark-mode-only\"])", css);
    }

    [Fact]
    public void ResolvesTheSystemColorSchemeForImageVariants()
    {
        var html = Resources.HtmlTemplateContent();

        Assert.Contains("const systemTheme = matchMedia(\"(prefers-color-scheme: dark)\")", html);
        Assert.Contains("root.dataset.colorScheme", html);
        Assert.Contains("systemTheme.addEventListener(\"change\", updateColorScheme)", html);
    }

    [Fact]
    public void PdfKeepsLightVariantsWithoutTheirMarkersAndOmitsDarkVariants()
    {
        var html = new HtmlRenderer().RenderPdfMarkdownFragment("""
            ![Light logo](light.svg#only-light)
            ![Dark logo](dark.svg#only-dark)
            ![GitHub light logo](github-light.svg#gh-light-mode-only)
            ![GitHub dark logo](github-dark.svg#gh-dark-mode-only)
            ![Regular logo](regular.svg)
            [![Light link logo](logo.png#only-light)](logo-large.png#only-light)
            """);
        var document = PdfHtmlDocument.Parse(html);
        var sources = document.SelectNodes("//img")!.Cast<System.Xml.XmlElement>()
            .Select(image => image.GetAttribute("src"));

        Assert.Equal(["light.svg", "github-light.svg", "regular.svg", "logo.png"], sources);
        Assert.Single(document.SelectNodes("//a[@href='logo-large.png']")!);
    }

    [Fact]
    public void PdfOmitsDarkMarkdownLinksAndFiltersThemeVariantsInRawHtml()
    {
        var html = new HtmlRenderer().RenderPdfMarkdownFragment("""
            [![Dark Markdown logo](markdown-dark.svg#only-dark)](https://example.com)

            <a href="raw-dark.svg#gh-dark-mode-only"><img alt="Dark HTML logo" src="raw-dark.svg#gh-dark-mode-only"></a>

            <img alt="Light HTML logo" src="raw-light.svg#gh-light-mode-only">
            """);

        var document = PdfHtmlDocument.Parse(html);
        var sources = document.SelectNodes("//img")!.Cast<System.Xml.XmlElement>()
            .Select(image => image.GetAttribute("src"));

        Assert.Equal(["raw-light.svg"], sources);
        Assert.Empty(document.SelectNodes("//a[@href='https://example.com']")!);
        Assert.Empty(document.SelectNodes("//a[@href='raw-dark.svg#gh-dark-mode-only']")!);
    }

    [Fact]
    public void PdfRemovesParagraphsThatOnlyContainedDarkImages()
    {
        var html = new HtmlRenderer().RenderPdfMarkdownFragment("![Dark logo](dark.svg#only-dark)");

        var document = PdfHtmlDocument.Parse(html);

        Assert.Empty(document.SelectNodes("//p")!);
    }
}
