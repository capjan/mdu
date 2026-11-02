namespace Markdown.Renderer.Test;

public sealed class MediaDiscoveryTest
{
    [Theory]
    [InlineData("[Watch](https://youtu.be/hyeCMRQhVOI)", "YouTube")]
    [InlineData("> [Watch](https://vimeo.com/76979871)", "Vimeo")]
    [InlineData("[Watch][video]\n\n[video]: https://youtu.be/hyeCMRQhVOI", "YouTube")]
    [InlineData("<p><a href=\"https://vimeo.com/76979871\">Watch</a></p>", "Vimeo")]
    [InlineData("[Watch](https://you&#116;u.be/hyeCMRQhVOI)", "YouTube")]
    public void KeepsRenderedLinkSemantics(string markdown, string provider)
    {
        Assert.Equal(new[] { provider }, new HtmlRenderer().FindMediaProviders(markdown));
    }

    [Theory]
    [InlineData("```csharp\n// https://youtu.be/hyeCMRQhVOI\n```")]
    [InlineData("```plantuml\n@startuml\n[One] --> [Two]\n@enduml\n```")]
    [InlineData("Inline [watch](https://youtu.be/hyeCMRQhVOI) in a sentence.")]
    [InlineData("![Poster](https://youtu.be/hyeCMRQhVOI)")]
    [InlineData("`:material-home:` :material-home:")]
    public void ContentWithoutStandaloneMediaHasNoProviders(string markdown)
    {
        Assert.Empty(new HtmlRenderer().FindMediaProviders(markdown));
    }

    [Fact]
    public void FootnoteContainersRetainTheRenderedHtmlDecision()
    {
        const string markdown = "Text[^1]\n\n[^1]: [Watch](https://youtu.be/hyeCMRQhVOI)";
        var renderer = new HtmlRenderer();
        // Compare discovery against actual rendered consent markup for this container.
        var rendered = renderer.RenderMarkdownFragment(markdown);
        Assert.Equal(rendered.Contains("data-provider=\"YouTube\"", StringComparison.Ordinal), renderer.FindMediaProviders(markdown).Contains("YouTube"));
    }
}
