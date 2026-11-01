using System.Text;

namespace Markdown.Renderer.Test;

public class HeroTest
{
    [Fact]
    public void RendersTitleTextAndActionsFromTheYamlOptions()
    {
        var html = Hero.Render("""
            title: Ship <docs>
            text: Plain Markdown & static HTML
            time: sunset
            height: 600px
            speed: 1.5
            waves: 2
            align: center
            actions:
              - text: Get started
                link: Guide/Setup.md#install
              - text: Overview
                link: ../README.md
              - text: GitHub
                link: https://github.com/capjan/mdu
                style: primary
            colors:
              water: "#0a3d62"
              sand: red
            """);

        Assert.StartsWith("<section class=\"hero hero-full hero-align-center\" style=\"--hero-height: 600px\"", html);
        Assert.Contains("data-hero-time=\"sunset\"", html);
        Assert.Contains("data-hero-speed=\"1.5\"", html);
        Assert.Contains("data-hero-waves=\"2\"", html);
        Assert.Contains("data-hero-water=\"#0a3d62\"", html);
        Assert.DoesNotContain("data-hero-sand", html);
        Assert.Contains("<p class=\"hero-title\">Ship &lt;docs&gt;</p>", html);
        Assert.Contains("<p class=\"hero-text\">Plain Markdown &amp; static HTML</p>", html);
        Assert.Contains("<a class=\"hero-button hero-button-primary\" href=\"Guide/Setup.html#install\">Get started</a>", html);
        Assert.Contains("<a class=\"hero-button\" href=\"../index.html\">Overview</a>", html);
        Assert.Contains("<a class=\"hero-button hero-button-primary\" href=\"https://github.com/capjan/mdu\" target=\"_blank\" rel=\"noopener noreferrer\">GitHub</a>", html);
        Assert.EndsWith("</section>", html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("title: [unclosed")]
    [InlineData("time: noon\nquality: ultra\nheight: 99999\nspeed: fast\nwidth: wide")]
    public void FallsBackToDefaultsForMissingOrInvalidOptions(string options)
    {
        var html = Hero.Render(options);

        Assert.StartsWith("<section class=\"hero hero-full hero-align-left\" style=\"--hero-height: ", html);
        Assert.Contains("data-hero-time=\"auto\"", html);
        Assert.Contains("data-hero-quality=\"auto\"", html);
        Assert.Contains("data-hero-speed=\"1\"", html);
        Assert.Contains("<canvas class=\"hero-canvas\" aria-hidden=\"true\"></canvas>", html);
        Assert.DoesNotContain("hero-actions", html);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData(" java\tscript:alert(1)")]
    [InlineData("\u0001javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    public void DropsActionsWhoseLinksRunScript(string link)
    {
        var html = Hero.Render($"actions:\n  - text: Click\n    link: \"{link.Replace("\t", "\\t").Replace("\u0001", "\\x01")}\"\n");

        Assert.DoesNotContain("hero-button", html);
        Assert.DoesNotContain("script:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Setup.md", "Setup.html")]
    [InlineData("#options", "#options")]
    [InlineData("https://example.com/a?b=c", "https://example.com/a?b=c")]
    [InlineData("mailto:docs@example.com", "mailto:docs@example.com")]
    public void KeepsRelativeWebAndMailLinks(string link, string href)
    {
        var html = Hero.Render($"actions:\n  - text: Click\n    link: \"{link}\"\n");

        Assert.Contains($"href=\"{href}\"", html);
    }

    [Fact]
    public void ClampsTheHeight()
    {
        Assert.Contains("--hero-height: 1200px", Hero.Render("height: 99999"));
        Assert.Contains("--hero-height: 240px", Hero.Render("height: 10"));
    }

    [Fact]
    public async Task PlacesAFullWidthHeroAboveTheContentAndAddsItsScript()
    {
        var page = await RenderPage("# Home\n\n```hero\ntitle: Welcome\n```\n\nIntro\n");

        var heroStart = page.IndexOf("<section class=\"hero hero-full", StringComparison.Ordinal);
        Assert.True(heroStart > 0);
        Assert.True(heroStart < page.IndexOf("<div class=\"site-layout", StringComparison.Ordinal));
        Assert.True(heroStart > page.IndexOf("<nav class=\"top-navigation", StringComparison.Ordinal));
        Assert.Contains("const fragmentSource", page);
        Assert.DoesNotContain("<template class=\"hero-content-position\">", page);
        Assert.Contains("<h1 id=\"home\">", page);
        var styles = page.IndexOf("<style>.hero {", StringComparison.Ordinal);
        Assert.True(styles > 0 && styles < page.IndexOf("</head>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeepsAContentWidthHeroInThePage()
    {
        var page = await RenderPage("# Home\n\n```hero\ntitle: Welcome\nwidth: content\n```\n");

        var article = page.IndexOf("<article class=\"markdown-content\">", StringComparison.Ordinal);
        var hero = page.IndexOf("<section class=\"hero hero-inline", StringComparison.Ordinal);
        Assert.True(article > 0 && hero > article);
    }

    [Fact]
    public async Task LeavesPagesWithoutAHeroUnchanged()
    {
        var page = await RenderPage("# Home\n\n```yaml\ntitle: Welcome\n```\n");

        Assert.DoesNotContain("<section class=\"hero", page);
        Assert.DoesNotContain("const fragmentSource", page);
        Assert.DoesNotContain(".hero-canvas", page);
    }

    [Fact]
    public void KeepsHeroStylesOutOfTheSiteStylesheet()
    {
        Assert.DoesNotContain(".hero", Resources.CssContent());
        Assert.Contains(".hero-canvas", Hero.Styles);
    }

    [Theory]
    [InlineData("beach")]
    [InlineData("connections")]
    [InlineData("flow")]
    [InlineData("voronoi")]
    [InlineData(" FLOW ")]
    public void SelectsAnEffectWithoutChangingTheSimpleSyntax(string effect)
    {
        var html = Hero.Render($"title: Welcome\neffect: '{effect}'\ncolors:\n  accent: '#abc'");

        Assert.Contains($"data-hero-scene=\"{effect.Trim().ToLowerInvariant()}\"", html);
        Assert.Contains("data-hero-accent=\"#abc\"", html);
        Assert.DoesNotContain("class=\"hero-playground-template\"", html);
        Assert.Contains("class=\"hero-error\" role=\"status\" hidden=\"hidden\"", html);
    }

    [Fact]
    public void EncodesShaderSourceAsInertTemplateText()
    {
        const string source = "void mainImage(out vec4 c, in vec2 p) { c = vec4(1.0); }\n// </template></section><script>alert('x')</script> & < >";
        var html = Hero.Render("effect: shader\nshader: |\n  " + source.Replace("\n", "\n  "));

        Assert.Contains("<template class=\"hero-shader\">", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("</template></section>", html);
        Assert.Contains("&lt;/template&gt;&lt;/section&gt;&lt;script&gt;", html);
        Assert.Contains(source, System.Net.WebUtility.HtmlDecode(html));
    }

    [Theory]
    [InlineData("effect: unknown", "Unknown hero effect")]
    [InlineData("effect: shader", "needs GLSL code")]
    [InlineData("effect: shader\nshader: ' '", "needs GLSL code")]
    public void ReportsNewConfigurationErrorsInsteadOfPretendingAnEffectStarted(string configuration, string message)
    {
        var html = Hero.Render(configuration);

        Assert.Contains($"<p class=\"hero-error\" role=\"status\">", html);
        Assert.Contains(message, html);
    }

    [Fact]
    public void LimitsShaderSizeAndEncodesUnknownEffectNames()
    {
        var oversized = Hero.Render("effect: shader\nshader: " + new string('x', 32769));
        Assert.Contains("must not exceed 32768", oversized);
        Assert.DoesNotContain("<template class=\"hero-shader\">", oversized);
        Assert.Contains("<template class=\"hero-shader\">", Hero.Render("effect: shader\nshader: " + new string('x', 32768)));

        var unknown = Hero.Render("effect: '<img src=x onerror=alert(1)>'");
        Assert.Contains("data-hero-scene=\"invalid\"", unknown);
        Assert.DoesNotContain("<img", unknown);
    }

    [Fact]
    public async Task LoadsOnlyTheRequiredEffectGroupAndNoEditorForNormalHeroes()
    {
        var beach = await RenderPage("```hero\ntitle: Beach\n```");
        Assert.Contains("const fragmentSource", beach);
        Assert.DoesNotContain("hero.effects.connections", beach);
        Assert.DoesNotContain("const exampleShader", beach);

        var connections = await RenderPage("```hero title=\"Banner\"\neffect: connections\n```");
        Assert.Contains("hero.effects.connections", connections);
        Assert.Contains("const controllers =", connections);
        Assert.DoesNotContain("const fragmentSource", connections);
        Assert.DoesNotContain("const exampleShader", connections);

        var shader = await RenderPage("```hero\neffect: shader\nshader: 'void mainImage(out vec4 c, in vec2 p) { c = vec4(1.0); }'\n```");
        Assert.Contains("const controllers =", shader);
        Assert.DoesNotContain("hero.effects.connections", shader);
        Assert.DoesNotContain("const fragmentSource", shader);
    }

    [Fact]
    public async Task PlaygroundIncludesAllEffectsAndKeepsOriginalActionLinks()
    {
        var page = await RenderPage("""
            ```hero
            effect: connections
            playground: true
            actions:
              - text: Guide
                link: Guide/README.md#install
            ```
            """);

        Assert.Contains("class=\"hero-playground-template\"", page);
        Assert.Contains("hero.effects.connections", page);
        Assert.Contains("const fragmentSource", page);
        Assert.Contains("const exampleShader", page);
        Assert.Contains("href=\"Guide/index.html#install\" data-hero-link=\"Guide/README.md#install\"", page);
    }

    [Fact]
    public void PlaygroundOffersLiveControlsAndACollapsedExpertEditor()
    {
        var html = Hero.Render("title: Welcome\nplayground: true");
        var document = new System.Xml.XmlDocument();
        document.LoadXml(PdfLayout.NormalizeVoidElements(html));
        var template = document.SelectSingleNode("//template[@class='hero-playground-template']")!;

        foreach (var name in new[] { "title", "text", "effect", "width", "theme", "speed", "height", "align", "quality",
                     "time", "waves", "water", "shallow", "sand", "accent", "colorsReset", "shader", "reset", "pause", "preview", "yaml", "copy" })
            Assert.NotNull(template.SelectSingleNode($".//*[@name='{name}']"));
        var width = template.SelectSingleNode(".//select[@name='width']")!;
        Assert.Equal(new[] { "full", "content" }, width.SelectNodes("option")!.Cast<System.Xml.XmlElement>().Select(option => option.GetAttribute("value")));
        foreach (var name in new[] { "hero-layout-options", "hero-appearance-options", "hero-yaml-options" })
            Assert.False(((System.Xml.XmlElement)template.SelectSingleNode($".//details[@class='{name}']")!).HasAttribute("open"));
        Assert.NotNull(template.SelectSingleNode(".//details[@class='hero-layout-options']//*[@name='quality']"));
        Assert.NotNull(template.SelectSingleNode(".//div[@class='hero-control-actions']//*[@name='copy']"));
        var editor = (System.Xml.XmlElement)template.SelectSingleNode(".//details[@class='hero-shader-editor']")!;
        Assert.False(editor.HasAttribute("open"));
        Assert.NotNull(editor.SelectSingleNode("summary"));
        Assert.Null(template.SelectSingleNode(".//*[@name='apply']"));
        Assert.Contains("preview automatically", template.InnerText);
    }

    [Fact]
    public async Task PreservesTheFullPlaygroundsOriginalArticlePosition()
    {
        var page = await RenderPage("""
            # Home

            Before the banner.

            ```hero
            title: Welcome
            playground: true
            ```

            After the banner.
            """);

        var article = page.IndexOf("<article class=\"markdown-content\">", StringComparison.Ordinal);
        var position = page.IndexOf("<template class=\"hero-content-position\"></template>", StringComparison.Ordinal);
        Assert.True(position > article);
        Assert.True(position > page.IndexOf("<p>Before the banner.", StringComparison.Ordinal));
        Assert.True(position < page.IndexOf("<p>After the banner.", StringComparison.Ordinal));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(page, "<template class=\"hero-content-position\">"));
    }

    [Fact]
    public async Task KeepsAnInitiallyContentWidthPlaygroundInItsArticle()
    {
        var page = await RenderPage("# Home\n\n```hero\ntitle: Welcome\nwidth: content\nplayground: true\n```");

        var article = page.IndexOf("<article class=\"markdown-content\">", StringComparison.Ordinal);
        Assert.True(page.IndexOf("<section class=\"hero hero-inline", StringComparison.Ordinal) > article);
        Assert.DoesNotContain("<template class=\"hero-content-position\">", page);
        Assert.Contains("<select name=\"width\">", page);
        Assert.Contains("const exampleShader", page);
    }

    [Fact]
    public async Task KeepsMultipleEffectsAndShaderTemplatesIndependent()
    {
        var page = await RenderPage("""
            # Effects

            ```hero
            effect: shader
            title: First
            shader: |
              void mainImage(out vec4 c, in vec2 p) { c = vec4(0.25); }
              // </section>
            playground: true
            ```

            ```hero
            effect: connections
            title: Second
            playground: true
            ```

            ```hero
            title: Third
            ```
            """);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(page, "<section class=\"hero hero-full"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(page, "<section class=\"hero hero-inline").Count);
        var first = page.IndexOf("<section class=\"hero hero-full", StringComparison.Ordinal);
        var layout = page.IndexOf("<div class=\"site-layout", StringComparison.Ordinal);
        Assert.True(first < layout);
        Assert.True(page.IndexOf("class=\"hero-playground-template\"", StringComparison.Ordinal) < layout);
        Assert.True(page.IndexOf("<p class=\"hero-title\">Second", StringComparison.Ordinal) > layout);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(page, "<template class=\"hero-playground-template\">").Count);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(page, "<template class=\"hero-content-position\">"));
        Assert.Contains("// &lt;/section&gt;", page);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(page, "const controllers ="));
    }

    private static async Task<string> RenderPage(string markdown)
    {
        var html = new StringBuilder();
        await using var writer = new StringWriter(html);
        await new HtmlRenderer().WriteHtml(writer, "Test", "", 0, markdown, "");
        return html.ToString();
    }
}
