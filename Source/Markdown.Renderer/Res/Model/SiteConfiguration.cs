namespace Markdown.Renderer.Res.Model;

public sealed class SiteConfiguration
{
    public string SiteName { get; set; } = "Documentation";
    public bool AttributionVisible { get; set; } = true;
    public string? Glossary { get; set; }
    public string? Icons { get; set; }
    public string? Tags { get; set; }
    /// <summary>The <c>content.code.annotate</c> setting: whether code blocks show annotations. Pages and blocks can override it.</summary>
    public bool CodeAnnotations { get; set; } = true;
    public int? MaxDiagramElements { get; set; }
    public int? MaxDiagramLinks { get; set; }
    public string? LogoLight { get; set; }
    public string? LogoDark { get; set; }
    public string? FaviconLight { get; set; }
    public string? FaviconDark { get; set; }
    public SitePalette? Light { get; set; }
    public SitePalette? Dark { get; set; }
}

public sealed class SitePalette
{
    public string? Background { get; set; }
    public string? Text { get; set; }
    public string? Primary { get; set; }
}
