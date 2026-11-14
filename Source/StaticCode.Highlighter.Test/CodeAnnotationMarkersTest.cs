using Core.Extensions.TextRelated;

namespace StaticCode.Highlighter.Test;

public class CodeAnnotationMarkersTest
{
    private static IReadOnlyList<LanguageToken> Apply(string language, string code, int count = 9)
    {
        var lexer = Lexer.ForLanguage(language);
        return CodeAnnotationMarkers.Apply(lexer.Analyse(code), number => number <= count, lexer.AllowsLineEndMarkers);
    }

    private static string Render(IEnumerable<LanguageToken> tokens) =>
        string.Concat(tokens.Select(token => token.Type == TokenType.Annotation ? $"[{token.Text}]" : token.Text));

    [Fact]
    public void ReplacesAMarkerAnywhereInAComment()
    {
        var tokens = Apply("yaml", "name: Handbook # shown in the header (1)\n");

        Assert.Equal("name: Handbook # shown in the header [1]\n", Render(tokens));
        Assert.Contains(tokens, token => token.Type == TokenType.Comment && token.Text == "# shown in the header ");
    }

    [Theory]
    [InlineData("yaml", "name: Handbook # (1)!", "name: Handbook [1]")]
    [InlineData("csharp", "var x = 1; // (1)!", "var x = 1; [1]")]
    [InlineData("sql", "select 1 -- (1)!", "select 1 [1]")]
    [InlineData("html", "<p>Hi</p> <!-- (1)! -->", "<p>Hi</p> [1]")]
    [InlineData("csharp", "var x = 1; /* (1)! (2)! */", "var x = 1; [1][2]")]
    public void RemovesTheCharactersOfAnOtherwiseEmptyComment(string language, string code, string expected) =>
        Assert.Equal(expected, Render(Apply(language, code)));

    [Fact]
    public void KeepsACommentWithTextWhenTheMarkerStripsIt() =>
        Assert.Equal("x = 1 # the value [1]", Render(Apply("python", "x = 1 # the value (1)!")));

    [Theory]
    [InlineData("bash", "kubectl apply -f app.yaml (1)!", "kubectl apply -f app.yaml[1]")]
    [InlineData("text", "Server started (2)!", "Server started[2]")]
    [InlineData("json", "{ \"port\": 8080 } (1)!", "{ \"port\": 8080 }[1]")]
    [InlineData("unknown-language", "run (1)!  ", "run[1]")]
    public void ReplacesAnExplicitMarkerOutsideCommentsAtTheEndOfALine(string language, string code, string expected) =>
        Assert.Equal(expected, Render(Apply(language, code)));

    [Theory]
    [InlineData("bash", "kubectl apply -f app.yaml (1)")]
    [InlineData("text", "Retrying download (1)")]
    [InlineData("unknown-language", "INSERT INTO t VALUES (1)")]
    [InlineData("NoHighlight", "x = (1)")]
    public void KeepsAPlainLineEndMarkerOutsideComments(string language, string code) =>
        Assert.Equal(code, Render(Apply(language, code)));

    [Theory]
    [InlineData("python", "print(1)")]
    [InlineData("python", "items[(1)]")]
    [InlineData("python", "f (1) + 2")]
    [InlineData("python", "value = \"(1)\" + x")]
    [InlineData("text", "Step(1)")]
    public void KeepsParenthesesInCode(string language, string code) =>
        Assert.Equal(code, Render(Apply(language, code)));

    [Theory]
    [InlineData("sql", "INSERT INTO t VALUES (1)")]
    [InlineData("sql", "SELECT * FROM t WHERE id IN (1)")]
    [InlineData("python", "x = (1)")]
    [InlineData("csharp", "var x = (1)")]
    [InlineData("yaml", "title: Release notes (1)")]
    public void KeepsLineEndParenthesesInLanguagesWithComments(string language, string code) =>
        Assert.Equal(code, Render(Apply(language, code)));

    [Theory]
    [InlineData("bash", true)]
    [InlineData("text", true)]
    [InlineData("json", true)]
    [InlineData("unknown-language", true)]
    [InlineData("sql", false)]
    [InlineData("python", false)]
    [InlineData("yaml", false)]
    [InlineData("markdown", false)]
    public void AllowsLineEndMarkersWhereTheyCannotBeCode(string language, bool allowed) =>
        Assert.Equal(allowed, Lexer.ForLanguage(language).AllowsLineEndMarkers);

    [Fact]
    public void KeepsMarkersWithoutAnEntry() =>
        Assert.Equal("a = 1 # (3)", Render(Apply("python", "a = 1 # (3)", count: 2)));

    [Fact]
    public void AllowsSeveralMarkersInOneCommentAndTheSameMarkerTwice() =>
        Assert.Equal("a = 1 # see [1] and [2]\nb = 2 # [1]\n", Render(Apply("python", "a = 1 # see (1) and (2)\nb = 2 # (1)\n")));

    [Fact]
    public void FindsTheNumbersInOrder() =>
        Assert.Equal([2, 1], CodeAnnotationMarkers.FindNumbers(Lexer.ForLanguage("python").Analyse("a # (2)\nb # (1)\nc # (2)"), lineEndMarkers: false));

    [Fact]
    public void CopiesTheCodeWithoutMarkers()
    {
        var tokens = Apply("yaml", "name: Handbook # (1)!\nurl: https://example.com # docs (2)\n");

        Assert.Equal("name: Handbook\nurl: https://example.com # docs\n", CodeAnnotationMarkers.CopyText(tokens));
    }

    [Fact]
    public void KeepsTrailingSpacesOfLinesWithoutMarkers() =>
        Assert.Equal("Line one  \nx = 1 # note", CodeAnnotationMarkers.CopyText(Apply("python", "Line one  \nx = 1 # note (1)")));

    [Fact]
    public void GivesEachMarkerOfARepeatedNumberItsOwnAnchor()
    {
        var html = new HtmlFormatter { AnnotationIdPrefix = "a-" }.WriteToString(Apply("python", "x = 1 # (1)\ny = 2 # (1)\nz = 3 # (1)"));

        Assert.Contains("popovertarget=\"a-1\" style=\"anchor-name: --a-1\"", html);
        Assert.Contains("popovertarget=\"a-1\" style=\"anchor-name: --a-1-2\"", html);
        Assert.Contains("popovertarget=\"a-1\" style=\"anchor-name: --a-1-3\"", html);
    }

    [Fact]
    public void CopiesWithoutACommentThatHeldOnlyAMarker() =>
        Assert.Equal("url: https://example.com", CodeAnnotationMarkers.CopyText(Apply("yaml", "url: https://example.com # (2)")));

    [Fact]
    public void WritesAMarkerAsAPopoverButton()
    {
        var html = new HtmlFormatter { AnnotationIdPrefix = "a-7-" }.WriteToString(Apply("python", "x = 1 # (1)!"));

        Assert.Contains(
            "<button class=\"code-annotation\" type=\"button\" popovertarget=\"a-7-1\" style=\"anchor-name: --a-7-1\" aria-label=\"Annotation 1\">1</button>",
            html);
        Assert.DoesNotContain("#", html);
    }
}
