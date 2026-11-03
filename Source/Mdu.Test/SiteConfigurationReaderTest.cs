using Mdu.Service;
using Markdown.Document.Impl;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Service.Renderer;
using Mdu.Service.Visitors;

namespace Mdu.Test;

public class SiteConfigurationReaderTest
{
    [Theory]
    [InlineData("", true)]
    [InlineData("site.attribution: {}", true)]
    [InlineData("site.attribution:", true)]
    [InlineData("site.attribution: ~", true)]
    [InlineData("site.attribution: null", true)]
    [InlineData("site.attribution: Null", true)]
    [InlineData("site.attribution: NULL", true)]
    [InlineData("site:\n  attribution:\nsite.attribution.visible: false", false)]
    [InlineData("site.attribution: {visible: false}\nsite.attribution.visible: false", false)]
    [InlineData("site.attribution: {visible: true}\nsite.attribution.visible: true", true)]
    [InlineData("site.attribution.visible: false", false)]
    [InlineData("site.attribution.visible: true", true)]
    [InlineData("site:\n  attribution:\n    visible: false", false)]
    [InlineData("site:\n  attribution:\n    visible: true", true)]
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
    [InlineData("site:\n  name: Nested", "Nested")]
    [InlineData("site.name: Compact", "Compact")]
    [InlineData("site:\n  name: Same\nsite.name: Same", "Same")]
    public void ReadsEverySettingNestedOrCompact(string yaml, string expected)
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "mdu.yml"), yaml);
            Assert.Equal(expected, SiteConfigurationReader.Read(root).SiteName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("site_name: Docs", "'site_name' is now 'site.name'")]
    [InlineData("logo:\n  light: logo.svg", "'logo.light' is now 'site.logo.light'")]
    [InlineData("colors:\n  dark:\n    primary: '#fff'", "'colors.dark.primary' is now 'site.colors.dark.primary'")]
    [InlineData("glossary: terms.md\nicons: icons", "'glossary' is now 'content.glossary', 'icons' is now 'content.icons'")]
    [InlineData("attribution.visible: false", "'attribution.visible' is now 'site.attribution.visible'")]
    [InlineData("diagrams:\n  maxLinks: 3", "'diagrams.maxLinks' is now 'diagrams.limits.links'")]
    [InlineData("site.nmae: Docs", "Unknown setting 'site.nmae'")]
    [InlineData("site: Docs", "'site' setting in mdu.yml groups other settings")]
    [InlineData("site.name: A\nsite:\n  name: B", "'site.name' setting in mdu.yml has conflicting")]
    [InlineData("site.name: [a]", "'site.name' setting in mdu.yml must be a single value")]
    [InlineData("site.logo: [a]", "'site.logo' setting in mdu.yml must be a single value")]
    [InlineData("site.logo: a.svg\nsite.logo.dark: b.svg", "'site.logo' setting in mdu.yml must be one value")]
    public void RejectsRenamedUnknownAndConflictingSettings(string yaml, string expected)
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "mdu.yml"), yaml);
            var exception = Assert.Throws<InvalidOperationException>(() => SiteConfigurationReader.Read(root));
            Assert.Contains(expected, exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("site.attribution.visible: True\nsite:\n  attribution:\n    visible: true")]
    [InlineData("diagrams.limits.links: 03\ndiagrams:\n  limits:\n    links: 3")]
    public void TheSameValueInAnotherSpellingIsNoConflict(string yaml)
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "mdu.yml"), yaml);
            SiteConfigurationReader.Read(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("site.name: Docs\nsite:\n  name: docs")]
    [InlineData("site.attribution.visible: true\nsite:\n  attribution:\n    visible: false")]
    [InlineData("diagrams.limits.links: 3\ndiagrams:\n  limits:\n    links: 4")]
    public void DifferentValuesStayAConflict(string yaml)
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "mdu.yml"), yaml);
            Assert.Contains("conflicting", Assert.Throws<InvalidOperationException>(() => SiteConfigurationReader.Read(root)).Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ContentReadersRejectRenamedSettings()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "mdu.yml"), "glossary: terms.md\n");
            File.WriteAllText(Path.Combine(root, "terms.md"), "*[API]: Application Programming Interface\n");

            Assert.Contains("'glossary' is now 'content.glossary'",
                Assert.Throws<InvalidOperationException>(() => SiteConfigurationReader.ReadForContent(root)).Message);
            Assert.Contains("is now 'content.glossary'",
                Assert.Throws<InvalidOperationException>(() => SiteConfigurationReader.ReadGlossarySetting(root)).Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("site.logo: logo.svg", "logo.svg", "logo.svg")]
    [InlineData("site.logo:\n  light: l.svg\n  dark: d.svg", "l.svg", "d.svg")]
    public void ReadsOneValueOrOnePerTheme(string yaml, string light, string dark)
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "mdu.yml"), yaml);
            var configuration = SiteConfigurationReader.Read(root);
            Assert.Equal(light, configuration.LogoLight);
            Assert.Equal(dark, configuration.LogoDark);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("site.attribution: false")]
    [InlineData("site.attribution: \"null\"")]
    [InlineData("site.attribution: \"~\"")]
    [InlineData("site.attribution.visible: maybe")]
    [InlineData("site:\n  attribution:\n    visible: []")]
    [InlineData("site.attribution.visible:")]
    [InlineData("site.attribution: {visible: false}\nsite.attribution.visible: true")]
    [InlineData("site.attribution: {visible: true}\nsite.attribution.visible: false")]
    [InlineData("site.attribution: {visible: maybe}\nsite.attribution.visible: true")]
    [InlineData("site.attribution: {visible: true}\nsite.attribution.visible: maybe")]
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
    public void ReadsContentSettingsAndResolvesGlossaryDespiteInvalidHtmlAttribution()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-site-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var glossary = Path.Combine(root, "terms.md");
            File.WriteAllText(glossary, "*[API]: Application Programming Interface\n");
            File.WriteAllText(Path.Combine(root, "mdu.yml"), "content.glossary: terms.md\nsite.attribution: false\n");

            Assert.Throws<InvalidOperationException>(() => SiteConfigurationReader.Read(root));
            Assert.Equal("terms.md", SiteConfigurationReader.ReadForContent(root).Glossary);
            Assert.Equal(glossary, SiteConfigurationReader.TryResolveGlossaryPath(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LintAndFixKeepConfiguredGlossaryExcludedDespiteInvalidHtmlAttribution(bool fix)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-site-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "README.md"), "# Documentation\n\nAPI documentation.\n");
            var glossary = Path.Combine(root, "terms.md");
            const string terms = "*[API]: Application Programming Interface\n";
            File.WriteAllText(glossary, terms);
            File.WriteAllText(Path.Combine(root, "mdu.yml"), "content.glossary: terms.md\nsite.attribution: false\n");
            var graph = new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader()));
            var receiver = new ConsoleLintErrorReceiver();
            var linter = new LinterVisitor(receiver);
            var settings = new VisitorSettings { RootPath = root, SearchPattern = "*.md", IncludeHidden = true };

            var result = fix
                ? new FixCommand(graph, linter).Execute(settings)
                : new LintCommand(graph, linter, new LintResultRenderer(receiver)).Execute(settings);

            Assert.Equal(0, result);
            Assert.Equal(0, receiver.ErrorCount);
            Assert.Equal(0, receiver.WarningCount);
            Assert.Equal(terms, File.ReadAllText(glossary));
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
            File.WriteAllText(Path.Combine(root, "mdu.yml"), "content.glossary: includes/abbreviations.md\n");

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
                site.logo:
                  light: assets/logo-light.svg
                  dark: assets/logo-dark.svg
                """);
            var configuration = SiteConfigurationReader.Read(root);

            Assert.Equal("assets/logo-light.svg", configuration.FaviconLight);
            Assert.Equal("assets/logo-dark.svg", configuration.FaviconDark);

            File.WriteAllText(Path.Combine(root, "mdu.yml"), """
                site:
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
            site:
              name: My Wiki
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
