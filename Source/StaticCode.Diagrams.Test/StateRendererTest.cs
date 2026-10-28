using System.Globalization;
using System.Xml.Linq;

namespace StaticCode.Diagrams.Test;

public class StateRendererTest
{
    private const string Sample = """
        stateDiagram-v2
        accTitle: Document lifecycle
        [*] --> Draft
        state "In review" as Review {
            [*] --> Pending
            Pending --> Approved: approve
            Pending --> Pending: request changes
            Approved --> [*]
        }
        Draft --> Review: submit
        Review --> Draft: revise
        Review --> Published: publish
        Published --> [*]
        click Draft href "../README.md#draft"
        click Review href "review.md"
        click Pending href "https://example.com/reviews"
        """;

    [Fact]
    public void ProducesDeterministicAccessibleThemeableSvgWithoutRuntimeDependencies()
    {
        var svg = MermaidRenderer.RenderSvg(Sample);
        var root = XElement.Parse(svg);
        Assert.Equal("diagram-state", root.Attribute("class")?.Value);
        Assert.Equal("group", root.Attribute("role")?.Value);
        Assert.Equal("Document lifecycle", root.Attribute("aria-label")?.Value);
        Assert.Equal(svg, MermaidRenderer.RenderSvg(Sample));
        Assert.DoesNotMatch(@"\b(?:fill|stroke|style)=", svg);
        Assert.DoesNotContain("<script", svg);
        Assert.DoesNotContain("foreignObject", svg);
        Assert.Contains("diagram-node-rounded", svg);
        Assert.Contains("diagram-container-shape", svg);
        Assert.Contains("diagram-edge-label-background", svg);
        Assert.Equal("State diagram", XElement.Parse(MermaidRenderer.RenderSvg("stateDiagram\nA")).Attribute("aria-label")?.Value);
    }

    [Theory]
    [InlineData("TD", 1, false)]
    [InlineData("BT", -1, false)]
    [InlineData("LR", 1, true)]
    [InlineData("RL", -1, true)]
    public void LaysOutStatesInTheRequestedDirectionWithSeparatedBounds(string direction, int sign, bool horizontal)
    {
        var root = XElement.Parse(MermaidRenderer.RenderSvg($"stateDiagram-v2\ndirection {direction}\nA --> B: next\nB --> C\nC --> A: restart"));
        var a = Bounds(Node(root, "A"));
        var b = Bounds(Node(root, "B"));
        var c = Bounds(Node(root, "C"));
        Assert.True(sign * (horizontal ? b.X - a.X : b.Y - a.Y) > 0);
        Assert.True(sign * (horizontal ? c.X - b.X : c.Y - b.Y) > 0);
        Assert.False(Overlaps(a, b));
        Assert.False(Overlaps(b, c));
        Assert.Equal(3, WithClass(root, "diagram-marker").Count());
        AssertInsideViewBox(root);
    }

    [Fact]
    public void SizesNestedFramesAroundTheirChildrenAndKeepsSiblingStatesOutside()
    {
        var root = XElement.Parse(MermaidRenderer.RenderSvg("""
            stateDiagram-v2
            state "Outer composite with a long title that wraps over multiple lines" as Outer {
                direction LR
                state Inner {
                    [*] --> A
                    A --> B
                    B --> [*]
                }
                Inner --> C
            }
            Outer --> D
            """));
        var frames = WithClass(root, "diagram-state-composite").ToArray();
        Assert.Equal(2, frames.Length);
        foreach (var frame in frames)
        {
            var bounds = Bounds(frame.Elements().First(element => HasClass(element, "diagram-container-shape")));
            foreach (var shape in frame.Descendants().Where(element => HasClass(element, "diagram-node-shape")))
            {
                var child = Bounds(shape);
                Assert.True(child.X >= bounds.X + 17);
                Assert.True(child.Y >= bounds.Y + 36);
                Assert.True(child.X + child.Width <= bounds.X + bounds.Width - 17);
                Assert.True(child.Y + child.Height <= bounds.Y + bounds.Height - 17);
            }
        }
        Assert.False(Overlaps(Bounds(frames[0].Elements().First()), Bounds(Node(root, "D"))));
        AssertInsideViewBox(root);
    }

    [Theory]
    [InlineData("TD")]
    [InlineData("BT")]
    [InlineData("LR")]
    [InlineData("RL")]
    public void ReservesSpaceForMultipleLabelledSelfTransitions(string direction)
    {
        var root = XElement.Parse(MermaidRenderer.RenderSvg($"stateDiagram-v2\ndirection {direction}\nA --> A: Retry the current operation\nA --> A: wait\nA --> B"));
        Assert.Equal(3, WithClass(root, "diagram-edge").Count());
        Assert.Equal(3, WithClass(root, "diagram-marker").Count());
        Assert.Equal(2, WithClass(root, "diagram-edge-label").Count());
        AssertInsideViewBox(root);
        var labels = WithClass(root, "diagram-edge-label-background").ToArray();
        foreach (var label in labels)
            Assert.False(Overlaps(Bounds(Node(root, "A")), Bounds(label)));
        Assert.False(Overlaps(Bounds(labels[0]), Bounds(labels[1])));
    }

    [Theory]
    [InlineData("TB", 56, 6)]
    [InlineData("LR", 6, 56)]
    public void DrawsStartEndChoiceAndForkJoinSymbols(string direction, double width, double height)
    {
        var root = XElement.Parse(MermaidRenderer.RenderSvg($"""
            stateDiagram-v2
            direction {direction}
            [*] --> Fork
            state Fork <<fork>>
            state Choice <<choice>>
            state Join <<join>>
            Fork --> A
            Fork --> Choice
            Choice --> B: yes
            Choice --> A: no
            A --> Join
            B --> Join
            Join --> [*]
            """));
        var start = Assert.Single(WithClass(root, "diagram-state-start"));
        var end = Assert.Single(WithClass(root, "diagram-state-end"));
        Assert.Single(start.Descendants().Where(element => element.Name.LocalName == "circle"));
        Assert.Equal(2, end.Descendants().Count(element => element.Name.LocalName == "circle"));
        Assert.Single(WithClass(root, "diagram-node-diamond"));
        foreach (var kind in new[] { "fork", "join" })
        {
            var shape = Assert.Single(WithClass(root, "diagram-state-" + kind)).Elements().Single(element => element.Name.LocalName == "rect");
            Assert.Equal(width, Number(shape, "width"));
            Assert.Equal(height, Number(shape, "height"));
        }
        AssertInsideViewBox(root);
    }

    [Fact]
    public void WritesLinksWithAbsolutePdfBoundsAndWithoutNestedAnchors()
    {
        var root = XElement.Parse(MermaidRenderer.RenderSvg(Sample));
        var links = WithClass(root, "diagram-link").ToArray();
        Assert.Equal(3, links.Length);
        Assert.Equal(new[] { "../index.html#draft", "review.html", "https://example.com/reviews" }, links.Select(link => link.Attribute("href")?.Value));
        Assert.All(links, link =>
        {
            Assert.NotNull(link.Attribute("data-pdf-bounds"));
            Assert.DoesNotContain(link.Descendants(), element => element.Name.LocalName == "a");
        });
    }

    [Theory]
    [InlineData("guide.md?q=1&b=2#state", "guide.html?q=1&b=2#state")]
    [InlineData("../README.md#state", "../index.html#state")]
    [InlineData("https://example.com/page.md", "https://example.com/page.md")]
    [InlineData("mailto:team@example.com", "mailto:team@example.com")]
    [InlineData("#state", "#state")]
    public void UsesSharedSafeLinkRewritingForOrdinaryCompositeAndChoiceStates(string url, string expected)
    {
        var root = XElement.Parse(MermaidRenderer.RenderSvg($$"""
            stateDiagram-v2
            A
            state B {
                C
            }
            state D <<choice>>
            click A href "{{url}}"
            click B href "{{url}}"
            click D href "{{url}}"
            """));
        var links = WithClass(root, "diagram-link").ToArray();
        Assert.Equal(3, links.Length);
        Assert.All(links, link => Assert.Equal(expected, link.Attribute("href")?.Value));
        Assert.Equal("D", links[2].Attribute("aria-label")?.Value);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:evil")]
    [InlineData("file:///tmp/file")]
    public void OmitsUnsafeLinks(string url)
    {
        var svg = MermaidRenderer.RenderSvg($"stateDiagram-v2\nA\nstate B {{\nC\n}}\nstate D <<choice>>\nclick A href \"{url}\"\nclick B href \"{url}\"\nclick D href \"{url}\"");
        Assert.DoesNotContain("<a ", svg);
        Assert.DoesNotContain(url, svg);
    }

    [Fact]
    public void EscapesTitlesLabelsAndUrlsAndWrapsLongText()
    {
        var root = XElement.Parse(MermaidRenderer.RenderSvg("""
            ---
            title: ignored front matter
            ---
            stateDiagram-v2
            accTitle: A < B & C
            state "<script>alert(1)</script> & label that is long enough to wrap onto several lines" as A
            A --> B: <img src=x onerror=evil> & more
            click A href "https://example.com/?a=1&b=2"
            """));
        Assert.Equal("A < B & C", root.Attribute("aria-label")?.Value);
        Assert.DoesNotContain(root.Descendants(), element => element.Name.LocalName is "script" or "img");
        Assert.True(root.Descendants().Count(element => element.Name.LocalName == "tspan") >= 2);
        Assert.Equal("https://example.com/?a=1&b=2", Assert.Single(WithClass(root, "diagram-link")).Attribute("href")?.Value);
    }

    private static IEnumerable<XElement> WithClass(XElement root, string cssClass) => root.Descendants().Where(element => HasClass(element, cssClass));
    private static bool HasClass(XElement element, string cssClass) => element.Attribute("class")?.Value.Split(' ').Contains(cssClass) == true;
    private static double Number(XElement element, string name) => double.Parse(element.Attribute(name)!.Value, CultureInfo.InvariantCulture);
    private static XElement Node(XElement root, string label) => root.Descendants().Single(element =>
        HasClass(element, "diagram-node-text") && element.Value == label).Parent!.Elements().First();

    private static (double X, double Y, double Width, double Height) Bounds(XElement shape) => shape.Name.LocalName == "circle"
        ? (Number(shape, "cx") - Number(shape, "r"), Number(shape, "cy") - Number(shape, "r"), Number(shape, "r") * 2, Number(shape, "r") * 2)
        : (Number(shape, "x"), Number(shape, "y"), Number(shape, "width"), Number(shape, "height"));

    private static bool Overlaps((double X, double Y, double Width, double Height) a, (double X, double Y, double Width, double Height) b) =>
        a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;

    private static void AssertInsideViewBox(XElement root)
    {
        var viewBox = root.Attribute("viewBox")!.Value.Split(' ').Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        foreach (var shape in root.Descendants().Where(element => element.Name.LocalName is "rect" or "circle"))
        {
            var bounds = Bounds(shape);
            Assert.True(bounds.X >= viewBox[0] && bounds.Y >= viewBox[1]);
            Assert.True(bounds.X + bounds.Width <= viewBox[0] + viewBox[2] + 0.02);
            Assert.True(bounds.Y + bounds.Height <= viewBox[1] + viewBox[3] + 0.02);
        }
    }
}
