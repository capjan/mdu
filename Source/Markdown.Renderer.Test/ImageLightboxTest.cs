namespace Markdown.Renderer.Test;

public class ImageLightboxTest
{
    private const string ScriptMarker = "js/image-lightbox.js";

    private static async Task<string> RenderAsync(string markdown, bool imageZoom = true)
    {
        await using var writer = new StringWriter();
        await new HtmlRenderer().WriteHtml(writer, "test", "", 0, markdown, "", imageZoom: imageZoom);
        return writer.ToString();
    }

    [Fact]
    public async Task AddsTheViewerOnlyToPagesWithImages()
    {
        Assert.Contains(ScriptMarker, await RenderAsync("# Title\n\n![Alt](image.png)\n"));
        Assert.Contains(ScriptMarker, await RenderAsync("# Title\n\n<IMG src=\"image.png\">\n"));
        Assert.DoesNotContain(ScriptMarker, await RenderAsync("# Title\n\nNo image, only `<img>` as code.\n"));
    }

    [Theory]
    [InlineData("---\nimages.zoom: false\n---", false)]
    [InlineData("---\nimages:\n  zoom: false\n---", false)]
    [InlineData("---\nimages.zoom: true\n---", true)]
    [InlineData("---\ntitle: Images\n---", true)]
    [InlineData("---\nimages.zoom: maybe\n---", true)]
    [InlineData("---\nimages: [\n---", true)]
    [InlineData("", true)]
    public void ReadsThePageSettingFromFrontMatter(string frontMatter, bool expected)
    {
        Assert.Equal(expected, ImageZoom.ReadFrontMatter(frontMatter));
    }

    [Theory]
    [InlineData("---\nimages.zoom: off\n---", "images.zoom must be 'true' or 'false', not 'off'.")]
    [InlineData("---\nimages:\n  zoom: 1\n---", "images.zoom must be 'true' or 'false', not '1'.")]
    [InlineData("---\nimages: false\n---", "Use 'images.zoom: false' instead of 'images: false'.")]
    [InlineData("---\nimages: [cover.png]\n---", null)]
    [InlineData("---\nimages: cover.png\n---", null)]
    [InlineData("---\nimages: false\nimages.zoom: false\n---", null)]
    [InlineData("---\nimages.zoom: false\n---", null)]
    [InlineData("---\ntitle: Images\n---", null)]
    public void FindsProblemsWithThePageSetting(string frontMatter, string? expected)
    {
        Assert.Equal(expected, ImageZoom.FindProblem(frontMatter));
    }

    [Fact]
    public void LeavesFootnotePreviewsOutOfTheTabOrder()
    {
        Assert.Contains(".footnote-preview", Resources.ImageLightboxScriptContent());
    }

    [Fact]
    public void LeavesModifierClicksOnLinksToTheBrowser()
    {
        Assert.Contains("event.metaKey || event.ctrlKey || event.shiftKey || event.altKey", Resources.ImageLightboxScriptContent());
    }

    [Fact]
    public async Task LeavesTheViewerOutWhenThePageTurnsItOff()
    {
        var html = await RenderAsync("# Title\n\n![Alt](image.png)\n", imageZoom: false);

        Assert.DoesNotContain(ScriptMarker, html);
    }

    [Fact]
    public async Task KeepsTheViewerForImagesThatTurnItOnAgain()
    {
        var html = await RenderAsync("# Title\n\n![Alt](image.png){.image-zoom-on}\n", imageZoom: false);

        Assert.Contains("class=\"image-zoom-on\"", html);
        Assert.Contains("<script defer src=\"js/image-lightbox.js\" data-image-zoom=\"off\"></script>", html);
    }

    [Fact]
    public async Task LetsSingleImagesTurnTheViewerOff()
    {
        var html = await RenderAsync("# Title\n\n![Logo](logo.png){.image-zoom-off}\n");

        Assert.Contains("class=\"image-zoom-off\"", html);
        Assert.Contains("image.closest(\".image-zoom-on, .image-zoom-off\")", Resources.ImageLightboxScriptContent());
        Assert.Contains(ScriptMarker, html);
    }

    [Fact]
    public void OffersZoomControlsThatStayFocusable()
    {
        var script = Resources.ImageLightboxScriptContent();

        foreach (var action in new[] { "in", "out", "fit", "actual" })
            Assert.Contains($"data-zoom=\"{action}\"", script);
        Assert.Contains("role=\"group\"", script);
        Assert.Contains("setAttribute(\"aria-disabled\"", script);
        Assert.DoesNotContain(".disabled", script);
    }

    [Fact]
    public void KeepsThePageFromScrollingWhileTheViewerIsOpen()
    {
        Assert.Matches(@"html:has\(\.image-lightbox\[open\]\)\s*\{\s*overflow:\s*hidden;\s*scrollbar-gutter:\s*auto;\s*\}", Resources.CssContent());
    }

    [Fact]
    public void ShowsAZoomCursorOnImagesThatOpenTheViewer()
    {
        Assert.Contains("image.classList.toggle(\"image-zoomable\", opens)", Resources.ImageLightboxScriptContent());
        Assert.Matches(@"\.image-zoomable\s*\{\s*cursor:\s*zoom-in;\s*\}", Resources.CssContent());
    }

    [Fact]
    public void DoesNotOfferTheViewerForHiddenImages()
    {
        Assert.Contains("image.getClientRects().length === 0", Resources.ImageLightboxScriptContent());
    }

    [Fact]
    public void UsesTheSharedFocusRuleForViewerButtons()
    {
        var css = Resources.CssContent();
        var sharedFocusRule = css[..css.IndexOf("outline: 2px solid var(--link);", StringComparison.Ordinal)];

        Assert.Contains(".image-lightbox button:focus-visible", sharedFocusRule);
        Assert.Contains(".image-lightbox-trigger:focus-visible", sharedFocusRule);
    }
}
