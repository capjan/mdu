using Markdown.Renderer.Res.Model;
using StaticCode.Diagrams;
using StaticCode.Diagrams.Component;
using StaticCode.Icons;
using YamlDotNet.RepresentationModel;

namespace Mdu.Service;

public static class SiteConfigurationReader
{
    public const string DefaultIconsFolder = ".icons";

    private static readonly HashSet<string> KnownSettings =
    [
        "site.name", "site.logo", "site.logo.light", "site.logo.dark", "site.favicon", "site.favicon.light", "site.favicon.dark",
        "site.attribution.visible",
        "site.colors.light.background", "site.colors.light.text", "site.colors.light.primary",
        "site.colors.dark.background", "site.colors.dark.text", "site.colors.dark.primary",
        "content.glossary", "content.icons", "content.tags",
        "diagrams.limits.elements", "diagrams.limits.links"
    ];

    // Names of earlier versions, so that an old file gets a precise error instead of silently losing its settings.
    private static readonly (string Old, string Replacement)[] RenamedSettings =
    [
        ("site_name", "site.name"), ("logo", "site.logo"), ("favicon", "site.favicon"), ("colors", "site.colors"),
        ("attribution", "site.attribution"), ("glossary", "content.glossary"), ("icons", "content.icons"),
        ("diagrams.maxElements", "diagrams.limits.elements"), ("diagrams.maxLinks", "diagrams.limits.links")
    ];

    public static SiteConfiguration Read(string rootPath) => Read(rootPath, includeHtmlSettings: true);

    public static SiteConfiguration ReadForContent(string rootPath) => Read(rootPath, includeHtmlSettings: false);

    private static SiteConfiguration Read(string rootPath, bool includeHtmlSettings)
    {
        var path = Path.Combine(rootPath, "mdu.yml");
        if (!File.Exists(path)) return new SiteConfiguration();

        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(path)));
        if (stream.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root) return new SiteConfiguration();

        var settings = ReadSettings(root, out var conflicts);
        if (DescribeRenamedSettings(settings) is { } renamed) throw new InvalidOperationException(renamed);
        if (includeHtmlSettings) Validate(settings, conflicts);
        var configuration = new SiteConfiguration
        {
            SiteName = ReadScalar(settings, "site.name") ?? "Documentation",
            AttributionVisible = !includeHtmlSettings || ReadAttributionVisible(settings),
            Glossary = ReadGlossary(settings),
            Icons = ReadIcons(settings),
            Tags = ReadTagsSetting(settings),
            MaxDiagramElements = ReadDiagramLimit(settings, "elements"),
            MaxDiagramLinks = ReadDiagramLimit(settings, "links"),
            LogoLight = ReadAsset(settings, "site.logo", "light"),
            LogoDark = ReadAsset(settings, "site.logo", "dark"),
            FaviconLight = ReadAsset(settings, "site.favicon", "light"),
            FaviconDark = ReadAsset(settings, "site.favicon", "dark"),
            Light = ReadPalette(settings, "light"),
            Dark = ReadPalette(settings, "dark")
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

    /// <summary>Returns the tag vocabulary file of <c>content.tags</c>, or null when the site has none.</summary>
    /// <exception cref="InvalidOperationException">The configured file does not exist.</exception>
    public static string? ResolveTagsPath(string rootPath, SiteConfiguration configuration)
    {
        if (configuration.Tags is null) return null;
        var path = Path.GetFullPath(configuration.Tags, rootPath);
        if (!File.Exists(path))
            throw new InvalidOperationException($"Configured tag vocabulary does not exist: {path}");
        return path;
    }

    /// <summary>Reads only the <c>content.glossary</c> setting, so errors in unrelated settings of the file do not matter.</summary>
    public static string? ReadGlossarySetting(string rootPath)
    {
        var path = Path.Combine(rootPath, "mdu.yml");
        if (!File.Exists(path)) return null;

        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(path)));
        if (stream.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root) return null;
        var settings = ReadSettings(root, out _);
        if (settings.ContainsKey("glossary"))
            throw new InvalidOperationException("The 'glossary' setting in mdu.yml is now 'content.glossary'.");
        return ReadGlossary(settings);
    }

    /// <summary>
    /// For the commands that also run when the file has errors: they ignore a file with renamed settings, so they say why.
    /// </summary>
    public static void WarnAboutRenamedSettings(string rootPath)
    {
        if (DescribeRenamedSettings(rootPath) is { } renamed)
            Console.Error.WriteLine($"Warning: {renamed} MDU ignores the file until they are updated.");
    }

    /// <summary>Names the renamed settings of the file, or returns null when it has none or cannot be read.</summary>
    public static string? DescribeRenamedSettings(string rootPath)
    {
        var path = Path.Combine(rootPath, "mdu.yml");
        if (!File.Exists(path)) return null;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(File.ReadAllText(path)));
            return stream.Documents.FirstOrDefault()?.RootNode is YamlMappingNode root
                ? DescribeRenamedSettings(ReadSettings(root, out _))
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    public static string? TryResolveGlossaryPath(string rootPath)
    {
        try
        {
            return ResolveGlossaryPath(rootPath, ReadForContent(rootPath));
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
    /// Returns the folder of the custom icon sets: the <c>content.icons</c> setting, or the <c>.icons</c> folder of the root
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

    /// <summary>Applies the <c>diagrams.limits</c> of the site, or the defaults without them.</summary>
    public static void UseDiagramLimits(SiteConfiguration configuration) =>
        DiagramRenderer.UseComponentDiagramLimits(
            configuration.MaxDiagramElements ?? ComponentDiagram.DefaultMaxElements,
            configuration.MaxDiagramLinks ?? ComponentDiagram.DefaultMaxLinks);

    /// <summary>For mdu lint, which also runs when the file has errors in unrelated settings: the limits then stay the defaults.</summary>
    public static void TryUseDiagramLimits(string rootPath)
    {
        try
        {
            UseDiagramLimits(ReadForContent(rootPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            DiagramRenderer.UseComponentDiagramLimits();
        }
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

    /// <summary>
    /// Flattens the file to full setting paths, so that <c>site:</c> with a nested <c>name:</c> and the compact
    /// <c>site.name:</c> are the same setting. Paths given twice with different values are reported as conflicts.
    /// </summary>
    internal static Dictionary<string, YamlNode> ReadSettings(YamlMappingNode root, out IReadOnlyList<string> conflicts)
    {
        var settings = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
        var found = new List<string>();
        Add(root, string.Empty);
        conflicts = found;
        return settings;

        void Add(YamlMappingNode mapping, string prefix)
        {
            foreach (var (key, value) in mapping.Children)
            {
                if (key is not YamlScalarNode { Value: { } name }) continue;
                var path = prefix + name;
                if (value is YamlMappingNode nested)
                    Add(nested, path + ".");
                else if (!settings.TryAdd(path, value) && !IsSameValue(path, settings[path], value))
                    found.Add(path);
            }
        }
    }

    // "True" and "true" are the same value for a setting that reads a boolean, and "03" and "3" for one that reads a number.
    private static bool IsSameValue(string path, YamlNode left, YamlNode right)
    {
        if (Equals(left, right)) return true;
        if (left is not YamlScalarNode { Value: var first } || right is not YamlScalarNode { Value: var second }) return false;
        return path switch
        {
            "site.attribution.visible" => bool.TryParse(first, out var a) && bool.TryParse(second, out var b) && a == b,
            "diagrams.limits.elements" or "diagrams.limits.links" => TryParseLimit(first, out var a) && TryParseLimit(second, out var b) && a == b,
            _ => false
        };
    }

    private static void Validate(Dictionary<string, YamlNode> settings, IReadOnlyList<string> conflicts)
    {
        if (conflicts.Count > 0)
            throw new InvalidOperationException($"The '{conflicts[0]}' setting in mdu.yml has conflicting nested and compact values.");

        foreach (var (path, value) in settings)
        {
            if (KnownSettings.Contains(path))
            {
                if (value is not YamlScalarNode)
                    throw new InvalidOperationException($"The '{path}' setting in mdu.yml must be a single value.");
                continue;
            }
            if (!KnownSettings.Any(known => known.StartsWith(path + ".", StringComparison.Ordinal)))
                throw new InvalidOperationException($"Unknown setting '{path}' in mdu.yml.");
            if (!IsYamlNull(value))
                throw new InvalidOperationException($"The '{path}' setting in mdu.yml groups other settings and cannot have a value of its own.");
        }

        foreach (var key in new[] { "site.logo", "site.favicon" })
            if (ReadScalar(settings, key) is not null && (settings.ContainsKey($"{key}.light") || settings.ContainsKey($"{key}.dark")))
                throw new InvalidOperationException($"The '{key}' setting in mdu.yml must be one value or 'light' and 'dark', not both.");
    }

    private static string? DescribeRenamedSettings(Dictionary<string, YamlNode> settings)
    {
        var renamed = settings.Keys
            .Select(path => (Path: path, Replacement: FindReplacement(path)))
            .Where(setting => setting.Replacement is not null)
            .Select(setting => $"'{setting.Path}' is now '{setting.Replacement}'")
            .ToArray();
        return renamed.Length > 0 ? $"mdu.yml uses renamed settings: {string.Join(", ", renamed)}." : null;
    }

    private static string? FindReplacement(string path)
    {
        foreach (var (old, replacement) in RenamedSettings)
        {
            if (path == old) return replacement;
            if (path.StartsWith(old + ".", StringComparison.Ordinal)) return replacement + path[old.Length..];
        }
        return null;
    }

    private static bool ReadAttributionVisible(Dictionary<string, YamlNode> settings)
    {
        if (!settings.TryGetValue("site.attribution.visible", out var value)) return true;
        if (value is YamlScalarNode scalar && bool.TryParse(scalar.Value, out var visible)) return visible;
        throw new InvalidOperationException("The 'site.attribution.visible' setting in mdu.yml must be true or false.");
    }

    private static bool IsYamlNull(YamlNode node) =>
        node is YamlScalarNode scalar && scalar.Style == YamlDotNet.Core.ScalarStyle.Plain &&
        scalar.Value is null or "" or "~" or "null" or "Null" or "NULL";

    private static SitePalette? ReadPalette(Dictionary<string, YamlNode> settings, string theme)
    {
        var prefix = $"site.colors.{theme}.";
        if (!settings.Keys.Any(path => path.StartsWith(prefix, StringComparison.Ordinal))) return null;
        return new SitePalette
        {
            Background = ReadScalar(settings, prefix + "background"),
            Text = ReadScalar(settings, prefix + "text"),
            Primary = ReadScalar(settings, prefix + "primary")
        };
    }

    // One value applies to both themes; "light" and "dark" set them apart.
    private static string? ReadAsset(Dictionary<string, YamlNode> settings, string key, string theme) =>
        ReadScalar(settings, key) ?? ReadScalar(settings, $"{key}.{theme}");

    private static string? ReadGlossary(Dictionary<string, YamlNode> settings)
    {
        if (!settings.TryGetValue("content.glossary", out var node)) return null;
        if (node is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value))
            throw new InvalidOperationException("The 'content.glossary' setting in mdu.yml must be a Markdown file path.");
        return scalar.Value;
    }

    private static string? ReadTagsSetting(Dictionary<string, YamlNode> settings)
    {
        if (!settings.TryGetValue("content.tags", out var node)) return null;
        if (node is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value))
            throw new InvalidOperationException("The 'content.tags' setting in mdu.yml must be a Markdown file path.");
        return scalar.Value;
    }

    private static string? ReadIcons(Dictionary<string, YamlNode> settings)
    {
        if (!settings.TryGetValue("content.icons", out var node)) return null;
        if (node is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value))
            throw new InvalidOperationException("The 'content.icons' setting in mdu.yml must be a folder path.");
        return scalar.Value;
    }

    private static int? ReadDiagramLimit(Dictionary<string, YamlNode> settings, string key)
    {
        if (!settings.TryGetValue($"diagrams.limits.{key}", out var node)) return null;
        if (node is YamlScalarNode scalar && TryParseLimit(scalar.Value, out var limit)) return limit;
        throw new InvalidOperationException($"The 'diagrams.limits.{key}' setting in mdu.yml must be a whole number, or 0 for no limit.");
    }

    private static bool TryParseLimit(string? value, out int limit) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out limit);

    private static string? ReadScalar(Dictionary<string, YamlNode> settings, string path) =>
        settings.TryGetValue(path, out var value) && value is YamlScalarNode scalar ? scalar.Value : null;
}
