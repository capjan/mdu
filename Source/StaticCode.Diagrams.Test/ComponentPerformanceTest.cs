using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace StaticCode.Diagrams.Test;

public class ComponentPerformanceTest
{
    // A limit against runaway growth, not a benchmark: it has to hold on slow machines and in debug builds.
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    [Theory]
    [InlineData(Shape.Sparse)]
    [InlineData(Shape.Dense)]
    [InlineData(Shape.Packages)]
    [InlineData(Shape.Nested)]
    public void LargeDiagramsStayWithinTheLimit(Shape shape)
    {
        var watch = Stopwatch.StartNew();
        DiagramRenderer.RenderPlantUmlSvg(Diagram(shape, 300, 7));

        Assert.True(watch.Elapsed < Limit, $"{shape} took {watch.Elapsed}");
    }

    [Theory]
    [InlineData(Shape.Sparse)]
    [InlineData(Shape.Packages)]
    [InlineData(Shape.Nested)]
    public void LinksStayOutOfOtherElementsInLargeDiagrams(Shape shape)
    {
        XNamespace svg = "http://www.w3.org/2000/svg";
        var source = Diagram(shape, 300, 7);
        var root = XElement.Parse(DiagramRenderer.RenderPlantUmlSvg(source));

        double Value(XElement element, string name) => double.Parse(element.Attribute(name)!.Value, CultureInfo.InvariantCulture);
        var boxes = root.Descendants(svg + "g").Where(group => group.Attribute("class")!.Value.Contains("diagram-component-component"))
            .ToDictionary(group => group.Element(svg + "text")!.Value, group => group.Element(svg + "rect")!);
        var links = Regex.Matches(source, @"^\[(N\d+)\] \S+ \[(N\d+)\]", RegexOptions.Multiline);
        var routes = root.Descendants(svg + "path").Where(path => path.Attribute("class")!.Value.Contains("diagram-edge"))
            .Select(path => Regex.Matches(path.Attribute("d")!.Value, @"-?\d+(\.\d+)?\s+-?\d+(\.\d+)?")
                .Select(match => match.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(number => double.Parse(number, CultureInfo.InvariantCulture)).ToArray())
                .ToList())
            .ToList();
        Assert.Equal(links.Count, routes.Count);

        for (var index = 0; index < routes.Count; index++)
        foreach (var (from, to) in routes[index].Zip(routes[index].Skip(1)))
        {
            var steps = (int)Math.Clamp(Math.Abs(to[0] - from[0]) / 8 + Math.Abs(to[1] - from[1]) / 8, 1, 200);
            foreach (var (name, box) in boxes.Where(box => box.Key != links[index].Groups[1].Value && box.Key != links[index].Groups[2].Value))
            {
                var (left, top) = (Value(box, "x"), Value(box, "y"));
                var (right, bottom) = (left + Value(box, "width"), top + Value(box, "height"));
                if (Math.Max(from[0], to[0]) < left || Math.Min(from[0], to[0]) > right || Math.Max(from[1], to[1]) < top || Math.Min(from[1], to[1]) > bottom) continue;

                for (var step = 0; step <= steps; step++)
                {
                    var (x, y) = (from[0] + (to[0] - from[0]) * step / steps, from[1] + (to[1] - from[1]) * step / steps);
                    Assert.False(x > left + 1 && x < right - 1 && y > top + 1 && y < bottom - 1, $"link {index} runs through {name}");
                }
            }
        }
    }

    [Theory]
    [InlineData(1000, 0, false)]
    [InlineData(1001, 0, true)]
    [InlineData(2, 2000, false)]
    [InlineData(2, 2001, true)]
    public void DiagramsAboveTheLimitAreNotDrawn(int elements, int links, bool tooLarge)
    {
        var source = string.Join('\n', Enumerable.Range(0, elements).Select(element => $"[N{element}]").Concat(Enumerable.Repeat("[N0] --> [N1]", links)));

        Assert.Equal(tooLarge, DiagramRenderer.IsPlantUmlTooLarge(source));
        if (tooLarge) Assert.False(DiagramRenderer.TryRenderSvg("plantuml", source, out _));
    }

    [Fact]
    public void TheSameDiagramAlwaysGivesTheSameSvg()
    {
        var source = Diagram(Shape.Dense, 300, 7);

        Assert.Equal(DiagramRenderer.RenderPlantUmlSvg(source), DiagramRenderer.RenderPlantUmlSvg(source));
    }

    /// <summary>Writes a timing table to the file in MDU_DIAGRAM_BENCHMARK; run it with <c>--configuration Release</c>.</summary>
    [Fact]
    public void Benchmark()
    {
        var output = Environment.GetEnvironmentVariable("MDU_DIAGRAM_BENCHMARK");
        if (string.IsNullOrEmpty(output)) return;

        DiagramRenderer.RenderPlantUmlSvg("[A] --> [B]");
        var report = new StringBuilder("shape\telements\tseed\tms\n");
        foreach (var shape in Enum.GetValues<Shape>())
        foreach (var elements in new[] { 30, 80, 150, 300, 500, 800 })
        foreach (var seed in new[] { 7, 99 })
        {
            var source = Diagram(shape, elements, seed);
            var watch = Stopwatch.StartNew();
            DiagramRenderer.RenderPlantUmlSvg(source);
            report.AppendLine($"{shape}\t{elements}\t{seed}\t{watch.ElapsedMilliseconds}");
            File.WriteAllText(output, report.ToString());
        }
    }

    public enum Shape
    {
        Sparse,
        Dense,
        Packages,
        Nested
    }

    private static string Diagram(Shape shape, int elements, int seed)
    {
        var random = new Random(seed);
        var source = new StringBuilder();
        var perPackage = shape == Shape.Nested ? 6 : 8;
        var open = 0;
        for (var element = 0; element < elements; element++)
        {
            if (shape is Shape.Packages or Shape.Nested && element % perPackage == 0)
            {
                var startsGroup = shape == Shape.Nested && element % (4 * perPackage) == 0;
                for (; open > (startsGroup || shape == Shape.Packages ? 0 : 1); open--) source.AppendLine("}");
                if (startsGroup && open++ >= 0) source.AppendLine($"package \"Group {element}\" {{");
                source.AppendLine($"package \"Package {element}\" {{");
                open++;
            }

            source.AppendLine($"[N{element}]");
        }

        for (; open > 0; open--) source.AppendLine("}");
        var links = shape == Shape.Dense ? 3 * elements : elements * 5 / 3;
        for (var link = 0; link < links; link++)
        {
            var from = random.Next(elements);
            var to = random.Next(elements);
            if (from == to) continue;
            source.AppendLine($"[N{from}] {new[] { "-->", "-->", "..>", "-right->", "--->" }[random.Next(5)]} [N{to}]{(random.Next(4) == 0 ? $" : label {link}" : "")}");
        }

        return source.ToString();
    }
}
