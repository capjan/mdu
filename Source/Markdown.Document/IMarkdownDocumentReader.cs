using Core.Parser;
using Core.Parser.Special;
using Markdown.Document.Model;

namespace Markdown.Document;

public interface IMarkdownDocumentReader
{
    /// <summary>
    /// Reads the markdown file header consisting of title and optional front matter.
    /// </summary>
    /// <param name="input">input of the Parser</param>
    /// <returns></returns>
    IMarkdownHeader ReadHeader(IParserInput input);

    /// <summary>
    /// Reads all links from the markdown file
    /// </summary>
    /// <param name="input">input of the parser</param>
    /// <returns>Links that are found in the markdown file</returns>
    IEnumerable<IMarkdownLink> ReadLinks(IParserInput input);

    /// <summary>
    /// Reads links and footnote diagnostics from one parsed Markdown document.
    /// </summary>
    MarkdownDocumentAnalysis ReadAnalysis(IParserInput input);
}

public static class MarkdownDocumentReaderExtensions
{
    public static IMarkdownHeader ReadHeader(this IMarkdownDocumentReader reader, string documentContent)
    {
        var parserInput = ParserInput.CreateFromString(documentContent);
        return reader.ReadHeader(parserInput);
    }
    
    public static IEnumerable<IMarkdownLink> ReadLinks(this IMarkdownDocumentReader reader, string documentContent)
    {
        var parserInput = ParserInput.CreateFromString(documentContent);
        return reader.ReadLinks(parserInput);
    }

    public static MarkdownDocumentAnalysis ReadAnalysis(this IMarkdownDocumentReader reader, string documentContent)
    {
        var parserInput = ParserInput.CreateFromString(documentContent);
        return reader.ReadAnalysis(parserInput);
    }
}
