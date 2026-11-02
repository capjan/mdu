using Markdown.Linter;
using Markdown.Linter.Rules.Rule3;

namespace Markdown.Linter.Test;

public class Rule3Test
{
    [Fact]
    public void CorrectCasingPasses()
    {
        WithSource((source, root) =>
        {
            File.WriteAllText(Path.Combine(root, "target.md"), "# Target");

            var result = new Rule3Validator().Validate(new Rule3Input(source, "target.md"));

            Assert.True(result.IsValid);
        });
    }

    [Fact]
    public void IncorrectCasingHasStableDiagnosticCode()
    {
        WithSource((source, root) =>
        {
            File.WriteAllText(Path.Combine(root, "target.md"), "# Target");

            var result = new Rule3Validator().Validate(new Rule3Input(source, "Target.md"));

            Assert.False(result.IsValid);
            Assert.Contains(RuleCodes.LinkCasing, result.ToString());
        });
    }

    [Fact]
    public void QueryAndFragmentDoNotAffectCasingCheck()
    {
        WithSource((source, root) =>
        {
            File.WriteAllText(Path.Combine(root, "target.md"), "# Target");

            var result = new Rule3Validator().Validate(new Rule3Input(source, "target.md?view=raw#section"));

            Assert.True(result.IsValid);
        });
    }

    [Fact]
    public void MissingTargetIsLeftToTheExistenceRule()
    {
        WithSource((source, _) =>
        {
            var result = new Rule3Validator().Validate(new Rule3Input(source, "missing.md"));

            Assert.True(result.IsValid);
        });
    }

    [Fact]
    public void ChecksRelativeParentAndEncodedPathSegments()
    {
        WithSource((source, root) =>
        {
            var folder = Directory.CreateDirectory(Path.Combine(root, "Folder Name"));
            File.WriteAllText(Path.Combine(folder.FullName, "Target.md"), "# Target");
            var nested = Directory.CreateDirectory(Path.Combine(root, "nested"));
            var nestedSource = Path.Combine(nested.FullName, "source.md");
            File.WriteAllText(nestedSource, "# Source");

            Assert.True(new Rule3Validator().Validate(new Rule3Input(nestedSource, "../Folder%20Name/Target.md")).IsValid);
            Assert.False(new Rule3Validator().Validate(new Rule3Input(nestedSource, "../folder%20name/Target.md")).IsValid);
        });
    }

    private static void WithSource(Action<string, string> action)
    {
        var root = CreateDirectory();
        try
        {
            var source = Path.Combine(root, "source.md");
            File.WriteAllText(source, "# Source");
            action(source, root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
