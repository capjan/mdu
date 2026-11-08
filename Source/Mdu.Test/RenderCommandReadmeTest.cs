using Markdown.Document.Impl;
using Markdown.Renderer;
using Markdown.Search;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Test;

public class RenderCommandReadmeTest
{
    [Fact]
    public void OnlyRenamesExactReadmeFileName()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var output = Path.Combine(root, "site");

        try
        {
            File.WriteAllText(Path.Combine(root, "README.md"), "# Home\n\n[Guide](guide-readme.md)\n");
            File.WriteAllText(Path.Combine(root, "guide-readme.md"), "# Guide\n");
            File.WriteAllText(Path.Combine(root, "guide-index.md"), "# Guide index\n");

            var command = new RenderCommand(
                new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())),
                new CountNodesVisitor<MarkdownFile>(),
                new HtmlRenderer(),
                new SearchIndexBuilder());

            Assert.Equal(0, command.Execute(new RenderCommandSettings
            {
                RootPath = root,
                OutputPath = output,
                SearchPattern = "*.md"
            }));

            var home = File.ReadAllText(Path.Combine(output, "index.html"));
            Assert.Contains("href=\"guide-readme.html\"", home);
            Assert.Contains("Guide</h1>", File.ReadAllText(Path.Combine(output, "guide-readme.html")));
            Assert.Contains("Guide index</h1>", File.ReadAllText(Path.Combine(output, "guide-index.html")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
