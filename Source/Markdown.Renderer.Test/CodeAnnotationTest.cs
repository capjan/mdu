using System.Text;
using System.Text.RegularExpressions;
using Markdig.Syntax;

namespace Markdown.Renderer.Test;

public class CodeAnnotationTest
{
    private const string Annotated = """
        ```yaml title="mdu.yml"
        site:
          name: Handbook # (1)!
          url: https://docs.example.com # (2)
        ```

        1. Shown in the header and the browser tab.
        2. Without `url` there is no sitemap.

        After.
        """;

    [Fact]
    public void RendersMarkersAsButtonsThatOpenTheirEntries()
    {
        var html = new HtmlRenderer().RenderMarkdownFragment(Annotated);
        var id = Regex.Match(html, "popovertarget=\"(?<id>code-annotation-\\d+-)1\"").Groups["id"].Value;

        Assert.Contains("<figure class=\"code-block code-block-titled code-block-annotated\">", html);
        Assert.NotEmpty(id);
        Assert.Contains($": Handbook <button class=\"code-annotation\" type=\"button\" popovertarget=\"{id}1\"", html);
        Assert.Contains($"<span class=\"comment\"># </span><button class=\"code-annotation\" type=\"button\" popovertarget=\"{id}2\"", html);
        Assert.Contains($"<li id=\"{id}1\" popover=\"auto\" style=\"position-anchor: --{id}1\">Shown in the header and the browser tab.</li>", html);
        Assert.Contains($"<li id=\"{id}2\" popover=\"auto\" style=\"position-anchor: --{id}2\">Without <code>url</code> there is no sitemap.</li>", html);
        Assert.Contains("data-copy=\"site:\n  name: Handbook\n  url: https://docs.example.com\">", html.ReplaceLineEndings("\n"));
        // The list moved into the code block and is no longer an ordinary list after it.
        Assert.Single(Regex.Matches(html, "<ol"));
        Assert.True(html.IndexOf("</figure>", StringComparison.Ordinal) < html.IndexOf("<p>After.</p>", StringComparison.Ordinal));
    }

    [Fact]
    public void TellsTwoAnnotatedBlocksOnOnePageApart()
    {
        var html = new HtmlRenderer().RenderMarkdownFragment(Annotated + "\n\n" + Annotated);
        var ids = Regex.Matches(html, "<li id=\"(?<id>[^\"]+)\"").Select(match => match.Groups["id"].Value).ToArray();

        Assert.Equal(4, ids.Length);
        Assert.Equal(4, ids.Distinct().Count());
    }

    [Fact]
    public void KeepsAListThatHasNoMarkerInTheCode()
    {
        var html = new HtmlRenderer().RenderMarkdownFragment("```python\nprint(1)\n```\n\n1. First\n2. Second\n");

        Assert.DoesNotContain("code-annotation", html);
        Assert.Contains("<ol>\n<li>First</li>", html);
    }

    [Fact]
    public void KeepsAListThatDoesNotStartAtOne()
    {
        var html = new HtmlRenderer().RenderMarkdownFragment("```python\nx = 1 # (1)\n```\n\n3. Third\n");

        Assert.DoesNotContain("code-annotation", html);
        Assert.Contains("# (1)", html);
    }

    [Fact]
    public void KeepsCodeWithoutAListUnchanged()
    {
        var html = new HtmlRenderer().RenderMarkdownFragment("```python\nx = 1 # (1)\n```\n\nText.\n");

        Assert.DoesNotContain("code-annotation", html);
        Assert.Contains("# (1)", html);
    }

    [Fact]
    public void AnnotatesBelowAResult()
    {
        var html = new HtmlRenderer().RenderMarkdownFragment("```bash\nmdu tree (1)\n```\n> [!RESULT]\n> Docs\n\n1. Prints the tree.\n");

        Assert.Contains("class=\"code-annotation\"", html);
        Assert.Contains("Prints the tree.</li>", html);
        Assert.Contains("result-block", html);
    }

    [Fact]
    public void AnnotatesCodeInsideAListItem()
    {
        var html = new HtmlRenderer().RenderMarkdownFragment("- Step\n\n  ```bash\n  mdu render (1)\n  ```\n\n  1. Writes the site.\n");

        Assert.Contains("class=\"code-annotation\"", html);
        Assert.Contains("Writes the site.</li>", html);
    }

    [Theory]
    [InlineData("```python annotate=false\nx = 1 # (1)\n```\n\n1. One\n", null, false)]
    [InlineData("```python\nx = 1 # (1)\n```\n\n1. One\n", false, false)]
    [InlineData("```python annotate=true\nx = 1 # (1)\n```\n\n1. One\n", false, true)]
    [InlineData("```python title=\"x.py\" annotate=true\nx = 1 # (1)\n```\n\n1. One\n", null, true)]
    public void TheNarrowestSettingWins(string markdown, bool? page, bool annotated)
    {
        var html = new HtmlRenderer().RenderPdfMarkdownFragment(markdown, page);

        Assert.Equal(annotated, html.Contains("class=\"code-annotation\"", StringComparison.Ordinal));
    }

    [Fact]
    public void LeavesHiddenAnnotationsOutOfThePdf()
    {
        var html = new HtmlRenderer().RenderPdfMarkdownFragment(
            "Before\n\n<!-- pdf:hidden -->\n```python\nx = 1 # (1)\n```\n\n1. SecretEntry\n\n<!-- /pdf:hidden -->\n\nAfter\n");

        Assert.DoesNotContain("SecretEntry", html);
        Assert.Contains("After", html);
    }

    [Fact]
    public void AnnotatedCodeIsSearchedWithoutMarkers()
    {
        var document = Markdig.Markdown.Parse("```python\nx = 1 # (1)!\ny = 2 # keep (2)\n```\n\n1. One\n2. Two\n", HtmlRenderer.CreatePipeline());
        var code = document.Descendants<Markdig.Syntax.FencedCodeBlock>().Single();

        Assert.Equal("x = 1\ny = 2 # keep", CodeAnnotations.SearchText(code).ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("math", false)]
    [InlineData("Mermaid", false)]
    [InlineData("hero", false)]
    [InlineData("python", true)]
    [InlineData("NoHighlight", true)]
    public void KnowsWhichLanguagesAreCode(string language, bool code) =>
        Assert.Equal(code, HighlightedCodeBlockRenderer.IsCodeLanguage(language));

    [Fact]
    public void LeavesDiagramsAlone()
    {
        var html = new HtmlRenderer().RenderMarkdownFragment("```mermaid\nflowchart LR\n  A --> B (1)\n```\n\n1. One\n");

        Assert.DoesNotContain("code-annotation", html);
    }

    [Theory]
    [InlineData("content.code.annotate: false", false)]
    [InlineData("content:\n  code:\n    annotate: false", false)]
    [InlineData("content:\n  code.annotate: true", true)]
    [InlineData("content.code:\n  annotate: false", false)]
    [InlineData("title: Page", null)]
    [InlineData("content.code.annotate: maybe", null)]
    public void ReadsThePageSettingNestedOrDotted(string frontMatter, bool? expected) =>
        Assert.Equal(expected, CodeAnnotations.ReadFrontMatter($"---\n{frontMatter}\n---"));

    [Fact]
    public void DescribesAnInvalidPageSetting() =>
        Assert.Equal("content.code.annotate must be 'true' or 'false', not 'maybe'.",
            CodeAnnotations.FindProblem("---\ncontent.code.annotate: maybe\n---"));

    [Fact]
    public void FindsMarkersWithoutEntriesAndEntriesWithoutMarkers()
    {
        var problems = CodeAnnotations.FindProblems("```python\na = 1 # (1)\nb = 2 # (3)\n```\n\n1. One\n2. Two\n\n```python annotate=yes\nc\n```\n", enabled: true);

        Assert.Collection(problems,
            problem => Assert.Equal((1, CodeAnnotations.ProblemKind.MarkerWithoutEntry), (problem.Line, problem.Kind)),
            problem => Assert.Equal((7, CodeAnnotations.ProblemKind.EntryWithoutMarker), (problem.Line, problem.Kind)),
            problem => Assert.Equal((9, CodeAnnotations.ProblemKind.InvalidSetting), (problem.Line, problem.Kind)));
    }

    [Fact]
    public void ThePdfShowsMarkersInTheCodeAndTheEntriesBelowIt()
    {
        var layout = new PdfLayout(new HtmlRenderer(), "");
        layout.RenderSection(new PdfSourceSection("Config", "config.html", Annotated));
        var text = string.Concat(layout.Complete().Pages.SelectMany(page =>
            Regex.Matches(page.Content.ToString(), @"<(?<hex>[0-9A-F]*)> Tj")
                .Select(match => Encoding.Latin1.GetString(Convert.FromHexString(match.Groups["hex"].Value)))));

        Assert.Contains("(1)", text);
        Assert.Contains("(2)", text);
        Assert.Contains("Shown in the header and the browser tab.", text);
        Assert.True(text.IndexOf("(1)", StringComparison.Ordinal) < text.IndexOf("Shown in the header", StringComparison.Ordinal));
    }
}
