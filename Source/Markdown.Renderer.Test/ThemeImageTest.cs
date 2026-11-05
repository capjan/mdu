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

        Assert.Contains(":root[data-theme=\"light\"]", css);
        Assert.Contains(":root[data-theme=\"dark\"]", css);
        Assert.Contains(":root[data-theme=\"auto\"]", css);
        Assert.Contains("@media (prefers-color-scheme: dark)", css);
        foreach (var marker in new[] { "only-light", "only-dark", "gh-light-mode-only", "gh-dark-mode-only" })
        {
            Assert.Contains($"img[src$=\"#{marker}\"]", css);
            Assert.Contains($"a:has(> img[src$=\"#{marker}\"])", css);
        }
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
            """);

        Assert.Contains("src=\"light.svg\"", html);
        Assert.Contains("src=\"github-light.svg\"", html);
        Assert.Contains("src=\"regular.svg\"", html);
        Assert.DoesNotContain("dark.svg", html);
        Assert.DoesNotContain("gh-dark-mode-only", html);
        Assert.DoesNotContain("only-light", html);
        Assert.DoesNotContain("gh-light-mode-only", html);
    }
}
