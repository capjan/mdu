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
}