using System.Text.RegularExpressions;
using System.Xml.Linq;
using StaticCode.Diagrams.Isometric;

namespace StaticCode.Diagrams.Test;

public class IsometricDiagramTest
{
    private const string Sample = """
        accTitle: Platform overview
        zone cloud "Cloud" "Compute" tone blue at 1,0
          api "API" icon material-server
          db  "Database" icon material-database shape tall tone teal
        end
        zone office "Office" tone purple at 0,0
          files "Files" icon material-folder
        end
        user "User" shape disc at 1,1

        phase Install
          api --> db
        phase Sync
          files -.-> db : Diff
        phase Query
          user ==> api : Request
        """;

    [Fact]
    public void ParsesZonesNodesAndPhases()
    {
        var diagram = IsometricParser.Parse(Sample);

        Assert.Equal("Platform overview", diagram.Title);
        Assert.Equal(new[] { "cloud", "office" }, diagram.Zones.Select(zone => zone.Id));
        Assert.Equal(new[] { "api", "db", "files", "user" }, diagram.Nodes.Select(node => node.Id));
        Assert.Equal(new[] { "Install", "Sync", "Query" }, diagram.Phases);

        var cloud = diagram.Zones[0];
        Assert.Equal("Cloud", cloud.Title);
        Assert.Equal("Compute", cloud.Subtitle);
        Assert.Equal(new GridCell(1, 0), cloud.Cell);

        var db = diagram.Nodes.Single(node => node.Id == "db");
        Assert.Equal(IsometricShape.Tall, db.Shape);
        Assert.Equal("teal", db.Tone);
        Assert.Same(cloud, db.Zone);
        Assert.Equal(IsometricShape.Disc, diagram.Nodes.Single(node => node.Id == "user").Shape);
    }

    [Fact]
    public void NodesAppearInTheFirstPhaseThatLinksThem()
    {
        var diagram = IsometricParser.Parse(Sample);

        Assert.Equal(new[] { 1, 1, 2, 3 }, diagram.Nodes.Select(node => node.Phase));
        Assert.Equal(1, diagram.Zones[0].Phase);
        Assert.Equal(2, diagram.Zones[1].Phase);
        Assert.Equal(new[] { 1, 2, 3 }, diagram.Edges.Select(edge => edge.Phase));
        Assert.Equal(IsometricEdgeStyle.Dashed, diagram.Edges[1].Style);
        Assert.Equal("Request", diagram.Edges[2].Label);
    }

    [Fact]
    public void ExpandsLinkListsAndCreatesUndeclaredNodes()
    {
        var diagram = IsometricParser.Parse("a, b --> c, d : Sync");

        Assert.Equal(4, diagram.Edges.Count);
        Assert.Equal(new[] { "a", "b", "c", "d" }, diagram.Nodes.Select(node => node.Id).Order());
        Assert.All(diagram.Edges, edge => Assert.Equal("Sync", edge.Label));
        Assert.Empty(diagram.Phases);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a --> a")]
    [InlineData("zone z\na")]
    [InlineData("end")]
    [InlineData("zone a\nzone b\nend")]
    [InlineData("a \"A\" shape cube")]
    [InlineData("a \"A\" tone pink")]
    [InlineData("a \"A\" at 1")]
    [InlineData("a \"A\" icon")]
    [InlineData("a\na")]
    [InlineData("zone z\nphase One\nend")]
    [InlineData("zone z\nend\nz --> b")]
    [InlineData("a \"unterminated")]
    public void RejectsInvalidDiagrams(string source)
    {
        Assert.Throws<DiagramSyntaxException>(() => IsometricParser.Parse(source));
    }

    [Fact]
    public void RejectsMoreThanNinePhases()
    {
        var source = string.Join('\n', Enumerable.Range(1, 10).Select(index => $"phase P{index}\na{index} --> b{index}"));

        Assert.Throws<DiagramSyntaxException>(() => IsometricParser.Parse(source));
    }

    [Fact]
    public void RendersPhaseControlsAndPhasedSvg()
    {
        Assert.True(DiagramRenderer.TryRenderSvg("isometric", Sample, out var html));

        var root = XElement.Parse(html!);
        Assert.Equal("iso", root.Attribute("class")?.Value);
        var inputs = root.Descendants("input").ToList();
        Assert.Equal(new[] { "1", "2", "3" }, inputs.Select(input => input.Attribute("data-phase")?.Value));
        Assert.Equal(new[] { false, false, true }, inputs.Select(input => input.Attribute("checked") != null));
        Assert.Single(inputs.Select(input => input.Attribute("name")?.Value).Distinct());

        XNamespace svg = "http://www.w3.org/2000/svg";
        var drawing = root.Element(svg + "svg")!;
        Assert.Equal("Platform overview", drawing.Attribute("aria-label")?.Value);
        Assert.Equal(2, drawing.Descendants().Count(element => element.Attribute("class")?.Value.Contains("iso-zone ") == true));
        Assert.Equal(4, drawing.Descendants().Count(element => element.Attribute("class")?.Value.Contains("iso-node ") == true));
        Assert.Equal(3, drawing.Descendants(svg + "animateMotion").Select(motion => motion.Parent!.Parent!.Parent).Distinct().Count());
        Assert.Equal("Phase 2: Sync", inputs[1].Attribute("aria-label")?.Value);
        Assert.Contains("--iso-phase: 3", html);
        Assert.DoesNotContain("style=\"", Regex.Replace(drawing.ToString(), "style=\"--iso-phase: \\d\"", string.Empty));
    }

    [Fact]
    public void LeavesOutControlsWithoutPhases()
    {
        Assert.True(DiagramRenderer.TryRenderSvg("isometric", "a --> b", out var html));

        Assert.DoesNotContain("iso-controls", html);
        Assert.Contains("<svg", html);
    }

    [Fact]
    public void RejectsUnknownIcons()
    {
        Assert.False(DiagramRenderer.TryRenderSvg("isometric", "a \"A\" icon material-does-not-exist-anywhere", out _));
    }

    [Fact]
    public void RendersTheSameOutputForTheSameSource()
    {
        Assert.Equal(DiagramRenderer.RenderIsometricHtml(Sample), DiagramRenderer.RenderIsometricHtml(Sample));
    }
}
