using StaticCode.Diagrams.Flowchart;
using StaticCode.Diagrams.State;

namespace StaticCode.Diagrams.Test;

public class StateParserTest
{
    [Theory]
    [InlineData("stateDiagram")]
    [InlineData("stateDiagram-v2")]
    public void RecognizesHeadersAfterCommentsAndBlankLines(string header)
    {
        var source = $"\r\n%% Example\r\n{header}; %% Header\r\n[*] --> Idle\r\nIdle --> [*]";
        Assert.True(StateParser.CanParse(source));
        var diagram = StateParser.Parse(source);
        Assert.Equal(new[] { StateKind.Start, StateKind.State, StateKind.End }, diagram.Nodes.Select(node => node.Kind));
        Assert.Equal(2, diagram.Edges.Count);
    }

    [Theory]
    [InlineData("TD", FlowDirection.TopToBottom)]
    [InlineData("TB", FlowDirection.TopToBottom)]
    [InlineData("BT", FlowDirection.BottomToTop)]
    [InlineData("LR", FlowDirection.LeftToRight)]
    [InlineData("RL", FlowDirection.RightToLeft)]
    public void ParsesDirections(string direction, FlowDirection expected)
    {
        Assert.Equal(expected, StateParser.Parse($"stateDiagram-v2\ndirection {direction}\nA --> B").Direction);
    }

    [Fact]
    public void ParsesImplicitStatesAliasesDescriptionsAndTransitionLabels()
    {
        var diagram = StateParser.Parse("""
            stateDiagram-v2
            accTitle: Order lifecycle
            Idle-->In-Progress: start
            state "Work in progress" as In-Progress
            Idle: Waiting<br>for #quot;work#quot;
            Idle: It's ready %% comment
            In-Progress --> In-Progress : retry
            In-Progress --> Idle
            """);

        Assert.Equal("Order lifecycle", diagram.Title);
        Assert.Equal(2, diagram.Nodes.Count);
        Assert.Equal("Waiting\nfor \"work\"\nIt's ready", diagram.Nodes[0].Label);
        Assert.Equal("Work in progress", diagram.Nodes[1].Label);
        Assert.Equal(new[] { "start", "retry", null }, diagram.Edges.Select(edge => edge.Label));
        Assert.Equal("In-Progress", diagram.Edges[0].To);
        Assert.Equal(diagram.Edges[1].From, diagram.Edges[1].To);
    }

    [Fact]
    public void ParsesNestedCompositesWithSeparateStartEndStatesAndDirections()
    {
        var diagram = StateParser.Parse("""
            stateDiagram-v2
            direction LR
            [*] --> Active
            state "Active session" as Active {
                direction BT
                [*] --> Working
                state Working {
                    [*] --> Busy
                    Busy --> [*]
                }
                Working --> [*]
            }
            Active --> [*]
            """);
        var active = diagram.Nodes.Single(node => node.Id == "Active");
        Assert.Equal("Active session", active.Label);
        Assert.Equal(FlowDirection.BottomToTop, active.Content!.Direction);
        var working = active.Content.Nodes.Single(node => node.Id == "Working");
        Assert.Equal(FlowDirection.TopToBottom, working.Content!.Direction);
        foreach (var scope in new[] { diagram, active.Content, working.Content })
        {
            Assert.Single(scope.Nodes.Where(node => node.Kind == StateKind.Start));
            Assert.Single(scope.Nodes.Where(node => node.Kind == StateKind.End));
            Assert.Equal(2, scope.Edges.Count);
        }
    }

    [Fact]
    public void PreservesAccessibleDescriptionsAtTheirDiagramScope()
    {
        var diagram = StateParser.Parse("""
            stateDiagram-v2
            accDescr:  Drafts & reviews #quot;described#quot; %% Comment
            state Review {
                accDescr: Reviewers approve or reject a document.
                Pending
            }
            """);

        Assert.Equal("Drafts & reviews \"described\"", diagram.Description);
        Assert.Equal("Reviewers approve or reject a document.", Assert.Single(diagram.Nodes).Content!.Description);
    }

    [Fact]
    public void ParsesChoiceForkJoinAndIgnoresThemeOverrides()
    {
        var diagram = StateParser.Parse("""
            stateDiagram-v2
            state Decision <<choice>>
            state Split <<fork>>
            state Merge <<join>>
            Split --> Decision:::custom : check
            Decision --> Merge: [valid]
            classDef custom fill:red
            class Decision custom
            style Split fill:blue
            accDescr: An accessible diagram description
            """);
        Assert.Equal(new[] { StateKind.Choice, StateKind.Fork, StateKind.Join }, diagram.Nodes.Select(node => node.Kind));
        Assert.Equal("[valid]", diagram.Edges[1].Label);
        Assert.Equal("Decision", diagram.Nodes[0].Label);
        Assert.Equal("A", Assert.Single(StateParser.Parse("stateDiagram-v2\nA:::custom").Nodes).Label);
    }

    [Fact]
    public void ResolvesLinksDeclaredBeforeStatesAndInsideComposites()
    {
        var diagram = StateParser.Parse("""
            stateDiagram-v2
            click Outer href "guide.md#state" "Tooltip"
            state Outer {
                click Inner 'https://example.com/?a=1&b=2'
                Inner
            }
            """);
        var outer = Assert.Single(diagram.Nodes);
        Assert.Equal("guide.md#state", outer.Href);
        Assert.Equal("https://example.com/?a=1&b=2", Assert.Single(outer.Content!.Nodes).Href);
    }

    [Theory]
    [InlineData("details.md")]
    [InlineData("../guide.md#details")]
    [InlineData("https://example.com/?a=1&b=2")]
    public void ParsesUnquotedHrefDestinations(string destination)
    {
        var diagram = StateParser.Parse($"stateDiagram-v2\nA\nclick A href {destination}; %% link");
        Assert.Equal(destination, Assert.Single(diagram.Nodes).Href);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" {\nPending\n}")]
    public void AppendsDescriptionsToExplicitDisplayNames(string content)
    {
        var diagram = StateParser.Parse($"stateDiagram-v2\nstate \"In review\" as Review{content}\nReview: Waiting for approval\nReview: Please respond");
        Assert.Equal("In review\nWaiting for approval\nPlease respond", Assert.Single(diagram.Nodes).Label);
    }

    [Theory]
    [InlineData("stateDiagram-v3\nA", 1)]
    [InlineData("stateDiagram-v2\nA -->", 2)]
    [InlineData("stateDiagram-v2\nA --> B --> C", 2)]
    [InlineData("stateDiagram-v2\ndirection sideways", 2)]
    [InlineData("stateDiagram-v2\nstate A <<history>>", 2)]
    [InlineData("stateDiagram-v2\n}", 2)]
    [InlineData("stateDiagram-v2\nstate A {\nB", 4)]
    [InlineData("stateDiagram-v2\nstate A {\nB\n}\nB --> C", 5)]
    [InlineData("stateDiagram-v2\nstate A {\nA --> B\n}", 3)]
    [InlineData("stateDiagram-v2\nstate A <<choice>> {\nB\n}", 2)]
    [InlineData("stateDiagram-v2\nstate A {\n}\nstate A {\n}", 4)]
    [InlineData("stateDiagram-v2\nclick Missing href \"guide.md\"", 2)]
    [InlineData("stateDiagram-v2\nclick A callback", 2)]
    [InlineData("stateDiagram-v2\nA\nnote right of A: Note", 3)]
    [InlineData("stateDiagram-v2\nstate A {\nB\n--\nC\n}", 4)]
    [InlineData("stateDiagram-v2\naccDescr {\nDescription\n}", 2)]
    public void RejectsMalformedAndUnsupportedStatementsWithSourceLine(string source, int line)
    {
        Assert.Equal(line, Assert.Throws<DiagramSyntaxException>(() => StateParser.Parse(source)).Line);
        Assert.False(MermaidRenderer.TryRenderSvg(source, out var svg));
        Assert.Null(svg);
    }
}
