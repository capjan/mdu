namespace Markdown.Renderer;

/// <summary>
/// Light-mode colors of isometric diagrams for the PDF. The HTML version themes them with CSS custom properties and
/// <c>color-mix</c>, which the PDF drawer cannot resolve, so the same mixes are computed here against a white page.
/// </summary>
internal static class PdfIsometricStyle
{
    private const string Ink = "#1d1d1f";
    private const string Secondary = "#6e6e73";
    private const string Icon = "#2c2c30";
    private const string White = "#ffffff";

    private static readonly Dictionary<string, (int R, int G, int B)> Hues = new(StringComparer.Ordinal)
    {
        ["gray"] = (0x8e, 0x8e, 0x93),
        ["blue"] = (0x3b, 0x78, 0xc4),
        ["teal"] = (0x2a, 0x9d, 0x8f),
        ["green"] = (0x3d, 0x9a, 0x50),
        ["purple"] = (0x8a, 0x5c, 0xc0),
        ["orange"] = (0xd9, 0x82, 0x2b),
        ["red"] = (0xd0, 0x52, 0x4a),
    };

    private static bool IsIsometric(string classes) =>
        classes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(token => token.StartsWith("iso-", StringComparison.Ordinal));

    public static string? Fill(string classes)
    {
        if (!IsIsometric(classes)) return null;
        if (Has(classes, "iso-edge-line")) return "none";
        if (Has(classes, "iso-edge-end") || Has(classes, "iso-particle") || Has(classes, "iso-edge")) return EdgeColor(classes);
        if (Has(classes, "iso-label-background")) return White;
        if (Has(classes, "iso-icon")) return Icon;
        if (Has(classes, "iso-face-top")) return Mix(classes, FacePercent(classes, 20, 7), 255);
        if (Has(classes, "iso-face-left")) return Mix(classes, FacePercent(classes, 34, 18), 255);
        if (Has(classes, "iso-face-right")) return Mix(classes, FacePercent(classes, 46, 26), 255);
        return null;
    }

    public static string? Stroke(string classes)
    {
        if (!IsIsometric(classes)) return null;
        if (Has(classes, "iso-icon") || Has(classes, "iso-edge-end") || Has(classes, "iso-particle")) return "none";
        if (Has(classes, "iso-edge")) return EdgeColor(classes);
        if (Has(classes, "iso-grid")) return Mix(classes, 18, 255);
        if (Has(classes, "iso-platform")) return Mix(classes, 32, 255);
        if (Has(classes, "iso-node") || Has(classes, "iso-block")) return Mix(classes, 60, 255);
        return null;
    }

    public static string? Text(string classes)
    {
        if (!IsIsometric(classes)) return null;
        return Has(classes, "iso-edge-label") || Has(classes, "iso-zone-subtitle") ? Secondary : Has(classes, "iso-zone-title") || Has(classes, "iso-label") ? Ink : null;
    }

    public static double? StrokeWidth(string classes)
    {
        if (!IsIsometric(classes)) return null;
        if (Has(classes, "iso-edge-line")) return Has(classes, "iso-edge-thick") ? 3 : 1.75;
        if (Has(classes, "iso-grid")) return 0.75;
        if (Has(classes, "iso-platform") || Has(classes, "iso-node") || Has(classes, "iso-block")) return 1;
        return null;
    }

    private static bool Has(string classes, string name) =>
        classes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal);

    // Platform faces are tinted much lighter than node faces; the percentages are the HTML ones for each kind.
    private static int FacePercent(string classes, int node, int platform) => Has(classes, "iso-platform") ? platform : node;

    private static string EdgeColor(string classes) => Mix(classes, 88, 0);

    /// <summary>Mixes the tone hue of the element into the page color, like <c>color-mix(in srgb, hue N%, base)</c>.</summary>
    private static string Mix(string classes, int percent, int baseChannel)
    {
        var hue = Hue(classes);
        return $"#{Channel(hue.R, percent, baseChannel):X2}{Channel(hue.G, percent, baseChannel):X2}{Channel(hue.B, percent, baseChannel):X2}";
    }

    private static int Channel(int hue, int percent, int baseChannel) =>
        (int)Math.Round(hue * percent / 100.0 + baseChannel * (100 - percent) / 100.0);

    private static (int R, int G, int B) Hue(string classes)
    {
        var tone = classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(token => token.StartsWith("iso-tone-", StringComparison.Ordinal));
        return tone is not null && Hues.TryGetValue(tone["iso-tone-".Length..], out var hue) ? hue : Hues["gray"];
    }
}
