using Markdown.Document.Model;

namespace Markdown.Document;

public interface IMarkdownDocumentReader
{
    /// <summary>
    /// Reads the markdown file header consisting of title and optional front matter.
    /// </summary>
    /// <param name="input">input of the Parser</param>
    /// <returns></returns>
    IMarkdownHeader ReadHeader(string input);

    /// <summary>
    /// Reads all links from the markdown file
    /// </summary>
    /// <param name="input">input of the parser</param>
    /// <returns>Links that are found in the markdown file</returns>
    IEnumerable<IMarkdownLink> ReadLinks(string input);

    /// <summary>
    /// Reads links and footnote diagnostics from one parsed Markdown document.
    /// </summary>
    MarkdownDocumentAnalysis ReadAnalysis(string input);
}
