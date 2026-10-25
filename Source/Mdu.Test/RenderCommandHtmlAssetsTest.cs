using Markdown.Document.Impl;
using Markdown.Renderer;
using Markdown.Search;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Test;

public class RenderCommandHtmlAssetsTest
{
    [Fact]
    public void CopiesEmbeddedHtmlPagesWithTheFilesTheyLoad()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var widget = Path.Combine(root, "assets", "widget");
        var output = Path.Combine(root, "site");
        Directory.CreateDirectory(Path.Combine(widget, "img"));
        File.WriteAllText(Path.Combine(root, "README.md"), "# Home\n\n<iframe src=\"assets/widget/index.html\"></iframe>\n");
        File.WriteAllText(Path.Combine(widget, "index.html"),
            "<link href=\"style.css\" rel=\"stylesheet\"><script src=\"app.js\"></script><img src=\"img/logo.png\">" +
            "<!-- <script src=\"old.js\"></script> --><a href=\"index.html\">Reload</a><a href=\"https://example.com\">Web</a>");
        File.WriteAllText(Path.Combine(widget, "style.css"), "body {}");
        File.WriteAllText(Path.Combine(widget, "app.js"), "console.log(1);");
        File.WriteAllText(Path.Combine(widget, "old.js"), "console.log(0);");
        File.WriteAllText(Path.Combine(widget, "img", "logo.png"), "png");

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
            var copied = Path.Combine(output, "assets", "widget");
            Assert.True(File.Exists(Path.Combine(copied, "index.html")));
            Assert.True(File.Exists(Path.Combine(copied, "style.css")));
            Assert.True(File.Exists(Path.Combine(copied, "app.js")));
            Assert.True(File.Exists(Path.Combine(copied, "img", "logo.png")));
            Assert.False(File.Exists(Path.Combine(copied, "old.js")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
