using Mdu.Service;

namespace Mdu.Test;

public class SiteConfigurationReaderTest
{
    [Theory]
    [InlineData("", true)]
    [InlineData("attribution: {}", true)]
    [InlineData("attribution:", true)]
    [InlineData("attribution:\nattribution.visible: false", false)]
    [InlineData("attribution: {visible: false}\nattribution.visible: false", false)]
    [InlineData("attribution: {visible: true}\nattribution.visible: true", true)]
    [InlineData("attribution.visible: false", false)]
    [InlineData("attribution.visible: true", true)]
    [InlineData("attribution:\n  visible: false", false)]
    [InlineData("attribution:\n  visible: true", true)]
    public void ReadsAttributionVisibility(string yaml, bool expected)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-site-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "mdu.yml"), yaml);
            Assert.Equal(expected, SiteConfigurationReader.Read(root).AttributionVisible);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("attribution: false")]
    [InlineData("attribution.visible: maybe")]
    [InlineData("attribution:\n  visible: []")]
    [InlineData("attribution.visible:")]
    [InlineData("attribution: {visible: false}\nattribution.visible: true")]
    [InlineData("attribution: {visible: true}\nattribution.visible: false")]
    [InlineData("attribution: {visible: maybe}\nattribution.visible: true")]
    [InlineData("attribution: {visible: true}\nattribution.visible: maybe")]
    public void RejectsInvalidAttributionVisibility(string yaml)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-site-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "mdu.yml"), yaml);
            var exception = Assert.Throws<InvalidOperationException>(() => SiteConfigurationReader.Read(root));
            Assert.Contains("attribution", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadsAndResolvesConfiguredGlossaryRelativeToTheDocumentationRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-site-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "includes"));
            var glossaryPath = Path.Combine(root, "includes", "abbreviations.md");
            File.WriteAllText(glossaryPath, "*[API]: Application Programming Interface\n");
            File.WriteAllText(Path.Combine(root, "mdu.yml"), "glossary: includes/abbreviations.md\n");

            var configuration = SiteConfigurationReader.Read(root);

            Assert.Equal("includes/abbreviations.md", configuration.Glossary);
            Assert.Equal(Path.GetFullPath(glossaryPath), SiteConfigurationReader.ResolveGlossaryPath(root, configuration));
            Assert.Contains("*[API]", SiteConfigurationReader.ReadGlossary(root, configuration));

            File.Delete(glossaryPath);
            var exception = Assert.Throws<InvalidOperationException>(
                () => SiteConfigurationReader.ResolveGlossaryPath(root, configuration));
            Assert.Contains(glossaryPath, exception.Message);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UsesLogoAsDefaultAndMissingLightFavicon()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-site-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "mdu.yml"), """
                logo:
                  light: assets/logo-light.svg
                  dark: assets/logo-dark.svg
                """);
            var configuration = SiteConfigurationReader.Read(root);

            Assert.Equal("assets/logo-light.svg", configuration.FaviconLight);
            Assert.Equal("assets/logo-dark.svg", configuration.FaviconDark);

            File.WriteAllText(Path.Combine(root, "mdu.yml"), """
                logo:
                  light: assets/logo-light.svg
                  dark: assets/logo-dark.svg
                favicon:
                  dark: assets/favicon-dark.svg
                """);
            configuration = SiteConfigurationReader.Read(root);

            Assert.Equal("assets/logo-light.svg", configuration.FaviconLight);
            Assert.Equal("assets/favicon-dark.svg", configuration.FaviconDark);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadsBrandingColorsAndCopiesConfiguredAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-site-config-test-{Guid.NewGuid():N}");
        var assetDirectory = Path.Combine(root, "assets");
        var output = Path.Combine(root, "site");
        Directory.CreateDirectory(assetDirectory);
        File.WriteAllText(Path.Combine(root, "mdu.yml"), """
            site_name: My Wiki
            logo:
              light: assets/logo-light.svg
              dark: assets/logo-dark.svg
            favicon:
              light: assets/favicon.svg
            colors:
              light:
                background: "#ffffff"
                text: "#222222"
                primary: "#0066cc"
            """);
        foreach (var asset in new[] { "logo-light.svg", "logo-dark.svg", "favicon.svg" })
            File.WriteAllText(Path.Combine(assetDirectory, asset), "<svg />");

        try
        {
            var configuration = SiteConfigurationReader.Read(root);
            SiteConfigurationReader.CopyAssets(root, output, configuration);

            Assert.Equal("My Wiki", configuration.SiteName);
            Assert.Equal("assets/logo-light.svg", configuration.LogoLight);
            Assert.Equal("#0066cc", configuration.Light?.Primary);
            Assert.Equal("assets/logo-dark.svg", configuration.LogoDark);
            Assert.Equal("assets/favicon.svg", configuration.FaviconDark);
            Assert.True(File.Exists(Path.Combine(output, "assets", "logo-light.svg")));
            Assert.True(File.Exists(Path.Combine(output, "assets", "logo-dark.svg")));
            Assert.True(File.Exists(Path.Combine(output, "assets", "favicon.svg")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
