using Markdown.Document.Impl;
using Markdown.Renderer;
using Markdown.Search;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;
using StaticCode.Icons;

namespace Mdu.Test;

// Custom icon sets are process-wide, so rendering with them must not overlap with other renders.
[Collection("Process state")]
public sealed class RenderCommandCustomIconsTest : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        IconCatalog.UseCustomIcons(null);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void RendersIconsOfTheDefaultIconsFolder()
    {
        WriteFile(".icons/acme/logo.svg", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M1 1h22"/></svg>""");
        WriteFile("README.md", "# Home\n\nBuilt by :acme-logo:\n");

        Assert.Equal(0, Render());

        var html = File.ReadAllText(Path.Combine(_root, "site", "index.html"));
        Assert.Contains("Built by <span class=\"icon\"><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" aria-hidden=\"true\" focusable=\"false\"><path d=\"M1 1h22\" /></svg></span>", html);
        Assert.False(Directory.Exists(Path.Combine(_root, "site", ".icons")));
    }

    [Fact]
    public void RendersIconsOfTheConfiguredIconsFolder()
    {
        WriteFile("mdu.yml", "icons: assets/icons\n");
        WriteFile("assets/icons/acme/logo.svg", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M1 1h22"/></svg>""");
        WriteFile("README.md", "# Home\n\n:acme-logo:\n");

        Assert.Equal(0, Render());

        Assert.Contains("viewBox=\"0 0 24 24\"", File.ReadAllText(Path.Combine(_root, "site", "index.html")));
    }

    [Fact]
    public void FailsWhenTheConfiguredIconsFolderIsMissing()
    {
        WriteFile("mdu.yml", "icons: assets/icons\n");
        WriteFile("README.md", "# Home\n");

        Assert.Equal(1, Render());
    }

    [Fact]
    public void ReadsTheIconsSetting()
    {
        WriteFile("mdu.yml", "icons: assets/icons\n");

        var configuration = SiteConfigurationReader.Read(_root);

        Assert.Equal("assets/icons", configuration.Icons);
        Assert.Equal(Path.Combine(_root, "assets", "icons"), SiteConfigurationReader.GetIconsPath(_root, configuration));
    }

    private int Render()
    {
        var command = new RenderCommand(
            new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())),
            new CountNodesVisitor<MarkdownFile>(),
            new HtmlRenderer(),
            new SearchIndexBuilder());

        return command.Execute(new RenderCommandSettings
        {
            RootPath = _root,
            OutputPath = Path.Combine(_root, "site"),
            SearchPattern = "*.md"
        });
    }

    private void WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
