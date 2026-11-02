using Mdu.Performance;
using Xunit;

namespace Development.Test;

public sealed class CorporaTest
{
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
            Assert.Equal("ff1a499b59d5ff313993fedf5cb98509fee1f7b8007d65f01d1011cee3c9b2a8", result["large"]!["sha256"]!.GetValue<string>());
            Assert.Equal("16f69c248f928d9c578234b27f4b0aea019a93c3793d5b280e26b317b533c1a2", result["diagram-36"]!["sha256"]!.GetValue<string>());
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
