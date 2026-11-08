using AngleSharp.Html.Parser;

namespace Mdu.Commands;

public static class CheckSiteCommand
{
    public static int Execute(DirectoryInfo directory, string[] dynamicFragments)
    {
        if (!directory.Exists)
        {
            Console.Error.WriteLine($"Site directory does not exist: {directory.FullName}");
            return 1;
        }

        var root = directory.FullName;
        var parser = new HtmlParser();
        var pages = Directory.EnumerateFiles(root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            })
            .Where(path => Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToDictionary(path => path, path => ReadPage(parser, path));

        if (pages.Count == 0)
        {
            Console.Error.WriteLine($"No HTML pages found in {root}");
            return 1;
        }

        var exceptions = dynamicFragments.ToHashSet(StringComparer.Ordinal);
        var directoryEntries = new Dictionary<string, HashSet<string?>>(StringComparer.Ordinal);
        var anchors = pages.ToDictionary(pair => pair.Key, pair => pair.Value.Anchors);
        var failed = false;
        foreach (var (page, data) in pages)
        {
            var source = Path.GetRelativePath(root, page).Replace(Path.DirectorySeparatorChar, '/');
            foreach (var reference in data.References)
            {
                var error = CheckReference(root, source, data.BaseHref, reference.Trim(), anchors, exceptions, directoryEntries);
                if (error is null)
                    continue;

                Console.Error.WriteLine($"{source}: {reference}: {error}");
                failed = true;
            }
        }

        if (!failed)
            Console.WriteLine($"Checked {pages.Count} HTML pages.");

        return failed ? 1 : 0;
    }

    private static PageLinks ReadPage(HtmlParser parser, string path)
    {
        var document = parser.ParseDocument(File.ReadAllText(path));
        var anchors = document.QuerySelectorAll("[id], a[name]")
            .SelectMany(element => new[] { element.Id, element.LocalName == "a" ? element.GetAttribute("name") : null })
            .ToHashSet(StringComparer.Ordinal);
        var baseHref = document.QuerySelector("base[href]")?.GetAttribute("href")?.Trim() ?? "";
        var references = document.QuerySelectorAll("[href], [src], [poster]")
            .Where(element => element.LocalName != "base")
            .SelectMany(element => element.Attributes)
            .Where(attribute => attribute.Name is "href" or "src" or "poster")
            .Select(attribute => attribute.Value)
            .ToArray();
        return new PageLinks(anchors, baseHref, references);
    }

    private sealed record PageLinks(HashSet<string?> Anchors, string BaseHref, string[] References);

    private static string? CheckReference(
        string root, string source, string baseHref, string reference,
        Dictionary<string, HashSet<string?>> anchors, HashSet<string> exceptions,
        Dictionary<string, HashSet<string?>> directoryEntries)
    {
        if (IsExternal(reference) || IsExternal(baseHref))
            return null;

        var parts = reference.Split('#', 2);
        var urlPath = UrlPath(parts[0]);
        var basePath = UrlPath(baseHref);
        if (basePath.Length > 0 && !urlPath.StartsWith('/'))
        {
            if (urlPath.Length == 0)
                urlPath = basePath;
            else if (Uri.UnescapeDataString(basePath.Split('/')[^1]) is "." or "..")
                urlPath = basePath + "/" + urlPath;
            else
                urlPath = basePath[..(basePath.LastIndexOf('/') + 1)] + urlPath;
        }

        var segments = urlPath.StartsWith('/') ? new List<string>() : source.Split('/').SkipLast(1).ToList();
        foreach (var encodedSegment in urlPath.Split('/'))
        {
            var segment = Uri.UnescapeDataString(encodedSegment);
            if (segment.Contains('/') || segment.Contains('\\'))
                return "encoded path separator";
            if (segment is "" or ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count == 0)
                    return "path leaves the site directory";
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.Contains(':') ||
                (OperatingSystem.IsWindows() && (segment.EndsWith(' ') || segment.EndsWith('.'))))
                return "invalid local path";
            segments.Add(segment);
        }

        if (urlPath.Length == 0)
            segments.Add(source.Split('/')[^1]);

        var target = root;
        foreach (var segment in segments)
        {
            if (!HasExactName(target, segment, directoryEntries))
                return "local target does not exist with this casing";
            target = Path.Combine(target, segment);
            if (IsLink(target))
                return "symbolic links are not supported in site paths";
        }

        var finalSegment = Uri.UnescapeDataString(urlPath[(urlPath.LastIndexOf('/') + 1)..]);
        if (Directory.Exists(target) || urlPath.EndsWith('/') || finalSegment is "." or "..")
        {
            if (!HasExactName(target, "index.html", directoryEntries))
                return "local target does not exist with this casing";
            target = Path.Combine(target, "index.html");
            if (IsLink(target))
                return "symbolic links are not supported in site paths";
        }

        if (!File.Exists(target))
            return "local target does not exist";

        if (parts.Length == 1 || parts[1].Length == 0 || !anchors.TryGetValue(target, out var ids))
            return null;

        var fragment = Uri.UnescapeDataString(parts[1].Split(":~:", 2)[0]);
        var relativeTarget = Path.GetRelativePath(root, target).Replace(Path.DirectorySeparatorChar, '/');
        return fragment.Length == 0 || fragment.Equals("top", StringComparison.OrdinalIgnoreCase)
            || exceptions.Contains(relativeTarget) || ids.Contains(fragment)
            ? null : "fragment does not exist";
    }

    private static string UrlPath(string url) => url.Split('?', '#')[0].Replace('\\', '/');

    private static bool IsExternal(string url) =>
        UrlPath(url).StartsWith("//", StringComparison.Ordinal) ||
        (!url.StartsWith('/') && Uri.TryCreate(url, UriKind.Absolute, out _));

    private static bool HasExactName(
        string directory, string name, Dictionary<string, HashSet<string?>> directoryEntries)
    {
        if (!directoryEntries.TryGetValue(directory, out var names))
        {
            names = Directory.Exists(directory)
                ? Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string?>(StringComparer.Ordinal);
            directoryEntries.Add(directory, names);
        }

        return names.Contains(name);
    }

    private static bool IsLink(string path) => new FileInfo(path).LinkTarget is not null;
}
