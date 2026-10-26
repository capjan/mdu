using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Markdown.Renderer;

internal static class PdfSvgRenderer
{
    private static readonly Regex PathTokenRegex = new("[A-Za-z]|[-+]?(?:\\d*\\.\\d+|\\d+\\.?\\d*)(?:[eE][-+]?\\d+)?", RegexOptions.Compiled);
    private static readonly Regex TransformRegex = new(@"(?<name>[A-Za-z]+)\s*\((?<values>[^)]*)\)", RegexOptions.Compiled);

    public static SvgDimensions Measure(XmlElement svg)
    {
        var viewBox = Numbers(svg.GetAttribute("viewBox"));
        if (viewBox.Length == 4 && viewBox[2] > 0 && viewBox[3] > 0)
            return new SvgDimensions(viewBox[0], viewBox[1], viewBox[2], viewBox[3]);

        var width = ParseLength(svg.GetAttribute("width"), 24);
        var height = ParseLength(svg.GetAttribute("height"), 24);
        return new SvgDimensions(0, 0, width, height);
    }

    public static void Draw(
        XmlElement svg, StringBuilder output, double left, double top, double width, double height, double pageHeight,
        Action<string, double, double, double, double>? addLink = null)
    {
        var dimensions = Measure(svg);
        var scale = Math.Min(width / dimensions.Width, height / dimensions.Height);
        var x = left + (width - dimensions.Width * scale) / 2 - dimensions.X * scale;
        var y = pageHeight - top + dimensions.Y * scale;
        if (addLink is not null) AddLinks(svg, dimensions, scale, x, y, addLink);
        output.Append("q ").Append(F(scale)).Append(" 0 0 -").Append(F(scale)).Append(' ')
            .Append(F(x)).Append(' ').Append(F(y)).Append(" cm\n");
        DrawChildren(svg, output, string.Empty, "#1d1d1f", "none", 1, ParseLength(svg.GetAttribute("font-size"), 12));
        output.Append("Q\n");
    }

    private static void AddLinks(
        XmlElement svg, SvgDimensions dimensions, double scale, double x, double y,
        Action<string, double, double, double, double> addLink)
    {
        foreach (var anchor in svg.GetElementsByTagName("a").OfType<XmlElement>())
        {
            var href = anchor.GetAttribute("href");
            var bounds = Numbers(anchor.GetAttribute("data-pdf-bounds"));
            if (href.Length == 0 || bounds.Length != 4 || bounds[2] <= 0 || bounds[3] <= 0) continue;

            var left = Math.Max(dimensions.X, bounds[0]);
            var top = Math.Max(dimensions.Y, bounds[1]);
            var right = Math.Min(dimensions.X + dimensions.Width, bounds[0] + bounds[2]);
            var bottom = Math.Min(dimensions.Y + dimensions.Height, bounds[1] + bounds[3]);
            if (right <= left || bottom <= top) continue;

            addLink(href, x + left * scale, y - bottom * scale, x + right * scale, y - top * scale);
        }
    }

    private static void DrawChildren(XmlElement parent, StringBuilder output, string inheritedClasses, string inheritedFill, string inheritedStroke, double inheritedStrokeWidth, double inheritedFontSize)
    {
        foreach (XmlNode node in parent.ChildNodes)
        {
            if (node is not XmlElement element) continue;
            var classes = string.Join(' ', inheritedClasses, element.GetAttribute("class")).Trim();
            // The PDF is always light, so the dark icon variants that the stylesheet hides in light mode are skipped.
            if (ClassContains(classes, "icon-dark") || ClassContains(classes, "iso-icon-dark")) continue;
            var name = element.LocalName.ToLowerInvariant();
            if (name is "defs" or "clippath") continue;

            var hasTransform = ApplyTransform(element.GetAttribute("transform"), output);
            var opacity = ParseLength(element.GetAttribute("opacity"), 1);
            var fillOpacity = opacity * ParseLength(element.GetAttribute("fill-opacity"), 1);
            var strokeOpacity = opacity * ParseLength(element.GetAttribute("stroke-opacity"), 1);

            var fill = ReadColor(element, "fill") ?? PdfIsometricStyle.Fill(classes) ?? ClassFill(classes) ?? inheritedFill;
            var stroke = ReadColor(element, "stroke") ?? PdfIsometricStyle.Stroke(classes) ?? ClassStroke(classes) ?? inheritedStroke;
            var textColor = ReadColor(element, "fill") ?? PdfIsometricStyle.Text(classes) ?? ClassText(classes) ?? inheritedFill;
            fill = ApplyOpacity(fill, fillOpacity);
            stroke = ApplyOpacity(stroke, strokeOpacity);
            textColor = ApplyOpacity(textColor, fillOpacity);
            var strokeWidth = ParseLength(element.GetAttribute("stroke-width"), PdfIsometricStyle.StrokeWidth(classes) ?? ClassStrokeWidth(classes, inheritedStrokeWidth));
            var fontSize = ParseLength(element.GetAttribute("font-size"), inheritedFontSize);
            switch (name)
            {
                case "g":
                    DrawChildren(element, output, classes, fill, stroke, strokeWidth, fontSize);
                    break;
                case "path":
                    DrawPath(element.GetAttribute("d"), output, fill, stroke, strokeWidth, classes,
                        element.GetAttribute("fill-rule").Equals("evenodd", StringComparison.OrdinalIgnoreCase));
                    break;
                case "rect":
                    DrawRect(element, output, fill, stroke, strokeWidth);
                    break;
                case "circle":
                    DrawCircle(element, output, fill, stroke, strokeWidth);
                    break;
                case "ellipse":
                    DrawEllipse(element, output, fill, stroke, strokeWidth);
                    break;
                case "line":
                    DrawLine(element, output, stroke, strokeWidth);
                    break;
                case "polyline": case "polygon":
                    DrawPoints(element, output, fill, stroke, strokeWidth, name == "polygon");
                    break;
                case "text":
                    DrawText(element, output, textColor, classes, fontSize);
                    break;
                default:
                    DrawChildren(element, output, classes, fill, stroke, strokeWidth, fontSize);
                    break;
            }
            if (hasTransform) output.Append("Q\n");
        }
    }

    private static bool ApplyTransform(string value, StringBuilder output)
    {
        var transforms = TransformRegex.Matches(value);
        if (transforms.Count == 0) return false;
        output.Append("q\n");
        foreach (Match transform in transforms)
        {
            var values = Numbers(transform.Groups["values"].Value);
            var name = transform.Groups["name"].Value;
            double a, b, c, d, e, f;
            switch (name)
            {
                case "matrix" when values.Length == 6:
                    (a, b, c, d, e, f) = (values[0], values[1], values[2], values[3], values[4], values[5]);
                    break;
                case "translate" when values.Length is 1 or 2:
                    (a, b, c, d, e, f) = (1, 0, 0, 1, values[0], values.Length == 2 ? values[1] : 0);
                    break;
                case "scale" when values.Length is 1 or 2:
                    (a, b, c, d, e, f) = (values[0], 0, 0, values.Length == 2 ? values[1] : values[0], 0, 0);
                    break;
                case "rotate" when values.Length is 1 or 3:
                    var radians = values[0] * Math.PI / 180;
                    var cosine = Math.Cos(radians);
                    var sine = Math.Sin(radians);
                    var centerX = values.Length == 3 ? values[1] : 0;
                    var centerY = values.Length == 3 ? values[2] : 0;
                    (a, b, c, d, e, f) = (cosine, sine, -sine, cosine,
                        centerX - cosine * centerX + sine * centerY,
                        centerY - sine * centerX - cosine * centerY);
                    break;
                case "skewX" when values.Length == 1:
                    (a, b, c, d, e, f) = (1, 0, Math.Tan(values[0] * Math.PI / 180), 1, 0, 0);
                    break;
                case "skewY" when values.Length == 1:
                    (a, b, c, d, e, f) = (1, Math.Tan(values[0] * Math.PI / 180), 0, 1, 0, 0);
                    break;
                default:
                    continue;
            }
            output.Append(F(a)).Append(' ').Append(F(b)).Append(' ').Append(F(c)).Append(' ')
                .Append(F(d)).Append(' ').Append(F(e)).Append(' ').Append(F(f)).Append(" cm\n");
        }
        return true;
    }

    private static string ApplyOpacity(string color, double opacity)
    {
        if (color == "none" || opacity >= 1) return color;
        if (opacity <= 0) return "none";
        var parsed = ParseColor(color);
        return $"#{Blend(parsed.R):X2}{Blend(parsed.G):X2}{Blend(parsed.B):X2}";

        byte Blend(byte channel) => (byte)Math.Clamp(Math.Round(channel * opacity + 255 * (1 - opacity)), 0, 255);
    }

    private static void DrawPath(string data, StringBuilder output, string fill, string stroke, double strokeWidth, string classes, bool evenOdd)
    {
        if (string.IsNullOrWhiteSpace(data)) return;
        output.Append("q ");
        SetPaint(output, fill, stroke, strokeWidth, classes);
        var tokens = PathTokenRegex.Matches(data).Select(match => match.Value).ToArray();
        var index = 0;
        var command = ' ';
        var x = 0.0;
        var y = 0.0;
        var startX = 0.0;
        var startY = 0.0;
        var previousControlX = 0.0;
        var previousControlY = 0.0;
        while (index < tokens.Length)
        {
            if (IsCommand(tokens[index])) command = tokens[index++][0];
            if (command == ' ') break;
            var relative = char.IsLower(command);
            switch (char.ToUpperInvariant(command))
            {
                case 'Z':
                    output.Append("h\n");
                    x = startX;
                    y = startY;
                    command = ' ';
                    break;
                case 'M':
                case 'L':
                    var move = ReadValues(tokens, ref index, 2);
                    if (move is null) { command = ' '; break; }
                    var moveX = move[0];
                    var moveY = move[1];
                    if (relative) { moveX += x; moveY += y; }
                    if (char.ToUpperInvariant(command) == 'M')
                    {
                        output.Append(F(moveX)).Append(' ').Append(F(moveY)).Append(" m\n");
                        startX = moveX;
                        startY = moveY;
                        command = relative ? 'l' : 'L';
                    }
                    else output.Append(F(moveX)).Append(' ').Append(F(moveY)).Append(" l\n");
                    x = moveX;
                    y = moveY;
                    break;
                case 'H':
                    var horizontalValues = ReadValues(tokens, ref index, 1);
                    if (horizontalValues is null) { command = ' '; break; }
                    var horizontal = horizontalValues[0];
                    if (relative) horizontal += x;
                    output.Append(F(horizontal)).Append(' ').Append(F(y)).Append(" l\n");
                    x = horizontal;
                    break;
                case 'V':
                    var verticalValues = ReadValues(tokens, ref index, 1);
                    if (verticalValues is null) { command = ' '; break; }
                    var vertical = verticalValues[0];
                    if (relative) vertical += y;
                    output.Append(F(x)).Append(' ').Append(F(vertical)).Append(" l\n");
                    y = vertical;
                    break;
                case 'C':
                    var cubic = ReadValues(tokens, ref index, 6);
                    if (cubic is null) { command = ' '; break; }
                    var cx1 = cubic[0]; var cy1 = cubic[1]; var cx2 = cubic[2]; var cy2 = cubic[3]; var endX = cubic[4]; var endY = cubic[5];
                    if (relative) { cx1 += x; cy1 += y; cx2 += x; cy2 += y; endX += x; endY += y; }
                    output.Append(F(cx1)).Append(' ').Append(F(cy1)).Append(' ').Append(F(cx2)).Append(' ').Append(F(cy2)).Append(' ')
                        .Append(F(endX)).Append(' ').Append(F(endY)).Append(" c\n");
                    x = endX; y = endY; previousControlX = cx2; previousControlY = cy2;
                    break;
                case 'S':
                    var smooth = ReadValues(tokens, ref index, 4);
                    if (smooth is null) { command = ' '; break; }
                    var scx2 = smooth[0]; var scy2 = smooth[1]; var sendX = smooth[2]; var sendY = smooth[3];
                    if (relative) { scx2 += x; scy2 += y; sendX += x; sendY += y; }
                    var scx1 = 2 * x - previousControlX;
                    var scy1 = 2 * y - previousControlY;
                    output.Append(F(scx1)).Append(' ').Append(F(scy1)).Append(' ').Append(F(scx2)).Append(' ').Append(F(scy2)).Append(' ')
                        .Append(F(sendX)).Append(' ').Append(F(sendY)).Append(" c\n");
                    x = sendX; y = sendY; previousControlX = scx2; previousControlY = scy2;
                    break;
                case 'Q':
                    var quadratic = ReadValues(tokens, ref index, 4);
                    if (quadratic is null) { command = ' '; break; }
                    var qx = quadratic[0]; var qy = quadratic[1]; var qendX = quadratic[2]; var qendY = quadratic[3];
                    if (relative) { qx += x; qy += y; qendX += x; qendY += y; }
                    var qcx1 = x + (qx - x) * 2 / 3;
                    var qcy1 = y + (qy - y) * 2 / 3;
                    var qcx2 = qendX + (qx - qendX) * 2 / 3;
                    var qcy2 = qendY + (qy - qendY) * 2 / 3;
                    output.Append(F(qcx1)).Append(' ').Append(F(qcy1)).Append(' ').Append(F(qcx2)).Append(' ').Append(F(qcy2)).Append(' ')
                        .Append(F(qendX)).Append(' ').Append(F(qendY)).Append(" c\n");
                    x = qendX; y = qendY; previousControlX = qx; previousControlY = qy;
                    break;
                case 'A':
                    var arc = ReadValues(tokens, ref index, 7);
                    if (arc is null) { command = ' '; break; }
                    var arcX = arc[5]; var arcY = arc[6];
                    if (relative) { arcX += x; arcY += y; }
                    output.Append(F(arcX)).Append(' ').Append(F(arcY)).Append(" l\n");
                    x = arcX; y = arcY;
                    break;
                default:
                    command = ' ';
                    break;
            }
        }
        Paint(output, fill, stroke, evenOdd);
        output.Append("Q\n");
    }

    private static void DrawRect(XmlElement element, StringBuilder output, string fill, string stroke, double strokeWidth)
    {
        var x = ParseLength(element.GetAttribute("x"), 0);
        var y = ParseLength(element.GetAttribute("y"), 0);
        var width = ParseLength(element.GetAttribute("width"), 0);
        var height = ParseLength(element.GetAttribute("height"), 0);
        output.Append("q ");
        SetPaint(output, fill, stroke, strokeWidth, "");
        output.Append(F(x)).Append(' ').Append(F(y)).Append(' ').Append(F(width)).Append(' ').Append(F(height)).Append(" re\n");
        Paint(output, fill, stroke);
        output.Append("Q\n");
    }

    private static void DrawCircle(XmlElement element, StringBuilder output, string fill, string stroke, double strokeWidth) =>
        DrawEllipse(element, output, fill, stroke, strokeWidth, true);

    private static void DrawEllipse(XmlElement element, StringBuilder output, string fill, string stroke, double strokeWidth, bool circle = false)
    {
        var cx = ParseLength(element.GetAttribute("cx"), 0);
        var cy = ParseLength(element.GetAttribute("cy"), 0);
        var rx = circle ? ParseLength(element.GetAttribute("r"), 0) : ParseLength(element.GetAttribute("rx"), 0);
        var ry = circle ? rx : ParseLength(element.GetAttribute("ry"), 0);
        const double k = 0.5522847498;
        output.Append("q ");
        SetPaint(output, fill, stroke, strokeWidth, "");
        output.Append(F(cx + rx)).Append(' ').Append(F(cy)).Append(" m ")
            .Append(F(cx + rx)).Append(' ').Append(F(cy + ry * k)).Append(' ').Append(F(cx + rx * k)).Append(' ').Append(F(cy + ry)).Append(' ').Append(F(cx)).Append(' ').Append(F(cy + ry)).Append(" c ")
            .Append(F(cx - rx * k)).Append(' ').Append(F(cy + ry)).Append(' ').Append(F(cx - rx)).Append(' ').Append(F(cy + ry * k)).Append(' ').Append(F(cx - rx)).Append(' ').Append(F(cy)).Append(" c ")
            .Append(F(cx - rx)).Append(' ').Append(F(cy - ry * k)).Append(' ').Append(F(cx - rx * k)).Append(' ').Append(F(cy - ry)).Append(' ').Append(F(cx)).Append(' ').Append(F(cy - ry)).Append(" c ")
            .Append(F(cx + rx * k)).Append(' ').Append(F(cy - ry)).Append(' ').Append(F(cx + rx)).Append(' ').Append(F(cy - ry * k)).Append(' ').Append(F(cx + rx)).Append(' ').Append(F(cy)).Append(" c h\n");
        Paint(output, fill, stroke);
        output.Append("Q\n");
    }

    private static void DrawLine(XmlElement element, StringBuilder output, string stroke, double strokeWidth)
    {
        output.Append("q ");
        SetPaint(output, "none", stroke, strokeWidth, "");
        output.Append(F(ParseLength(element.GetAttribute("x1"), 0))).Append(' ').Append(F(ParseLength(element.GetAttribute("y1"), 0))).Append(" m ")
            .Append(F(ParseLength(element.GetAttribute("x2"), 0))).Append(' ').Append(F(ParseLength(element.GetAttribute("y2"), 0))).Append(" l S Q\n");
    }

    private static void DrawPoints(XmlElement element, StringBuilder output, string fill, string stroke, double strokeWidth, bool close)
    {
        var points = Numbers(element.GetAttribute("points"));
        if (points.Length < 4) return;
        output.Append("q ");
        SetPaint(output, fill, stroke, strokeWidth, "");
        output.Append(F(points[0])).Append(' ').Append(F(points[1])).Append(" m ");
        for (var index = 2; index + 1 < points.Length; index += 2)
            output.Append(F(points[index])).Append(' ').Append(F(points[index + 1])).Append(" l ");
        if (close) output.Append("h ");
        output.Append('\n');
        Paint(output, close ? fill : "none", stroke);
        output.Append("Q\n");
    }

    private static void DrawText(XmlElement element, StringBuilder output, string color, string classes, double fontSize)
    {
        var font = classes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(name => name.Contains("title", StringComparison.Ordinal) || name.Contains("keyword", StringComparison.Ordinal))
            ? "F2" : "F1";
        var anchor = element.GetAttribute("text-anchor");
        var children = element.ChildNodes.OfType<XmlElement>().Where(child => child.LocalName == "tspan").ToArray();
        var textNodes = children.Length == 0 ? new[] { (element.InnerText, element.GetAttribute("x"), element.GetAttribute("y")) } :
            children.Select(child => (child.InnerText, child.GetAttribute("x").Length > 0 ? child.GetAttribute("x") : element.GetAttribute("x"), child.GetAttribute("y")));
        foreach (var (text, xValue, yValue) in textNodes)
        {
            if (text.Length == 0) continue;
            var svgX = ParseLength(xValue, 0);
            var svgY = ParseLength(yValue, 0);
            var width = TextWidth(text, fontSize);
            if (anchor == "middle") svgX -= width / 2;
            else if (anchor == "end") svgX -= width;
            output.Append(ColorCommand(color, fill: true)).Append("BT /").Append(font).Append(' ').Append(F(fontSize))
                .Append(" Tf 1 0 0 -1 ").Append(F(svgX)).Append(' ').Append(F(svgY)).Append(" Tm <")
                .Append(PdfEncoding.Hex(text)).Append("> Tj ET\n");
        }
    }

    private static void SetPaint(StringBuilder output, string fill, string stroke, double strokeWidth, string classes)
    {
        if (fill != "none") output.Append(ColorCommand(fill, fill: true));
        if (stroke != "none") output.Append(ColorCommand(stroke, fill: false)).Append(F(strokeWidth)).Append(" w ");
        if (classes.Contains("dotted", StringComparison.Ordinal)) output.Append("[1 5] 0 d ");
        else if (classes.Contains("dashed", StringComparison.Ordinal)) output.Append("[4 4] 0 d ");
        else output.Append("[] 0 d ");
    }

    private static void Paint(StringBuilder output, string fill, string stroke, bool evenOdd = false)
    {
        if (fill != "none" && stroke != "none") output.Append(evenOdd ? "B*\n" : "B\n");
        else if (fill != "none") output.Append(evenOdd ? "f*\n" : "f\n");
        else if (stroke != "none") output.Append("S\n");
        else output.Append("n\n");
    }

    private static string? ReadColor(XmlElement element, string property)
    {
        var direct = element.GetAttribute(property);
        if (direct.Length > 0) return NormalizeColor(direct);
        var match = Regex.Match(element.GetAttribute("style"), $@"(?:^|;)\s*{property}\s*:\s*(?<value>#[0-9a-fA-F]{{3,8}}|[a-zA-Z]+)");
        return match.Success ? NormalizeColor(match.Groups["value"].Value) : null;
    }

    private static string? NormalizeColor(string value) => value switch
    {
        "currentColor" => "#1d1d1f",
        "white" => "#ffffff",
        "black" => "#000000",
        "none" => "none",
        _ when value.StartsWith("var(", StringComparison.Ordinal) => null,
        _ => value
    };

    private static string? ClassFill(string classes) =>
        ClassContains(classes, "diagram-node-diamond") || ClassContains(classes, "diagram-context-center") ? "#eef5fd" :
        ClassContains(classes, "diagram-node-shape") || ClassContains(classes, "diagram-sequence-activation") ||
        ClassContains(classes, "diagram-sequence-frame-tab") || ClassContains(classes, "diagram-actor-head") ? "#f5f5f7" :
        ClassContains(classes, "diagram-node-icon") ? "#f5f5f7" :
        ClassContains(classes, "diagram-marker-hollow") ? "#ffffff" :
        ClassContains(classes, "diagram-edge-label-background") ? "#ffffff" :
        ClassContains(classes, "diagram-sequence-highlight") ? "#f6f6f7" :
        ClassContains(classes, "diagram-marker") || ClassContains(classes, "diagram-sequence-number") ? "#8e8e93" :
        ClassContains(classes, "diagram-node-detail") || ClassContains(classes, "diagram-edge") ||
        ClassContains(classes, "diagram-sequence-lifeline") || ClassContains(classes, "diagram-frame-divider") ||
        ClassContains(classes, "diagram-sequence-frame") || ClassContains(classes, "diagram-actor-body") ? "none" :
        ClassContains(classes, "diagram-note-shape") || ClassContains(classes, "diagram-sequence-note-shape") ? "#fdf6e7" :
        ClassContains(classes, "diagram-container-shape") || ClassContains(classes, "diagram-container-tab") ? "#fafafa" : null;

    private static string? ClassStroke(string classes) =>
        ClassContains(classes, "diagram-node-diamond") || ClassContains(classes, "diagram-context-center") ? "#8ab6e6" :
        ClassContains(classes, "diagram-node-shape") || ClassContains(classes, "diagram-sequence-lifeline") ||
        ClassContains(classes, "diagram-sequence-activation") || ClassContains(classes, "diagram-sequence-frame") ||
        ClassContains(classes, "diagram-sequence-frame-tab") || ClassContains(classes, "diagram-node-detail") ||
        ClassContains(classes, "diagram-node-icon") ||
        ClassContains(classes, "diagram-container-shape") || ClassContains(classes, "diagram-container-tab") ? "#c7c7cc" :
        ClassContains(classes, "diagram-note-shape") || ClassContains(classes, "diagram-sequence-note-shape") ? "#e3c27a" :
        ClassContains(classes, "diagram-edge") || ClassContains(classes, "diagram-marker") ||
        ClassContains(classes, "diagram-actor-head") || ClassContains(classes, "diagram-actor-body") ? "#8e8e93" : null;

    private static string? ClassText(string classes) =>
        ClassContains(classes, "diagram-edge-label-text") || ClassContains(classes, "diagram-sequence-frame-label") ? "#6e6e73" :
        ClassContains(classes, "diagram-sequence-number") ? "#ffffff" :
        "#1d1d1f";

    private static double ClassStrokeWidth(string classes, double fallback) =>
        ClassContains(classes, "diagram-edge-thick") ? 3 : ClassContains(classes, "diagram-edge") ? 1.5 : fallback;

    private static bool ClassContains(string classes, string name) =>
        classes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal);
    private static bool IsCommand(string token) => token.Length == 1 && char.IsLetter(token[0]);

    private static double[]? ReadValues(string[] tokens, ref int index, int count)
    {
        var values = new double[count];
        for (var value = 0; value < count; value++)
        {
            if (index >= tokens.Length || IsCommand(tokens[index]) || !double.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out values[value]))
                return null;
            index++;
        }
        return values;
    }

    private static double[] Numbers(string value) => Regex.Matches(value, @"[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?")
        .Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture)).ToArray();

    private static double ParseLength(string value, double fallback)
    {
        var match = Regex.Match(value, @"[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?");
        return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : fallback;
    }

    private static string ColorCommand(string color, bool fill)
    {
        var parsed = ParseColor(color);
        return $"{F(parsed.R / 255.0)} {F(parsed.G / 255.0)} {F(parsed.B / 255.0)} {(fill ? "rg" : "RG")} ";
    }

    private static string ColorCommand(PdfColor color, bool fill) =>
        $"{F(color.R / 255.0)} {F(color.G / 255.0)} {F(color.B / 255.0)} {(fill ? "rg" : "RG")} ";

    private static PdfColor ParseColor(string value)
    {
        if (!value.StartsWith('#')) return PdfColor.Text;
        var hex = value[1..];
        if (hex.Length == 3) hex = string.Concat(hex.Select(character => new string(character, 2)));
        if (hex.Length >= 6 && byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) &&
            byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) &&
            byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
            return new PdfColor(red, green, blue);
        return PdfColor.Text;
    }

    private static double TextWidth(string text, double size) => text.Length * size * 0.52;
    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

internal readonly record struct SvgDimensions(double X, double Y, double Width, double Height);
