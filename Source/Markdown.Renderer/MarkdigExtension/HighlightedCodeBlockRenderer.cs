using System.Net;
using System.Text;
using Core.Extensions.TextRelated;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdown.Renderer.MarkdigExtension;
using StaticCode.Diagrams;
using StaticCode.Highlighter;

namespace Markdown.Renderer;

public class HighlightedCodeBlockRenderer : HtmlObjectRenderer<CodeBlock>
{
  private readonly HtmlFormatter _codeFormatter = new();

  protected override void Write(Markdig.Renderers.HtmlRenderer renderer, CodeBlock obj)
    {
        var code = GetCode(obj);
        var languageName = "NoHighlight";
        string? title = null;

        if (obj is FencedCodeBlock fencedCodeBlock)
        {
            var parser = fencedCodeBlock.Parser as FencedCodeBlockParser
                ?? throw new InvalidOperationException("expected FencedCodeBlockParser");
            var info = fencedCodeBlock.Info?.Trim() ?? string.Empty;
            var infoPrefix = parser.InfoPrefix ?? string.Empty;
            if (info.StartsWith(infoPrefix, StringComparison.Ordinal))
                info = info[infoPrefix.Length..].TrimStart();

            var languageEnd = info.IndexOfAny([' ', '\t']);
            languageName = languageEnd < 0 ? info : info[..languageEnd];
            title = GetTitle(fencedCodeBlock.Arguments ?? string.Empty);
            if (string.IsNullOrEmpty(languageName)) languageName = "NoHighlight";

            if (languageName == IconSearch.CodeBlockLanguage)
            {
                renderer.WriteLine(IconSearch.Html);
                return;
            }

            if (languageName == Hero.CodeBlockLanguage)
            {
                renderer.WriteLine(Hero.Render(code));
                return;
            }

            if (languageName.Equals("math", StringComparison.OrdinalIgnoreCase))
            {
                renderer.WriteLine(MathFormulaRenderer.Render(code, display: true));
                return;
            }

            // Supported diagrams become static SVG, isometric ones with phase controls; everything else stays a readable
            // code block. The block's position tells equal diagrams on one page apart.
            if (DiagramRenderer.TryRenderHtml(languageName, code, fencedCodeBlock.Span.Start, out var diagram))
            {
                renderer.Write("<figure class=\"diagram\">").Write(diagram).WriteLine("</figure>");
                return;
            }
        }

        var lexer = Lexer.ForLanguage(languageName);
        var tokens = lexer.Analyse(code);
        var renderedHtml = _codeFormatter.WriteToString(tokens);

        if (title != null)
        {
            renderer.WriteLine("<figure class=\"code-block code-block-titled\">");
            renderer.Write("<figcaption class=\"code-block-title\">")
                .Write(WebUtility.HtmlEncode(title))
                .WriteLine("</figcaption>");
        }
        else
        {
            renderer.WriteLine("<figure class=\"code-block code-block-untitled\">");
        }

        renderer.WriteLine("<div class=\"code-block-body\">");
        WriteCopyButton(renderer);
        renderer.WriteLine(renderedHtml);
        renderer.WriteLine("</div>");
        renderer.WriteLine("</figure>");
    }

    private static string GetCode(LeafBlock obj)
    {
      var stringBuilder = new StringBuilder();
      // An empty code block has no line array.
      if (obj.Lines.Lines == null) return string.Empty;
      var isFirstLine = true;
      foreach (var line in obj.Lines.Lines)
      {
        var slice = line.Slice;
        if (slice.Text != null)
        {
          if (!isFirstLine)
            stringBuilder.AppendLine();
          stringBuilder.Append(slice.Text.Substring(slice.Start, slice.Length));
          isFirstLine = false;
        }
      }
      return stringBuilder.ToString();
    }

    private static string? GetTitle(string info)
    {
        var titleIndex = info.IndexOf("title=", StringComparison.Ordinal);
        if (titleIndex < 0) return null;

        var titleStart = titleIndex + "title=".Length;
        if (titleStart == info.Length) return null;

        var quote = info[titleStart] is '"' or '\'' ? info[titleStart++] : '\0';
        var titleEnd = quote == '\0'
            ? info.IndexOfAny([' ', '\t'], titleStart)
            : info.IndexOf(quote, titleStart);
        if (titleEnd < 0) titleEnd = info.Length;

        var title = info[titleStart..titleEnd];
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static void WriteCopyButton(Markdig.Renderers.HtmlRenderer renderer) =>
        renderer.Write("<button class=\"code-block-copy\" type=\"button\" aria-label=\"Copy code\" title=\"Copy code\">")
            .Write("<svg class=\"copy-icon\" viewBox=\"0 0 20 20\" aria-hidden=\"true\" focusable=\"false\"><rect x=\"7\" y=\"6\" width=\"10\" height=\"12\" rx=\"1.5\"></rect><path d=\"M13 6V4.5A1.5 1.5 0 0 0 11.5 3h-7A1.5 1.5 0 0 0 3 4.5v9A1.5 1.5 0 0 0 4.5 15H7\"></path></svg><svg class=\"success-icon\" viewBox=\"0 0 20 20\" aria-hidden=\"true\" focusable=\"false\"><path d=\"m4 10 4 4 8-8\"></path></svg><span>Copy</span>")
            .WriteLine("</button>");
    
  }
