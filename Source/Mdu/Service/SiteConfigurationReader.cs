using Markdown.Renderer.Res.Model;
using StaticCode.Icons;
using YamlDotNet.RepresentationModel;

namespace Mdu.Service;

public static class SiteConfigurationReader
{
    public const string DefaultIconsFolder = ".icons";

    public static SiteConfiguration Read(string rootPath)
    {
        var path = Path.Combine(rootPath, "mdu.yml");
        if (!File.Exists(path)) return new SiteConfiguration();

        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(path)));
        if (stream.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root) return new SiteConfiguration();

        var configuration = new SiteConfiguration
        {
            SiteName = ReadScalar(root, "site_name") ?? "Documentation",
            AttributionVisible = ReadAttributionVisible(root),
            Glossary = ReadGlossary(root),
            Icons = ReadIcons(root),
            LogoLight = ReadAsset(root, "logo", "light"),
            LogoDark = ReadAsset(root, "logo", "dark"),
            FaviconLight = ReadAsset(root, "favicon", "light"),
            FaviconDark = ReadAsset(root, "favicon", "dark"),
            Light = ReadPalette(root, "light"),
            Dark = ReadPalette(root, "dark")
        };
        configuration.LogoDark ??= configuration.LogoLight;
        if (configuration.FaviconLight is null && configuration.FaviconDark is null)
        {
            configuration.FaviconLight = configuration.LogoLight;
            configuration.FaviconDark = configuration.LogoDark;
        }
        else
        {
            configuration.FaviconLight ??= configuration.LogoLight;
        }
        configuration.FaviconDark ??= configuration.FaviconLight;
        return configuration;
    }

    public static string? ResolveGlossaryPath(string rootPath, SiteConfiguration configuration)
    {
        var path = GetGlossaryPath(rootPath, configuration);
        if (path is null) return null;
        if (!File.Exists(path))
            throw new InvalidOperationException($"Configured glossary file does not exist: {path}");
        return path;
    }

    public static string? GetGlossaryPath(string rootPath, SiteConfiguration configuration) =>
        configuration.Glossary is null ? null : Path.GetFullPath(configuration.Glossary, rootPath);

    public static string? TryResolveGlossaryPath(string rootPath)
    {
        try
        {
            return ResolveGlossaryPath(rootPath, Read(rootPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    public static string? ReadGlossary(string rootPath, SiteConfiguration configuration)
    {
        var path = ResolveGlossaryPath(rootPath, configuration);
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// Returns the folder of the custom icon sets: the <c>icons</c> setting, or the <c>.icons</c> folder of the root
    /// when it exists.
    /// </summary>
    public static string? GetIconsPath(string rootPath, SiteConfiguration configuration)
    {
        if (configuration.Icons is not null) return Path.GetFullPath(configuration.Icons, rootPath);
        var defaultPath = Path.Combine(Path.GetFullPath(rootPath), DefaultIconsFolder);
        return Directory.Exists(defaultPath) ? defaultPath : null;
    }

    /// <summary>Makes the custom icon sets of the site available to icon shortcodes.</summary>
    public static void UseCustomIcons(string rootPath, SiteConfiguration configuration)
    {
        var path = GetIconsPath(rootPath, configuration);
        if (path is not null && !Directory.Exists(path))
            throw new InvalidOperationException($"Configured icons folder does not exist: {path}");
        IconCatalog.UseCustomIcons(path);
    }

    public static void CopyAssets(string rootPath, string outputPath, SiteConfiguration configuration)
    {
        foreach (var asset in new[] { configuration.LogoLight, configuration.LogoDark, configuration.FaviconLight, configuration.FaviconDark }
                     .Where(asset => !string.IsNullOrWhiteSpace(asset)).Distinct(StringComparer.Ordinal))
        {
            if (Uri.TryCreate(asset, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") continue;
            var source = Path.GetFullPath(asset!, rootPath);
            var relative = Path.GetRelativePath(rootPath, source);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            if (!File.Exists(source)) continue;
            var target = Path.Combine(outputPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, true);
        }
    }

    private static bool ReadAttributionVisible(YamlMappingNode root)
    {
        bool? nested = null;
        if (root.Children.TryGetValue(new YamlScalarNode("attribution"), out var attribution))
        {
            if (attribution is YamlMappingNode mapping)
            {
                if (mapping.Children.TryGetValue(new YamlScalarNode("visible"), out var value))
                    nested = ReadVisible(value);
            }
            else if (attribution is not YamlScalarNode { Value: null or "" })
            {
                throw new InvalidOperationException("The 'attribution' setting in mdu.yml must be a mapping with a 'visible' boolean.");
            }
        }
        bool? compact = root.Children.TryGetValue(new YamlScalarNode("attribution.visible"), out var compactValue)
            ? ReadVisible(compactValue) : null;
        if (nested.HasValue && compact.HasValue && nested != compact)
            throw new InvalidOperationException("The 'attribution.visible' setting in mdu.yml has conflicting nested and compact values.");
        return compact ?? nested ?? true;
    }

    private static bool ReadVisible(YamlNode value)
    {
        if (value is YamlScalarNode scalar && bool.TryParse(scalar.Value, out var visible)) return visible;
        throw new InvalidOperationException("The 'attribution.visible' setting in mdu.yml must be true or false.");
    }

    private static SitePalette? ReadPalette(YamlMappingNode root, string name)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode("colors"), out var colorsNode) || colorsNode is not YamlMappingNode colors ||
            !colors.Children.TryGetValue(new YamlScalarNode(name), out var paletteNode) || paletteNode is not YamlMappingNode palette)
            return null;
        return new SitePalette
        {
            Background = ReadScalar(palette, "background"),
            Text = ReadScalar(palette, "text"),
            Primary = ReadScalar(palette, "primary")
        };
    }

    private static string? ReadAsset(YamlMappingNode root, string key, string theme)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode(key), out var node)) return null;
        if (node is YamlScalarNode scalar) return scalar.Value;
        if (node is YamlMappingNode mapping) return ReadScalar(mapping, theme);
        return null;
    }

    private static string? ReadGlossary(YamlMappingNode root)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode("glossary"), out var node)) return null;
        if (node is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value))
            throw new InvalidOperationException("The 'glossary' setting in mdu.yml must be a Markdown file path.");
        return scalar.Value;
    }

    private static string? ReadIcons(YamlMappingNode root)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode("icons"), out var node)) return null;
        if (node is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value))
            throw new InvalidOperationException("The 'icons' setting in mdu.yml must be a folder path.");
        return scalar.Value;
    }

    private static string? ReadScalar(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar
            ? scalar.Value
            : null;
}
