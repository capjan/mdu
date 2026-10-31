using System.Text;
using Markdown.Renderer.Res.Model;

namespace Markdown.Renderer.Test;

public class FaviconTest
{
    [Fact]
    public async Task KeepsConfiguredLogoInsideTheBrandBesideThePanelButton()
    {
        var html = await RenderAsync(new SiteConfiguration
        {
            LogoLight = "assets/light.svg",
            LogoDark = "assets/dark.svg"
        });

        var button = html.IndexOf("id=\"navigation-toggle\"", StringComparison.Ordinal);
        var brand = html.IndexOf("<a class=\"site-brand\"", StringComparison.Ordinal);
        var logo = html.IndexOf("<span class=\"site-brand-logo-slot\">", StringComparison.Ordinal);
        Assert.True(button >= 0 && brand > button && logo > brand);
        Assert.Contains("<img class=\"site-brand-logo site-brand-logo-light\" src=\"assets/light.svg\" alt=\"\"><img class=\"site-brand-logo site-brand-logo-dark\" src=\"assets/dark.svg\" alt=\"\">", html);
    }

    [Fact]
    public async Task RendersOneLogoImageWhenLogoFilesAreTheSame()
    {
        var html = await RenderAsync(new SiteConfiguration
        {
            LogoLight = "assets/logo.svg",
            LogoDark = "assets/logo.svg"
        });

        Assert.Contains("<span class=\"site-brand-logo-slot\"><img class=\"site-brand-logo\" src=\"assets/logo.svg\" alt=\"\"></span>", html);
        Assert.DoesNotContain("site-brand-logo-dark", html);
    }

    [Fact]
    public async Task RendersLightAndDarkLinksForDifferentFaviconFiles()
    {
        var html = await RenderAsync(new SiteConfiguration
        {
            FaviconLight = "assets/light.svg",
            FaviconDark = "assets/dark.svg"
        });

        Assert.Contains("<link rel=\"icon\" href=\"assets/light.svg\"><link rel=\"icon\" href=\"assets/dark.svg\" media=\"(prefers-color-scheme: dark)\">", html);
    }

    [Fact]
    public async Task RendersOneLinkWhenFaviconFilesAreTheSame()
    {
        var html = await RenderAsync(new SiteConfiguration
        {
            FaviconLight = "assets/favicon.svg",
            FaviconDark = "assets/favicon.svg"
        });

        Assert.Contains("<link rel=\"icon\" href=\"assets/favicon.svg\">", html);
        Assert.DoesNotContain("media=\"(prefers-color-scheme: dark)\"", html);
    }

    [Fact]
    public async Task RendersNoFaviconLinkWhenNoFaviconIsConfigured()
    {
        var html = await RenderAsync(new SiteConfiguration());

        Assert.DoesNotContain("<link rel=\"icon\"", html);
    }

    [Fact]
    public async Task RendersDarkLinkWhenOnlyDarkFaviconIsConfigured()
    {
        var html = await RenderAsync(new SiteConfiguration
        {
            FaviconDark = "assets/dark.svg"
        });

        Assert.Contains("<link rel=\"icon\" href=\"assets/dark.svg\" media=\"(prefers-color-scheme: dark)\">", html);
    }

    private static async Task<string> RenderAsync(SiteConfiguration configuration)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var renderer = new HtmlRenderer();
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);

        try
        {
            renderer.PrepareBasePath(outputPath, configuration);
            await renderer.WriteHtml(writer, "test", "", 0, "# Test", "");
            return sb.ToString();
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, recursive: true);
        }
    }
}
