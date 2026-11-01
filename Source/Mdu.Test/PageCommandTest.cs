using System.Text;
using Mdu.Commands;
using Mdu.Service;

namespace Mdu.Test;

internal sealed class CaseSensitiveFileSystemFactAttribute : FactAttribute
{
    public CaseSensitiveFileSystemFactAttribute()
    {
        var probePath = Path.Combine(Path.GetTempPath(), $"mdu-case-{Guid.NewGuid():N}a");
        var alternatePath = probePath[..^1] + "A";
        File.WriteAllText(probePath, string.Empty);
        try
        {
            if (File.Exists(alternatePath))
                Skip = "Requires a case-sensitive filesystem.";
        }
        finally
        {
            File.Delete(probePath);
        }
    }
}

internal sealed class UnicodeNormalizationEquivalentFileSystemFactAttribute : FactAttribute
{
    public UnicodeNormalizationEquivalentFileSystemFactAttribute()
    {
        var probePath = Path.Combine(Path.GetTempPath(), $"mdu-unicode-{Guid.NewGuid():N}-\u00fc");
        var equivalentPath = probePath.Normalize(NormalizationForm.FormD);
        File.WriteAllText(probePath, string.Empty);
        try
        {
            if (!File.Exists(equivalentPath))
                Skip = "Requires a filesystem that treats canonically equivalent Unicode names as the same entry.";
        }
        finally
        {
            File.Delete(probePath);
        }
    }
}

internal sealed class MacCaseInsensitiveFileSystemFactAttribute : FactAttribute
{
    public MacCaseInsensitiveFileSystemFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip = "Requires macOS.";
            return;
        }

        if (Environment.UserName == "root")
        {
            Skip = "Requires a non-root user for directory permission checks.";
            return;
        }

        var probePath = Path.Combine(Path.GetTempPath(), $"mdu-case-{Guid.NewGuid():N}a");
        var alternatePath = probePath[..^1] + "A";
        File.WriteAllText(probePath, string.Empty);
        try
        {
            if (!File.Exists(alternatePath))
                Skip = "Requires a case-insensitive filesystem.";
        }
        finally
        {
            File.Delete(probePath);
        }
    }
}

internal sealed class MacCaseSensitiveUnicodeEquivalentFileSystemFactAttribute : FactAttribute
{
    public MacCaseSensitiveUnicodeEquivalentFileSystemFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip = "Requires macOS.";
            return;
        }

        var caseProbePath = Path.Combine(Path.GetTempPath(), $"mdu-case-{Guid.NewGuid():N}a");
        var alternateCasePath = caseProbePath[..^1] + "A";
        File.WriteAllText(caseProbePath, string.Empty);
        try
        {
            if (File.Exists(alternateCasePath))
            {
                Skip = "Requires a case-sensitive filesystem.";
                return;
            }
        }
        finally
        {
            File.Delete(caseProbePath);
        }

        var normalizationProbePath = Path.Combine(Path.GetTempPath(), $"mdu-unicode-{Guid.NewGuid():N}-\u00fc");
        var equivalentPath = normalizationProbePath.Normalize(NormalizationForm.FormD);
        File.WriteAllText(normalizationProbePath, string.Empty);
        try
        {
            if (!File.Exists(equivalentPath))
                Skip = "Requires a filesystem that treats canonically equivalent Unicode names as the same entry.";
        }
        finally
        {
            File.Delete(normalizationProbePath);
        }
    }
}

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

    [CaseSensitiveFileSystemFact]
    public void RefusesMoveWhenCaseSensitiveTargetAlreadyExists()
    {
        Write("Setup.md", "# Source\n");
        Write("setup.md", "# Target\n");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PageMover.Plan(_root, Path.Combine(_root, "Setup.md"), Path.Combine(_root, "setup.md"), includeHidden: true));

        Assert.Contains("setup.md already exists", exception.Message);
        Assert.Equal("# Source\n", Read("Setup.md"));
        Assert.Equal("# Target\n", Read("setup.md"));
    }

    [CaseSensitiveFileSystemFact]
    public void RefusesMoveToCaseDistinctDirectoryMatchingTheDocsRoot()
    {
        var docs = Path.Combine(_root, "docs");
        var caseDistinctDocs = Path.Combine(_root, "DOCS");
        var source = Path.Combine(docs, "Guide.md");
        var target = Path.Combine(caseDistinctDocs, "Guide.md");
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(caseDistinctDocs);
        File.WriteAllText(source, "# Source\n");
        File.WriteAllText(target, "# Target\n");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PageMover.Plan(docs, source, target, includeHidden: true));

        Assert.Contains("not inside the documentation folder", exception.Message);
        Assert.Equal("# Source\n", File.ReadAllText(source));
        Assert.Equal("# Target\n", File.ReadAllText(target));
    }

    [MacCaseInsensitiveFileSystemFact]
    public void AcceptsPathsWithCaseDifferentDocsRootSpelling()
    {
        var actualDocs = Path.Combine(_root, "Docs");
        Directory.CreateDirectory(actualDocs);
        File.WriteAllText(Path.Combine(actualDocs, "Guide.md"), "# Guide\n");

        var output = new StringWriter();
        Assert.Equal(0, new PageCommand().Move(
            Path.Combine(_root, "docs"),
            Path.Combine(actualDocs, "Guide.md"),
            Path.Combine(_root, "docs", "Moved.md"),
            includeHidden: true,
            dryRun: true,
            output));

        Assert.Contains("Would move page: Guide.md -> Moved.md", output.ToString());
    }

    [UnicodeNormalizationEquivalentFileSystemFact]
    public void AcceptsPathsWithUnicodeNormalizationDifferentDocsRootSpelling()
    {
        var actualDocs = Path.Combine(_root, "\u00fcber");
        var equivalentDocs = Path.Combine(_root, "u\u0308ber");
        Directory.CreateDirectory(actualDocs);
        File.WriteAllText(Path.Combine(actualDocs, "Guide.md"), "# Guide\n");

        var output = new StringWriter();
        Assert.Equal(0, new PageCommand().Move(
            actualDocs,
            Path.Combine(equivalentDocs, "Guide.md"),
            Path.Combine(equivalentDocs, "Moved.md"),
            includeHidden: true,
            dryRun: true,
            output));

        Assert.Contains("Would move page: Guide.md -> Moved.md", output.ToString());
    }

    [UnicodeNormalizationEquivalentFileSystemFact]
    public void RenamesPageWhenCaseAndUnicodeNormalizationDiffer()
    {
        var source = "u\u0308ber.md";
        var target = "\u00dcber.md";
        Write(source, "# Guide\n");
        Write("README.md", $"# Docs\n\n[Guide]({source})\n");

        Assert.Equal(0, Move(source, target).Exit);

        var movedPath = Assert.Single(Directory.EnumerateFiles(_root)
            .Where(path => Path.GetFileName(path).Normalize(NormalizationForm.FormC) == target));
        Assert.Equal("# Guide\n", File.ReadAllText(movedPath));
        Assert.Equal($"# Docs\n\n[Guide]({Uri.EscapeDataString(target)})\n", Read("README.md"));
    }

    [UnicodeNormalizationEquivalentFileSystemFact]
    public void RenamesDirectoryWhenCaseAndUnicodeNormalizationDiffer()
    {
        var source = "u\u0308ber";
        var target = "\u00dcber";
        Write($"{source}/README.md", "# Guide\n");
        Write("README.md", $"# Docs\n\n[Guide]({source}/)\n");

        Assert.Equal(0, Move(source, target).Exit);

        var movedDirectory = Assert.Single(Directory.EnumerateDirectories(_root)
            .Where(path => Path.GetFileName(path).Normalize(NormalizationForm.FormC) == target));
        Assert.Equal("# Guide\n", File.ReadAllText(Path.Combine(movedDirectory, "README.md")));
        Assert.Equal($"# Docs\n\n[Guide]({Uri.EscapeDataString(target)}/)\n", Read("README.md"));
    }

    [UnicodeNormalizationEquivalentFileSystemFact]
    public void RewritesOpenApiReferenceWhenDirectoryUnicodeNormalizationChanges()
    {
        var source = "u\u0308ber";
        var target = "\u00dcber";
        Write($"{source}/api/orders.yaml", "openapi: 3.1.0\n");
        Write("Reference.md", $"# Orders\n\n```openapi\n{source}/api/orders.yaml\n```\n");

        Assert.Equal(0, Move(source, target).Exit);

        Assert.Equal($"# Orders\n\n```openapi\n{target}/api/orders.yaml\n```\n", Read("Reference.md"));
    }

    [MacCaseSensitiveUnicodeEquivalentFileSystemFact]
    public void RefusesToConflateCaseDistinctUnicodeEquivalentFiles()
    {
        var source = "u\u0308ber.md";
        var target = "U\u0308ber.md";
        Write(source, "# Source\n");
        Write(target, "# Target\n");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PageMover.Plan(_root, Path.Combine(_root, source), Path.Combine(_root, target), includeHidden: true));

        Assert.Contains($"{target} already exists", exception.Message);
        Assert.Equal("# Source\n", Read(source));
        Assert.Equal("# Target\n", Read(target));
    }

    [MacCaseSensitiveUnicodeEquivalentFileSystemFact]
    public void RefusesMoveWhenUnicodeLinkAliasIsAmbiguous()
    {
        var source = "u\u0308ber.md";
        var sibling = "U\u0308ber.md";
        Write(source, "# Source\n");
        Write(sibling, "# Sibling\n");
        Write("README.md", "# Docs\n\n[Source](\u00fcber.md)\n");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PageMover.Plan(_root, Path.Combine(_root, source), Path.Combine(_root, "Moved.md"), includeHidden: true));

        Assert.Contains("Could not unambiguously resolve", exception.Message);
        Assert.Equal("# Source\n", Read(source));
        Assert.Equal("# Sibling\n", Read(sibling));
    }

    [MacCaseInsensitiveFileSystemFact]
    public void CaseOnlyRenameDoesNotEnumerateAncestorDirectories()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var restrictedParent = Path.Combine(_root, "restricted");
        var docs = Path.Combine(restrictedParent, "docs");
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "Guide.md"), "# Guide\n");

        try
        {
            File.SetUnixFileMode(restrictedParent, UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var output = new StringWriter();
            Assert.Equal(0, new PageCommand().Move(
                docs,
                Path.Combine(docs, "Guide.md"),
                Path.Combine(docs, "guide.md"),
                includeHidden: true,
                dryRun: true,
                output));
        }
        finally
        {
            File.SetUnixFileMode(restrictedParent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(restrictedParent, recursive: true);
        }
    }

    [WindowsFact]
    public void CaseOnlyRenameDoesNotResolveCaseDifferentAncestorsOutsideDocsRoot()
    {
        var parent = Path.Combine(_root, "ancestor");
        var docs = Path.Combine(parent, "docs");
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "Guide.md"), "# Guide\n");

        var output = new StringWriter();
        Assert.Equal(0, new PageCommand().Move(
            docs,
            Path.Combine(docs, "Guide.md"),
            Path.Combine(parent, "DOCS", "guide.md"),
            includeHidden: true,
            dryRun: true,
            output));
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
