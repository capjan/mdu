using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StaticCode.Icons;

/// <summary>
/// An icon set in the reduced Iconify JSON format written by <c>update-icon-sets.sh</c>.
/// </summary>
internal sealed class IconSetData
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly IconSetJsonContext JsonContext = new(JsonOptions);

    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string License { get; init; } = string.Empty;
    public string LicenseUrl { get; init; } = string.Empty;
    public double Width { get; init; } = 16;
    public double Height { get; init; } = 16;
    public Dictionary<string, IconData> Icons { get; init; } = new();
    public Dictionary<string, string> Aliases { get; init; } = new();

    public static IconSetData Load(string resourceName)
    {
        using var resource = typeof(IconSetData).Assembly.GetManifestResourceStream($"StaticCode.Icons.Res.{resourceName}.json.gz")
            ?? throw new InvalidOperationException($"The icon set '{resourceName}' is missing.");
        using var json = new GZipStream(resource, CompressionMode.Decompress);
        return JsonSerializer.Deserialize(json, JsonContext.IconSetData)
            ?? throw new InvalidOperationException($"The icon set '{resourceName}' is empty.");
    }
}

internal sealed class IconData
{
    public string Body { get; init; } = string.Empty;
    public double Left { get; init; }
    public double Top { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public bool Hidden { get; init; }

    /// <summary>The variant for the dark theme, if the icon has light and dark variants.</summary>
    public IconData? Dark { get; init; }

    public IconData WithDark(IconData dark) => new()
    {
        Body = Body, Left = Left, Top = Top, Width = Width, Height = Height, Hidden = Hidden, Dark = dark
    };
}

[JsonSerializable(typeof(IconSetData))]
internal partial class IconSetJsonContext : JsonSerializerContext
{
}
