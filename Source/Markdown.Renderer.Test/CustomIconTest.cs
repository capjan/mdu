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
            "<defs><style> .acme-gradient-cls-1 { fill: url(#acme-gradient-fade); opacity: .5 }  #acme-gradient-dot &gt; .acme-gradient-cls-1 { fill: #f00 } </style>" +
            "<linearGradient id=\"acme-gradient-fade\"><stop offset=\"0\" /></linearGradient></defs>" +
            "<rect id=\"acme-gradient-dot\" class=\"acme-gradient-cls-1 acme-gradient-cls-2\" style=\"fill: url('#acme-gradient-fade')\" width=\"10\" height=\"10\" />" +
            "<use href=\"#acme-gradient-dot\" />",
            icon.Glyph.Body);
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
