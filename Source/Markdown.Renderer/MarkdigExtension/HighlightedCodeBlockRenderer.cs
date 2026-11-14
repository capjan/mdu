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

  // Languages whose blocks are not code: each renders its block, or returns null to keep the block as readable code.
  // The instance, the block's position, tells equal diagrams on one page apart.
  private static readonly Dictionary<string, Func<string, int, string?>> SpecialLanguages = new(StringComparer.OrdinalIgnoreCase)
  {
      [IconSearch.CodeBlockLanguage] = (_, _) => IconSearch.Html,
      [Hero.CodeBlockLanguage] = (code, _) => Hero.Render(code),
      ["math"] = (code, _) => MathFormulaRenderer.Render(code, display: true),
      ["mermaid"] = RenderDiagram("mermaid"),
      ["context"] = RenderDiagram("context"),
      ["plantuml"] = RenderDiagram("plantuml"),
      ["puml"] = RenderDiagram("puml"),
      [DiagramRenderer.IsometricLanguage] = RenderDiagram(DiagramRenderer.IsometricLanguage)
  };

  // Supported diagrams become static SVG, isometric ones with phase controls; everything else stays a readable code block.
  private static Func<string, int, string?> RenderDiagram(string language) => (code, instance) =>
      DiagramRenderer.TryRenderHtml(language, code, instance, out var diagram) ? $"<figure class=\"diagram\">{diagram}</figure>" : null;

  /// <summary>Returns whether blocks of a language are code, as opposed to diagrams, formulas, or page components.</summary>
  public static bool IsCodeLanguage(string language) => !SpecialLanguages.ContainsKey(language);

  protected override void Write(Markdig.Renderers.HtmlRenderer renderer, CodeBlock obj)
    {
        var code = GetCode(obj);
        var languageName = "NoHighlight";
        string? title = null;

        if (obj is FencedCodeBlock fencedCodeBlock)
        {
            languageName = GetLanguage(fencedCodeBlock);
            title = GetTitle(fencedCodeBlock.Arguments ?? string.Empty);

            if (SpecialLanguages.TryGetValue(languageName, out var render) &&
                render(code, fencedCodeBlock.Span.Start) is { } special)
            {
                renderer.WriteLine(special);
                return;
            }
        }

        var annotations = CodeAnnotations.GetAnnotations(obj);
        var formatter = _codeFormatter;
        string? copyText = null;
        IReadOnlyList<LanguageToken> tokens;
        if (annotations is not null)
        {
            tokens = annotations.MarkedTokens();
            formatter = new HtmlFormatter { AnnotationIdPrefix = CodeAnnotations.IdPrefix(obj) };
            copyText = CodeAnnotationMarkers.CopyText(tokens);
        }
        else
        {
            tokens = Lexer.ForLanguage(languageName).Analyse(code).ToList();
        }
        var renderedHtml = formatter.WriteToString(tokens);
        // Floating markers need more room between the lines of their block; see main.css.
        var annotatedClass = annotations is null ? string.Empty
            : renderedHtml.Contains(HtmlFormatter.FloatingAnnotationClass, StringComparison.Ordinal)
                ? " code-block-annotated code-block-floating-markers"
                : " code-block-annotated";

        if (title != null)
        {
            renderer.WriteLine($"<figure class=\"code-block code-block-titled{annotatedClass}\">");
            renderer.Write("<figcaption class=\"code-block-title\">")
                .Write(WebUtility.HtmlEncode(title))
                .WriteLine("</figcaption>");
        }
        else
        {
            renderer.WriteLine($"<figure class=\"code-block code-block-untitled{annotatedClass}\">");
        }

        renderer.Write("<div class=\"code-block-body\"");
        if (copyText is not null) renderer.Write(" data-copy=\"").Write(WebUtility.HtmlEncode(copyText)).Write('"');
        renderer.WriteLine(">");
        WriteCopyButton(renderer);
        renderer.WriteLine(renderedHtml);
        renderer.WriteLine("</div>");
        if (annotations is not null) WriteAnnotations(renderer, obj, annotations.List);
        renderer.WriteLine("</figure>");
    }

    // Each entry opens as a popover from its marker, with its number at the start. The legend below the code lists all
    // entries for an overview, for touch screens, for browsers without popovers, and for print; it starts closed.
    private static void WriteAnnotations(Markdig.Renderers.HtmlRenderer renderer, CodeBlock code, ListBlock list)
    {
        var idPrefix = WebUtility.HtmlEncode(CodeAnnotations.IdPrefix(code));
        var implicitParagraph = renderer.ImplicitParagraph;
        renderer.ImplicitParagraph = !list.IsLoose;
        // Each entry is rendered once and shown twice. Only the popover keeps the ids of its content, such as those of
        // footnote references, so the page has every id once.
        var entries = list.OfType<ListItemBlock>().Select(item => RenderToString(renderer, item)).ToList();
        renderer.ImplicitParagraph = implicitParagraph;

        renderer.WriteLine("<ol class=\"code-annotations\">");
        for (var index = 0; index < entries.Count; index++)
        {
            var number = Number(index);
            var id = idPrefix + number;
            renderer.Write("<li id=\"").Write(id).Write("\" popover=\"auto\" style=\"position-anchor: --").Write(id).Write("\">");
            WriteEntry(renderer, number, entries[index]);
            renderer.WriteLine("</li>");
        }
        renderer.WriteLine("</ol>");

        var count = entries.Count == 1 ? "1 annotation" : $"{Number(entries.Count - 1)} annotations";
        renderer.Write("<details class=\"code-legend\"><summary>").Write(count).WriteLine("</summary>");
        renderer.WriteLine("<ol>");
        for (var index = 0; index < entries.Count; index++)
        {
            var number = Number(index);
            renderer.Write("<li data-annotation=\"").Write(idPrefix + number).Write("\">");
            WriteEntry(renderer, number, IdAttribute.Replace(entries[index], string.Empty));
            renderer.WriteLine("</li>");
        }
        renderer.WriteLine("</ol>");
        renderer.WriteLine("</details>");

        static string Number(int index) => (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static readonly System.Text.RegularExpressions.Regex IdAttribute = new(
        "\\s+id=\"[^\"]*\"", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string RenderToString(Markdig.Renderers.HtmlRenderer renderer, ListItemBlock item)
    {
        var writer = renderer.Writer;
        using var text = new StringWriter();
        renderer.Writer = text;
        try
        {
            renderer.WriteChildren(item);
        }
        finally
        {
            renderer.Writer = writer;
        }
        return text.ToString();
    }

    private static void WriteEntry(Markdig.Renderers.HtmlRenderer renderer, string number, string html)
    {
        renderer.Write("<span class=\"code-annotation-number\" aria-hidden=\"true\">").Write(number).Write("</span>");
        renderer.Write("<div class=\"code-annotation-text\">").Write(html).Write("</div>");
    }

    internal static string GetLanguage(FencedCodeBlock block)
    {
        var parser = block.Parser as FencedCodeBlockParser
            ?? throw new InvalidOperationException("expected FencedCodeBlockParser");
        var info = block.Info?.Trim() ?? string.Empty;
        var infoPrefix = parser.InfoPrefix ?? string.Empty;
        if (info.StartsWith(infoPrefix, StringComparison.Ordinal))
            info = info[infoPrefix.Length..].TrimStart();

        var languageEnd = info.IndexOfAny([' ', '\t']);
        var languageName = languageEnd < 0 ? info : info[..languageEnd];
        return string.IsNullOrEmpty(languageName) ? "NoHighlight" : languageName;
    }

    internal static string GetCode(LeafBlock obj)
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
