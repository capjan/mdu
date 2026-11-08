using Mdu.Commands;

namespace Mdu.Test;

public sealed class CheckSiteCommandTest : IDisposable
{
    private readonly DirectoryInfo _fixture = Directory.CreateTempSubdirectory();
    private DirectoryInfo Site => _fixture.CreateSubdirectory("site");

    [Fact]
    public void AcceptsEncodedLinksWithQueriesAndStaticAnchors()
    {
        var site = Site;
        var guide = site.CreateSubdirectory("user guide");
        File.WriteAllText(Path.Combine(guide.FullName, "index.html"), """<h1 id="café">Guide</h1>""");
        WriteIndex(site, """<a href="user%20guide/?mode=print#caf%C3%A9">Guide</a><a href="mailto:docs@example.com">Contact</a>""");

        Assert.Equal(0, CheckSiteCommand.Execute(site, []));
    }

    [Theory]
    [InlineData("../outside.js")]
    [InlineData("%2e%2e/outside.js")]
    [InlineData("missing.js")]
    [InlineData("docs%2Fguide.html")]
    [InlineData("docs%5Cguide.html")]
    [InlineData("Docs/guide.html")]
    [InlineData("docs/guide.html/.")]
    [InlineData("docs/guide.html/child/..")]
    [InlineData("docs/guide.html/%2e?mode=print")]
    [InlineData("#missing")]
    public void RejectsBrokenReferences(string reference)
    {
        var site = Site;
        File.WriteAllText(Path.Combine(_fixture.FullName, "outside.js"), "");
        var docs = site.CreateSubdirectory("docs");
        File.WriteAllText(Path.Combine(docs.FullName, "guide.html"), "");
        WriteIndex(site, $"""<a href="{reference}">Link</a>""");

        Assert.Equal(1, CheckSiteCommand.Execute(site, []));
    }

    [Theory]
    [InlineData("#intro%3A~%3Aexample", "intro:~:example", 0)]
    [InlineData("#intro%3A~%3Aexample", "intro", 1)]
    [InlineData("#intro:~:text=example", "intro", 0)]
    public void DistinguishesEncodedAnchorIdsFromTextFragments(string reference, string id, int expected)
    {
        var site = Site;
        WriteIndex(site, $"""<h1 id="{id}">Intro</h1><a href="{reference}">Link</a>""");

        Assert.Equal(expected, CheckSiteCommand.Execute(site, []));
    }

    [Fact]
    public void ResolvesReferencesAgainstTheFirstBaseHref()
    {
        var site = Site;
        var nested = site.CreateSubdirectory("nested");
        var shared = site.CreateSubdirectory("shared");
        WriteIndex(nested, """<base href="/shared/"><base href="/nested/"><a href="guide.html#intro">Guide</a>""");
        File.WriteAllText(Path.Combine(nested.FullName, "guide.html"), """<h1 id="intro">Guide</h1>""");

        Assert.Equal(1, CheckSiteCommand.Execute(site, []));

        File.WriteAllText(Path.Combine(shared.FullName, "guide.html"), """<h1 id="intro">Guide</h1>""");
        Assert.Equal(0, CheckSiteCommand.Execute(site, []));
    }

    [Fact]
    public void ChecksAssetsEvenWhenDynamicAnchorsAreAllowed()
    {
        var site = Site;
        var reference = site.CreateSubdirectory("reference");
        var generated = Path.Combine(reference.FullName, "index.html");
        WriteIndex(site, """<a href="reference/#generated">API</a>""");
        File.WriteAllText(generated, """<script src="missing.js"></script>""");

        Assert.Equal(1, CheckSiteCommand.Execute(site, ["reference/index.html"]));

        File.WriteAllText(Path.Combine(reference.FullName, "missing.js"), "");
        Assert.Equal(0, CheckSiteCommand.Execute(site, ["reference/index.html"]));
    }

    [Fact]
    public void RejectsAnAssetSymlinkOutsideTheSite()
    {
        if (OperatingSystem.IsWindows())
            return; // Creating symlinks requires additional privileges on Windows.

        var site = Site;
        var outside = Path.Combine(_fixture.FullName, "outside.js");
        File.WriteAllText(outside, "");
        File.CreateSymbolicLink(Path.Combine(site.FullName, "linked.js"), outside);
        WriteIndex(site, """<script src="linked.js"></script>""");

        Assert.Equal(1, CheckSiteCommand.Execute(site, []));
    }

    private static void WriteIndex(DirectoryInfo site, string html) =>
        File.WriteAllText(Path.Combine(site.FullName, "index.html"), html);

    public void Dispose() => _fixture.Delete(recursive: true);
}
