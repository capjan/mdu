using Markdown.Document.Impl;
using Markdown.Renderer;
using Markdown.Search;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Test;

public class RenderCommandImageZoomTest
{
    [Fact]
    public void AppliesThePageSettingAndPublishesTheScriptOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "site");
        Directory.CreateDirectory(Path.Combine(root, "Guide"));
        File.WriteAllText(Path.Combine(root, "README.md"), "# Home\n\n![Plain](logo.png)\n");
        File.WriteAllText(Path.Combine(root, "logo.png"), "png");
        File.WriteAllText(Path.Combine(root, "Off.md"), "---\nimages.zoom: false\n---\n# Off\n\n![Plain](logo.png)\n");
        File.WriteAllText(Path.Combine(root, "Guide", "README.md"), "---\nimages:\n  zoom: false\n---\n# Guide\n\n![On](../logo.png){.image-zoom-on}\n");

        try
        {
            var command = new RenderCommand(
                new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())),
                new CountNodesVisitor<MarkdownFile>(),
                new HtmlRenderer(),
                new SearchIndexBuilder());

            var result = command.Execute(new RenderCommandSettings
            {
                RootPath = root,
                OutputPath = output,
                SearchPattern = "*.md",
                IncludeHidden = true
            });

            Assert.Equal(0, result);
            Assert.True(File.Exists(Path.Combine(output, "js", "image-lightbox.js")));
            Assert.Contains("<script defer src=\"js/image-lightbox.js\"></script>", File.ReadAllText(Path.Combine(output, "index.html")));
            Assert.DoesNotContain("image-lightbox.js", File.ReadAllText(Path.Combine(output, "Off.html")));
            Assert.Contains("<script defer src=\"../js/image-lightbox.js\" data-image-zoom=\"off\"></script>",
                File.ReadAllText(Path.Combine(output, "Guide", "index.html")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
