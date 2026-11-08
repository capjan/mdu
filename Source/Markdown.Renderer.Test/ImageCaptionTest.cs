namespace Markdown.Renderer.Test;

public class ImageCaptionTest
{
    private const string Figure = """
        ^^^
        ![Lesson grid](lessons-light.png#only-light){.image-frame}
        ![Lesson grid](lessons-dark.png#only-dark){.image-frame}
        ^^^ Use keyboard previews to **choose an exercise**.
        """;

    [Fact]
    public void ThemeVariantsShareOneCaptionAndKeepTheirAltText()
    {
        var html = new HtmlRenderer().RenderMarkdownFragment(Figure);

        Assert.Contains("<figure>", html);
        Assert.Contains("<img src=\"lessons-light.png#only-light\" class=\"image-frame\" alt=\"Lesson grid\"", html);
        Assert.Contains("<img src=\"lessons-dark.png#only-dark\" class=\"image-frame\" alt=\"Lesson grid\"", html);
        Assert.Contains("<figcaption>Use keyboard previews to <strong>choose an exercise</strong>.</figcaption>", html);
        Assert.Equal(2, html.Split("<figcaption>").Length);
    }

    [Fact]
    public void ViewerUsesPlainCaptionTextAndKeepsThePreviewAltText()
    {
        var script = Resources.ImageLightboxScriptContent();

        Assert.Contains("image.closest(\"figure\")?.querySelector(\":scope > figcaption\")?.textContent", script);
        Assert.Contains("const label = figureCaption || image.alt;", script);
        Assert.Contains("caption.textContent = label;", script);
        Assert.Contains("dialog.setAttribute(\"aria-label\", label || \"Image\");", script);
        Assert.Contains("preview.alt = image.alt;", script);
    }

    [Fact]
    public void ImageFiguresAndViewerCaptionsHaveDedicatedStyles()
    {
        var css = Resources.CssContent();

        Assert.Contains(".markdown-content figure:has(img) > figcaption {", css);
        Assert.Contains(".image-lightbox-caption.has-figure-caption {", css);
    }
}
