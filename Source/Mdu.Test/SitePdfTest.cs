using System.Text;
using System.Text.RegularExpressions;
using Markdown.Document.Impl;
using Markdown.Renderer;
using Markdown.Search;
using Mdu.Commands;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Test;

public class SitePdfTest
{
    [Fact]
    public void RendersDistinctSubtreesAndRemovesObsoleteExportsAcrossRuns()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-site-pdf-{Guid.NewGuid():N}");
        var docs = Path.Combine(root, "docs");
        var output = Path.Combine(root, "site");
        Directory.CreateDirectory(Path.Combine(docs, "Topic A"));
        Directory.CreateDirectory(Path.Combine(docs, "Other"));
        var home = Path.Combine(docs, "README.md");
        var topic = Path.Combine(docs, "Topic A", "README.md");
        File.WriteAllText(home, "# Home\n\n[All](mdu:pdf)\n\n[Topic](mdu:pdf?root=Topic%20A/README.md)");
        File.WriteAllText(topic, "# Selected\n\n[Self](mdu:pdf?root=README.md)\n\n[Other](mdu:pdf?root=../Other/README.md)");
        File.WriteAllText(Path.Combine(docs, "Topic A", "Details.md"), "# Details\n");
        File.WriteAllText(Path.Combine(docs, "Other", "README.md"), "# Unrelated\n");
        try
        {
            Assert.Equal(0, Render(docs, output));
            Assert.True(File.Exists(Path.Combine(output, "documentation.pdf")));
            var generated = Directory.GetFiles(Path.Combine(output, "_mdu-pdf"), "*.pdf");
            Assert.Equal(2, generated.Length);
            var topicUrl = ReadScopedUrl(File.ReadAllText(Path.Combine(output, "index.html")));
            var topicPdf = File.ReadAllText(Path.Combine(output, topicUrl), Encoding.Latin1);
            Assert.Contains(Hex("Selected"), topicPdf);
            Assert.Contains(Hex("Details"), topicPdf);
            Assert.DoesNotContain(Hex("Unrelated"), topicPdf);
            Assert.DoesNotContain(Hex("Home"), topicPdf);
            var topicHtml = File.ReadAllText(Path.Combine(output, "Topic A", "index.html"));
            Assert.Contains($"href=\"../{topicUrl}\"", topicHtml);

            File.WriteAllText(home, "# Home\n\n[Topic](mdu:pdf?root=Topic%20A/README.md)");
            File.WriteAllText(topic, "# Selected\n\nUpdated");
            Assert.Equal(0, Render(docs, output));
            Assert.False(File.Exists(Path.Combine(output, "documentation.pdf")));
            Assert.Single(Directory.GetFiles(Path.Combine(output, "_mdu-pdf"), "*.pdf"));
            Assert.Contains(Hex("Updated"), File.ReadAllText(Path.Combine(output, topicUrl), Encoding.Latin1));

            File.WriteAllText(home, "# Home");
            Assert.Equal(0, Render(docs, output));
            Assert.Empty(Directory.GetFiles(Path.Combine(output, "_mdu-pdf"), "*.pdf"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("mdu:pdf?root=Missing.md")]
    [InlineData("mdu:pdf?root=../../Outside.md")]
    [InlineData("mdu:pdf?root=")]
    public void InvalidLinkedRootFailsSiteRendering(string link)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-site-pdf-invalid-{Guid.NewGuid():N}");
        var docs = Path.Combine(root, "docs");
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "README.md"), $"# Home\n\n[PDF]({link})");
        try
        {
            Assert.Equal(1, Render(docs, Path.Combine(root, "site")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static int Render(string docs, string output) =>
        new RenderCommand(new GraphBuilder(new DataPreparationVisitor(new MarkdownDocumentReader())),
            new CountNodesVisitor<MarkdownFile>(), new HtmlRenderer(), new SearchIndexBuilder())
            .Execute(new RenderCommandSettings { RootPath = docs, OutputPath = output });

    private static string ReadScopedUrl(string html) =>
        Regex.Match(html, "href=\"(?<url>_mdu-pdf/[a-f0-9]+\\.pdf)\"").Groups["url"].Value;

    private static string Hex(string text) => Convert.ToHexString(Encoding.Latin1.GetBytes(text));
}
