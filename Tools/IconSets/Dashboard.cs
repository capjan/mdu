using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using StaticCode.Icons;

namespace Mdu.IconSets;

internal static class Dashboard
{
    private sealed record Glyph(IconData Data, bool? Monochrome, bool LightLogo, bool DarkLogo, bool WhiteLogo);
    private sealed record Paint(double Luminance, bool Neutral);

    internal static JsonObject Convert(string checkout, string revision)
    {
        var glyphs = new SortedDictionary<string, Glyph>(StringComparer.Ordinal);
        var skipped = 0;
        foreach (var path in Directory.EnumerateFiles(Path.Combine(checkout, "svg"), "*.svg").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var source = File.ReadAllText(path);
            if (source.Length > 20000 || !Regex.IsMatch(name, "^[a-z0-9-]+$")) { skipped++; continue; }
            try
            {
                using var xml = XmlReader.Create(new StringReader(source), new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Parse, XmlResolver = null,
                    MaxCharactersFromEntities = 20000, MaxCharactersInDocument = 200000
                });
                var root = XElement.Load(xml);
                if (root.Name == "svg")
                    foreach (var element in root.DescendantsAndSelf().Where(e => e.Name.Namespace == XNamespace.None))
                        element.Name = XNamespace.Get("http://www.w3.org/2000/svg") + element.Name.LocalName;
                // The previous optimizer discarded explicitly hidden editor layers before checking images.
                foreach (var hidden in root.Descendants().Where(e =>
                             (string?)e.Attribute("display") == "none" ||
                             Regex.IsMatch((string?)e.Attribute("style") ?? "", "(?:^|;)\\s*display\\s*:\\s*none\\s*(?:;|$)", RegexOptions.IgnoreCase)).ToArray())
                    hidden.Remove();
                // Match the bundled icon contract: do not embed images, foreign markup or links.
                if (root.DescendantsAndSelf().Any(e => e.Name.LocalName is "image" or "foreignObject" or "iframe" or "a")) { skipped++; continue; }
                var paints = Profile(root, ignoreClipPath: false);
                var themePaints = Profile(root, ignoreClipPath: true);
                var monochrome = paints is { Count: > 0 } && paints.All(p => p.Neutral) &&
                    (paints.All(p => p.Luminance <= .03) || paints.All(p => p.Luminance >= .97));
                // Reuse the product's XML sanitizer and ID/CSS isolation rather than a second SVG parser.
                var data = SvgIconReader.ReadRoot(root, path, "di-" + name + "--");
                data = new IconData { Body = "<g fill=\"#000\">" + data.Body + "</g>", Left = data.Left, Top = data.Top, Width = data.Width, Height = data.Height };
                glyphs[name] = new Glyph(data, monochrome,
                    themePaints is { Count: > 0 } && themePaints.All(p => p.Luminance >= .25),
                    themePaints is { Count: > 0 } && themePaints.All(p => p.Luminance <= .2),
                    themePaints is { Count: > 0 } && themePaints.All(p => p.Luminance >= .9));
            }
            catch (Exception exception) when (exception is XmlException or InvalidOperationException) { skipped++; }
        }
        var themes = new Dictionary<string, (Glyph Light, Glyph Dark)>(StringComparer.Ordinal);
        foreach (var (name, glyph) in glyphs)
        {
            if (name.EndsWith("-light", StringComparison.Ordinal) || name.EndsWith("-dark", StringComparison.Ordinal)) continue;
            var light = glyphs.GetValueOrDefault(name + "-dark") ?? glyph;
            var dark = glyphs.GetValueOrDefault(name + "-light") ?? glyph;
            if (!ReferenceEquals(light, dark)) themes[name] = (light, dark);
        }
        foreach (var name in glyphs.Keys.Where(name => name.EndsWith("-dark", StringComparison.Ordinal)))
        {
            var key = name[..^5];
            if (!glyphs.ContainsKey(key) && glyphs.TryGetValue(key + "-light", out var dark)) themes[key] = (glyphs[name], dark);
        }
        foreach (var (name, pair) in themes.ToArray())
            if (pair.Light.LightLogo && pair.Dark.DarkLogo || pair.Light.WhiteLogo && !pair.Dark.WhiteLogo)
                themes[name] = (pair.Dark, pair.Light);
        var icons = new JsonObject();
        foreach (var name in glyphs.Keys.Concat(themes.Keys).Distinct().Order(StringComparer.Ordinal))
        {
            var themed = themes.TryGetValue(name, out var pair);
            var glyph = themed ? pair.Light : glyphs[name];
            var explicitVariant = name.EndsWith("-light", StringComparison.Ordinal) || name.EndsWith("-dark", StringComparison.Ordinal);
            var body = glyph.Data.Body;
            if (!themed && !explicitVariant && glyph.Monochrome == true)
                body = Regex.Replace(body, "\\b(fill|stroke)(=\"|\\s*:\\s*)(#[\\da-f]{3}(?:[\\da-f]{3})?|black|white)(?=[\";\\s])",
                    match => ReadPaint(match.Groups[3].Value) is { Neutral: true } paint && (paint.Luminance <= .03 || paint.Luminance >= .97)
                        ? match.Groups[1].Value + match.Groups[2].Value + "currentColor" : match.Value, RegexOptions.IgnoreCase);
            var icon = Data(glyph.Data, body);
            if (themed) icon["dark"] = Data(pair.Dark.Data, pair.Dark.Data.Body);
            if (name == "docker" || explicitVariant && themes.ContainsKey(name[..(name.EndsWith("-light", StringComparison.Ordinal) ? ^6 : ^5)])) icon["hidden"] = true;
            icons[name] = icon;
        }
        Console.WriteLine($"Converted {glyphs.Count} Dashboard Icons, {themes.Count} with theme variants; skipped {skipped}");
        return new JsonObject
        {
            ["name"] = "Dashboard Icons", ["version"] = revision[..Math.Min(7, revision.Length)],
            ["author"] = "Bjorn Lammers, Meier Lukas, Thomas Camlong and Homarr Labs", ["license"] = "Apache-2.0",
            ["licenseUrl"] = "https://github.com/homarr-labs/dashboard-icons/blob/main/LICENSE",
            ["width"] = 512, ["height"] = 512, ["icons"] = icons, ["aliases"] = new JsonObject()
        };
    }

    private static JsonObject Data(IconData data, string body)
    {
        var json = new JsonObject { ["body"] = body, ["width"] = data.Width, ["height"] = data.Height };
        if (data.Left != 0) json["left"] = data.Left;
        if (data.Top != 0) json["top"] = data.Top;
        return json;
    }

    private static List<Paint>? Profile(XElement root, bool ignoreClipPath)
    {
        var paints = new List<Paint>();
        var simple = true;
        Visit(root, "#000", "none");
        return simple && paints.Count > 0 ? paints : null;
        void Visit(XElement element, string fill, string stroke)
        {
            var name = element.Name.LocalName;
            if (name is "title" or "desc" or "metadata" || ignoreClipPath && name is "defs" or "clipPath") return;
            if (name is not ("svg" or "g" or "path" or "rect" or "circle" or "ellipse" or "polygon" or "polyline" or "line") ||
                element.Attribute("mask") is not null || element.Attribute("filter") is not null || !ignoreClipPath && element.Attribute("clip-path") is not null)
            { simple = false; return; }
            var styles = ((string?)element.Attribute("style") ?? "").Split(';').Select(s => s.Split(':', 2)).Where(s => s.Length == 2)
                .GroupBy(s => s[0].Trim(), StringComparer.Ordinal).ToDictionary(s => s.Key, s => s.Last()[1].Trim(), StringComparer.Ordinal);
            if (styles.ContainsKey("mask") || styles.ContainsKey("filter") || !ignoreClipPath && styles.ContainsKey("clip-path")) simple = false;
            fill = styles.GetValueOrDefault("fill") ?? (string?)element.Attribute("fill") ?? fill;
            stroke = styles.GetValueOrDefault("stroke") ?? (string?)element.Attribute("stroke") ?? stroke;
            if (name is not ("svg" or "g"))
                foreach (var value in name == "line" ? new[] { stroke } : new[] { fill, stroke })
                {
                    if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
                    if (ReadPaint(value) is { } paint) paints.Add(paint); else simple = false;
                }
            foreach (var child in element.Elements()) Visit(child, fill, stroke);
        }
    }

    private static Paint? ReadPaint(string text)
    {
        text = text.Trim().ToLowerInvariant() switch { "black" => "#000000", "white" => "#ffffff", var value => value };
        if (Regex.IsMatch(text, "^#[0-9a-f]{3}$")) text = "#" + string.Concat(text[1..].Select(c => new string(c, 2)));
        if (!Regex.IsMatch(text, "^#[0-9a-f]{6}$")) return null;
        var rgb = Enumerable.Range(0, 3).Select(i => int.Parse(text.AsSpan(1 + i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d).ToArray();
        var luminance = rgb.Select((value, i) => (value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4)) * new[] { .2126, .7152, .0722 }[i]).Sum();
        return new Paint(luminance, rgb.Max() - rgb.Min() < .04);
    }
}
