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
    public void ReadLinksDoesNotTreatFootnoteDefinitionsAsLinks()
    {
        const string markdown = "# Overview\n\n[^unused]: Not a link target.\n";

        Assert.Empty(MarkdownDocument.ReadLinks(markdown));
    }
}
