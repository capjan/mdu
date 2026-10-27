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
/// Turns an SVG file into the body of an inline icon. Scripts, event handlers, references to other files, and metadata
/// of editors are removed, and <c>use</c> references are replaced with copies. Identifiers get a prefix, and so do class
/// names when the SVG has its own style sheet, whose selectors are limited to the icon, so that several icons on one
/// page do not affect each other.
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
        var hasStyleSheet = root.Descendants(Svg + "style").Any();
        var context = new CleanContext(prefix, hasStyleSheet, GetCopiedIds(root));
        var content = new XElement("g", root.Nodes().Select(node => Clean(node, context)).OfType<XNode>());
        InlineUseReferences(content, context, width, height);
        // The originals stay for the references into them, such as gradients, but are never drawn themselves.
        foreach (var symbol in content.Descendants("symbol").Where(symbol => symbol.Ancestors("defs").FirstOrDefault() is null).ToArray())
            symbol.ReplaceWith(new XElement("defs", symbol));

        // Presentation attributes of the root, such as fill="none" or stroke="currentColor", move to the group. An
        // icon with a style sheet always keeps the group, because its class limits the style sheet to this icon.
        content.Add(CleanAttributes(root, context, isRoot: true));
        if (hasStyleSheet)
            content.SetAttributeValue("class", string.Join(' ', new[] { context.ScopeClass, (string?)content.Attribute("class") }.OfType<string>()));
        var body = content.HasAttributes
            ? Serialize(content)
            : string.Concat(content.Nodes().Select(Serialize));

        return new IconData { Body = body, Left = left, Top = top, Width = width, Height = height };
    }

    /// <summary>
    /// Replaces every <c>use</c> element with a copy of the element it refers to, because the PDF renderer draws
    /// neither <c>defs</c> nor <c>use</c>. A reference to a missing element or a chain of references deeper than
    /// <see cref="MaxUseDepth"/>, for example a cycle, is removed.
    /// </summary>
    private static void InlineUseReferences(XElement content, CleanContext context, double viewportWidth, double viewportHeight)
    {
        var elementsById = content.Descendants()
            .Where(element => element.Attribute("id") is not null)
            .GroupBy(element => (string)element.Attribute("id")!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        for (var depth = 0; depth <= MaxUseDepth; depth++)
        {
            var uses = content.Descendants("use").ToArray();
            if (uses.Length == 0) return;
            foreach (var use in uses)
            {
                var href = (string?)use.Attribute("href");
                if (depth == MaxUseDepth || href is null || !href.StartsWith('#') || !elementsById.TryGetValue(href[1..], out var target))
                {
                    use.Remove();
                    continue;
                }

                // A copy must not repeat the identifiers. A class takes their place for the style sheet.
                var copy = new XElement(target);
                foreach (var element in copy.DescendantsAndSelf().Where(element => element.Attribute("id") is not null))
                {
                    var id = (string)element.Attribute("id")!;
                    element.Attribute("id")!.Remove();
                    if (context.PrefixClasses)
                        element.SetAttributeValue("class", string.Join(' ', new[] { (string?)element.Attribute("class"), CopyClass(id) }.OfType<string>()));
                }
                if (copy.Name.LocalName == "symbol")
                {
                    copy.Name = "g";
                    var symbolTransform = SymbolTransform(copy, use, viewportWidth, viewportHeight);
                    copy.Attribute("viewBox")?.Remove();
                    copy.Attribute("preserveAspectRatio")?.Remove();
                    copy.Attribute("width")?.Remove();
                    copy.Attribute("height")?.Remove();
                    if (symbolTransform is not null) copy.SetAttributeValue("transform", symbolTransform);
                }

                var transform = string.Join(' ', new[]
                {
                    (string?)use.Attribute("transform"),
                    use.Attribute("x") is null && use.Attribute("y") is null
                        ? null
                        : $"translate({(string?)use.Attribute("x") ?? "0"} {(string?)use.Attribute("y") ?? "0"})"
                }.OfType<string>());
                var group = new XElement("g", use.Attributes().Where(attribute =>
                    attribute.Name.LocalName is not ("href" or "x" or "y" or "width" or "height" or "transform")));
                if (transform.Length > 0) group.SetAttributeValue("transform", transform);
                group.Add(copy);
                use.ReplaceWith(group);
            }
        }
    }

    private const int MaxUseDepth = 8;

    private static HashSet<string> GetCopiedIds(XElement root)
    {
        var elementsById = root.Descendants()
            .Where(element => element.Attribute("id") is not null)
            .GroupBy(element => (string)element.Attribute("id")!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return root.Descendants(Svg + "use")
            .Select(use => (string?)use.Attribute("href") ?? (string?)use.Attribute(XLink + "href"))
            .Where(href => href is not null && href.StartsWith('#') && elementsById.ContainsKey(href[1..]))
            .SelectMany(href => elementsById[href![1..]].DescendantsAndSelf())
            .Select(element => (string?)element.Attribute("id"))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string CopyClass(string prefixedId) => prefixedId + "--copy";

    /// <summary>
    /// Returns the transform that maps the view box of a symbol onto the size that the <c>use</c> element asks for,
    /// following <c>preserveAspectRatio</c>, or <c>null</c> when the symbol has no view box.
    /// </summary>
    private static string? SymbolTransform(XElement symbol, XElement use, double viewportWidth, double viewportHeight)
    {
        if (symbol.Attribute("viewBox") is null || ReadViewBox(symbol) is not { } viewBox) return null;
        var (viewLeft, viewTop, viewWidth, viewHeight) = viewBox;
        // Sizes default to the whole viewport around the use, which is the view box of the icon.
        var width = ReadSize(use, "width", viewportWidth) ?? ReadSize(symbol, "width", viewportWidth) ?? viewportWidth;
        var height = ReadSize(use, "height", viewportHeight) ?? ReadSize(symbol, "height", viewportHeight) ?? viewportHeight;

        var aspectRatio = ((string?)symbol.Attribute("preserveAspectRatio") ?? "xMidYMid meet")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var align = aspectRatio.FirstOrDefault() ?? "xMidYMid";
        var scaleX = width / viewWidth;
        var scaleY = height / viewHeight;
        if (align != "none")
        {
            var slice = aspectRatio.Length > 1 && aspectRatio[1] == "slice";
            scaleX = scaleY = slice ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
        }

        var translateX = Align(align, "xMid", "xMax", width - viewWidth * scaleX) - viewLeft * scaleX;
        var translateY = Align(align, "YMid", "YMax", height - viewHeight * scaleY) - viewTop * scaleY;
        return $"translate({Format(translateX)} {Format(translateY)}) scale({Format(scaleX)} {Format(scaleY)})";

        static double Align(string align, string middle, string end, double space) =>
            align.Contains(middle, StringComparison.Ordinal) ? space / 2
            : align.Contains(end, StringComparison.Ordinal) ? space
            : 0;
    }

    private static string Format(double value) => Math.Round(value, 6).ToString(CultureInfo.InvariantCulture);

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

    // References to the icon's own elements get the prefix; references to other files become none, so that the page
    // loads nothing from elsewhere. Embedded images stay.
    private static string RewriteUrls(string value, CleanContext context) =>
        UrlFunction().Replace(value, match =>
        {
            var target = match.Groups["target"].Value.Trim();
            if (target.StartsWith('#')) return $"url(#{context.Prefix}{target[1..]})";
            return target.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) ? match.Value : "none";
        });

    private static string RewriteStyleSheet(string css, CleanContext context)
    {
        css = CssImport().Replace(CssComment().Replace(css, string.Empty), string.Empty);
        var parts = CssBlockSeparator().Split(css);
        var result = new StringBuilder(css.Length);
        // What each open block holds: rules, nested rules of a grouping at-rule, or the frames of @keyframes.
        var blocks = new Stack<CssBlock>();
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            if (part == "{" || part == "}")
            {
                if (part == "}" && blocks.Count > 0) blocks.Pop();
                result.Append(part);
            }
            else if (index + 1 < parts.Length && parts[index + 1] == "{")
            {
                // Text before an opening brace is a selector or an at-rule prelude.
                var prelude = part.Trim();
                var enclosing = blocks.Count > 0 ? blocks.Peek() : CssBlock.Group;
                if (enclosing == CssBlock.Keyframes)
                {
                    blocks.Push(CssBlock.Rule);
                    result.Append(part);
                }
                else if (prelude.StartsWith('@'))
                {
                    var keyword = prelude.Split((char[]?)null, 2)[0].ToLowerInvariant();
                    blocks.Push(keyword.EndsWith("keyframes", StringComparison.Ordinal) ? CssBlock.Keyframes
                        : keyword is "@media" or "@supports" or "@container" or "@layer" ? CssBlock.Group
                        : CssBlock.Rule);
                    result.Append(part);
                }
                else
                {
                    blocks.Push(CssBlock.Rule);
                    result.Append(part[..(part.Length - part.TrimStart().Length)])
                        .Append(ScopeSelectors(prelude, context))
                        .Append(part[part.TrimEnd().Length..]);
                }
            }
            else
            {
                result.Append(RewriteUrls(part, context));
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Limits every selector to the group around the icon and prefixes its identifiers and classes, so that
    /// <c>path { fill: red }</c> colors only this icon. The root <c>svg</c> and <c>:root</c> stand for that group.
    /// </summary>
    private static string ScopeSelectors(string selectors, CleanContext context)
    {
        var scope = "." + context.ScopeClass;
        return string.Join(", ", SplitSelectors(selectors).Select(selector =>
        {
            selector = CssSelectorName().Replace(selector.Trim(), match =>
            {
                var name = context.Prefix + match.Groups[2].Value;
                if (match.Groups[1].Value == ".") return "." + name;
                // An element copied in place of a use reference has a class instead of its identifier.
                return context.CopiedIds.Contains(match.Groups[2].Value) ? $":is(#{name}, .{CopyClass(name)})" : "#" + name;
            });
            var root = CssRootSelector().Match(selector);
            return root.Success ? scope + selector[root.Length..] : $"{scope} {selector}";
        }));
    }

    // Splits a selector list at commas outside of parentheses, such as the one in :is(a, b).
    private static IEnumerable<string> SplitSelectors(string selectors)
    {
        var depth = 0;
        var start = 0;
        for (var index = 0; index < selectors.Length; index++)
        {
            switch (selectors[index])
            {
                case '(': depth++; break;
                case ')': depth = Math.Max(0, depth - 1); break;
                case ',' when depth == 0:
                    yield return selectors[start..index];
                    start = index + 1;
                    break;
            }
        }

        yield return selectors[start..];
    }

    private enum CssBlock
    {
        Group,
        Rule,
        Keyframes
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

    // Reads a length that may be a percentage of the viewport, such as width="50%".
    private static double? ReadSize(XElement element, string name, double viewportSize)
    {
        var value = ((string?)element.Attribute(name))?.Trim();
        if (value is null || !value.EndsWith('%')) return ReadLength(element, name);
        return double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) && percent > 0
            ? viewportSize * percent / 100
            : null;
    }

    private static double? ReadLength(XElement root, string name)
    {
        var value = ((string?)root.Attribute(name))?.Trim();
        if (value is null) return null;
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase)) value = value[..^2];
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var length) && length > 0 ? length : null;
    }

    /// <param name="CopiedIds">The identifiers, without prefix, of the elements that <c>use</c> references copy.</param>
    private sealed record CleanContext(string Prefix, bool PrefixClasses, IReadOnlySet<string> CopiedIds)
    {
        public string ScopeClass => Prefix + "scope";
    }

    [GeneratedRegex("[^A-Za-z0-9_-]")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"url\(\s*(?<quote>['""]?)(?<target>.*?)\k<quote>\s*\)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex UrlFunction();

    [GeneratedRegex(@"@import\b[^;]*;?", RegexOptions.IgnoreCase)]
    private static partial Regex CssImport();

    [GeneratedRegex(@"^(?:svg|:root)(?![\w-])", RegexOptions.IgnoreCase)]
    private static partial Regex CssRootSelector();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CssComment();

    [GeneratedRegex("([{}])")]
    private static partial Regex CssBlockSeparator();

    [GeneratedRegex(@"([.#])(-?[_A-Za-z][\w-]*)")]
    private static partial Regex CssSelectorName();
}
