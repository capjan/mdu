using Mdu.Model;

namespace Mdu.Test;

public sealed class MarkdownSnapshotTest
{
    [Fact]
    public void ReadsOneConsistentVersionUntilTheWatcherInvalidatesIt()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "First version");
            var page = new MarkdownFile(new FileInfo(file), null);
            Assert.Equal("First version", page.ReadMarkdown());
            File.WriteAllText(file, "Second version");
            Assert.Equal("First version", page.ReadMarkdown());
            page.InvalidateMarkdown();
            Assert.Equal("Second version", page.ReadMarkdown());
            page.MarkdownContent = "Imported override";
            Assert.Equal("Imported override", page.ReadMarkdown());
            page.InvalidateMarkdown();
            Assert.Equal("Imported override", page.ReadMarkdown());
        }
        finally { File.Delete(file); }
    }
}
