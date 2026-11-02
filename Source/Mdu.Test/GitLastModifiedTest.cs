using System.Diagnostics;
using Mdu.Service;

namespace Mdu.Test;

public class GitLastModifiedTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewDatesRefreshForCommitsPackedRefsAndDetachedHeads(bool linkedWorktree)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-git-cache-{Guid.NewGuid():N}");
        var repository = Path.Combine(root, "repository");
        Directory.CreateDirectory(Path.Combine(repository, "Docs"));
        try
        {
            Git(repository, null, "init", "--quiet");
            File.WriteAllText(Path.Combine(repository, "Docs/README.md"), "# Home");
            Git(repository, null, "add", ".");
            Git(repository, "2026-01-02T10:00:00Z", "commit", "--quiet", "-m", "chore: first");
            var checkout = repository;
            if (linkedWorktree)
            {
                checkout = Path.Combine(root, "linked");
                Git(repository, null, "worktree", "add", "--quiet", "-b", "preview", checkout);
            }
            var docs = Path.Combine(checkout, "Docs");
            var cache = new GitLastModifiedCache();
            var first = cache.Read(docs);
            File.AppendAllText(Path.Combine(docs, "README.md"), "\nUncommitted");
            Assert.Same(first, cache.Read(docs));
            Git(checkout, "2026-03-04T10:00:00Z", "commit", "--quiet", "-am", "chore: second");
            Git(checkout, null, "pack-refs", "--all", "--prune");
            var second = cache.Read(docs);
            Assert.Equal(DateTimeOffset.Parse("2026-03-04T10:00:00Z"), second["README.md"]);
            Assert.Same(second, cache.Read(docs));
            File.AppendAllText(Path.Combine(docs, "README.md"), "\nThird");
            Git(checkout, "2026-05-06T10:00:00Z", "commit", "--quiet", "-am", "chore: third");
            Assert.Equal(DateTimeOffset.Parse("2026-05-06T10:00:00Z"), cache.Read(docs)["README.md"]);
            Git(checkout, null, "checkout", "--quiet", "--detach", "HEAD~1");
            Assert.Equal(second["README.md"], cache.Read(docs)["README.md"]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PreviewDatesRefreshWhenAShallowCloneBecomesComplete()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-git-shallow-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        try
        {
            Git(source, null, "init", "--quiet");
            File.WriteAllText(Path.Combine(source, "README.md"), "# Home");
            Git(source, null, "add", ".");
            Git(source, "2026-01-02T10:00:00Z", "commit", "--quiet", "-m", "chore: first");
            File.AppendAllText(Path.Combine(source, "README.md"), "\nSecond");
            Git(source, "2026-03-04T10:00:00Z", "commit", "--quiet", "-am", "chore: second");
            var clone = Path.Combine(root, "clone");
            Git(root, null, "clone", "--quiet", "--depth=1", new Uri(source).AbsoluteUri, clone);
            var cache = new GitLastModifiedCache();
            Assert.Empty(cache.Read(clone));
            Git(clone, null, "fetch", "--quiet", "--unshallow");
            Assert.NotEmpty(cache.Read(clone));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ReadsTheLastCommitDateOfEachFileBelowTheFolder()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"mdu-git-test-{Guid.NewGuid():N}");
        var docsPath = Path.Combine(rootPath, "Docs");
        Directory.CreateDirectory(Path.Combine(docsPath, "Guide"));
        try
        {
            Git(rootPath, null, "init", "--quiet");
            File.WriteAllText(Path.Combine(docsPath, "README.md"), "# Home\n");
            File.WriteAllText(Path.Combine(docsPath, "Guide", "Setup.md"), "# Setup\n");
            File.WriteAllText(Path.Combine(rootPath, "outside.md"), "# Outside\n");
            Git(rootPath, "2026-01-02T10:00:00+01:00", "add", ".");
            Git(rootPath, "2026-01-02T10:00:00+01:00", "commit", "--quiet", "-m", "first");
            File.AppendAllText(Path.Combine(docsPath, "Guide", "Setup.md"), "More\n");
            Git(rootPath, "2026-03-04T08:30:00+01:00", "commit", "--quiet", "-am", "second");

            var dates = GitLastModified.Read(docsPath);

            Assert.Equal(new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.FromHours(1)), dates["README.md"]);
            Assert.Equal(new DateTimeOffset(2026, 3, 4, 8, 30, 0, TimeSpan.FromHours(1)), dates["Guide/Setup.md"]);
            Assert.DoesNotContain(dates.Keys, key => key.Contains("outside", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public void ReturnsNoDatesOutsideAGitWorkTree()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"mdu-git-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        try
        {
            File.WriteAllText(Path.Combine(rootPath, "README.md"), "# Home\n");

            Assert.Empty(GitLastModified.Read(rootPath));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static void Git(string workingDirectory, string? date, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("user.name=MDU Test");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("user.email=mdu@example.com");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("commit.gpgsign=false");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.hooksPath=/dev/null");
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        if (date is not null)
        {
            startInfo.Environment["GIT_AUTHOR_DATE"] = date;
            startInfo.Environment["GIT_COMMITTER_DATE"] = date;
        }

        using var process = Process.Start(startInfo)!;
        var error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
    }
}
