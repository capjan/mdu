using Mdu.Performance;
using Xunit;

namespace Development.Test;

public sealed class CorporaTest
{
    [Theory]
    [InlineData("site.name: Baseline\n", "site.name: Edited\n")]
    [InlineData("site:\n  name: MDU\n  logo: a.svg\n", "site:\n  name: Edited\n  logo: a.svg\n")]
    public void ConfigEditChangesTheSiteNameInBothForms(string configuration, string expected)
    {
        Assert.Equal(expected, Measurements.ChangeSiteName(configuration, "Edited"));
    }

    [Fact]
    public void FrozenDocumentationWithOldSettingsIsUpdated()
    {
        var root = Path.Combine(Path.GetTempPath(), "mdu-corpus-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Docs/Architecture"));
        File.WriteAllText(Path.Combine(root, "Docs/README.md"), "# Docs\n");
        File.WriteAllText(Path.Combine(root, "Docs/Architecture/README.md"), "# Architecture\n");
        File.WriteAllText(Path.Combine(root, "Docs/mdu.yml"), "site_name: MDU\nlogo:\n  light: a.svg\n");
        try
        {
            Corpora.Generate(new Options(root, Path.Combine(root, "work"), "generate", 1, 1, 1, ["small"], null));
            Assert.Equal("site.name: MDU\nsite.logo:\n  light: a.svg\n", File.ReadAllText(Path.Combine(root, "work/corpora/docs/mdu.yml")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void NativeGeneratorPreservesFrozenSyntheticInputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "mdu-corpus-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Docs/Architecture"));
        File.WriteAllText(Path.Combine(root, "Docs/README.md"), "# Docs\n");
        File.WriteAllText(Path.Combine(root, "Docs/Architecture/README.md"), "# Architecture\n");
        try
        {
            var options = new Options(root, Path.Combine(root, "work"), "generate", 1, 1, 1, ["small"], null);
            var result = Corpora.Generate(options);
            Assert.Equal(503, result["large"]!["pages"]!.GetValue<int>());
            // Differs from the ff1a499b… of the reports under Tools/Performance/results only by the renamed site.name in mdu.yml.
            Assert.Equal("05cd41aa08615e45ec554ef363219bd24891cfee6aa0cca100c9998749b15ce4", result["large"]!["sha256"]!.GetValue<string>());
            Assert.Equal("16f69c248f928d9c578234b27f4b0aea019a93c3793d5b280e26b317b533c1a2", result["diagram-36"]!["sha256"]!.GetValue<string>());
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
