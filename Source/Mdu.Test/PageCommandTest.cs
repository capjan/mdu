using Mdu.Commands;
using Mdu.Service;

namespace Mdu.Test;

public class PageCommandTest : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mdu-page-" + Guid.NewGuid().ToString("N"));

    public PageCommandTest()
    {
        Write("README.md", "# Docs\n\n- [Setup](Setup.md#install)\n- [Guide](Guide/)\n- [Reference][setup]\n\n[setup]: ./Setup.md\n");
        Write("Setup.md", "# Setup\n\n![Logo](assets/logo.svg)\n\nSee the [guide](Guide/README.md) and [code](`not a link`).\n\n`[Setup](Setup.md)` stays code.\n");
        Write("assets/logo.svg", "<svg />");
        Write("Guide/README.md", "# Guide\n\n[Steps](./Steps.md) · [Setup](../Setup.md) · [Diagram](diagram.svg) · [Web](https://example.com/Setup.md)\n");
        Write("Guide/Steps.md", "# Steps\n\n[Back](README.md)\n");
        Write("Guide/diagram.svg", "<svg />");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void PageLinkToolsIgnoreFootnoteDefinitions()
    {
        const string content = "# Overview\n\n[^note]: Guide.md\n";
        Write("Overview.md", content);
        Write("Guide.md", "# Guide\n");

        var sourcePath = Path.Combine(_root, "Overview.md");
        var newSourcePath = Path.Combine(_root, "Moved", "Overview.md");
        var targetPath = Path.Combine(_root, "Guide.md");
        var movedTargetPath = Path.Combine(_root, "MovedGuide.md");
        var movedPaths = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [targetPath] = movedTargetPath
        };

        var rewritten = MarkdownLinks.Rewrite(content, sourcePath, newSourcePath, movedPaths, StringComparer.Ordinal, out var changedLinkCount);

        Assert.Equal(content, rewritten);
        Assert.Equal(0, changedLinkCount);
        Assert.Empty(MarkdownLinks.Find(content, sourcePath));
    }

    [Fact]
    public void PageLinkToolsFindAndRewriteLinksInsideUnusedAndDuplicateFootnotes()
    {
        const string content = "# Overview\n\nA note[^used].\n\n[^unused]: See [Guide](Guide.md).\n\n[^duplicate]: See [Guide](Guide.md).\n\n[^duplicate]: See [Guide](Guide.md).\n\n[^used]: See [Guide](Guide.md).\n";
        Write("Overview.md", content);
        Write("Guide.md", "# Guide\n");

        var sourcePath = Path.Combine(_root, "Overview.md");
        var newSourcePath = Path.Combine(_root, "Moved", "Overview.md");
        var targetPath = Path.Combine(_root, "Guide.md");
        var movedTargetPath = Path.Combine(_root, "MovedGuide.md");
        var movedPaths = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [targetPath] = movedTargetPath
        };

        var rewritten = MarkdownLinks.Rewrite(content, sourcePath, newSourcePath, movedPaths, StringComparer.Ordinal, out var changedLinkCount);

        Assert.Equal(4, changedLinkCount);
        Assert.Equal(content.Replace("(Guide.md)", "(../MovedGuide.md)"), rewritten);
        Assert.Equal(4, MarkdownLinks.Find(content, sourcePath).Count);
    }

    [Fact]
    public void MovesAPageAndRewritesLinksToItAndInsideIt()
    {
        var result = Move("Setup.md", "Start/Setup.md");

        Assert.Equal(0, result.Exit);
        Assert.False(File.Exists(Path.Combine(_root, "Setup.md")));
        Assert.Equal("# Docs\n\n- [Setup](Start/Setup.md#install)\n- [Guide](Guide/)\n- [Reference][setup]\n\n[setup]: Start/Setup.md\n", Read("README.md"));
        Assert.Equal("# Setup\n\n![Logo](../assets/logo.svg)\n\nSee the [guide](../Guide/README.md) and [code](`not a link`).\n\n`[Setup](Setup.md)` stays code.\n",
            Read("Start/Setup.md"));
        Assert.Contains("[Setup](../Start/Setup.md)", Read("Guide/README.md"));
        Assert.Contains("Updated 5 links in 3 pages:", result.Output);
    }

    [Fact]
    public void UpdatesOpenApiReferenceWhenPageMoves()
    {
        Write("api/orders.yaml", "openapi: 3.1.0\n");
        Write("Reference.md", "# Orders\n\n```openapi\napi/orders.yaml\n```\n");

        Assert.Equal(0, Move("Reference.md", "Guide/Reference.md").Exit);

        Assert.Equal("# Orders\n\n```openapi\n../api/orders.yaml\n```\n", Read("Guide/Reference.md"));
    }

    [Fact]
    public void QuotesOpenApiReferenceWhenMovedPathNeedsYamlQuotes()
    {
        Write("api/order specs.yaml", "openapi: 3.1.0\n");
        Write("Reference.md", "# Orders\n\n```openapi\napi/order specs.yaml\n```\n");

        Assert.Equal(0, Move("Reference.md", "Guide/Reference.md").Exit);

        Assert.Equal("# Orders\n\n```openapi\n\"../api/order specs.yaml\"\n```\n", Read("Guide/Reference.md"));
    }

    [Fact]
    public void LeavesRemoteOpenApiUrlsAloneAndRewritesBlocksInListsAndBlockquotesWhenPageMoves()
    {
        Write("api/orders.yaml", "openapi: 3.1.0\n");
        Write("Reference.md", """
            # Orders

            ```openapi
            https://example.com/openapi.yaml
            ```

            > ```openapi
            > spec: api/orders.yaml
            > ```

            - Item:

              ```openapi
              api/orders.yaml
              ```
            """);

        Assert.Equal(0, Move("Reference.md", "Guide/Reference.md").Exit);

        Assert.Equal("""
            # Orders

            ```openapi
            https://example.com/openapi.yaml
            ```

            > ```openapi
            > spec: ../api/orders.yaml
            > ```

            - Item:

              ```openapi
              ../api/orders.yaml
              ```
            """, Read("Guide/Reference.md").ReplaceLineEndings("\n"));
    }

    [Fact]
    public void MovesAFolderWithItsFilesAndKeepsLinksBetweenThem()
    {
        var result = Move("Guide", "Handbook/Guide");

        Assert.Equal(0, result.Exit);
        Assert.Contains("Moved folder with 3 files: Guide -> Handbook/Guide", result.Output);
        Assert.Contains("[Guide](Handbook/Guide/)", Read("README.md"));
        Assert.Contains("[guide](Handbook/Guide/README.md)", Read("Setup.md"));
        Assert.Equal("# Guide\n\n[Steps](./Steps.md) · [Setup](../../Setup.md) · [Diagram](diagram.svg) · [Web](https://example.com/Setup.md)\n",
            Read("Handbook/Guide/README.md"));
        Assert.Equal("# Steps\n\n[Back](README.md)\n", Read("Handbook/Guide/Steps.md"));
        Assert.True(File.Exists(Path.Combine(_root, "Handbook", "Guide", "diagram.svg")));
    }

    [Fact]
    public void MovesIntoAnExistingFolderAndAddsTheExtension()
    {
        Assert.Equal(0, Move("Guide/Steps.md", "Setup-Steps").Exit);
        Assert.True(File.Exists(Path.Combine(_root, "Setup-Steps.md")));

        Assert.Equal(0, Move("Setup-Steps.md", "Guide").Exit);
        Assert.True(File.Exists(Path.Combine(_root, "Guide", "Setup-Steps.md")));
    }

    [Fact]
    public void ShowsChangesWithoutApplyingThemInADryRun()
    {
        var before = Read("README.md");

        var result = Move("Setup.md", "Start/Setup.md", dryRun: true);

        Assert.Contains("Would move page: Setup.md -> Start/Setup.md", result.Output);
        Assert.Contains("Would update 5 links in 3 pages:", result.Output);
        Assert.True(File.Exists(Path.Combine(_root, "Setup.md")));
        Assert.Equal(before, Read("README.md"));
    }

    [Fact]
    public void RenamesAPageWhenOnlyTheCaseChanges()
    {
        Assert.Equal(0, Move("Setup.md", "setup.md").Exit);

        Assert.Contains("setup.md", Directory.GetFiles(_root).Select(Path.GetFileName));
        Assert.Contains("[Setup](setup.md#install)", Read("README.md"));
    }

    [Theory]
    [InlineData("Missing.md", "Other.md", "does not exist")]
    [InlineData("assets/logo.svg", "logo.svg", "is not a Markdown page")]
    [InlineData("Setup.md", "Guide/Steps.md", "already exists")]
    [InlineData("Guide", "Guide/Inner", "cannot move into itself")]
    [InlineData("Setup.md", "../Outside.md", "is not inside the documentation folder")]
    public void RefusesImpossibleMoves(string source, string target, string reason)
    {
        var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            Assert.Equal(1, Move(source, target).Exit);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Contains(reason, error.ToString());
        Assert.True(File.Exists(Path.Combine(_root, "Setup.md")));
    }

    [Fact]
    public void RefusesMovesThroughSymbolicLinksAndLeavesLinkedPagesAlone()
    {
        var outside = _root + "-outside";
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(outside, "Shared.md"), "# Shared\n\n[Setup](../Setup.md)\n");
            Directory.CreateSymbolicLink(Path.Combine(_root, "External"), outside);
            var error = new StringWriter();
            var originalError = Console.Error;
            Console.SetError(error);
            try
            {
                Assert.Equal(1, Move("Setup.md", "External/Setup.md").Exit);
                Assert.Equal(1, Move("External", "Moved").Exit);
            }
            finally
            {
                Console.SetError(originalError);
            }

            Assert.Contains("goes through the symbolic link External", error.ToString());
            Assert.True(File.Exists(Path.Combine(_root, "Setup.md")));
            Assert.False(File.Exists(Path.Combine(outside, "Setup.md")));

            Assert.Equal(0, Move("Setup.md", "Start/Setup.md").Exit);
            Assert.Equal("# Shared\n\n[Setup](../Setup.md)\n", File.ReadAllText(Path.Combine(outside, "Shared.md")));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void RefusesFolderMovesThatWouldRedirectRelativeSymbolicLinks()
    {
        File.CreateSymbolicLink(Path.Combine(_root, "Guide", "logo.svg"), "../assets/logo.svg");
        var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            Assert.Equal(1, Move("Guide", "Handbook/Guide").Exit);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Contains("Guide/logo.svg is a symbolic link to ../assets/logo.svg", error.ToString().Replace('\\', '/'));
        Assert.True(Directory.Exists(Path.Combine(_root, "Guide")));
        Assert.False(Directory.Exists(Path.Combine(_root, "Handbook")));
    }

    [Fact]
    public void MovesFoldersWhoseSymbolicLinksStayValid()
    {
        File.CreateSymbolicLink(Path.Combine(_root, "Guide", "steps-link.md"), "Steps.md");
        File.CreateSymbolicLink(Path.Combine(_root, "Guide", "absolute-logo.svg"), Path.Combine(_root, "assets", "logo.svg"));

        Assert.Equal(0, Move("Guide", "Handbook/Guide").Exit);

        var moved = Path.Combine(_root, "Handbook", "Guide");
        Assert.Equal("# Steps\n\n[Back](README.md)\n", File.ReadAllText(Path.Combine(moved, "steps-link.md")));
        Assert.Equal("<svg />", File.ReadAllText(Path.Combine(moved, "absolute-logo.svg")));
    }

    [Fact]
    public void RefusesFolderMovesThatBreakAbsoluteSymbolicLinksIntoTheFolder()
    {
        File.CreateSymbolicLink(Path.Combine(_root, "Guide", "steps-link.md"), Path.Combine(_root, "Guide", "Steps.md"));
        var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);
        try
        {
            Assert.Equal(1, Move("Guide", "Handbook/Guide").Exit);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Contains("steps-link.md is a symbolic link to", error.ToString());
        Assert.True(Directory.Exists(Path.Combine(_root, "Guide")));
    }

    [Fact]
    public void RenamesFoldersInPlaceWhenRelativeSymbolicLinksKeepTheirTarget()
    {
        File.CreateSymbolicLink(Path.Combine(_root, "Guide", "logo.svg"), "../assets/logo.svg");

        Assert.Equal(0, Move("Guide", "Manual").Exit);

        Assert.Equal("<svg />", File.ReadAllText(Path.Combine(_root, "Manual", "logo.svg")));
    }

    [Fact]
    public void ListsTheLinksThatPointToAPage()
    {
        var output = new StringWriter();

        var exit = new PageCommand().Links(_root, Path.Combine(_root, "Setup.md"), includeHidden: true, output);

        Assert.Equal(0, exit);
        Assert.Equal(
            "Guide/README.md:3: [Setup](../Setup.md)\n" +
            "README.md:3: [Setup](Setup.md#install)\n" +
            "README.md:5: [Reference][setup]\n" +
            "README.md:7: [setup]: ./Setup.md\n" +
            "4 links in 2 pages point to Setup.md.\n",
            output.ToString().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ListsLinksToAFolderAndToImages()
    {
        var folder = new StringWriter();
        new PageCommand().Links(_root, Path.Combine(_root, "Guide"), includeHidden: true, folder);
        Assert.Contains("README.md:4: [Guide](Guide/)", folder.ToString());
        Assert.Contains("Setup.md:5: [guide](Guide/README.md)", folder.ToString());
        Assert.Contains("Guide/Steps.md:3: [Back](README.md)", folder.ToString());

        var image = new StringWriter();
        new PageCommand().Links(_root, Path.Combine(_root, "assets", "logo.svg"), includeHidden: true, image);
        Assert.Contains("Setup.md:3: ![Logo](assets/logo.svg)", image.ToString());
    }

    [Fact]
    public void ListsLinksToOpenApiSpecificationsInCodeBlocks()
    {
        Write("api/orders.yaml", "openapi: 3.1.0\n");
        Write("Reference.md", "# Orders\n\n```openapi\napi/orders.yaml\n```\n");
        Write("Guide/Overview.md", "# Overview\n\n```openapi\nspec: ../api/orders.yaml\n```\n");
        var output = new StringWriter();

        var exit = new PageCommand().Links(_root, Path.Combine(_root, "api", "orders.yaml"), includeHidden: true, output);

        Assert.Equal(0, exit);
        Assert.Equal(
            "Guide/Overview.md:4: ../api/orders.yaml\n" +
            "Reference.md:4: api/orders.yaml\n" +
            "2 links in 2 pages point to api/orders.yaml.\n",
            output.ToString().ReplaceLineEndings("\n"));
    }

    private (int Exit, string Output) Move(string source, string target, bool dryRun = false)
    {
        var output = new StringWriter();
        var exit = new PageCommand().Move(_root, Path.Combine(_root, source), Path.Combine(_root, target), includeHidden: true, dryRun, output);
        return (exit, output.ToString().ReplaceLineEndings("\n"));
    }

    private string Read(string path) => File.ReadAllText(Path.Combine(_root, path));

    private void Write(string path, string content)
    {
        var fullPath = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}
