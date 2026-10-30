using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdown.Renderer;
using Markdown.Renderer.MarkdigExtension;
using StaticCode.Diagrams;

namespace Markdown.Confluence;

public sealed class ConfluenceStorageRenderer
{
    private static readonly XNamespace Ac = "http://atlassian.com/content";
    private static readonly XNamespace Ri = "http://atlassian.com/resource/identifier";
    private static readonly Lazy<string> DiagramStyles = new(() =>
    {
        using var stream = typeof(Renderer.HtmlRenderer).Assembly.GetManifestResourceStream("Markdown.Renderer.Res.Css.main.css")
            ?? throw new InvalidOperationException("Could not load the MDU stylesheet for a static diagram.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
    private readonly ConfluenceTarget _target;
    private readonly IReadOnlyDictionary<string, ConfluenceSource> _sources;
    private readonly IReadOnlyDictionary<string, ConfluenceSource> _htmlSources;
    private readonly MarkdownPipeline _pipeline;
    private readonly ConfluenceAsset? _pdf;

    public ConfluenceStorageRenderer(ConfluenceTarget target, IReadOnlyList<ConfluenceSource> sources, string? glossary = null, ConfluenceAsset? pdf = null)
    {
        _target = target;
        _sources = sources.ToDictionary(source => source.Path, StringComparer.Ordinal);
        _htmlSources = sources.ToDictionary(source => LinkReplacer.ReplaceUrl(source.Path)!, StringComparer.Ordinal);
        _pdf = pdf;
        var builder = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseHeadingIds()
            .UseRenderedMath().UseGitHubAlerts().UseIconsAndEmojis().UseKeyboardKeys();
        if (glossary is not null) builder.UseSharedAbbreviations(glossary);
        _pipeline = builder.Build();
    }

    public ConfluenceDocument Render(ConfluenceSource source)
    {
        var diagnostics = new List<string>();
        var assets = new Dictionary<string, ConfluenceAsset>(StringComparer.Ordinal);
        var markdown = EscapeSourceHtml(source.Markdown, diagnostics);
        markdown = OpenApiMarkdownRenderer.ExpandCodeBlocks(markdown, source.FilePath,
            source.SourceRoot, includeSpecificationPageLink: false)
            .Replace(OpenApiMarkdownRenderer.BlockStartMarker, "", StringComparison.Ordinal)
            .Replace(OpenApiMarkdownRenderer.BlockEndMarker, "", StringComparison.Ordinal);
        var document = Markdig.Markdown.Parse(markdown, _pipeline);
        using var writer = new StringWriter();
        var renderer = new Markdig.Renderers.HtmlRenderer(writer);
        _pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        var html = Regex.Replace(HtmlFragment.NormalizeVoidElements(writer.ToString()), @"&[A-Za-z][A-Za-z0-9]+;", match =>
        {
            var decoded = WebUtility.HtmlDecode(match.Value);
            return decoded == match.Value ? match.Value : new XText(decoded).ToString();
        });
        var root = XElement.Parse("<root>" + html + "</root>", LoadOptions.PreserveWhitespace);
        Transform(root, source, assets, diagnostics);
        foreach (var spec in OpenApiMarkdownRenderer.FindReferencedSpecPaths(source.Markdown).Distinct(StringComparer.Ordinal))
        {
            var asset = LoadAsset(source, spec, assets, isImage: false);
            root.Add(new XElement("p", new XElement(Ac + "link", Attachment(asset.Name),
                new XElement(Ac + "link-body", $"OpenAPI specification: {Path.GetFileName(spec)}"))));
        }
        var storage = new XElement("root", new XAttribute(XNamespace.Xmlns + "ac", Ac), new XAttribute(XNamespace.Xmlns + "ri", Ri),
            new XElement("p", new XElement("em", "Generated from Markdown by MDU. Edit the source documentation, not this page.")),
            root.Nodes());
        var xml = storage.ToString(SaveOptions.DisableFormatting);
        return new ConfluenceDocument(xml[(xml.IndexOf('>') + 1)..^"</root>".Length],
            assets.Values.ToArray(), diagnostics.Distinct(StringComparer.Ordinal).ToArray());
    }

    private string EscapeSourceHtml(string markdown, List<string> diagnostics)
    {
        var parsed = Markdig.Markdown.Parse(markdown, _pipeline);
        var spans = parsed.Descendants().Where(node => node is HtmlBlock or HtmlInline)
            .Select(node => node.Span).OrderByDescending(span => span.Start).ToArray();
        if (spans.Length > 0) diagnostics.Add("Raw source HTML is exported as text, not executed.");
        foreach (var span in spans)
            markdown = markdown[..span.Start] + HtmlEncoder.Default.Encode(markdown.Substring(span.Start, span.Length)) + markdown[(span.End + 1)..];
        return markdown;
    }

    private void Transform(XElement root, ConfluenceSource source, Dictionary<string, ConfluenceAsset> assets, List<string> diagnostics)
    {
        foreach (var element in root.Elements().ToArray())
        {
            var tag = element.Name.LocalName;
            if (tag == "pre")
            {
                var code = element.Element("code");
                var classes = (element.Attribute("class")?.Value ?? "") + " " + (code?.Attribute("class")?.Value ?? "");
                var language = classes.Split(' ')
                    .FirstOrDefault(value => value.StartsWith("language-", StringComparison.Ordinal))?["language-".Length..] ??
                    classes.Split(' ').FirstOrDefault(value => value is "mermaid" or "plantuml" or "puml" or "context") ?? "";
                var text = code?.Value ?? element.Value;
                if (DiagramRenderer.TryRenderSvg(language, text, out var svg))
                {
                    element.ReplaceWith(GeneratedImage(svg, source, assets));
                }
                else
                {
                    if (language is "mermaid" or "plantuml" or "puml" or "context" or "isometric" or "hero" or "icon-search" or "openapi")
                        diagnostics.Add($"'{language}' block is exported as its source text; interactive content is not available.");
                    element.ReplaceWith(CodeMacro(text, language));
                }
                continue;
            }
            if (tag == "svg")
            {
                element.ReplaceWith(GeneratedImage(element.ToString(SaveOptions.DisableFormatting), source, assets));
                continue;
            }
            if (tag == "img")
            {
                var url = element.Attribute("src")?.Value ?? throw new InvalidOperationException("Image has no source.");
                var external = External(url, image: true);
                var resource = external
                    ? new XElement(Ri + "url", new XAttribute(Ri + "value", url))
                    : Attachment(LoadAsset(source, url, assets, isImage: true).Name);
                element.ReplaceWith(new XElement(Ac + "image",
                    new XAttribute(Ac + "alt", element.Attribute("alt")?.Value ?? ""), resource));
                continue;
            }
            Transform(element, source, assets, diagnostics);
            var id = element.Attribute("id")?.Value;
            if (id is not null)
            {
                if (tag is "li" or "td" or "th") element.AddFirst(Anchor(id));
                else element.AddBeforeSelf(Anchor(id));
            }
            if (tag == "a")
            {
                var url = element.Attribute("href")?.Value;
                if (url is not null && !External(url, image: false))
                {
                    element.ReplaceWith(InternalLink(element, source, url, assets));
                    continue;
                }
                if (url is null)
                {
                    element.ReplaceWith(element.Nodes());
                    continue;
                }
            }
            if (tag == "div" && (element.Attribute("class")?.Value ?? "").Contains("markdown-alert", StringComparison.Ordinal))
            {
                element.ReplaceWith(new XElement(Ac + "structured-macro", new XAttribute(Ac + "name", "panel"),
                    new XElement(Ac + "rich-text-body", element.Nodes())));
                continue;
            }
            if (tag == "input")
            {
                element.ReplaceWith(new XText(element.Attribute("checked") is null ? "[ ] " : "[x] "));
                continue;
            }
            if (tag == "details")
            {
                diagnostics.Add("Collapsible details are expanded in the Confluence copy.");
                element.Name = "div";
            }
            if (tag == "summary") element.Name = "strong";
            if (tag == "kbd") element.Name = "code";
            foreach (var attribute in element.Attributes().ToArray())
            {
                if (attribute.Name.Namespace != XNamespace.None || attribute.Name.LocalName is not ("href" or "title" or "colspan" or "rowspan"))
                    attribute.Remove();
            }
            if (tag is "script" or "style" or "iframe" or "object")
                throw new InvalidOperationException($"Active HTML element '{tag}' cannot be exported.");
        }
    }

    private XElement InternalLink(XElement link, ConfluenceSource source, string url, Dictionary<string, ConfluenceAsset> assets)
    {
        if (LinkReplacer.IsPdfLink(url))
        {
            if (_pdf is null) throw new InvalidOperationException("The documentation PDF must be prepared before exporting an mdu:pdf link.");
            assets.TryAdd(_pdf.Name, _pdf);
            return new XElement(Ac + "link", Attachment(_pdf.Name), new XElement(Ac + "link-body", link.Nodes()));
        }
        var hash = url.IndexOf('#');
        var anchor = hash < 0 ? null : Uri.UnescapeDataString(url[(hash + 1)..]);
        var path = hash < 0 ? url : url[..hash];
        if (path.Contains('?')) throw new InvalidOperationException($"Local link query strings are not supported in Confluence: {url}");
        XElement? resource = null;
        if (path.Length > 0)
        {
            var decoded = Uri.UnescapeDataString(path);
            var logical = NormalizePath(source.Path, decoded);
            if (decoded.EndsWith('/') || Path.GetExtension(decoded).Length == 0)
            {
                var prefix = logical.TrimEnd('/') + "/";
                logical = _sources.ContainsKey(prefix + "README.md") ? prefix + "README.md" : prefix + "index.md";
            }
            if (TryFindSource(logical, out var destination))
                resource = new XElement(Ri + "page", new XAttribute(Ri + "content-title", _target.PageTitle(destination)),
                    new XAttribute(Ri + "space-key", _target.SpaceKey));
            else if (Path.GetExtension(decoded).Equals(".md", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Markdown link has no exported target: {url} in {source.Path}");
            else resource = Attachment(LoadAsset(source, path, assets, isImage: false).Name);
        }
        return new XElement(Ac + "link", anchor is null ? null : new XAttribute(Ac + "anchor", anchor),
            resource, new XElement(Ac + "link-body", link.Nodes()));
    }

    private static string NormalizePath(string source, string path)
    {
        if (path.StartsWith('/') || path.Contains('\\'))
            throw new InvalidOperationException("Confluence local links must be relative paths using forward slashes.");
        var segments = new List<string>();
        foreach (var segment in ((Path.GetDirectoryName(source)?.Replace('\\', '/') ?? "") + "/" + path).Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) throw new InvalidOperationException("Local link escapes the documentation tree.");
                segments.RemoveAt(segments.Count - 1);
            }
            else segments.Add(segment);
        }
        return string.Join('/', segments);
    }

    private static bool External(string url, bool image)
    {
        if (LinkReplacer.IsPdfLink(url) && !image) return false;
        if (url.StartsWith("//", StringComparison.Ordinal)) throw new InvalidOperationException("Protocol-relative URLs cannot be exported.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("https" or "http") && (image || uri.Scheme != "mailto"))
            throw new InvalidOperationException("Only HTTP(S) links and images, and mailto links, can be exported.");
        if (uri.UserInfo.Length != 0) throw new InvalidOperationException("URLs containing credentials cannot be exported.");
        return true;
    }

    private static ConfluenceAsset LoadAsset(ConfluenceSource source, string url, Dictionary<string, ConfluenceAsset> assets, bool isImage)
    {
        var path = Uri.UnescapeDataString(url);
        if (path.Contains('\\') || path.StartsWith('/'))
            throw new InvalidOperationException("Attachment paths must be relative.");
        var root = Path.GetFullPath(source.SourceRoot);
        var file = Path.GetFullPath(path, Path.GetDirectoryName(source.FilePath)!);
        var relative = Path.GetRelativePath(root, file);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal))
            throw new InvalidOperationException("Attachment path escapes its source root.");
        for (FileSystemInfo? item = new FileInfo(file); item is not null; item = item switch
             {
                 FileInfo info => info.Directory,
                 DirectoryInfo directory => directory.Parent,
                 _ => null
             })
        {
            if (item.LinkTarget is not null) throw new InvalidOperationException("Symbolic links cannot be exported as attachments.");
            if (item.FullName == root) break;
        }
        var bytes = File.ReadAllBytes(file);
        var extension = Path.GetExtension(file).ToLowerInvariant();
        if (!Regex.IsMatch(extension, @"\A\.[a-z0-9]{1,12}\z"))
            throw new InvalidOperationException("Attachments need a simple file extension.");
        var name = "mdu-" + ConfluenceTarget.Hash(bytes) + extension;
        var asset = new ConfluenceAsset(name, bytes, isImage);
        assets.TryAdd(name, asset);
        return asset;
    }

    private XElement GeneratedImage(string svg, ConfluenceSource source, Dictionary<string, ConfluenceAsset> assets)
    {
        var image = XElement.Parse(svg);
        if (image.DescendantsAndSelf().Any(element => element.Name.LocalName is "script" or "foreignObject" or "iframe" or "object" ||
            element.Attributes().Any(attribute => attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Active SVG content cannot be exported.");
        foreach (var href in image.DescendantsAndSelf().Attributes().Where(attribute => attribute.Name.LocalName == "href"))
        {
            if (href.Value.StartsWith('#') && href.Parent?.Name.LocalName != "a" || External(href.Value, image: false)) continue;
            var parts = href.Value.Split('#', 2);
            var path = parts[0].Length == 0 ? source.Path : NormalizePath(source.Path, Uri.UnescapeDataString(parts[0]));
            if (!TryFindSource(path, out var destination))
                throw new InvalidOperationException($"Diagram link has no exported target: {href.Value}");
            var title = _target.PageTitle(destination);
            var fragment = parts.Length == 2 ? "#" + Uri.EscapeDataString(
                string.Concat((title + "-" + Uri.UnescapeDataString(parts[1])).Where(character => !char.IsWhiteSpace(character)))) : "";
            href.Value = new Uri(_target.BaseUri, $"display/{Uri.EscapeDataString(_target.SpaceKey)}/{Uri.EscapeDataString(title)}").AbsoluteUri + fragment;
        }
        XNamespace ns = "http://www.w3.org/2000/svg";
        foreach (var element in image.DescendantsAndSelf().Where(element => element.Name.Namespace == XNamespace.None))
            element.Name = ns + element.Name.LocalName;
        image.SetAttributeValue("xmlns", ns.NamespaceName);
        // Diagram SVGs share the site theme; icons and formulas need only their color variables.
        var diagram = (image.Attribute("class")?.Value ?? "").Split(' ').Contains("diagram", StringComparer.Ordinal);
        image.AddFirst(new XElement(ns + "style", ":root { --text-primary: #202020; --text: #202020; --link: #1769aa; --bg: #ffffff; } " +
            (diagram ? DiagramStyles.Value : "")));
        var bytes = Encoding.UTF8.GetBytes(image.ToString(SaveOptions.DisableFormatting));
        var name = "mdu-" + ConfluenceTarget.Hash(bytes) + ".svg";
        assets.TryAdd(name, new ConfluenceAsset(name, bytes, true));
        return new XElement(Ac + "image", Attachment(name));
    }

    private static XElement Attachment(string name) => new(Ri + "attachment", new XAttribute(Ri + "filename", name));
    private bool TryFindSource(string path, out ConfluenceSource source) =>
        _sources.TryGetValue(path, out source!) || _htmlSources.TryGetValue(path, out source!);
    private static XElement Anchor(string id) => new(Ac + "structured-macro", new XAttribute(Ac + "name", "anchor"),
        new XElement(Ac + "parameter", new XAttribute(Ac + "name", ""), id));
    private static XElement CodeMacro(string text, string language) => new(Ac + "structured-macro", new XAttribute(Ac + "name", "code"),
        new XElement(Ac + "parameter", new XAttribute(Ac + "name", "language"), language.Length == 0 ? "none" : language),
        new XElement(Ac + "plain-text-body", new XCData(text)));
}
