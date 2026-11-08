using System.Text.RegularExpressions;

namespace Markdown.Renderer.Test;

public class ImageFrameTest
{
    [Fact]
    public void HtmlKeepsTheFrameClassOnEachThemeVariant()
    {
        var html = new HtmlRenderer().RenderMarkdownFragment("""
            ![Lesson grid](lessons-light.png#only-light){.image-frame}
            ![Lesson grid](lessons-dark.png#only-dark){.image-frame}
            ![Diagram](diagram.png)
            """);

        Assert.Contains("<img src=\"lessons-light.png#only-light\" class=\"image-frame\"", html);
        Assert.Contains("<img src=\"lessons-dark.png#only-dark\" class=\"image-frame\"", html);
        Assert.Contains("<img src=\"diagram.png\" alt=\"Diagram\"", html);
    }

    [Theory]
    [InlineData(":root {", "#c7c7cc")]
    [InlineData(":root[data-theme=\"dark\"] {", "#48484d")]
    [InlineData("@media (prefers-color-scheme: dark) {\n    :root[data-theme=\"auto\"] {", "#48484d")]
    [InlineData(".image-lightbox {", "#48484d")]
    public void EveryThemeDefinesAnOpaqueFrameColor(string selector, string color)
    {
        Assert.Contains($"--image-frame: {color};", Block(Resources.CssContent(), selector));
    }

    [Fact]
    public void FramesOnlyImagesThatOptInAndYieldsToTheFocusRing()
    {
        var css = Resources.CssContent();

        Assert.Contains("drop-shadow(1px 0 0 var(--image-frame))", Block(css, "img.image-frame {"));
        Assert.Contains("filter: none;", Block(css, "img.image-frame:focus-visible {"));
        Assert.DoesNotContain("filter", Block(css, "img {"));
    }

    [Fact]
    public void ViewerCarriesTheFrameOverWithoutARectangularShadow()
    {
        Assert.Contains("preview.classList.toggle(\"image-frame\", image.classList.contains(\"image-frame\"))", Resources.ImageLightboxScriptContent());
        Assert.DoesNotContain("box-shadow", Block(Resources.CssContent(), ".image-lightbox-stage img {"));
    }

    /// <summary>Returns the declarations of the first rule that starts a line with <paramref name="selector"/>.</summary>
    private static string Block(string css, string selector)
    {
        var start = Regex.Match(css, "(?m)^" + Regex.Escape(selector.Replace("\n", Environment.NewLine)));
        if (!start.Success) start = Regex.Match(css, "(?m)^" + Regex.Escape(selector));
        Assert.True(start.Success, $"No rule starts with '{selector}'.");
        var end = css.IndexOf('}', start.Index + start.Length);
        return css[(start.Index + start.Length)..end];
    }
}
