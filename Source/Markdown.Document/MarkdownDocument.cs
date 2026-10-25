using Core.Parser.Special;
using Markdown.Document.Impl;
using Markdown.Document.Model;

namespace Markdown.Document;

public static class MarkdownDocument
{
    public static IMarkdownDocumentReader Reader()
    {
        return new MarkdownDocumentReader();
    }
    
    public static IMarkdownHeader ReadHeader(string documentContent)
    {
        return Reader().ReadHeader(documentContent);
    }

    public static IEnumerable<IMarkdownLink> ReadLinks(string documentContent)
    {
        return Reader().ReadLinks(documentContent);
    }

    public static MarkdownDocumentAnalysis ReadAnalysis(string documentContent)
    {
        return Reader().ReadAnalysis(documentContent);
    }

    /// <summary>
    /// Reads the URLs in <c>src</c>, <c>href</c> and <c>poster</c> attributes of an HTML file, outside comments.
    /// </summary>
    public static IEnumerable<string> ReadHtmlUrls(string html)
    {
        return MarkdownDocumentReader.ReadHtmlAttributeUrls(html);
    }
}