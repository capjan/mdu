using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Markdown.Document.Test;

public class UnitTestMarkdownDocument
{
    [Theory]
    [InlineData("# Home\n\n## Themen\n\nOtherText", "Home")]
    [InlineData("  # Home\n\n## Themen\n\nOtherText", "Home")]
    [InlineData("", "")]
    [InlineData("# h1 ( bla )\n", "h1 ( bla )")]
    [InlineData("# h1 /|\\.,.<>1234567890$#()\r\n", "h1 /|\\.,.<>1234567890$#()")]
    [InlineData("# Home\n\nHome\n\nOther Text", "Home")]
    [InlineData("---\ntitle: pageTitle\n---\n# Headline 1\n\n[Home](../README.md)", "Headline 1")]
    public void TestGetHeader(string input, string expectedTitle)
    {
        var header = MarkdownDocument.ReadHeader(input);
        Assert.Equal(expectedTitle, header.Title);
    }

    [Fact]
    public void TestHead()
    {
        var simpleDocument = EmbeddedResources.Strings.SimpleDocument;
        var header = MarkdownDocument.ReadHeader(simpleDocument);
        Assert.Equal("Title of the Markdown File", header.Title);
    }
    
    [Fact]
    public void ReadLinks()
    {
        var simpleDocument = EmbeddedResources.Strings.SimpleDocument;
        var links = MarkdownDocument.ReadLinks(simpleDocument).ToArray();
        Assert.NotEmpty(links);
        Assert.Equal("Project Website", links[0].Name);
    }

    [Fact]
    public void ReadLinksIncludesLinksInUnusedAndDuplicateFootnotes()
    {
        const string markdown = "# Overview\n\nA note[^used].\n\n[^unused]: See [Guide](guide.md).\n\n[^duplicate]: See [First](first.md).\n\n[^duplicate]: See [Second](second.md).\n\n[^used]: See [Used](used.md).\n";

        var links = MarkdownDocument.ReadLinks(markdown).ToArray();

        Assert.Equal(new[] { "first.md", "guide.md", "second.md", "used.md" },
            links.Select(link => link.Destination).OrderBy(destination => destination, StringComparer.Ordinal));
    }

    [Fact]
    public void ReadLinksIncludesUrlsInRawHtml()
    {
        const string markdown = "# Overview\n\n<iframe src=\"assets/demo/index.html\"></iframe>\n\nText with <img src='images/logo.png' alt=\"Logo\"> inline.\n\n<video poster=\"poster.jpg\">\n<source src=\"clip.mp4\">\n</video>\n";

        var links = MarkdownDocument.ReadLinks(markdown).ToArray();

        Assert.Equal(new[] { "assets/demo/index.html", "clip.mp4", "images/logo.png", "poster.jpg" },
            links.Select(link => link.Destination).OrderBy(destination => destination, StringComparer.Ordinal));
        Assert.Equal(3, links.Single(link => link.Destination == "assets/demo/index.html").LineNumber);
    }

    [Fact]
    public void ReadLinksIgnoresHtmlComments()
    {
        const string markdown = "# Overview\n\n<!-- <iframe src=\"missing.html\"></iframe> -->\n\nText <!-- <img src=\"missing.png\"> --> inline.\n\n<div>\n<!--\n<img src=\"hidden.png\">\n-->\n<img src=\"visible.png\">\n</div>\n";

        var links = MarkdownDocument.ReadLinks(markdown).ToArray();

        var link = Assert.Single(links);
        Assert.Equal("visible.png", link.Destination);
        Assert.Equal(11, link.LineNumber);
    }

    [Fact]
    public void ReadLinksFindsAttributesOfMultiLineTags()
    {
        const string markdown = "<iframe\nsrc=\"assets/demo.html\"\ndata-src=\"ignored.html\">\n</iframe>\n";

        var link = Assert.Single(MarkdownDocument.ReadLinks(markdown));
        Assert.Equal("assets/demo.html", link.Destination);
        Assert.Equal(2, link.LineNumber);
    }

    [Fact]
    public void ReadLinksIgnoresHtmlInCode()
    {
        const string markdown = "# Overview\n\n```html\n<iframe src=\"demo.html\"></iframe>\n```\n\nUse `<img src=\"logo.png\">`.\n";

        Assert.Empty(MarkdownDocument.ReadLinks(markdown));
    }

    [Fact]
    public void ReadLinksDoesNotTreatFootnoteDefinitionsAsLinks()
    {
        const string markdown = "# Overview\n\n[^unused]: Not a link target.\n";

        Assert.Empty(MarkdownDocument.ReadLinks(markdown));
    }
}
