using System.Text;
using System.Text.Json;
using Markdig;
using Markdown.Renderer.MarkdigExtension;
using StaticCode.Icons;

namespace Markdown.Renderer.Test;

public class IconTest
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseIconsAndEmojis()
        .Build();

    private static string Render(string markdown) => Markdig.Markdown.ToHtml(markdown, Pipeline).ReplaceLineEndings("\n");

    [Fact]
    public void RendersGitHubEmojiShortcodesAsUnicode()
    {
        Assert.Equal("<p>Ship it <span class=\"emoji\" title=\":rocket:\">🚀</span></p>\n", Render("Ship it :rocket:"));
    }

    [Theory]
    [InlineData("material-account-circle", "0 0 24 24")]
    [InlineData("fontawesome-brands-github", "0 0 640 640")]
    [InlineData("fontawesome-regular-face-laugh-wink", "0 0 640 640")]
    [InlineData("fontawesome-solid-rocket", "0 0 640 640")]
    [InlineData("octicons-heart-fill-24", "0 0 24 24")]
    [InlineData("simple-dotnet", "0 0 24 24")]
    public void RendersIconShortcodesOfEverySetAsInlineSvg(string shortcode, string viewBox)
    {
        var html = Render($":{shortcode}:");

        Assert.StartsWith($"<p><span class=\"icon\"><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{viewBox}\" aria-hidden=\"true\" focusable=\"false\"><path", html);
        Assert.EndsWith("</svg></span></p>\n", html);
    }

    [Fact]
    public void RendersDashboardIconsInTheirOwnColors()
    {
        var html = Render(":dashboard-docker:");

        Assert.Matches("^<p><span class=\"icon\"><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"[-0-9. ]+\" aria-hidden=\"true\" focusable=\"false\"><g fill=", html);
        Assert.DoesNotContain("icon-custom-color", html);
        Assert.DoesNotContain("icon-dark", html);
    }

    [Fact]
    public void MarksIconsWithAnExplicitColorForPaletteOverride()
    {
        var html = Render(":dashboard-files:{ style=\"color: #16803c\" }");

        Assert.StartsWith("<p><span class=\"icon icon-custom-color\" style=\"color: #16803c\"><svg", html);
    }

    [Fact]
    public void RendersOneSvgPerThemeForIconsWithVariants()
    {
        Assert.True(IconCatalog.TryGetIcon("dashboard-github", out var icon));
        Assert.NotNull(icon.DarkGlyph);
        Assert.NotEqual(icon.Glyph.Body, icon.DarkGlyph!.Body);

        var html = Render(":dashboard-github:");

        Assert.Contains("<span class=\"icon\"><svg class=\"icon-light\"", html);
        Assert.Contains("</svg><svg class=\"icon-dark\"", html);
    }

    [Fact]
    public void KeepsDashboardIconVariantsUsableButOutOfTheSearch()
    {
        Assert.True(IconCatalog.TryGetIcon("dashboard-github-light", out var variant));
        Assert.Null(variant.DarkGlyph);

        var dashboard = IconCatalog.Sets.Single(set => set.ShortcodePrefix == "dashboard-");
        Assert.DoesNotContain(dashboard.VisibleIcons, icon => icon.Shortcode == "dashboard-github-light");
        Assert.Contains(dashboard.VisibleIcons, icon => icon.Shortcode == "dashboard-github");
    }

    [Fact]
    public void UsesMermaidVariantsForTheirBackgroundTheme()
    {
        Assert.True(IconCatalog.TryGetIcon("dashboard-mermaid", out var icon));
        Assert.True(IconCatalog.TryGetIcon("dashboard-mermaid-light", out var lightBackground));
        Assert.True(IconCatalog.TryGetIcon("dashboard-mermaid-dark", out var darkBackground));

        Assert.Equal(lightBackground.Glyph, icon.Glyph);
        Assert.Equal(darkBackground.Glyph, icon.DarkGlyph);
        Assert.Contains("fill=\"#000\"", icon.Glyph.Body);
        Assert.Contains("fill=\"#fff\"", icon.DarkGlyph!.Body);
        Assert.Null(lightBackground.DarkGlyph);
        Assert.Null(darkBackground.DarkGlyph);
    }

    [Theory]
    [InlineData("anthropic")]
    [InlineData("astro")]
    [InlineData("dotnet")]
    [InlineData("golang")]
    [InlineData("rust")]
    [InlineData("stripe")]
    [InlineData("vim")]
    [InlineData("django")]
    public void OrdersOtherMonochromeVariantsByTheirColors(string name)
    {
        Assert.True(IconCatalog.TryGetIcon("dashboard-" + name, out var icon));

        Assert.NotNull(icon.DarkGlyph);
        Assert.Matches("(?:fill=\"|fill:)#000", icon.Glyph.Body);
        Assert.Matches("(?:fill=\"|fill:)#fff", icon.DarkGlyph!.Body);
    }

    [Theory]
    [InlineData("alacritty")]
    [InlineData("railway")]
    [InlineData("framer")]
    [InlineData("ableton")]
    public void AdaptsOtherUnpairedMonochromeLogos(string name)
    {
        Assert.True(IconCatalog.TryGetIcon("dashboard-" + name, out var icon));

        Assert.Null(icon.DarkGlyph);
        Assert.Contains("currentColor", icon.Glyph.Body);
    }

    [Fact]
    public void MaterialDesignIconsInheritTheThemeTextColor()
    {
        Assert.True(IconCatalog.TryGetIcon("dashboard-material-design-icons", out var icon));

        Assert.Null(icon.DarkGlyph);
        Assert.Contains("<g fill=\"currentColor\">", icon.Glyph.Body);
        Assert.Contains("fill=\"none\"", icon.Glyph.Body);
        Assert.DoesNotContain("#000", icon.Glyph.Body);
    }

    [Theory]
    [InlineData("dashboard-docker", "#2396ed")]
    [InlineData("dashboard-visual-studio-code", "#0065a9", "#007acc", "#1f9cf0")]
    public void KeepsBrandColors(string shortcode, params string[] colors)
    {
        var html = Render($":{shortcode}:");

        Assert.All(colors, color => Assert.Contains(color, html));
        Assert.DoesNotContain("currentColor", html);
    }

    [Fact]
    public void BundlesOnlySelfContainedDashboardIcons()
    {
        var dashboard = IconCatalog.Sets.Single(set => set.ShortcodePrefix == "dashboard-");
        var unsafeContent = new System.Text.RegularExpressions.Regex(
            "<(script|image|foreignObject|iframe|a|style)\\b|\\son[a-z]+\\s*=|href\\s*=\\s*\"(?!#)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        Assert.All(dashboard.VisibleIcons.SelectMany(icon => new[] { icon.Glyph, icon.DarkGlyph }).OfType<IconGlyph>(),
            glyph => Assert.False(unsafeContent.IsMatch(glyph.Body), glyph.Body));
    }

    [Fact]
    public void ResolvesIconAliases()
    {
        Assert.True(IconCatalog.TryGetIcon("material-123", out var alias));
        Assert.True(IconCatalog.TryGetIcon("material-numeric", out var icon));
        Assert.Equal(icon.Glyph, alias.Glyph);
        Assert.Equal("material-123", alias.Shortcode);
    }

    [Theory]
    [InlineData("Unknown :material-does-not-exist: icon", "<p>Unknown :material-does-not-exist: icon</p>\n")]
    [InlineData("Unknown :not-an-emoji: emoji", "<p>Unknown :not-an-emoji: emoji</p>\n")]
    [InlineData("Starts at 10:30:00", "<p>Starts at 10:30:00</p>\n")]
    [InlineData("Ratio 1::2", "<p>Ratio 1::2</p>\n")]
    [InlineData("`:rocket:`", "<p><code>:rocket:</code></p>\n")]
    public void KeepsOtherTextUnchanged(string markdown, string expected)
    {
        Assert.Equal(expected, Render(markdown));
    }

    [Fact]
    public void KeepsShortcodesInCodeBlocks()
    {
        Assert.Contains(":material-account-circle:", Render("```\n:material-account-circle:\n```\n"));
    }

    [Fact]
    public void FindsShortcodesNextToText()
    {
        Assert.Equal(
            "<p>(<span class=\"emoji\" title=\":tada:\">🎉</span>)<span class=\"emoji\" title=\":+1:\">👍</span></p>\n",
            Render("(:tada:):+1:"));
    }

    [Fact]
    public void AppliesAttributeListsToIcons()
    {
        var html = Render(":octicons-heart-fill-24:{ .heart #love style=\"color: red\" }");

        Assert.StartsWith("<p><span id=\"love\" class=\"icon heart icon-custom-color\" style=\"color: red\"><svg", html);
    }

    [Fact]
    public void AppliesAttributeListsToEmojis()
    {
        Assert.Equal("<p><span class=\"emoji big\" title=\":rocket:\">🚀</span></p>\n", Render(":rocket:{.big}"));
    }

    [Fact]
    public void LeavesIconsOutOfHeadingIdentifiers()
    {
        var html = Render("## :material-rocket: Getting started :rocket:");

        Assert.StartsWith("<h2 id=\"getting-started\"><span class=\"icon\">", html);
    }

    [Fact]
    public void RendersEmptyCodeBlocks()
    {
        var html = Markdig.Markdown.ToHtml("```\n```\n", new MarkdownPipelineBuilder().UseStaticCodeHighlighter().Build());

        Assert.NotNull(html);
    }

    [Fact]
    public void ListsVisibleIconsOfEverySet()
    {
        foreach (var set in IconCatalog.Sets)
        {
            var icons = set.VisibleIcons.ToArray();
            Assert.NotEmpty(icons);
            Assert.All(icons, icon => Assert.StartsWith(set.ShortcodePrefix, icon.Shortcode));
            Assert.NotEmpty(set.Name);
            Assert.NotEmpty(set.License);
        }
    }

    [Fact]
    public async Task AddsIconSearchAndItsAssetsOnlyToPagesThatUseIt()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"mdu-icon-test-{Guid.NewGuid():N}");
        try
        {
            var renderer = new HtmlRenderer();
            renderer.PrepareBasePath(outputPath);

            var plainPage = await RenderPage(renderer, "# Plain\n\n:rocket:\n", 0);
            Assert.DoesNotContain("icon-search", plainPage);
            Assert.Empty(renderer.IconSearchAssets);
            Assert.False(File.Exists(Path.Combine(outputPath, "js", IconSearch.IndexFileName)));

            var searchPage = await RenderPage(renderer, "# Icons\n\n```icon-search\n```\n", 2);
            Assert.Contains("<div class=\"icon-search\">", searchPage);
            Assert.Contains("<script defer src=\"../../js/icon-search.js\"></script>", searchPage);
            Assert.Equal(2, renderer.IconSearchAssets.Count);

            using var index = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(outputPath, "js", IconSearch.IndexFileName)));
            var sets = index.RootElement.GetProperty("sets").EnumerateArray().ToArray();
            Assert.Equal(IconCatalog.Sets.Select(set => set.ShortcodePrefix), sets.Select(set => set.GetProperty("prefix").GetString()));
            Assert.Contains(index.RootElement.GetProperty("emojis").EnumerateArray(),
                emoji => emoji[0].GetString() == "rocket" && emoji[1].GetString() == "🚀");
            Assert.True(File.Exists(Path.Combine(outputPath, "js", IconSearch.ScriptFileName)));
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, recursive: true);
        }
    }

    private static async Task<string> RenderPage(HtmlRenderer renderer, string markdown, int nestingDeep)
    {
        var html = new StringBuilder();
        await using var writer = new StringWriter(html);
        await renderer.WriteHtml(writer, "Test", "", nestingDeep, markdown, "");
        return html.ToString();
    }
}
