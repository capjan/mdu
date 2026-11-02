using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace StaticCode.Diagrams.Test;

public class ComponentRendererTest
{
    private const string Sample = """
        @startuml
        title Order service
        actor Customer
        package "Shop" {
          [Web Shop] <<frontend>> as Web
          component [Order Service] as Orders
        }
        node "Data" {
          database "Orders DB" as DB
          queue "Events" as Bus
        }
        cloud {
          [CDN]
        }
        Customer --> Web : browses
        Web --> Orders : REST
        Orders --> DB
        Orders -right-> Bus : publishes
        Orders ..|> IOrders
        [CDN] -up-> Web
        Web -[hidden]- [CDN]
        note right of DB : PostgreSQL
        artifact "app.jar" as Jar
        file "config.yml" as Config
        storage "S3" as Store
        folder "Logs" as Logs
        frame "Frame" as Frame
        Jar .> Config
        @enduml
        """;

    private const string Layered = """
        package "Top" {
          [Commands] as Cli
          [Page tree and visitors] as Graph
        }
        package "Middle" {
          [Document] as Doc
          [Linter]
          [Renderer]
          [Search]
        }
        package "Bottom" {
          [Highlighter]
          [Icons]
        }
        Cli --> Graph
        Graph --> Doc
        Graph --> Linter
        Graph --> Renderer
        Graph --> Search
        Search --> Renderer
        Renderer --> Highlighter
        Renderer --> Icons
        """;

    private const string Crowded = """
        actor User
        node "Edge" {
          [LB]
          [Cache]
        }
        frame "App" {
          [API] as A
          [Worker] as K
          [Mailer] as X
        }
        database "DB" as D
        queue Bus
        User --> [LB] : https
        [LB] --> [Cache]
        [LB] --> A
        [Cache] --> A : miss
        A --> D
        A --> Bus
        Bus --> K
        K --> D
        K --> X
        X --> User : mail
        A --> Edge
        """;

    private const string FlatDetour = """
        [A] -right-> [B] : hello
        [B] -right-> [C]
        [A] -right-> [C]
        """;

    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    [Fact]
    public void RendersWellFormedThemeableSvg()
    {
        var svg = DiagramRenderer.RenderPlantUmlSvg(Sample);

        var root = XElement.Parse(svg);
        Assert.Equal("diagram-component", root.Attribute("class")?.Value);
        Assert.Equal("Order service", root.Attribute("aria-label")?.Value);
        Assert.DoesNotMatch(new Regex(@"\b(fill|stroke|style)="), svg);
        Assert.DoesNotContain("#", svg);
    }

    [Fact]
    public void DrawsEveryElementWithItsShape()
    {
        var svg = DiagramRenderer.RenderPlantUmlSvg(Sample);

        foreach (var cssClass in new[]
                 {
                     "diagram-component-component", "diagram-component-interface", "diagram-component-actor",
                     "diagram-component-database", "diagram-component-queue", "diagram-component-note",
                     "diagram-component-artifact", "diagram-component-file", "diagram-component-storage",
                     "diagram-component-folder", "diagram-component-frame", "diagram-container-package",
                     "diagram-container-node", "diagram-container-cloud", "diagram-node-icon", "diagram-note-shape",
                     "diagram-marker-hollow", "diagram-edge-dotted", "diagram-component-title"
                 })
            Assert.Contains(cssClass, svg);
        Assert.Contains("«frontend»", svg);
    }

    [Fact]
    public void SkipsHiddenLinks()
    {
        var visible = Regex.Matches(DiagramRenderer.RenderPlantUmlSvg("[A] --> [B]\n[A] -[hidden]-> [C]"), "class=\"diagram-edge\"").Count;

        Assert.Equal(1, visible);
    }

    [Fact]
    public void PlacesContainerContentInsideTheContainer()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg("rectangle P {\n[A] --> [B]\n}\n[C] --> [A]"));

        var container = Bounds(root.Descendants(Svg + "g").Single(group => Class(group).Contains("diagram-container-rectangle")).Element(Svg + "rect")!);
        foreach (var name in new[] { "A", "B" })
        {
            var rect = Bounds(ElementShape(root, name));
            Assert.True(rect.Left > container.Left && rect.Right < container.Right && rect.Top > container.Top && rect.Bottom < container.Bottom, name);
        }

        var outside = Bounds(ElementShape(root, "C"));
        Assert.True(outside.Bottom < container.Top || outside.Right < container.Left || outside.Left > container.Right);
    }

    [Fact]
    public void KeepsFlatLinksInOneRow()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg("[Left] -> [Right]\n[Top] -left-> [Bottom]"));

        Assert.Equal(Bounds(ElementShape(root, "Left")).Top, Bounds(ElementShape(root, "Right")).Top, 1);
        Assert.True(Bounds(ElementShape(root, "Left")).Right < Bounds(ElementShape(root, "Right")).Left);
        Assert.True(Bounds(ElementShape(root, "Bottom")).Right < Bounds(ElementShape(root, "Top")).Left);
    }

    [Fact]
    public void PlacesUpLinksAboveTheirSource()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg("[Source] -up-> [Target]"));

        Assert.True(Bounds(ElementShape(root, "Target")).Bottom < Bounds(ElementShape(root, "Source")).Top);
    }

    [Theory]
    [InlineData("plantuml")]
    [InlineData("puml")]
    public void RoutesPlantUmlFences(string language)
    {
        Assert.True(DiagramRenderer.TryRenderSvg(language, "@startuml\n[A] --> [B]\n@enduml", out var svg));
        Assert.Contains("diagram-component", svg);
        Assert.False(DiagramRenderer.TryRenderSvg(language, "@startuml\nAlice -> Bob: Hi\n@enduml", out _));
    }

    [Fact]
    public void RejectsLinksIntoTheirOwnContainer()
    {
        Assert.Throws<DiagramSyntaxException>(() => DiagramRenderer.RenderPlantUmlSvg("package P {\n[A]\n}\nP --> [A]"));
    }

    [Fact]
    public void DrawsAFreeLinkBetweenContainersAsOneStraightLine()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg("package P {\n[A]\n}\npackage Q {\n[B]\n}\nA --> B"));

        var path = Assert.Single(EdgeRoutes(root));
        Assert.Equal(2, path.Count);
    }

    [Fact]
    public void LinesUpLinkedElementsAcrossContainers()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg("package P {\n[Alpha]\n[Beta]\n[Gamma]\n}\npackage Q {\n[Target]\n}\nGamma --> Target"));

        var source = Bounds(ElementShape(root, "Gamma"));
        var target = Bounds(ElementShape(root, "Target"));
        Assert.True(target.Left < source.Right && target.Right > source.Left, $"{source} and {target}");
    }

    [Fact]
    public void KeepsAChainInOneColumnBesideALongLink()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg("package P {\n[A]\n[B]\n[C]\nA --> B\nB --> C\nA --> C\n}"));

        var upper = Bounds(ElementShape(root, "B"));
        var lower = Bounds(ElementShape(root, "C"));
        Assert.True(Math.Abs((upper.Left + upper.Right) / 2 - (lower.Left + lower.Right) / 2) < 15, $"{upper} and {lower}");
    }

    [Fact]
    public void LinksInACycleDockOnTheFacingSide()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg("package P {\n[A]\n[B]\n}\n[C]\nA --> C\nC --> B"));

        var target = Bounds(ElementShape(root, "B"));
        var below = Bounds(ElementShape(root, "C"));
        Assert.True(below.Top > target.Bottom);
        Assert.True(EdgeRoutes(root)[1][^1].Y > target.Bottom, $"{EdgeRoutes(root)[1][^1]} and {target}");
    }

    [Fact]
    public void LinksDoNotOvershootTheirDirection()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg(Layered));

        foreach (var route in EdgeRoutes(root))
            for (var index = 1; index < route.Count; index++)
                Assert.True(route[index].Y >= route[index - 1].Y - 0.5, $"{route[index - 1]} to {route[index]}");
    }

    [Fact]
    public void LinksStayOutOfOtherElementsAndContainerLabels()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg(Layered));

        var shapes = root.Descendants(Svg + "rect").Where(rect => Class(rect) == "diagram-node-shape").Select(Bounds).ToList();
        var labels = root.Descendants(Svg + "path").Where(path => Class(path) == "diagram-container-shape").Select(TabOf).ToList();
        Assert.Equal(8, shapes.Count);
        Assert.Equal(3, labels.Count);

        foreach (var route in EdgeRoutes(root))
        foreach (var (from, to) in route.Zip(route.Skip(1)))
            for (var step = 0; step <= 40; step++)
            {
                var x = from.X + (to.X - from.X) * step / 40;
                var y = from.Y + (to.Y - from.Y) * step / 40;
                foreach (var box in shapes.Concat(labels))
                    Assert.False(x > box.Left + 1 && x < box.Right - 1 && y > box.Top + 1 && y < box.Bottom - 1, $"({x}, {y}) in {box}");
            }
    }

    [Fact]
    public void LinksArriveWithoutAHook()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg(Crowded));

        foreach (var route in EdgeRoutes(root))
        {
            if (route.Count < 3) continue;
            var (last, previous, before) = (route[^1], route[^2], route[^3]);
            var dot = (last.X - previous.X) * (previous.X - before.X) + (last.Y - previous.Y) * (previous.Y - before.Y);
            Assert.True(dot >= 0, $"{before} to {previous} to {last}");
            Assert.True(Math.Abs(last.Y - previous.Y) * 4 >= Math.Abs(last.X - previous.X), $"flat arrival from {previous} to {last}");
        }
    }

    [Theory]
    [InlineData(Crowded)]
    [InlineData(Layered)]
    [InlineData("[A]\n[Some very wide middle element]\n[C]\nA --> C\nA --> [Some very wide middle element]\n[Some very wide middle element] --> C")]
    [InlineData(FlatDetour)]
    public void LinksStayInsideTheViewBox(string source)
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg(source));

        var box = root.Attribute("viewBox")!.Value.Split(' ').Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        foreach (var point in EdgeRoutes(root).SelectMany(route => route))
        {
            Assert.InRange(point.X, box[0], box[0] + box[2]);
            Assert.InRange(point.Y, box[1], box[1] + box[3]);
        }
    }

    [Fact]
    public void LinksStayOutOfOtherElementsWhenTheyDetour()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg(FlatDetour));

        var middle = Bounds(ElementShape(root, "C"));
        foreach (var route in EdgeRoutes(root))
        foreach (var point in PointsOf(route))
            Assert.False(point.X > middle.Left + 1 && point.X < middle.Right - 1 && point.Y > middle.Top + 1 && point.Y < middle.Bottom - 1, $"{point} in {middle}");
    }

    [Fact]
    public void AFlatLinkKeepsItsLabelOnItsDetour()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg(FlatDetour));

        var label = LabelCenter(root, "hello");
        var distance = EdgeRoutes(root).SelectMany(PointsOf).Min(point => Math.Sqrt(Math.Pow(point.X - label.X, 2) + Math.Pow(point.Y - label.Y, 2)));
        Assert.True(distance < 12, $"label at {label} is {distance} from the nearest link");
    }

    [Fact]
    public void LinksStayOffTheLabelsOfOtherLinks()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg("""
            [A] --> [D] : first long label
            [A] --> [E]
            [A] --> [F]
            [B] --> [D]
            [B] --> [E] : second long label
            [B] --> [F]
            [C] --> [D]
            [C] --> [E]
            [C] --> [F] : third long label
            """));

        var routes = EdgeRoutes(root);
        var labels = root.Descendants(Svg + "rect").Where(rect => Class(rect) == "diagram-edge-label-background").Select(Bounds).ToList();
        Assert.Equal(3, labels.Count);
        foreach (var (label, owner) in labels.Zip(new[] { 0, 4, 8 }))
        foreach (var point in routes.Where((_, index) => index != owner).SelectMany(PointsOf))
            Assert.False(point.X > label.Left && point.X < label.Right && point.Y > label.Top && point.Y < label.Bottom, $"{point} crosses {label}");
    }

    [Fact]
    public void InterfaceLinksAttachOnTheSideOfTheirRoute()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg("package P {\n[G]\ninterface API\n}\n[O]\nG --> O\nO --> API"));

        var circle = root.Descendants(Svg + "circle").Single(shape => Class(shape) == "diagram-node-shape");
        var center = (X: double.Parse(circle.Attribute("cx")!.Value, CultureInfo.InvariantCulture), Y: double.Parse(circle.Attribute("cy")!.Value, CultureInfo.InvariantCulture));
        var route = EdgeRoutes(root)[1];
        var (end, previous) = (route[^1], route[^2]);
        Assert.True((end.X - center.X) * (previous.X - center.X) + (end.Y - center.Y) * (previous.Y - center.Y) > 0, $"{end} on the circle at {center}, coming from {previous}");
    }

    [Fact]
    public void AFlatInterfaceLinkStartsOnTheCircle()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg("interface API\n[X]\nAPI -right-> X"));

        var circle = root.Descendants(Svg + "circle").Single(shape => Class(shape) == "diagram-node-shape");
        var bottom = double.Parse(circle.Attribute("cy")!.Value, CultureInfo.InvariantCulture) + double.Parse(circle.Attribute("r")!.Value, CultureInfo.InvariantCulture);
        Assert.True(EdgeRoutes(root).Single()[0].Y <= bottom, $"{EdgeRoutes(root).Single()[0]} is below the circle, which ends at {bottom}");
    }

    [Fact]
    public void LinksBelowAnInterfaceAttachApart()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg("interface \"A rather long interface name\" as API\n[X]\n[Y]\nAPI --> X\nAPI --> Y"));

        var starts = EdgeRoutes(root).Select(route => route[0]).ToList();
        Assert.True(Math.Abs(starts[0].X - starts[1].X) > 5, $"{starts[0]} and {starts[1]}");
    }

    [Theory]
    [InlineData("[A] -right-> [B]\n[A] -right-> [B]")]
    [InlineData("[A] -right-> [B] : one\n[B] -left-> [A] : two")]
    public void RepeatedFlatLinksDoNotCoincide(string source)
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg(source));

        var routes = EdgeRoutes(root);
        Assert.Equal(2, routes.Count);
        var boxes = new[] { "A", "B" }.Select(name => Bounds(ElementShape(root, name))).ToList();
        Assert.True(Math.Abs(routes[0][0].Y - routes[1][0].Y) > 5, $"{routes[0][0]} and {routes[1][0]}");
        foreach (var point in routes.SelectMany(route => route))
            Assert.InRange(point.Y, boxes.Min(box => box.Top), boxes.Max(box => box.Bottom));
    }

    [Fact]
    public void AFlatLinksLabelStaysOffElementsAndTheTitle()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg("title Some title\n" + FlatDetour));

        var label = Bounds(root.Descendants(Svg + "rect").Single(rect => Class(rect) == "diagram-edge-label-background"));
        var titleY = double.Parse(root.Descendants(Svg + "text").Single(text => Class(text) == "diagram-component-title").Attribute("y")!.Value, CultureInfo.InvariantCulture);
        Assert.True(label.Top > titleY, $"{label} reaches the title at {titleY}");
        foreach (var box in new[] { "A", "B", "C" }.Select(name => Bounds(ElementShape(root, name))))
            Assert.True(label.Right <= box.Left || label.Left >= box.Right || label.Bottom <= box.Top || label.Top >= box.Bottom, $"{label} overlaps {box}");
    }

    [Fact]
    public void LinksFindAWayAroundElementsBesideTheirDirectLine()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg("""
            left to right direction
            rectangle "Container 0 with name" {
            [N0]
            [N1]
            }
            [N2]
            [N0] -right-> [N2]
            [N0] ---> [N2]
            [N2] -left-> [N1] : label 5
            [N2] --> [N1]
            [N1] -up-> [N0]
            [N2] ---> [N1] : label 9
            [N1] -up-> [N2]
            [N1] --> [N0]
            [N0] -right-> [N2] : label 12
            """));

        var middle = Bounds(ElementShape(root, "N1"));
        foreach (var point in PointsOf(EdgeRoutes(root)[1]))
            Assert.False(point.X > middle.Left && point.X < middle.Right && point.Y > middle.Top && point.Y < middle.Bottom, $"{point} in {middle}");
    }

    [Fact]
    public void RandomDiagramsKeepTheirLinksClear()
    {
        var crossedLabels = 0;
        for (var seed = 0; seed < 400; seed++)
        {
            var (source, links) = RandomDiagram(seed);
            var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg(source));

            var routes = EdgeRoutes(root);
            Assert.Equal(links.Count, routes.Count);
            var elements = root.Descendants(Svg + "g").Where(group => Class(group).Contains("diagram-component-component"))
                .ToDictionary(group => group.Element(Svg + "text")!.Value, group => Bounds(group.Element(Svg + "rect")!));
            var view = root.Attribute("viewBox")!.Value.Split(' ').Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            for (var index = 0; index < routes.Count; index++)
            {
                var route = routes[index];
                for (var point = 2; point < route.Count; point++)
                {
                    var dot = (route[point].X - route[point - 1].X) * (route[point - 1].X - route[point - 2].X) + (route[point].Y - route[point - 1].Y) * (route[point - 1].Y - route[point - 2].Y);
                    if (point == route.Count - 1 || point == 2) Assert.True(dot >= 0, $"seed {seed}: link {index} doubles back at {route[point - 1]}\n{source}");
                }

                foreach (var point in PointsOf(route))
                {
                    Assert.InRange(point.X, view[0], view[0] + view[2]);
                    Assert.InRange(point.Y, view[1], view[1] + view[3]);
                    foreach (var (name, box) in elements)
                        if (name != links[index].From && name != links[index].To)
                            Assert.False(point.X > box.Left + 1 && point.X < box.Right - 1 && point.Y > box.Top + 1 && point.Y < box.Bottom - 1, $"seed {seed}: link {index} runs through {name}\n{source}");
                }
            }

            crossedLabels += root.Descendants(Svg + "rect").Where(rect => Class(rect) == "diagram-edge-label-background").Select(Bounds)
                .Count(label => routes.Count(route => PointsOf(route).Any(point => point.X > label.Left && point.X < label.Right && point.Y > label.Top && point.Y < label.Bottom)) > 1);
        }

        // One flat label in these diagrams is wider than the gap the layout leaves for it.
        Assert.True(crossedLabels <= 1, $"{crossedLabels} labels are crossed by a second link");
    }

    [Fact]
    public void KeepsContainerPaddingEvenAroundContent()
    {
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg("rectangle P {\n[Alpha]\n[Beta]\n[Gamma]\n}\nrectangle Q {\n[One]\n[Two]\n}\nAlpha --> Two\nAlpha --> One\nGamma --> Two\nBeta --> One"));

        var content = new[] { "Alpha", "Beta", "Gamma" }.Select(member => Bounds(ElementShape(root, member))).ToList();
        var container = root.Descendants(Svg + "g").Where(group => Class(group).Contains("diagram-container-rectangle")).Select(group => Bounds(group.Element(Svg + "rect")!))
            .First(box => box.Left <= content[0].Left && box.Right >= content[0].Right && box.Top <= content[0].Top && box.Bottom >= content[0].Bottom);
        Assert.Equal(content.Min(box => box.Left) - container.Left, container.Right - content.Max(box => box.Right), 0);
    }

    private static (string Source, List<(string From, string To)> Links) RandomDiagram(int seed)
    {
        var random = new Random(seed);
        var source = new StringBuilder();
        if (random.Next(3) == 0) source.AppendLine("left to right direction");
        if (random.Next(2) == 0) source.AppendLine("title Random");
        var elements = random.Next(3, 14);
        var next = 0;
        for (var container = random.Next(0, 5); container > 0; container--)
        {
            source.AppendLine($"{new[] { "package", "rectangle", "node", "frame", "cloud" }[random.Next(5)]} \"Container {container} with name\" {{");
            for (var count = random.Next(1, 4); count > 0 && next < elements; count--) source.AppendLine($"[N{next++}]");
            if (random.Next(3) == 0 && next < elements) source.AppendLine($"package \"Inner {container}\" {{\n[N{next++}]\n}}");
            source.AppendLine("}");
        }

        while (next < elements) source.AppendLine($"[N{next++}]");
        var links = new List<(string, string)>();
        for (var link = random.Next(2, 20); link > 0; link--)
        {
            var (from, to) = ($"N{random.Next(elements)}", $"N{random.Next(elements)}");
            if (from == to) continue;
            var arrow = new[] { "-->", "-->", "-->", "..>", "-right->", "-left->", "-up->", "--->" }[random.Next(8)];
            source.AppendLine($"[{from}] {arrow} [{to}]{(random.Next(4) == 0 ? $" : label {link}" : "")}");
            links.Add((from, to));
        }

        return (source.ToString(), links);
    }

    private static IEnumerable<(double X, double Y)> PointsOf(List<(double X, double Y)> route)
    {
        foreach (var (from, to) in route.Zip(route.Skip(1)))
            for (var step = 0; step <= 40; step++)
                yield return (from.X + (to.X - from.X) * step / 40, from.Y + (to.Y - from.Y) * step / 40);
    }

    private static (double X, double Y) LabelCenter(XElement root, string text)
    {
        var label = root.Descendants(Svg + "g").Single(group => Class(group).Contains("diagram-edge-labels")).Descendants(Svg + "text").Single(element => element.Value == text);
        return (double.Parse(label.Attribute("x")!.Value, CultureInfo.InvariantCulture), double.Parse(label.Attribute("y")!.Value, CultureInfo.InvariantCulture));
    }

    /// <summary>The points of every drawn link, including the corners of its rounded bends.</summary>
    private static List<List<(double X, double Y)>> EdgeRoutes(XElement root) =>
        root.Descendants(Svg + "path")
            .Where(path => Class(path).Contains("diagram-edge"))
            .Select(path => Regex.Matches(path.Attribute("d")!.Value, @"-?\d+(\.\d+)?\s+-?\d+(\.\d+)?")
                .Select(match => match.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Select(pair => (double.Parse(pair[0], CultureInfo.InvariantCulture), double.Parse(pair[1], CultureInfo.InvariantCulture)))
                .ToList())
            .ToList();

    /// <summary>A package's outline starts with its left edge, up to the top of the tab, across it, and down to the body.</summary>
    private static Box TabOf(XElement shape)
    {
        var numbers = Regex.Matches(shape.Attribute("d")!.Value, @"-?\d+(\.\d+)?").Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture)).ToList();
        return new Box(numbers[0], numbers[2], numbers[3], numbers[4]);
    }

    private static string Class(XElement element) => element.Attribute("class")?.Value ?? string.Empty;

    private static XElement ElementShape(XElement root, string text) =>
        root.Descendants(Svg + "g")
            .Where(group => Class(group).Contains("diagram-component-"))
            .Single(group => group.Element(Svg + "text")?.Value == text)
            .Element(Svg + "rect")!;

    private static Box Bounds(XElement shape)
    {
        double Value(string name) => double.Parse(shape.Attribute(name)!.Value, CultureInfo.InvariantCulture);
        return new Box(Value("x"), Value("y"), Value("x") + Value("width"), Value("y") + Value("height"));
    }

    private sealed record Box(double Left, double Top, double Right, double Bottom);
}
