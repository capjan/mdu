using System.Net;
using Core.Text.Formatter;

namespace StaticCode.Highlighter;

/// <summary>
/// Formatter that produces html markup from a token stream dedicated to be embedded in a html website
/// </summary>
public class HtmlFormatter : ITextFormatter<IEnumerable<LanguageToken>>
{
    /// <summary>
    /// Prefix of the element ids that annotation markers open. Each marker opens the element whose id is this prefix
    /// followed by the annotation number.
    /// </summary>
    public string AnnotationIdPrefix { get; init; } = "code-annotation-";

    /// <summary>Class of a marker in a comment that ends its line: it explains the line and sits in it.</summary>
    public const string InlineAnnotationClass = "code-annotation-inline";

    /// <summary>Class of any other marker: it explains the word before it and floats above it without taking width.</summary>
    public const string FloatingAnnotationClass = "code-annotation-floating";
    public void Write(IEnumerable<LanguageToken> tokens, TextWriter writer)
    {
        writer.Write("<pre>");
        writer.Write("<code>");
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        TokenType? previous = null;
        // A marker right after visible comment characters, or after another such marker, stays in the line.
        var previousInline = false;
        foreach (var token in tokens)
        {
            switch (token.Type)
            {
                case TokenType.Annotation:
                    var number = WebUtility.HtmlEncode(token.Text);
                    var target = WebUtility.HtmlEncode(AnnotationIdPrefix) + number;
                    // Each marker has its own anchor name. The popover's own position anchor is the first marker of its
                    // number; code-copy.js moves it to the marker that opens it when a number appears more than once.
                    var occurrence = occurrences[target] = occurrences.GetValueOrDefault(target) + 1;
                    var anchor = occurrence == 1 ? target : $"{target}-{occurrence}";
                    previousInline = previous is TokenType.Comment || previous is TokenType.Annotation && previousInline;
                    writer.Write("<button class=\"code-annotation ");
                    writer.Write(previousInline ? InlineAnnotationClass : FloatingAnnotationClass);
                    writer.Write("\" type=\"button\" popovertarget=\"");
                    writer.Write(target);
                    writer.Write("\" style=\"anchor-name: --");
                    writer.Write(anchor);
                    writer.Write("\" aria-label=\"Annotation ");
                    writer.Write(number);
                    writer.Write("\"><span class=\"code-annotation-number\">");
                    writer.Write(number);
                    writer.Write("</span></button>");
                    break;
                case TokenType.Comment:
                    writer.Write("<span class=\"comment\">");
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    writer.Write("</span>");
                    break;
                case TokenType.Keyword:
                    writer.Write("<span class=\"keyword\">");
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    writer.Write("</span>");
                    break;
                case TokenType.Key:
                    writer.Write("<span class=\"key\">");
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    writer.Write("</span>");
                    break;
                case TokenType.Command:
                    writer.Write("<span class=\"command\">");
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    writer.Write("</span>");
                    break;
                case TokenType.Subcommand:
                    writer.Write("<span class=\"subcommand\">");
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    writer.Write("</span>");
                    break;
                case TokenType.Option:
                    writer.Write("<span class=\"option\">");
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    writer.Write("</span>");
                    break;
                case TokenType.Argument:
                    writer.Write("<span class=\"argument\">");
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    writer.Write("</span>");
                    break;
                case TokenType.Operator:
                    writer.Write("<span class=\"operator\">");
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    writer.Write("</span>");
                    break;
                case TokenType.String:
                    writer.Write("<span class=\"string\">");
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    writer.Write("</span>");
                    break;
                case TokenType.Text:
                case TokenType.Whitespace:
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    break;
                case TokenType.NewLine:
                    writer.WriteLine();
                    break;
                case TokenType.Number:
                    writer.Write("<span class=\"number\">");
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    writer.Write("</span>");
                    break;
                case TokenType.MarkdownHeading:
                    writer.Write("<span class=\"md-heading\">");
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    writer.Write("</span>");
                    break;
                case TokenType.MarkdownSyntax:
                    writer.Write("<span class=\"md-syntax\">");
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    writer.Write("</span>");
                    break;
                case TokenType.MarkdownLink:
                    writer.Write("<span class=\"md-link\">");
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    writer.Write("</span>");
                    break;
                case TokenType.MarkdownUrl:
                    writer.Write("<span class=\"md-url\">");
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    writer.Write("</span>");
                    break;
                case TokenType.MarkdownCode:
                    writer.Write("<span class=\"md-code\">");
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    writer.Write("</span>");
                    break;
                case TokenType.MarkdownEmphasis:
                    writer.Write("<span class=\"md-emphasis\">");
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    writer.Write("</span>");
                    break;
                case TokenType.MarkdownStrong:
                    writer.Write("<span class=\"md-strong\">");
                    writer.Write(WebUtility.HtmlEncode(token.Text));
                    writer.Write("</span>");
                    break;
                case TokenType.MarkdownAlertNote:
                    WriteMarkdownAlert(token, writer, "note");
                    break;
                case TokenType.MarkdownAlertTip:
                    WriteMarkdownAlert(token, writer, "tip");
                    break;
                case TokenType.MarkdownAlertImportant:
                    WriteMarkdownAlert(token, writer, "important");
                    break;
                case TokenType.MarkdownAlertWarning:
                    WriteMarkdownAlert(token, writer, "warning");
                    break;
                case TokenType.MarkdownAlertCaution:
                    WriteMarkdownAlert(token, writer, "caution");
                    break;
            }
            previous = token.Type;
        }
        writer.Write("</code>");
        writer.Write("</pre>");
    }

    private static void WriteMarkdownAlert(LanguageToken token, TextWriter writer, string kind)
    {
        writer.Write($"<span class=\"md-alert-{kind}\">");
        writer.Write(WebUtility.HtmlEncode(token.Text));
        writer.Write("</span>");
    }
}
