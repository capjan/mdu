using System.Text.Json;
using Markdig;
using Markdown.Renderer.MarkdigExtension;
using StaticCode.Icons;

namespace Markdown.Renderer.Test;

// Custom icon sets are process-wide, so these tests must not run alongside other tests that list the icon sets.
[CollectionDefinition("Icon catalog", DisableParallelization = true)]
public sealed class IconCatalogCollectionDefinition;

[Collection("Icon catalog")]
public sealed class CustomIconTest : IDisposable
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseIconsAndEmojis()
        .Build();

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mdu-custom-icons-{Guid.NewGuid():N}");

    public void Dispose()
    {
        IconCatalog.UseCustomIcons(null);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void RendersSvgFilesOfASetFolderAsIconShortcodes()
    {
        WriteIcon("acme/logo.svg",
            """<svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor"><path d="M1 1h22"/></svg>""");

        IconCatalog.UseCustomIcons(_root);

        Assert.Equal(
            "<p><span class=\"icon\"><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" aria-hidden=\"true\" focusable=\"false\"><g fill=\"none\" stroke=\"currentColor\"><path d=\"M1 1h22\" /></g></svg></span></p>\n",
            Render(":acme-logo:"));
    }

    [Fact]
    public void NamesIconsInSubfoldersWithTheFolderNames()
    {
        WriteIcon("acme/cloud/server.svg", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><circle r="8"/></svg>""");

        IconCatalog.UseCustomIcons(_root);

        Assert.True(IconCatalog.TryGetIcon("acme-cloud-server", out var icon));
        Assert.Equal("<circle r=\"8\" />", icon.Glyph.Body);
    }

    [Fact]
    public void UsesWidthAndHeightWithoutViewBox()
    {
        WriteIcon("acme/box.svg", """<svg xmlns="http://www.w3.org/2000/svg" width="32px" height="20"><rect width="32" height="20"/></svg>""");

        IconCatalog.UseCustomIcons(_root);

        Assert.True(IconCatalog.TryGetIcon("acme-box", out var icon));
        Assert.Equal("0 0 32 20", icon.Glyph.ViewBox);
    }

    [Fact]
    public void ShowsTheDarkVariantInTheDarkTheme()
    {
        WriteIcon("acme/logo.svg", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><path fill="#000" d="M0 0h16"/></svg>""");
        WriteIcon("acme/logo.dark.svg", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><path fill="#fff" d="M0 0h16"/></svg>""");

        IconCatalog.UseCustomIcons(_root);

        Assert.False(IconCatalog.TryGetIcon("acme-logo.dark", out _));
        var html = Render(":acme-logo:");
        Assert.Contains("<svg class=\"icon-light\" xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 16 16\" aria-hidden=\"true\" focusable=\"false\"><path fill=\"#000\"", html);
        Assert.Contains("<svg class=\"icon-dark\" xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 16 16\" aria-hidden=\"true\" focusable=\"false\"><path fill=\"#fff\"", html);
    }

    [Fact]
    public void RemovesScriptsExternalReferencesAndEditorMetadata()
    {
        WriteIcon("acme/unsafe.svg",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd">
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"
                 xmlns:inkscape="http://www.inkscape.org/namespaces/inkscape" viewBox="0 0 10 10" onload="alert(1)">
              <title>Logo</title>
              <metadata>Created with an editor</metadata>
              <script>alert(1)</script>
              <foreignObject><div xmlns="http://www.w3.org/1999/xhtml">HTML</div></foreignObject>
              <inkscape:namedview pagecolor="#fff"/>
              <image xlink:href="https://example.com/tracker.png" width="1" height="1"/>
              <image href="data:image/png;base64,AAAA" width="10" height="10"/>
              <rect inkscape:label="Layer" onclick="alert(1)" width="10" height="10"/>
            </svg>
            """);

        IconCatalog.UseCustomIcons(_root);

        Assert.True(IconCatalog.TryGetIcon("acme-unsafe", out var icon));
        Assert.Equal(
            "<image width=\"1\" height=\"1\" /><image href=\"data:image/png;base64,AAAA\" width=\"10\" height=\"10\" /><rect width=\"10\" height=\"10\" />",
            icon.Glyph.Body);
    }

    [Fact]
    public void PrefixesIdentifiersAndStyleSheetClassesPerIcon()
    {
        WriteIcon("acme/gradient.svg",
            """
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 10 10">
              <defs>
                <style><![CDATA[ .cls-1 { fill: url(#fade); opacity: .5 } /* note */ #dot > .cls-1 { fill: #f00 } ]]></style>
                <linearGradient id="fade"><stop offset="0"/></linearGradient>
              </defs>
              <rect id="dot" class="cls-1 cls-2" style="fill: url('#fade')" width="10" height="10"/>
              <use xlink:href="#dot"/>
            </svg>
            """);

        IconCatalog.UseCustomIcons(_root);

        Assert.True(IconCatalog.TryGetIcon("acme-gradient", out var icon));
        Assert.Equal(
            "<g class=\"acme-gradient-scope\">" +
            "<defs><style> .acme-gradient-scope .acme-gradient-cls-1 { fill: url(#acme-gradient-fade); opacity: .5 }  .acme-gradient-scope :is(#acme-gradient-dot, .acme-gradient-dot--copy) &gt; .acme-gradient-cls-1 { fill: #f00 } </style>" +
            "<linearGradient id=\"acme-gradient-fade\"><stop offset=\"0\" /></linearGradient></defs>" +
            "<rect id=\"acme-gradient-dot\" class=\"acme-gradient-cls-1 acme-gradient-cls-2\" style=\"fill: url(#acme-gradient-fade)\" width=\"10\" height=\"10\" />" +
            "<g><rect class=\"acme-gradient-cls-1 acme-gradient-cls-2 acme-gradient-dot--copy\" style=\"fill: url(#acme-gradient-fade)\" width=\"10\" height=\"10\" /></g></g>",
            icon.Glyph.Body);
    }

    [Fact]
    public void RemovesReferencesToOtherFilesFromStylesAndAttributes()
    {
        WriteIcon("acme/external.svg",
            """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <style>@import url("https://example.com/theme.css"); @import 'https://example.com/other.css';
                rect { fill: url(https://example.com/paint.svg#gradient); stroke: URL( "#line" ) }
                @font-face { font-family: X; src: url(https://example.com/x.woff2) }</style>
              <rect fill="url(https://example.com/paint.svg#gradient)" style="background: url('//example.com/a.png')" width="10" height="10"/>
              <circle fill="url(data:image/png;base64,AAAA)" r="1"/>
            </svg>
            """);

        IconCatalog.UseCustomIcons(_root);

        Assert.True(IconCatalog.TryGetIcon("acme-external", out var icon));
        Assert.DoesNotContain("example.com", icon.Glyph.Body);
        Assert.DoesNotContain("@import", icon.Glyph.Body);
        Assert.Contains(".acme-external-scope rect { fill: none; stroke: url(#acme-external-line) }", icon.Glyph.Body);
        Assert.Contains("<rect fill=\"none\" style=\"background: none\"", icon.Glyph.Body);
        Assert.Contains("<circle fill=\"url(data:image/png;base64,AAAA)\"", icon.Glyph.Body);
    }

    [Fact]
    public void RemovesEscapedImportsAndUrlsAndImageFunctions()
    {
        WriteIcon("acme/escaped.svg",
            """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <style>@\69mport "https://example.com/a.css";
                rect { fill: u\72l(https://example.com/p.svg#g); background: image-set("https://example.com/a.png" 1x, url(x.png) 2x) }</style>
              <rect style="background: -webkit-image-set('https://example.com/b.png' 1x)" fill="u\72l(https://example.com/c.svg#g)" width="10" height="10"/>
            </svg>
            """);

        IconCatalog.UseCustomIcons(_root);

        Assert.True(IconCatalog.TryGetIcon("acme-escaped", out var icon));
        Assert.DoesNotContain("example.com", icon.Glyph.Body);
        Assert.DoesNotContain("import", icon.Glyph.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background: none }", icon.Glyph.Body);
        Assert.Contains("<rect style=\"background: none\"", icon.Glyph.Body);
    }

    [Fact]
    public void GivesEachIconItsOwnAnimationNames()
    {
        const string spinning = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><style>@keyframes spin { to { transform: rotate(1turn) } } rect { animation: spin 1s linear infinite } circle { animation-name: spin, fade }</style><rect width="10" height="10"/><circle style="animation: 2s spin" r="1"/></svg>""";
        WriteIcon("acme/one.svg", spinning);
        WriteIcon("acme/two.svg", spinning);

        IconCatalog.UseCustomIcons(_root);

        Assert.True(IconCatalog.TryGetIcon("acme-one", out var one));
        Assert.True(IconCatalog.TryGetIcon("acme-two", out var two));
        Assert.Contains("@keyframes acme-one-spin {", one.Glyph.Body);
        Assert.Contains("rect { animation: acme-one-spin 1s linear infinite }", one.Glyph.Body);
        Assert.Contains("circle { animation-name: acme-one-spin, fade }", one.Glyph.Body);
        Assert.Contains("<circle style=\"animation: 2s acme-one-spin\"", one.Glyph.Body);
        Assert.Contains("@keyframes acme-two-spin {", two.Glyph.Body);
    }

    [Fact]
    public void KeepsTheIdentifierOfTheRootSvg()
    {
        WriteIcon("acme/rooted.svg",
            """<svg xmlns="http://www.w3.org/2000/svg" id="logo" viewBox="0 0 10 10"><style>#logo path { fill: red } svg#logo { color: blue } #logo-part { fill: green }</style><path id="logo-part" d="M0 0"/></svg>""");

        IconCatalog.UseCustomIcons(_root);

        Assert.True(IconCatalog.TryGetIcon("acme-rooted", out var icon));
        Assert.StartsWith("<g id=\"acme-rooted-logo\" class=\"acme-rooted-scope\">", icon.Glyph.Body);
        Assert.Contains(".acme-rooted-scope#acme-rooted-logo path { fill: red }", icon.Glyph.Body);
        Assert.Contains(".acme-rooted-scope#acme-rooted-logo { color: blue }", icon.Glyph.Body);
        Assert.Contains(".acme-rooted-scope #acme-rooted-logo-part { fill: green }", icon.Glyph.Body);
    }

    [Fact]
    public void LimitsStyleSheetsToTheirIcon()
    {
        WriteIcon("acme/styled.svg",
            """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10" fill="none">
              <style>path, svg > circle:is(.a, .b) { fill: red } :root { color: blue }
                @media (prefers-color-scheme: dark) { path { fill: blue } }
                @keyframes spin { from { opacity: 0 } 50% { opacity: .5 } }</style>
              <path d="M0 0h10"/>
            </svg>
            """);
        WriteIcon("acme/styled.dark.svg",
            """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><style>path { fill: white }</style><path d="M0 0h10"/></svg>""");

        IconCatalog.UseCustomIcons(_root);

        Assert.True(IconCatalog.TryGetIcon("acme-styled", out var icon));
        Assert.StartsWith("<g fill=\"none\" class=\"acme-styled-scope\"><style>", icon.Glyph.Body);
        Assert.Contains(".acme-styled-scope path, .acme-styled-scope &gt; circle:is(.acme-styled-a, .acme-styled-b) { fill: red }", icon.Glyph.Body);
        Assert.Contains(".acme-styled-scope { color: blue }", icon.Glyph.Body);
        Assert.Contains("@media (prefers-color-scheme: dark) { .acme-styled-scope path { fill: blue } }", icon.Glyph.Body);
        Assert.Contains("@keyframes acme-styled-spin { from { opacity: 0 } 50% { opacity: .5 } }", icon.Glyph.Body);
        Assert.StartsWith("<g class=\"acme-styled-dark-scope\"><style>.acme-styled-dark-scope path { fill: white }</style>", icon.DarkGlyph!.Body);
    }

    [Fact]
    public void ReplacesUseReferencesWithCopies()
    {
        WriteIcon("acme/reuse.svg",
            """
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 10 10">
              <defs>
                <path id="shape" fill="currentColor" d="M0 0h2v2z"/>
                <symbol id="pair" viewBox="0 0 4 2"><use href="#shape"/><use href="#shape" x="2"/></symbol>
                <g id="loop"><use href="#loop"/></g>
              </defs>
              <use xlink:href="#pair" x="1" y="3" transform="scale(2)" opacity=".5"/>
              <use href="#missing"/>
              <use href="#loop"/>
            </svg>
            """);

        IconCatalog.UseCustomIcons(_root);

        Assert.True(IconCatalog.TryGetIcon("acme-reuse", out var icon));
        Assert.DoesNotContain("<use", icon.Glyph.Body);
        Assert.Contains(
            "<g opacity=\".5\" transform=\"scale(2) translate(1 3)\"><g transform=\"translate(0 2.5) scale(2.5 2.5)\"><g><path fill=\"currentColor\" d=\"M0 0h2v2z\" /></g><g transform=\"translate(2 0)\"><path fill=\"currentColor\" d=\"M0 0h2v2z\" /></g></g></g>",
            icon.Glyph.Body);
    }

    [Theory]
    [InlineData("0 0 100 100", "", "translate(0 0) scale(0.1 0.1)")]
    [InlineData("0 0 100 50", "", "translate(0 2.5) scale(0.1 0.1)")]
    [InlineData("10 10 100 50", "xMinYMax meet", "translate(-1 4) scale(0.1 0.1)")]
    [InlineData("0 0 100 50", "xMidYMid slice", "translate(-5 0) scale(0.2 0.2)")]
    [InlineData("0 0 100 50", "none", "translate(0 0) scale(0.1 0.2)")]
    public void ScalesSymbolsToTheSizeOfTheirUse(string viewBox, string preserveAspectRatio, string transform)
    {
        var aspectRatio = preserveAspectRatio.Length == 0 ? "" : $" preserveAspectRatio=\"{preserveAspectRatio}\"";
        WriteIcon("acme/symbol.svg",
            $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><symbol id="big" viewBox="{viewBox}"{aspectRatio}><rect width="100" height="100"/></symbol><use href="#big" width="10" height="10"/></svg>""");

        IconCatalog.UseCustomIcons(_root);

        Assert.True(IconCatalog.TryGetIcon("acme-symbol", out var icon));
        Assert.EndsWith($"<g><g transform=\"{transform}\"><rect width=\"100\" height=\"100\" /></g></g>", icon.Glyph.Body);
    }

    [Theory]
    [InlineData("", "translate(0 0) scale(0.24 0.24)")]
    [InlineData(" width=\"50%\" height=\"50%\"", "translate(0 0) scale(0.12 0.12)")]
    public void SizesSymbolsWithoutSizeByTheIconViewBox(string size, string transform)
    {
        WriteIcon("acme/viewport.svg",
            $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><symbol id="big" viewBox="0 0 100 100"><rect width="100" height="100"/></symbol><use href="#big"{size}/></svg>""");

        IconCatalog.UseCustomIcons(_root);

        Assert.True(IconCatalog.TryGetIcon("acme-viewport", out var icon));
        Assert.EndsWith($"<g><g transform=\"{transform}\"><rect width=\"100\" height=\"100\" /></g></g>", icon.Glyph.Body);
    }

    [Fact]
    public void DrawsOnlyTheCopiesOfSymbolsInPdf()
    {
        WriteIcon("acme/symbol-pdf.svg",
            """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><symbol id="big" viewBox="0 0 100 100"><rect fill="#000" width="100" height="100"/></symbol><use href="#big" width="10" height="10"/></svg>""");
        IconCatalog.UseCustomIcons(_root);
        Assert.True(IconCatalog.TryGetIcon("acme-symbol-pdf", out var icon));
        Assert.StartsWith("<defs><symbol id=\"acme-symbol-pdf-big\"", icon.Glyph.Body);

        Assert.Equal(1, CountPdfShapes(icon.Glyph.ToSvg()));
        // The PDF renderer skips symbols outside of defs too.
        Assert.Equal(0, CountPdfShapes("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><symbol viewBox="0 0 10 10"><rect fill="#000" width="10" height="10"/></symbol></svg>"""));

        static int CountPdfShapes(string svg)
        {
            var document = new System.Xml.XmlDocument();
            document.LoadXml(svg);
            var pdf = new System.Text.StringBuilder();
            PdfSvgRenderer.Draw(document.DocumentElement!, pdf, 0, 0, 24, 24, 800);
            return System.Text.RegularExpressions.Regex.Matches(pdf.ToString(), @" re\b|\bm\b").Count;
        }
    }

    [Fact]
    public void KeepsIdentifierStylesOnCopies()
    {
        WriteIcon("acme/styled-use.svg",
            """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><style>#shape { fill: red } #other { fill: blue }</style><defs><path id="shape" d="M0 0h2v2z"/></defs><use href="#shape"/><path id="other" d="M0 0"/></svg>""");

        IconCatalog.UseCustomIcons(_root);

        Assert.True(IconCatalog.TryGetIcon("acme-styled-use", out var icon));
        Assert.Contains(".acme-styled-use-scope :is(#acme-styled-use-shape, .acme-styled-use-shape--copy) { fill: red }", icon.Glyph.Body);
        Assert.Contains(".acme-styled-use-scope #acme-styled-use-other { fill: blue }", icon.Glyph.Body);
        Assert.Contains("<g><path d=\"M0 0h2v2z\" class=\"acme-styled-use-shape--copy\" /></g>", icon.Glyph.Body);
    }

    [Fact]
    public void DrawsIconsWithUseReferencesInPdf()
    {
        WriteIcon("acme/reuse.svg",
            """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><defs><path id="shape" fill="#000" d="M0 0h2v2z"/></defs><use href="#shape"/></svg>""");
        IconCatalog.UseCustomIcons(_root);
        Assert.True(IconCatalog.TryGetIcon("acme-reuse", out var icon));
        var document = new System.Xml.XmlDocument();
        document.LoadXml(icon.Glyph.ToSvg());

        var pdf = new System.Text.StringBuilder();
        PdfSvgRenderer.Draw(document.DocumentElement!, pdf, 0, 0, 24, 24, 800);

        Assert.Contains("0 0 m", pdf.ToString());
    }

    [Fact]
    public void KeepsClassNamesOfIconsWithoutStyleSheet()
    {
        WriteIcon("acme/plain.svg", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect class="accent" width="10" height="10"/></svg>""");

        IconCatalog.UseCustomIcons(_root);

        Assert.True(IconCatalog.TryGetIcon("acme-plain", out var icon));
        Assert.Equal("<rect class=\"accent\" width=\"10\" height=\"10\" />", icon.Glyph.Body);
    }

    [Theory]
    [InlineData("material")]
    [InlineData("fontawesome")]
    [InlineData("dashboard")]
    public void RejectsSetNamesThatMixWithBundledSets(string setName)
    {
        WriteIcon($"{setName}/logo.svg", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"/>""");

        var exception = Assert.Throws<InvalidOperationException>(() => IconCatalog.UseCustomIcons(_root));

        Assert.Contains($"'{setName}'", exception.Message);
        Assert.Equal(IconCatalog.BuiltInSets, IconCatalog.Sets);
    }

    [Fact]
    public void RejectsSetNamesThatMixWithEachOther()
    {
        WriteIcon("acme/logo.svg", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"/>""");
        WriteIcon("acme-cloud/logo.svg", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"/>""");

        Assert.Throws<InvalidOperationException>(() => IconCatalog.UseCustomIcons(_root));
    }

    [Theory]
    [InlineData("logo.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"/>", "folder named after their icon set")]
    [InlineData("acme/my logo.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"/>", "may contain only")]
    [InlineData("acme/logo.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>", "viewBox")]
    [InlineData("acme/logo.svg", "<html/>", "not an SVG file")]
    [InlineData("acme/logo.svg", "<svg", "not a valid SVG file")]
    [InlineData("acme/logo.dark.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"/>", "no light icon")]
    public void ExplainsInvalidIconFiles(string path, string svg, string message)
    {
        WriteIcon(path, svg);

        var exception = Assert.Throws<InvalidOperationException>(() => IconCatalog.UseCustomIcons(_root));

        Assert.Contains(message, exception.Message);
    }

    [Fact]
    public void RemovesCustomSetsAgain()
    {
        WriteIcon("acme/logo.svg", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"/>""");
        IconCatalog.UseCustomIcons(_root);
        Assert.True(IconCatalog.TryGetIcon("acme-logo", out _));

        IconCatalog.UseCustomIcons(null);

        Assert.False(IconCatalog.TryGetIcon("acme-logo", out _));
        Assert.Equal(":acme-logo:", Render(":acme-logo:")[3..^5]);
    }

    [Fact]
    public void AddsCustomSetsToTheIconSearch()
    {
        WriteIcon("acme/logo.svg", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10"/></svg>""");
        IconCatalog.UseCustomIcons(_root);
        var output = Path.Combine(_root, "site");

        IconSearch.WriteAssetsTo(output);

        using var index = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "js", IconSearch.IndexFileName)));
        var set = index.RootElement.GetProperty("sets").EnumerateArray().Last();
        Assert.Equal("acme-", set.GetProperty("prefix").GetString());
        Assert.Equal("acme", set.GetProperty("name").GetString());
        Assert.Equal("logo", set.GetProperty("icons")[0][0].GetString());
    }

    private void WriteIcon(string relativePath, string svg)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, svg);
    }

    private static string Render(string markdown) => Markdig.Markdown.ToHtml(markdown, Pipeline).ReplaceLineEndings("\n");
}
