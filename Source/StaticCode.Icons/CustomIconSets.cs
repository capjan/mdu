using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace StaticCode.Icons;

/// <summary>
/// Reads custom icon sets from SVG files, following the <c>.icons</c> folder of Material for MkDocs: every folder in
/// the icons directory is a set, and <c>acme/cloud/server.svg</c> becomes the shortcode <c>acme-cloud-server</c>.
/// A file named <c>server.dark.svg</c> is the dark theme variant of <c>server.svg</c>.
/// </summary>
internal static partial class CustomIconSets
{
    private const string DarkSuffix = ".dark";

    public static IReadOnlyList<IconSet> Load(string directory)
    {
        var root = Path.GetFullPath(directory);
        var stray = Directory.EnumerateFiles(root, "*.svg").FirstOrDefault();
        if (stray is not null)
            throw new InvalidOperationException(
                $"Custom icons must be in a folder named after their icon set, for example acme/{Path.GetFileName(stray)}: {stray}");

        return Directory.EnumerateDirectories(root)
            .Where(path => !Path.GetFileName(path).StartsWith('.'))
            .Order(StringComparer.Ordinal)
            .Select(LoadSet)
            .ToArray();
    }

    private static IconSet LoadSet(string directory)
    {
        var setName = Path.GetFileName(directory);
        ValidateName(setName, directory);

        var files = Directory.EnumerateFiles(directory, "*.svg", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar).Any(part => part.StartsWith('.')))
            .ToDictionary(path => GetIconName(directory, path), StringComparer.Ordinal);

        var icons = new Dictionary<string, IconData>(StringComparer.Ordinal);
        foreach (var (name, path) in files.Where(file => !file.Key.EndsWith(DarkSuffix, StringComparison.Ordinal)).OrderBy(file => file.Key, StringComparer.Ordinal))
        {
            ValidateName(name, path);
            var idPrefix = $"{setName}-{name}-";
            var light = SvgIconReader.Read(path, idPrefix);
            icons[name] = files.TryGetValue(name + DarkSuffix, out var darkPath)
                ? light.WithDark(SvgIconReader.Read(darkPath, idPrefix + "dark-"))
                : light;
        }

        var orphan = files.Keys.FirstOrDefault(name => name.EndsWith(DarkSuffix, StringComparison.Ordinal) && !icons.ContainsKey(name[..^DarkSuffix.Length]));
        if (orphan is not null)
            throw new InvalidOperationException($"The dark icon variant has no light icon next to it: {files[orphan]}");

        var data = new IconSetData { Name = setName, Icons = icons };
        return new IconSet(setName + "-", data, website: string.Empty);
    }

    private static string GetIconName(string setDirectory, string path)
    {
        var relative = Path.GetRelativePath(setDirectory, path);
        return relative[..^".svg".Length].Replace(Path.DirectorySeparatorChar, '-');
    }

    private static void ValidateName(string name, string path)
    {
        if (!ValidName().IsMatch(name))
            throw new InvalidOperationException(
                $"Custom icon names may contain only letters, digits, '-', '_', and '+': {path}");
    }

    [GeneratedRegex("^[A-Za-z0-9_+-]+$")]
    private static partial Regex ValidName();
}

/// <summary>
/// Turns an SVG file into the body of an inline icon. Scripts, event handlers, external references, and metadata of
/// editors are removed. Identifiers get a prefix, and so do class names when the SVG has its own style sheet, so that
/// several icons on one page do not affect each other.
/// </summary>
internal static partial class SvgIconReader
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    private static readonly HashSet<string> RemovedElements = new(StringComparer.Ordinal)
    {
        "script", "foreignObject", "title", "desc", "metadata"
    };

    // Attributes of the root element that only size or identify the SVG; the others, such as fill or stroke, are kept.
    private static readonly HashSet<string> RootOnlyAttributes = new(StringComparer.Ordinal)
    {
        "width", "height", "viewBox", "version", "baseProfile", "x", "y", "id", "preserveAspectRatio", "enable-background"
    };

    public static IconData Read(string path, string idPrefix)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            throw new InvalidOperationException($"The custom icon is not a valid SVG file: {path} ({exception.Message})", exception);
        }

        var root = document.Root;
        if (root is null || root.Name != Svg + "svg")
            throw new InvalidOperationException($"The custom icon is not an SVG file: {path}");

        var (left, top, width, height) = ReadViewBox(root)
            ?? throw new InvalidOperationException($"The custom icon needs a viewBox or a width and height: {path}");

        var prefix = Identifier().Replace(idPrefix, "_");
        var prefixClasses = root.Descendants(Svg + "style").Any();
        var context = new CleanContext(prefix, prefixClasses);
        var children = root.Nodes().Select(node => Clean(node, context)).OfType<XNode>().ToArray();

        // Presentation attributes of the root, such as fill="none" or stroke="currentColor", move to a group.
        var rootAttributes = CleanAttributes(root, context, isRoot: true).ToArray();
        var body = rootAttributes.Length == 0
            ? string.Concat(children.Select(Serialize))
            : Serialize(new XElement("g", rootAttributes, children));

        return new IconData { Body = body, Left = left, Top = top, Width = width, Height = height };
    }

    private static string Serialize(XNode node) => node.ToString(SaveOptions.DisableFormatting);

    private static XNode? Clean(XNode node, CleanContext context)
    {
        switch (node)
        {
            case XElement element:
                if (element.Name.Namespace != Svg || RemovedElements.Contains(element.Name.LocalName)) return null;
                var clean = new XElement(element.Name.LocalName, CleanAttributes(element, context, isRoot: false));
                if (element.Name.LocalName == "style")
                    clean.Add(new XText(RewriteStyleSheet(element.Value, context)));
                else
                    clean.Add(element.Nodes().Select(child => Clean(child, context)).OfType<XNode>());
                return clean;
            case XText text:
                return string.IsNullOrWhiteSpace(text.Value) ? null : new XText(text.Value);
            default:
                return null;
        }
    }

    private static IEnumerable<XAttribute> CleanAttributes(XElement element, CleanContext context, bool isRoot)
    {
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration) continue;

            string name;
            if (attribute.Name.Namespace == XLink && attribute.Name.LocalName == "href") name = "href";
            else if (attribute.Name.Namespace == XNamespace.None) name = attribute.Name.LocalName;
            else continue;

            if (isRoot && RootOnlyAttributes.Contains(name)) continue;
            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase)) continue;
            if (name == "href" && !attribute.Value.StartsWith('#') && !attribute.Value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return new XAttribute(name, RewriteValue(name, attribute.Value, context));
        }
    }

    private static string RewriteValue(string name, string value, CleanContext context) => name switch
    {
        "id" => context.Prefix + value,
        "href" when value.StartsWith('#') => "#" + context.Prefix + value[1..],
        "class" when context.PrefixClasses => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(className => context.Prefix + className)),
        _ => RewriteUrls(value, context)
    };

    private static string RewriteUrls(string value, CleanContext context) =>
        UrlReference().Replace(value, match => match.Groups[1].Value + context.Prefix);

    private static string RewriteStyleSheet(string css, CleanContext context)
    {
        css = CssComment().Replace(css, string.Empty);
        var parts = CssBlockSeparator().Split(css);
        var result = new StringBuilder(css.Length);
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            // Text before an opening brace is a selector or an at-rule prelude; everything else holds declarations.
            if (part is "{" or "}")
                result.Append(part);
            else if (index + 1 < parts.Length && parts[index + 1] == "{")
                result.Append(CssSelectorName().Replace(part, match => match.Groups[1].Value + context.Prefix + match.Groups[2].Value));
            else
                result.Append(RewriteUrls(part, context));
        }

        return result.ToString();
    }

    private static (double Left, double Top, double Width, double Height)? ReadViewBox(XElement root)
    {
        var viewBox = (string?)root.Attribute("viewBox");
        if (viewBox is not null)
        {
            var values = viewBox.Split(new[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length == 4 && values.All(value => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            {
                var numbers = values.Select(value => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
                if (numbers[2] > 0 && numbers[3] > 0) return (numbers[0], numbers[1], numbers[2], numbers[3]);
            }
        }

        return ReadLength(root, "width") is { } width && ReadLength(root, "height") is { } height ? (0, 0, width, height) : null;
    }

    private static double? ReadLength(XElement root, string name)
    {
        var value = ((string?)root.Attribute(name))?.Trim();
        if (value is null) return null;
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase)) value = value[..^2];
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var length) && length > 0 ? length : null;
    }

    private sealed record CleanContext(string Prefix, bool PrefixClasses);

    [GeneratedRegex("[^A-Za-z0-9_-]")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"(url\(\s*['""]?#)")]
    private static partial Regex UrlReference();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CssComment();

    [GeneratedRegex("([{}])")]
    private static partial Regex CssBlockSeparator();

    [GeneratedRegex(@"([.#])(-?[_A-Za-z][\w-]*)")]
    private static partial Regex CssSelectorName();
}
