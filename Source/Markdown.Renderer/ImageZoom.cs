using System.Text.Encodings.Web;
using YamlDotNet.RepresentationModel;

namespace Markdown.Renderer;

/// <summary>Reads the <c>images.zoom</c> front matter and publishes the image viewer's script.</summary>
public static class ImageZoom
{
    public const string ScriptFileName = "image-lightbox.js";
    private const string EnabledClass = "image-zoom-on";

    /// <summary>Returns the page's <c>images.zoom</c> setting, nested or dotted. Pages zoom images by default.</summary>
    public static bool ReadFrontMatter(string? frontMatter) =>
        ReadMetadata(frontMatter) is not { } metadata
        || FrontMatterYaml.FindSetting(metadata, "images", "zoom") is not YamlScalarNode scalar
        || !bool.TryParse(scalar.Value, out var zoom)
        || zoom;

    /// <summary>Describes what is wrong with a page's <c>images.zoom</c> setting, or returns null when it is valid or absent.</summary>
    public static string? FindProblem(string? frontMatter)
    {
        if (ReadMetadata(frontMatter) is not { } metadata) return null;
        return FrontMatterYaml.FindSetting(metadata, "images", "zoom") switch
        {
            null => FindMisplacedSwitch(metadata),
            YamlScalarNode scalar when bool.TryParse(scalar.Value, out _) => null,
            YamlScalarNode scalar => InvalidValue(scalar.Value ?? string.Empty),
            var node => InvalidValue(node.ToString()),
        };
    }

    private static string InvalidValue(string value) => $"images.zoom must be 'true' or 'false', not '{value}'.";

    internal static IReadOnlyList<string> WriteAssetsTo(string outputPath)
    {
        var scriptDirectory = Path.Combine(outputPath, "js");
        Directory.CreateDirectory(scriptDirectory);
        var scriptPath = Path.Combine(scriptDirectory, ScriptFileName);
        File.WriteAllText(scriptPath, Resources.ImageLightboxScriptContent());
        return new[] { scriptPath };
    }

    /// <summary>Returns the viewer's script tag, or nothing when the page has no image or turns the viewer off without an exception.</summary>
    internal static string ScriptFor(string renderedMarkdown, bool pageZoom, string prefix)
    {
        if (!renderedMarkdown.Contains("<img", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (!pageZoom && !renderedMarkdown.Contains(EnabledClass, StringComparison.Ordinal)) return string.Empty;

        var source = HtmlEncoder.Default.Encode(prefix + "js/" + ScriptFileName);
        var setting = pageZoom ? string.Empty : " data-image-zoom=\"off\"";
        return $"<script defer src=\"{source}\"{setting}></script>{Environment.NewLine}";
    }

    private static YamlMappingNode? ReadMetadata(string? frontMatter) =>
        frontMatter is not null && frontMatter.Contains("images", StringComparison.Ordinal)
            ? FrontMatterYaml.ReadMapping(frontMatter)
            : null;

    /// <summary>Finds <c>images: false</c>, a likely typo. Other <c>images</c> values can be metadata of the page, such as a list of pictures.</summary>
    private static string? FindMisplacedSwitch(YamlMappingNode metadata) =>
        metadata.Children.TryGetValue(new YamlScalarNode("images"), out var node) && node is YamlScalarNode scalar && bool.TryParse(scalar.Value, out _)
            ? $"Use 'images.zoom: {scalar.Value}' instead of 'images: {scalar.Value}'."
            : null;
}
