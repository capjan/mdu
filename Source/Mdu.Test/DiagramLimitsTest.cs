using Mdu.Service;
using StaticCode.Diagrams;

namespace Mdu.Test;

[Collection("Process state")]
public class DiagramLimitsTest : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;
    private static readonly string Oversized = string.Join('\n', Enumerable.Repeat("[A] --> [B]", 2001));

    public void Dispose()
    {
        DiagramRenderer.UseComponentDiagramLimits();
        Directory.Delete(_root, true);
    }

    [Theory]
    [InlineData("diagrams:\n  maxLinks: 3000", true)]
    [InlineData("diagrams:\n  maxLinks: 0", true)]
    [InlineData("diagrams:\n  maxElements: 5000", false)]
    [InlineData("site_name: Docs", false)]
    public void MduYmlSetsTheLimits(string yaml, bool drawn)
    {
        File.WriteAllText(Path.Combine(_root, "mdu.yml"), yaml);

        SiteConfigurationReader.UseDiagramLimits(SiteConfigurationReader.Read(_root));

        Assert.Equal(drawn, DiagramRenderer.DescribePlantUmlOverLimit(Oversized) == null);
    }

    [Theory]
    [InlineData("many")]
    [InlineData("-1")]
    public void InvalidLimitsAreRejected(string value)
    {
        File.WriteAllText(Path.Combine(_root, "mdu.yml"), $"diagrams:\n  maxLinks: {value}");

        Assert.Throws<InvalidOperationException>(() => SiteConfigurationReader.Read(_root));
    }

    [Fact]
    public void LintKeepsTheDefaultsWhenTheFileIsInvalid()
    {
        File.WriteAllText(Path.Combine(_root, "mdu.yml"), "diagrams:\n  maxLinks: many");

        SiteConfigurationReader.TryUseDiagramLimits(_root);

        Assert.NotNull(DiagramRenderer.DescribePlantUmlOverLimit(Oversized));
    }
}
