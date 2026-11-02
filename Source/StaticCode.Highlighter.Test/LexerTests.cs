namespace StaticCode.Highlighter.Test;

public class LexerTests
{
    [Fact]
    public void CSharp()
    {
        var lexer = Lexer.ForLanguage("csharp");

        var code = Samples.CSharp();
        var token = lexer.Analyse(code).ToList();
        
        Assert.NotEmpty(token);
        Assert.Contains(token, itm => itm.Type == TokenType.Comment);
        Assert.Contains(token, itm => itm.Type == TokenType.String);
    }

    [Theory]
    [InlineData("javascript", "const x = 1; return x;", "const|return", "1")]
    [InlineData("json", "{\"enabled\": true, \"count\": 1, \"missing\": null}", "true|null", "1")]
    [InlineData("sql", "Select name, 1 From users", "Select|From", "1")]
    [InlineData("html", "<div id='x'>text 1</div>", "div|id", "")]
    [InlineData("css", "@media screen { .x { background-color: red; border-radius: 2px; } }", "media|background-color|border-radius", "2")]
    [InlineData("java", "var x = 1; return x;", "var|return", "1")]
    [InlineData("rust", "fn first<'a>(x: &'a dyn Trait) -> &'a str { let y = 1; x }", "fn|dyn|let", "1")]
    [InlineData("go", "func f() { x := 1; return x }", "func|return", "1")]
    public void HighlightsKeywordsAndNumbers(string language, string code, string expectedKeywords, string expectedNumber)
    {
        var tokens = Lexer.ForLanguage(language).Analyse(code).ToList();

        foreach (var keyword in expectedKeywords.Split('|'))
            Assert.True(tokens.Any(token => token.Text == keyword && token.Type == TokenType.Keyword), keyword);
        if (expectedNumber.Length > 0)
            Assert.Contains(tokens, token => token.Text == expectedNumber && token.Type == TokenType.Number);
        Assert.Equal(code, string.Concat(tokens.Select(token => token.Text)));
    }

    [Fact]
    public void RustLifetimeIsNotHighlightedAsAString()
    {
        var tokens = Lexer.ForLanguage("rust").Analyse("fn first<'a>(x: &'a str)").ToList();

        Assert.DoesNotContain(tokens, token => token.Type == TokenType.String);
    }

    [Fact]
    public void RustCharacterLiteralDoesNotOpenAString()
    {
        var code = "if c == '\"' { return; }\nlet s = \"x\";";
        var tokens = Lexer.ForLanguage("rust").Analyse(code).ToList();

        Assert.Contains(tokens, token => token.Text == "'\"'" && token.Type == TokenType.String);
        Assert.Contains(tokens, token => token.Text == "return" && token.Type == TokenType.Keyword);
        Assert.Contains(tokens, token => token.Text == "let" && token.Type == TokenType.Keyword);
        Assert.Contains(tokens, token => token.Text == "\"x\"" && token.Type == TokenType.String);
        Assert.Equal(code, string.Concat(tokens.Select(token => token.Text)));
    }

    [Fact]
    public void RustCharacterLiteralsInCommentsRemainComments()
    {
        var tokens = Lexer.ForLanguage("rust").Analyse("// don't use 'x' here").ToList();

        Assert.Single(tokens);
        Assert.Equal(TokenType.Comment, tokens[0].Type);
    }

    [Fact]
    public void HtmlHighlightsAttributeStringsWithoutTreatingTextApostrophesAsStrings()
    {
        var code = "<p title='x'>Don't panic</p>\n<p>It's fine title</p>";
        var tokens = Lexer.ForLanguage("html").Analyse(code).ToList();

        Assert.Contains(tokens, token => token.Text == "'x'" && token.Type == TokenType.String);
        Assert.DoesNotContain(tokens, token => token.Type == TokenType.String && token.Text.Contains("Don't", StringComparison.Ordinal));
        Assert.DoesNotContain(tokens, token => token.Type == TokenType.String && token.Text.Contains("It's", StringComparison.Ordinal));
        Assert.Contains(tokens, token => token.Text == "title" && token.Type == TokenType.Keyword);
        Assert.Contains(tokens, token => token.Text == "title" && token.Type == TokenType.Text);
        Assert.Equal(code, string.Concat(tokens.Select(token => token.Text)));
    }

    [Fact]
    public void HtmlAndCssBlockCommentsTakePrecedenceOverStrings()
    {
        var html = Lexer.ForLanguage("html").Analyse("<!-- 'not a string' -->").ToList();
        var css = Lexer.ForLanguage("css").Analyse("/* \"unclosed */ \"valid string\"").ToList();
        var htmlAttribute = Lexer.ForLanguage("html").Analyse("<p title=\"<!-- not a comment -->\">text</p>").ToList();
        var cssString = Lexer.ForLanguage("css").Analyse("content: \"/* not a comment */\";").ToList();

        Assert.All(html, token => Assert.Equal(TokenType.Comment, token.Type));
        Assert.DoesNotContain(html, token => token.Type == TokenType.String);
        Assert.Contains(css, token => token.Text == "/* \"unclosed */" && token.Type == TokenType.Comment);
        Assert.DoesNotContain(css, token => token.Type == TokenType.String && token.Text.Contains("unclosed", StringComparison.Ordinal));
        Assert.Equal("<!-- 'not a string' -->", string.Concat(html.Select(token => token.Text)));
        Assert.Equal("/* \"unclosed */ \"valid string\"", string.Concat(css.Select(token => token.Text)));
        Assert.Contains(css, token => token.Text == "\"valid string\"" && token.Type == TokenType.String);
        Assert.DoesNotContain(htmlAttribute, token => token.Type == TokenType.Comment);
        Assert.Equal("\"<!-- not a comment -->\"", string.Concat(htmlAttribute.Where(token => token.Type == TokenType.String).Select(token => token.Text)));
        Assert.DoesNotContain(cssString, token => token.Type == TokenType.Comment);
        Assert.Contains(cssString, token => token.Text == "\"/* not a comment */\"" && token.Type == TokenType.String);
    }

    [Fact]
    public void HtmlIgnoresTagsInCommentsAndScriptContent()
    {
        var tokens = Lexer.ForLanguage("html")
            .Analyse("<!-- <div class=\"x\"> -->\n<script>if (a <b && c> d)</script>")
            .ToList();

        Assert.Contains(tokens, token => token.Text == "<!-- <div class=\"x\"> -->" && token.Type == TokenType.Comment);
        Assert.DoesNotContain(tokens, token => token.Type == TokenType.Keyword && token.Text is "div" or "class" or "b" or "c");
        Assert.DoesNotContain(tokens, token => token.Type == TokenType.String && token.Text.Contains("x", StringComparison.Ordinal));
    }

    [Fact]
    public void JavaHighlightsModernKeywordsAndTextBlocks()
    {
        var tokens = Lexer.ForLanguage("java").Analyse("record Item() { var text = \"\"\"\n return notAKeyword;\n\"\"\"; sealed class Example permits Item {} }").ToList();

        Assert.Contains(tokens, token => token.Text == "record" && token.Type == TokenType.Keyword);
        Assert.Contains(tokens, token => token.Text == "var" && token.Type == TokenType.Keyword);
        Assert.Contains(tokens, token => token.Text == "sealed" && token.Type == TokenType.Keyword);
        Assert.Contains(tokens, token => token.Text == "permits" && token.Type == TokenType.Keyword);
        Assert.DoesNotContain(tokens, token => token.Text == "return" && token.Type == TokenType.Keyword);
        Assert.Contains(tokens, token => token.Type == TokenType.String && token.Text.Contains("return notAKeyword;", StringComparison.Ordinal));
    }

    [Fact]
    public void JavaTextBlockDelimiterInsideLineCommentDoesNotPairWithLaterTextBlock()
    {
        var code = "// see \"\"\" below\nint x = 1;\nString s = \"\"\"text block\"\"\";";
        var tokens = Lexer.ForLanguage("java").Analyse(code).ToList();

        Assert.Contains(tokens, token => token.Text == "int" && token.Type == TokenType.Keyword);
        Assert.Contains(tokens, token => token.Text == "1" && token.Type == TokenType.Number);
        Assert.DoesNotContain(tokens, token => token.Type == TokenType.String && token.Text.Contains("int x = 1", StringComparison.Ordinal));
        Assert.Contains(tokens, token => token.Type == TokenType.String && token.Text == "\"\"\"text block\"\"\"");
    }

    [Fact]
    public void UnclosedJavaTextBlockFallsBackToShorterStringDelimiter()
    {
        var tokens = Lexer.ForLanguage("java").Analyse("String s = \"\"\"").ToList();

        Assert.Contains(tokens, token => token.Text == "\"\"" && token.Type == TokenType.String);
        Assert.Equal("String s = \"\"\"", string.Concat(tokens.Select(token => token.Text)));
    }

    [Theory]
    [InlineData("javascript")]
    [InlineData("typescript")]
    [InlineData("java")]
    [InlineData("rust")]
    [InlineData("go")]
    [InlineData("kotlin")]
    [InlineData("csharp")]
    [InlineData("swift")]
    public void CStyleBlockCommentsAreRecognized(string language)
    {
        var code = "// don't let /* inside line comments start a block\n/* \"comment\" */ return 1;";
        var tokens = Lexer.ForLanguage(language).Analyse(code).ToList();

        Assert.Contains(tokens, token => token.Text == "/* \"comment\" */" && token.Type == TokenType.Comment);
        Assert.Contains(tokens, token => token.Text == "return" && token.Type == TokenType.Keyword);
        Assert.Contains(tokens, token => token.Text == "1" && token.Type == TokenType.Number);
        Assert.Equal(code, string.Concat(tokens.Select(token => token.Text)));
    }

    [Fact]
    public void SqlBlockCommentsAreRecognizedAfterLineComments()
    {
        var code = "-- don't let /* inside line comments start a block\n/* comment */ select 1;";
        var tokens = Lexer.ForLanguage("sql").Analyse(code).ToList();

        Assert.Contains(tokens, token => token.Text == "/* comment */" && token.Type == TokenType.Comment);
        Assert.Contains(tokens, token => token.Text == "select" && token.Type == TokenType.Keyword);
        Assert.Equal(code, string.Concat(tokens.Select(token => token.Text)));
    }

    [Fact]
    public void PlainAndMarkdownDoNotHighlightNumbers()
    {
        var plain = Lexer.ForLanguage("text").Analyse("Version 2 is out").ToList();
        var markdown = Lexer.ForLanguage("markdown").Analyse("Version 2 is out").ToList();

        Assert.DoesNotContain(plain, token => token.Type == TokenType.Number);
        Assert.DoesNotContain(markdown, token => token.Type == TokenType.Number);
        Assert.Contains(plain, token => token.Text == "2" && token.Type == TokenType.Text);
        Assert.Contains(markdown, token => token.Text == "2" && token.Type == TokenType.Text);
    }

    [Fact]
    public void MarkdownHighlightsFrontMatterKeys()
    {
        const string markdown = """
            ---
            title: Orders API
            description: Reference for the Orders service.
            openapi: ./orders.yaml
            ---

            # Orders API
            """;

        var tokens = Lexer.ForLanguage("markdown").Analyse(markdown).ToList();

        Assert.Equal(markdown, string.Concat(tokens.Select(token => token.Text)));
        Assert.Contains(tokens, token => token.Text == "title" && token.Type == TokenType.Key);
        Assert.Contains(tokens, token => token.Text == "description" && token.Type == TokenType.Key);
        Assert.Contains(tokens, token => token.Text == "openapi" && token.Type == TokenType.Key);
        Assert.Contains(tokens, token => token.Text == "Orders" && token.Type == TokenType.MarkdownHeading);
    }

    [Theory]
    [InlineData("javascript")]
    [InlineData("css")]
    public void DecimalNumbersUseTheSameTokenType(string language)
    {
        var tokens = Lexer.ForLanguage(language).Analyse("1.5").ToList();

        Assert.NotEmpty(tokens);
        Assert.All(tokens, token => Assert.Equal(TokenType.Number, token.Type));
    }

    [Fact]
    public void CssHighlightsPropertiesOnlyInDeclarationsAndKeepsHexColorsConsistent()
    {
        var tokens = Lexer.ForLanguage("css")
            .Analyse(".color { /* c */ color: #000; background-color: #fff; width: 1.5em; /* w */ height: 2px; } @media screen {} @layer components {}")
            .ToList();
        var colorTokens = tokens.Where(token => token.Text == "color").ToList();

        Assert.Collection(colorTokens,
            token => Assert.Equal(TokenType.Text, token.Type),
            token => Assert.Equal(TokenType.Keyword, token.Type));
        Assert.Contains(tokens, token => token.Text == "background-color" && token.Type == TokenType.Keyword);
        Assert.Contains(tokens, token => token.Text == "width" && token.Type == TokenType.Keyword);
        Assert.Contains(tokens, token => token.Text == "height" && token.Type == TokenType.Keyword);
        Assert.Contains(tokens, token => token.Text == "media" && token.Type == TokenType.Keyword);
        Assert.Contains(tokens, token => token.Text == "layer" && token.Type == TokenType.Keyword);
        Assert.DoesNotContain(tokens, token => token.Text == "000" && token.Type == TokenType.Number);
        Assert.Contains(tokens, token => token.Text == "1" && token.Type == TokenType.Number);
        Assert.Contains(tokens, token => token.Text == "." && token.Type == TokenType.Number);
        Assert.Contains(tokens, token => token.Text == "5" && token.Type == TokenType.Number);
        Assert.Contains(tokens, token => token.Text == "2" && token.Type == TokenType.Number);
        Assert.Equal(".color { /* c */ color: #000; background-color: #fff; width: 1.5em; /* w */ height: 2px; } @media screen {} @layer components {}",
            string.Concat(tokens.Select(token => token.Text)));
    }

    [Theory]
    [InlineData("typescript", "const x = 1; return x;")]
    [InlineData("rust", "fn f() { let x = 1; return x; }")]
    [InlineData("html", "<p title=\"x\">return 1</p>")]
    [InlineData("bash", "echo x | grep x")]
    [InlineData("sql", "select x from t where id = 1;")]
    public void CachedLexerCanAnalyseConcurrently(string language, string code)
    {
        var lexer = Lexer.ForLanguage(language);
        var expected = lexer.Analyse(code).Select(token => (token.Text, token.Type)).ToArray();
        var mismatches = 0;

        Parallel.For(0, 20_000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
        {
            var actual = lexer.Analyse(code).Select(token => (token.Text, token.Type));
            if (!expected.SequenceEqual(actual))
                Interlocked.Increment(ref mismatches);
        });

        Assert.Equal(0, mismatches);
    }

    [Fact]
    public void LexerIsCachedPerLanguage()
    {
        Assert.Same(Lexer.ForLanguage("typescript"), Lexer.ForLanguage("typescript"));
        Assert.Same(Lexer.ForLanguage("typescript"), Lexer.ForLanguage("ts"));
        var plain = Lexer.ForLanguage("text");
        for (var number = 0; number < 1000; number++)
            Assert.Same(plain, Lexer.ForLanguage($"unknown{number}"));
    }
}
