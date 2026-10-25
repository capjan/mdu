using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using YamlDotNet.RepresentationModel;

namespace Markdown.Renderer;

public sealed record OpenApiSpecificationPage(
    string FileName,
    string Title,
    string SourceLabel,
    string Language,
    string Markdown);

public sealed record OpenApiSiblingPage(
    string MarkdownPath,
    string HtmlFileName,
    string? AllowedRootPath = null);

public static class OpenApiMarkdownRenderer
{
    public const string CodeBlockLanguage = "openapi";
    public const string BlockStartMarker = "<!--mdu-openapi-start-->";
    public const string BlockEndMarker = "<!--mdu-openapi-end-->";

    private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "PUT", "POST", "DELETE", "OPTIONS", "HEAD", "PATCH", "TRACE", "QUERY"
    };
    private static readonly Regex PathParameterRegex = new(@"\{[^{}]+\}", RegexOptions.Compiled);
    private static readonly Regex UrlSchemeRegex = new(@"^[A-Za-z][A-Za-z0-9+.-]*://", RegexOptions.Compiled);
    private static readonly MarkdownPipeline CodeBlockPipeline = new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build();

    private static readonly ConcurrentDictionary<string, CachedDocument> Cache = new(
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

    public static string ResolveSpecPath(string markdownPath, string specPath, string? allowedRootPath = null)
    {
        if (UrlSchemeRegex.IsMatch(specPath.Trim()))
            throw new InvalidOperationException($"OpenAPI document '{specPath}' must be a local file path.");

        var fullSpecPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(markdownPath)!, specPath));
        if (allowedRootPath is not null)
        {
            var relativePath = Path.GetRelativePath(Path.GetFullPath(allowedRootPath), fullSpecPath);
            if (Path.IsPathRooted(relativePath) || relativePath == ".." ||
                relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"OpenAPI document '{specPath}' must stay inside the imported repository.");
        }

        return fullSpecPath;
    }

    public static IReadOnlyList<string> FindReferencedSpecPaths(string markdown) =>
        FindSpecPathSpans(markdown).Select(span => span.Value).ToArray();

    public static IReadOnlyList<(string Value, int Start, int Length)> FindSpecPathSpans(string markdown)
    {
        if (!markdown.Contains(CodeBlockLanguage, StringComparison.OrdinalIgnoreCase))
            return Array.Empty<(string Value, int Start, int Length)>();

        var document = Markdig.Markdown.Parse(markdown, CodeBlockPipeline);
        var spans = new List<(string Value, int Start, int Length)>();
        foreach (var block in document.Descendants<FencedCodeBlock>().Where(IsOpenApiCodeBlock))
        {
            if (TryExtractBlockBody(block, out var bodyText, out var lineOffsets) &&
                TryParseSpecReferenceSpan(bodyText, out var relativeStart, out var length, out var specPath))
            {
                spans.Add((specPath, MapToSourceOffset(relativeStart, lineOffsets), length));
            }
        }

        return spans;
    }

    public static IReadOnlyList<string> GetSpecificationPageFileNames(
        string markdown,
        string markdownPath,
        string? allowedRootPath = null,
        string? pageOutputFileName = null,
        IEnumerable<OpenApiSiblingPage>? siblingPages = null)
    {
        if (!markdown.Contains(CodeBlockLanguage, StringComparison.OrdinalIgnoreCase))
        {
            PruneInlineCache(markdownPath, 0);
            return Array.Empty<string>();
        }

        var parsed = Markdig.Markdown.Parse(markdown, CodeBlockPipeline);
        var blocks = parsed.Descendants<FencedCodeBlock>().Where(IsOpenApiCodeBlock).ToArray();
        PruneInlineCache(markdownPath, blocks.Length);
        if (blocks.Length == 0)
            return Array.Empty<string>();

        var parentHtmlFileName = GetParentHtmlFileName(markdownPath, pageOutputFileName);
        var pageStem = Path.GetFileNameWithoutExtension(parentHtmlFileName);
        var usedFileNames = CollectReservedHtmlFileNames(markdownPath, parentHtmlFileName, allowedRootPath, siblingPages);
        var fileNames = new List<string>(blocks.Length);
        for (var index = 0; index < blocks.Length; index++)
        {
            var block = blocks[index];
            var bodyText = TryExtractBlockBody(block, out var extracted, out _)
                ? extracted
                : string.Empty;
            var slug = ResolveBlockSlug(bodyText, markdownPath, index, allowedRootPath, out _, out _);
            fileNames.Add(AllocateSpecFileName(pageStem, slug, usedFileNames));
        }

        return fileNames;
    }

    public static string ExpandCodeBlocks(
        string markdown,
        string markdownPath,
        string? allowedRootPath = null,
        string? pageOutputFileName = null,
        IEnumerable<OpenApiSiblingPage>? siblingPages = null,
        bool includeSpecificationPageLink = true) =>
        ExpandCodeBlocks(markdown, markdownPath, out _, allowedRootPath, pageOutputFileName, siblingPages, includeSpecificationPageLink);

    public static string ExpandCodeBlocks(
        string markdown,
        string markdownPath,
        out IReadOnlyList<OpenApiSpecificationPage> specificationPages,
        string? allowedRootPath = null,
        string? pageOutputFileName = null,
        IEnumerable<OpenApiSiblingPage>? siblingPages = null,
        bool includeSpecificationPageLink = true)
    {
        if (!markdown.Contains(CodeBlockLanguage, StringComparison.OrdinalIgnoreCase))
        {
            PruneInlineCache(markdownPath, 0);
            specificationPages = Array.Empty<OpenApiSpecificationPage>();
            return markdown;
        }

        var parsed = Markdig.Markdown.Parse(markdown, CodeBlockPipeline);
        var blocks = parsed.Descendants<FencedCodeBlock>().Where(IsOpenApiCodeBlock).ToArray();
        PruneInlineCache(markdownPath, blocks.Length);
        if (blocks.Length == 0)
        {
            specificationPages = Array.Empty<OpenApiSpecificationPage>();
            return markdown;
        }

        var levelOneHeadings = parsed.OfType<HeadingBlock>()
            .Where(heading => heading.Level == 1 && heading.Span.Start >= 0 && heading.Span.End < markdown.Length)
            .Select(heading => Regex.Replace(markdown[heading.Span.Start..(heading.Span.End + 1)], @"^#\s+|\s+#*\r?$", string.Empty).Trim())
            .Where(title => title.Length > 0)
            .ToArray();

        var parentHtmlFileName = GetParentHtmlFileName(markdownPath, pageOutputFileName);
        var pageStem = Path.GetFileNameWithoutExtension(parentHtmlFileName);
        var usedFileNames = CollectReservedHtmlFileNames(markdownPath, parentHtmlFileName, allowedRootPath, siblingPages);
        var generatedPages = new List<OpenApiSpecificationPage>(blocks.Length);
        var replacements = new (int Start, int Length, string Content)[blocks.Length];

        for (var index = 0; index < blocks.Length; index++)
        {
            var block = blocks[index];
            var bodyText = TryExtractBlockBody(block, out var extracted, out _)
                ? extracted
                : string.Empty;

            var slug = ResolveBlockSlug(
                bodyText,
                markdownPath,
                index,
                allowedRootPath,
                out var fullSpecPath,
                out var inlineSpecification);
            LoadedSpecification loaded;
            string sourceLabel;
            if (fullSpecPath is not null)
            {
                loaded = LoadFromFile(fullSpecPath);
                sourceLabel = Path.GetFileName(fullSpecPath);
            }
            else
            {
                loaded = inlineSpecification!;
                sourceLabel = $"{(string.IsNullOrWhiteSpace(slug) ? "openapi" : slug)}.{loaded.Format}";
            }

            var specFileName = AllocateSpecFileName(pageStem, slug, usedFileNames);

            var omitTitle = blocks.Length == 1 &&
                            !string.IsNullOrWhiteSpace(loaded.Document.Info?.Title) &&
                            levelOneHeadings.Any(heading => string.Equals(
                                heading,
                                loaded.Document.Info.Title.Trim(),
                                StringComparison.OrdinalIgnoreCase));

            var renderedMarkdown = ApplyContainerPrefix(
                markdown,
                block.Span.Start,
                Render(loaded.Document, index, includeSpecificationPageLink ? specFileName : null, omitTitle));
            var specPageTitle = string.IsNullOrWhiteSpace(loaded.Document.Info?.Title)
                ? $"OpenAPI Specification ({sourceLabel})"
                : $"{loaded.Document.Info.Title.Trim()} — OpenAPI Specification";
            var specPageMarkdown = BuildSpecificationPageMarkdown(
                specPageTitle,
                sourceLabel,
                loaded.Format,
                loaded.RawText,
                parentHtmlFileName,
                index);
            generatedPages.Add(new OpenApiSpecificationPage(
                specFileName,
                specPageTitle,
                sourceLabel,
                loaded.Format,
                specPageMarkdown));

            replacements[index] = (block.Span.Start, block.Span.Length, renderedMarkdown);
        }

        var result = markdown;
        for (var index = replacements.Length - 1; index >= 0; index--)
        {
            var replacement = replacements[index];
            result = result.Remove(replacement.Start, replacement.Length)
                .Insert(replacement.Start, replacement.Content);
        }

        specificationPages = generatedPages;
        return result;
    }

    private static bool IsOpenApiCodeBlock(FencedCodeBlock block) =>
        string.Equals(block.Info?.Trim(), CodeBlockLanguage, StringComparison.OrdinalIgnoreCase);

    private static bool TryExtractBlockBody(
        FencedCodeBlock block,
        out string bodyText,
        out (int TextOffset, int SourceOffset)[] lineOffsets)
    {
        if (block.Lines.Count == 0)
        {
            bodyText = string.Empty;
            lineOffsets = Array.Empty<(int TextOffset, int SourceOffset)>();
            return false;
        }

        var builder = new StringBuilder();
        var offsets = new (int TextOffset, int SourceOffset)[block.Lines.Count];
        for (var index = 0; index < block.Lines.Count; index++)
        {
            var slice = block.Lines.Lines[index].Slice;
            offsets[index] = (builder.Length, slice.Start);
            if (slice.Text is not null && slice.Start >= 0 && slice.Length > 0)
                builder.Append(slice.Text, slice.Start, slice.Length);
            if (index + 1 < block.Lines.Count)
                builder.Append('\n');
        }

        bodyText = builder.ToString();
        lineOffsets = offsets;
        return bodyText.Length > 0;
    }

    private static int MapToSourceOffset(int textOffset, (int TextOffset, int SourceOffset)[] lineOffsets)
    {
        for (var index = lineOffsets.Length - 1; index >= 0; index--)
        {
            if (textOffset >= lineOffsets[index].TextOffset)
                return lineOffsets[index].SourceOffset + (textOffset - lineOffsets[index].TextOffset);
        }

        return lineOffsets[0].SourceOffset + textOffset;
    }

    private static string ApplyContainerPrefix(string markdown, int blockStart, string renderedMarkdown)
    {
        var lineStart = blockStart > 0 ? markdown.LastIndexOf('\n', blockStart - 1) + 1 : 0;
        if (lineStart >= blockStart) return renderedMarkdown;

        var prefixChars = new char[blockStart - lineStart];
        for (var index = 0; index < prefixChars.Length; index++)
        {
            var character = markdown[lineStart + index];
            prefixChars[index] = character is '>' or '\t' ? character : ' ';
        }

        var continuationPrefix = new string(prefixChars);
        var normalized = renderedMarkdown.ReplaceLineEndings("\n").TrimEnd('\n');
        return normalized.Replace("\n", "\n" + continuationPrefix, StringComparison.Ordinal);
    }

    private static string ResolveBlockSlug(
        string bodyText,
        string markdownPath,
        int blockIndex,
        string? allowedRootPath,
        out string? fullSpecPath,
        out LoadedSpecification? inlineSpecification)
    {
        if (TryParseSpecReferenceSpan(bodyText, out _, out _, out var referencedSpecPath))
        {
            fullSpecPath = ResolveSpecPath(markdownPath, referencedSpecPath, allowedRootPath);
            inlineSpecification = null;
            return SlugifySchemaName(Path.GetFileNameWithoutExtension(fullSpecPath));
        }

        fullSpecPath = null;
        inlineSpecification = LoadInline(bodyText, markdownPath, blockIndex);
        return !string.IsNullOrWhiteSpace(inlineSpecification.Document.Info?.Title)
            ? SlugifySchemaName(inlineSpecification.Document.Info.Title)
            : "openapi";
    }

    private static bool TryParseSpecReferenceSpan(string bodyText, out int start, out int length, out string specPath)
    {
        start = 0;
        length = 0;
        specPath = string.Empty;
        if (string.IsNullOrWhiteSpace(bodyText)) return false;

        var trimmed = bodyText.Trim();
        if (trimmed.StartsWith('{')) return false;

        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(bodyText));
            var root = yaml.Documents.FirstOrDefault()?.RootNode;
            if (root is YamlMappingNode mapping)
            {
                if (mapping.Children.ContainsKey(new YamlScalarNode("openapi")) ||
                    mapping.Children.ContainsKey(new YamlScalarNode("swagger")) ||
                    mapping.Children.ContainsKey(new YamlScalarNode("paths")))
                    return false;

                if ((mapping.Children.TryGetValue(new YamlScalarNode("spec"), out var specNode) ||
                     mapping.Children.TryGetValue(new YamlScalarNode("path"), out specNode)) &&
                    specNode is YamlScalarNode scalarValue &&
                    !string.IsNullOrWhiteSpace(scalarValue.Value))
                {
                    start = checked((int)scalarValue.Start.Index);
                    length = checked((int)(scalarValue.End.Index - scalarValue.Start.Index));
                    specPath = scalarValue.Value.Trim();
                    return true;
                }

                return false;
            }

            if (root is YamlScalarNode scalar && !trimmed.Contains('\n') && !string.IsNullOrWhiteSpace(scalar.Value))
            {
                start = checked((int)scalar.Start.Index);
                length = checked((int)(scalar.End.Index - scalar.Start.Index));
                specPath = scalar.Value.Trim();
                return true;
            }
        }
        catch (YamlDotNet.Core.YamlException)
        {
            if (!trimmed.Contains('\n'))
            {
                start = bodyText.IndexOf(trimmed, StringComparison.Ordinal);
                length = trimmed.Length;
                specPath = trimmed;
                return true;
            }
        }

        return false;
    }

    private static string GetParentHtmlFileName(string markdownPath, string? pageOutputFileName)
    {
        if (!string.IsNullOrWhiteSpace(pageOutputFileName))
            return Path.GetFileName(pageOutputFileName);

        var fileName = Path.GetFileName(markdownPath);
        return fileName.Equals("README.md", StringComparison.OrdinalIgnoreCase)
            ? "index.html"
            : Path.ChangeExtension(fileName, ".html");
    }

    private static string AllocateSpecFileName(
        string pageStem,
        string slug,
        HashSet<string> usedFileNames)
    {
        var baseSlug = string.IsNullOrWhiteSpace(slug) ? "openapi" : slug;
        var baseFileName = string.Equals(pageStem, baseSlug, StringComparison.OrdinalIgnoreCase)
            ? $"{pageStem}-spec"
            : $"{pageStem}-{baseSlug}-spec";
        var specFileName = $"{baseFileName}.html";
        if (!usedFileNames.Add(specFileName))
        {
            var suffix = 2;
            while (!usedFileNames.Add($"{baseFileName}-{suffix}.html")) suffix++;
            specFileName = $"{baseFileName}-{suffix}.html";
        }

        return specFileName;
    }

    private static HashSet<string> CollectReservedHtmlFileNames(
        string markdownPath,
        string parentHtmlFileName,
        string? allowedRootPath,
        IEnumerable<OpenApiSiblingPage>? siblingPages)
    {
        var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            parentHtmlFileName
        };

        var siblingMarkdownFiles = new Dictionary<string, OpenApiSiblingPage>(StringComparer.OrdinalIgnoreCase);
        void AddSiblingPage(string filePath, string htmlFileName, string? siblingAllowedRootPath)
        {
            var normalizedHtmlFileName = Path.GetFileName(htmlFileName);
            if (string.IsNullOrWhiteSpace(normalizedHtmlFileName)) return;
            usedFileNames.Add(normalizedHtmlFileName);
            if (string.IsNullOrWhiteSpace(filePath)) return;
            var fullFilePath = Path.GetFullPath(filePath);
            var key = $"{fullFilePath}|{normalizedHtmlFileName}";
            siblingMarkdownFiles.TryAdd(key, new OpenApiSiblingPage(fullFilePath, normalizedHtmlFileName, siblingAllowedRootPath));
        }

        var markdownDirectory = Path.GetDirectoryName(markdownPath);
        if (!string.IsNullOrEmpty(markdownDirectory) && Directory.Exists(markdownDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(markdownDirectory))
            {
                var extension = Path.GetExtension(file);
                if (!extension.Equals(".md", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileName = Path.GetFileName(file);
                var htmlFileName = fileName.Equals("README.md", StringComparison.OrdinalIgnoreCase)
                    ? "index.html"
                    : Path.ChangeExtension(fileName, ".html");
                AddSiblingPage(file, htmlFileName, allowedRootPath);
            }
        }

        if (siblingPages is not null)
        {
            foreach (var sibling in siblingPages)
                AddSiblingPage(sibling.MarkdownPath, sibling.HtmlFileName, sibling.AllowedRootPath);
        }

        var current = (FilePath: Path.GetFullPath(markdownPath), HtmlFileName: parentHtmlFileName);
        var precedingSiblings = siblingMarkdownFiles.Values
            .Where(sibling => CompareSiblingOrder((sibling.MarkdownPath, sibling.HtmlFileName), current) < 0)
            .ToList();
        precedingSiblings.Sort((left, right) =>
            CompareSiblingOrder((left.MarkdownPath, left.HtmlFileName), (right.MarkdownPath, right.HtmlFileName)));

        foreach (var sibling in precedingSiblings)
            ReserveSiblingSpecFileNames(sibling.MarkdownPath, sibling.HtmlFileName, sibling.AllowedRootPath, usedFileNames);

        return usedFileNames;

        static int CompareSiblingOrder(
            (string FilePath, string HtmlFileName) left,
            (string FilePath, string HtmlFileName) right)
        {
            var stemComparison = StringComparer.OrdinalIgnoreCase.Compare(
                Path.GetFileNameWithoutExtension(left.HtmlFileName),
                Path.GetFileNameWithoutExtension(right.HtmlFileName));
            if (stemComparison != 0) return stemComparison;

            var htmlComparison = StringComparer.OrdinalIgnoreCase.Compare(left.HtmlFileName, right.HtmlFileName);
            if (htmlComparison != 0) return htmlComparison;

            return StringComparer.OrdinalIgnoreCase.Compare(left.FilePath, right.FilePath);
        }
    }

    private static void ReserveSiblingSpecFileNames(
        string siblingMarkdownPath,
        string siblingHtmlFileName,
        string? allowedRootPath,
        HashSet<string> usedFileNames)
    {
        string siblingMarkdown;
        try
        {
            siblingMarkdown = File.ReadAllText(siblingMarkdownPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (!siblingMarkdown.Contains(CodeBlockLanguage, StringComparison.OrdinalIgnoreCase))
            return;

        var parsed = Markdig.Markdown.Parse(siblingMarkdown, CodeBlockPipeline);
        var blocks = parsed.Descendants<FencedCodeBlock>().Where(IsOpenApiCodeBlock).ToArray();
        if (blocks.Length == 0) return;

        var siblingStem = Path.GetFileNameWithoutExtension(siblingHtmlFileName);
        for (var index = 0; index < blocks.Length; index++)
        {
            var block = blocks[index];
            var bodyText = TryExtractBlockBody(block, out var extracted, out _)
                ? extracted
                : string.Empty;

            string slug;
            try
            {
                slug = ResolveBlockSlug(bodyText, siblingMarkdownPath, index, allowedRootPath, out _, out _);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            AllocateSpecFileName(siblingStem, slug, usedFileNames);
        }
    }

    private static string GetOverviewAnchorId(int blockIndex) =>
        $"openapi-overview-{blockIndex + 1}";

    private static string BuildSpecificationPageMarkdown(
        string headingTitle,
        string sourceLabel,
        string language,
        string rawSpecification,
        string parentHtmlFileName,
        int blockIndex)
    {
        var backUrl = $"{Uri.EscapeDataString(parentHtmlFileName)}#{GetOverviewAnchorId(blockIndex)}";
        var fence = new string('`', Math.Max(3, LongestBacktickRun(rawSpecification) + 1));
        var safeTitle = sourceLabel.Replace("\"", "\\\"", StringComparison.Ordinal);
        var output = new StringBuilder(rawSpecification.Length + 256);
        output.Append("# ").AppendLine(EscapeMarkdown(headingTitle));
        output.AppendLine();
        output.Append("[← Back to API reference](").Append(backUrl).AppendLine(")");
        output.AppendLine();
        output.Append(fence).Append(language).Append(" title=\"").Append(safeTitle).AppendLine("\"");
        output.AppendLine(rawSpecification.TrimEnd());
        output.AppendLine(fence);
        return output.ToString();
    }

    private static LoadedSpecification LoadFromFile(string path)
    {
        var extension = Path.GetExtension(path);
        var format = extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".yml", StringComparison.OrdinalIgnoreCase)
            ? "yaml"
            : extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? "json"
                : throw new InvalidOperationException(
                    $"OpenAPI document '{path}' must use a .json, .yaml, or .yml extension.");

        try
        {
            var bytes = File.ReadAllBytes(path);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (Cache.TryGetValue(path, out var cached) && cached.Hash == hash)
                return new LoadedSpecification(cached.Document, cached.RawText, format);

            var rawText = DecodeUtf8WithoutBom(bytes);
            var document = ParseOpenApiDocument(bytes, format, path);
            Cache[path] = new CachedDocument(hash, document, rawText);
            return new LoadedSpecification(document, rawText, format);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OpenApiException or JsonException)
        {
            throw new InvalidOperationException($"Could not read OpenAPI document '{path}': {exception.Message}", exception);
        }
    }

    private static string DecodeUtf8WithoutBom(byte[] bytes)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        var start = bytes.AsSpan().StartsWith(preamble) ? preamble.Length : 0;
        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }

    private static void PruneInlineCache(string markdownPath, int activeBlockCount)
    {
        var prefix = $"inline:{Path.GetFullPath(markdownPath)}#";
        foreach (var key in Cache.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(key.AsSpan(prefix.Length), out var blockIndex) && blockIndex >= activeBlockCount)
                Cache.TryRemove(key, out _);
        }
    }

    private static LoadedSpecification LoadInline(string content, string markdownPath, int blockIndex)
    {
        var trimmed = content.Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException($"OpenAPI code block in '{markdownPath}' is empty.");

        var format = trimmed.StartsWith('{') ? "json" : "yaml";
        var bytes = Encoding.UTF8.GetBytes(trimmed);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var cacheKey = $"inline:{Path.GetFullPath(markdownPath)}#{blockIndex}";
        if (Cache.TryGetValue(cacheKey, out var cached) && cached.Hash == hash)
            return new LoadedSpecification(cached.Document, cached.RawText, format);

        try
        {
            var document = ParseOpenApiDocument(bytes, format, markdownPath);
            Cache[cacheKey] = new CachedDocument(hash, document, trimmed);
            return new LoadedSpecification(document, trimmed, format);
        }
        catch (Exception exception) when (exception is OpenApiException or JsonException)
        {
            throw new InvalidOperationException($"Could not read OpenAPI document '{markdownPath}': {exception.Message}", exception);
        }
    }

    private static OpenApiDocument ParseOpenApiDocument(byte[] bytes, string format, string sourceDescription)
    {
        using var stream = new MemoryStream(bytes);
        var settings = new OpenApiReaderSettings { LoadExternalRefs = false };
        settings.AddJsonReader();
        settings.AddYamlReader();
        var result = OpenApiDocument.LoadAsync(stream, format, settings).GetAwaiter().GetResult();
        var errors = result.Diagnostic?.Errors?.Select(error => error.Message).ToArray() ?? Array.Empty<string>();
        if (result.Document is null || errors.Length > 0)
            throw new InvalidOperationException($"Could not parse OpenAPI document '{sourceDescription}': {string.Join("; ", errors)}");

        return result.Document;
    }

    private static string Render(
        OpenApiDocument document,
        int blockIndex = 0,
        string? specificationPageUrl = null,
        bool omitTitle = false)
    {
        var output = new StringBuilder();
        output.AppendLine(BlockStartMarker);
        output.AppendLine();
        var info = document.Info;
        output.AppendLine($"## API overview {{#{GetOverviewAnchorId(blockIndex)} .openapi-overview}}");
        if (!omitTitle && !string.IsNullOrWhiteSpace(info?.Title))
            AppendParagraph(output, $"<p class=\"openapi-api-title\"><strong>{HtmlEncoder.Default.Encode(info.Title)}</strong></p>");
        if (!string.IsNullOrWhiteSpace(info?.Version))
            AppendParagraph(output, $"<p class=\"openapi-api-version\">Version <code>{HtmlEncoder.Default.Encode(info.Version)}</code></p>");
        AppendInfoMeta(output, info, specificationPageUrl);
        AppendMarkdown(output, info?.Description);

        // Raw HTML keeps the server list from merging into a list that ends the API description.
        var servers = (document.Servers ?? []).Where(server => !string.IsNullOrWhiteSpace(server.Url)).ToArray();
        if (servers.Length > 0)
        {
            output.Append("<div class=\"openapi-servers\"><span class=\"openapi-servers-label\">")
                .Append(servers.Length == 1 ? "Server" : "Servers")
                .Append("</span><ul>");
            foreach (var server in servers)
            {
                output.Append("<li><code>").Append(HtmlEncoder.Default.Encode(server.Url!)).Append("</code>");
                if (!string.IsNullOrWhiteSpace(server.Description))
                    output.Append("<span class=\"openapi-server-description\">")
                        .Append(HtmlEncoder.Default.Encode(server.Description))
                        .Append("</span>");
                output.Append("</li>");
            }
            output.AppendLine("</ul></div>");
            output.AppendLine();
        }

        AppendSecuritySchemes(output, document);

        var declaredTags = (document.Tags ?? new HashSet<OpenApiTag>()).Where(tag => !string.IsNullOrWhiteSpace(tag.Name)).ToArray();
        var tags = declaredTags.ToDictionary(tag => tag.Name!, tag => tag.Description ?? string.Empty, StringComparer.Ordinal);
        var tagOrder = declaredTags.Select(tag => tag.Name!).ToList();
        var operations = ReadOperations(document);
        var blockPrefix = blockIndex == 0 ? string.Empty : $"b{blockIndex + 1}-";
        foreach (var group in operations.GroupBy(operation => operation.Tag)
                     .OrderBy(group => TagOrder(tagOrder, group.Key)))
        {
            output.AppendLine($"## {CodeSpan(group.Key)} {{.openapi-tag}}");
            AppendMarkdown(output, tags.GetValueOrDefault(group.Key));
            foreach (var operation in group
                         .OrderBy(operation => OperationMethodOrder(operation.Method))
                         .ThenBy(operation => operation.Method, StringComparer.Ordinal)
                         .ThenBy(operation => operation.Path, StringComparer.Ordinal))
                AppendOperation(output, operation, document.Components?.Schemas, operation.Data.Security ?? document.Security, blockPrefix);
        }

        output.AppendLine(BlockEndMarker);
        return output.ToString();
    }

    private static List<Operation> ReadOperations(OpenApiDocument document)
    {
        var operations = new List<Operation>();
        foreach (var path in document.Paths)
        {
            var pathItem = ResolveReference(path.Value);
            foreach (var method in pathItem.Operations ?? [])
            {
                var tag = method.Value.Tags?.FirstOrDefault()?.Name ?? "Endpoints";
                var name = method.Key.Method.ToUpperInvariant();
                if (HttpMethods.Contains(name))
                    operations.Add(new Operation(path.Key, name, tag, pathItem, method.Value));
            }
        }

        return operations;
    }

    private static int OperationMethodOrder(string method) => method switch
    {
        "GET" => 0,
        "POST" => 1,
        "PUT" => 2,
        "PATCH" => 3,
        "DELETE" => 4,
        _ => 5
    };

    private static void AppendOperation(
        StringBuilder output,
        Operation operation,
        IDictionary<string, IOpenApiSchema>? schemas,
        IList<OpenApiSecurityRequirement>? security,
        string blockPrefix = "")
    {
        var pathParameterColors = GetPathParameterColors(operation.Path);
        var requirements = (security ?? []).Where(requirement => requirement.Count > 0).ToArray();
        var deprecated = operation.Data.Deprecated;
        var headingMarkup = new StringBuilder($"{CodeSpan(operation.Method)} {PathMarkup(operation.Path, pathParameterColors)}");
        if (deprecated) headingMarkup.Append(' ').Append(DeprecatedBadge);
        if (requirements.Length > 0)
        {
            var schemeNames = string.Join(" or ", requirements.Select(requirement =>
                string.Join(" + ", requirement.Keys.Select(SecuritySchemeName))));
            headingMarkup.Append(" <span class=\"openapi-lock\" role=\"img\" aria-label=\"Requires authorization\" data-tooltip=\"Requires ")
                .Append(HtmlEncoder.Default.Encode(schemeNames))
                .Append("\">").Append(LockIcon).Append("</span>");
        }
        output.AppendLine($"### {headingMarkup} {{.openapi-operation .openapi-method-{operation.Method.ToLowerInvariant()}{(deprecated ? " .openapi-deprecated" : string.Empty)}}}");
        var summary = operation.Data.Summary;
        if (!string.IsNullOrWhiteSpace(summary))
            AppendParagraph(output, $"<p class=\"openapi-operation-summary\">{HtmlEncoder.Default.Encode(summary.Trim())}</p>");
        if (deprecated)
            AppendParagraph(output, "<p class=\"openapi-deprecated-notice\">This operation is deprecated and should no longer be used.</p>");
        var operationId = operation.Data.OperationId;
        if (!string.IsNullOrWhiteSpace(operationId))
            AppendParagraph(output, $"<p class=\"openapi-operation-id\">Operation ID <code>{HtmlEncoder.Default.Encode(operationId)}</code></p>");
        // Many generators repeat the summary as the description; the summary already shows in the endpoint row.
        if (!SameText(summary, operation.Data.Description))
            AppendMarkdown(output, operation.Data.Description);
        AppendSecurityRequirements(output, requirements);

        AppendParameters(output, operation.PathItem, operation.Data, pathParameterColors, schemas);
        var operationKey = $"{blockPrefix}{operation.Method}-{operation.Path}";
        AppendRequestBody(output, operation.Data, schemas, operationKey);
        AppendResponses(output, operation.Data, schemas, operationKey);
    }

    private static void AppendParameters(
        StringBuilder output,
        IOpenApiPathItem pathItem,
        OpenApiOperation operation,
        IReadOnlyDictionary<string, string> pathParameterColors,
        IDictionary<string, IOpenApiSchema>? schemas)
    {
        var parameters = new Dictionary<string, IOpenApiParameter>(StringComparer.Ordinal);
        AddParameters(pathItem.Parameters ?? [], parameters);
        AddParameters(operation.Parameters ?? [], parameters);
        if (parameters.Count == 0) return;

        foreach (var group in parameters.Values
                     .Select(ResolveReference)
                     .GroupBy(parameter => parameter.In)
                     .OrderBy(group => ParameterLocationOrder(group.Key)))
        {
            output.AppendLine($"#### {ParameterHeading(group.Key)}");
            output.AppendLine("| Parameter | Description |");
            output.AppendLine("| --- | --- |");
            foreach (var parameter in group)
            {
                var name = parameter.Name ?? string.Empty;
                var nameMarkup = parameter.In == ParameterLocation.Path && pathParameterColors.TryGetValue(name, out var colorClass)
                    ? $"<span class=\"openapi-path-parameter {colorClass}\">{HtmlEncoder.Default.Encode(name)}</span>"
                    : HtmlEncoder.Default.Encode(name);
                if (parameter.Required) nameMarkup += RequiredMarker;
                output.AppendLine($"| {EscapeTableCell(DeclarationMarkup(nameMarkup, SchemaTypeMarkup(parameter.Schema), parameter.Schema, schemas, parameter.Deprecated, parameter.Example))} | {EscapeTableCell(parameter.Description)} |");
            }
            output.AppendLine();
        }
    }

    private static int ParameterLocationOrder(ParameterLocation? location) => location switch
    {
        ParameterLocation.Path => 0,
        ParameterLocation.Query => 1,
        ParameterLocation.Header => 2,
        ParameterLocation.Cookie => 3,
        _ => 4
    };

    private static string ParameterHeading(ParameterLocation? location) => location switch
    {
        ParameterLocation.Path => "Path Parameters",
        ParameterLocation.Query => "Query Parameters",
        ParameterLocation.Header => "Header Parameters",
        ParameterLocation.Cookie => "Cookie Parameters",
        _ => $"{location?.ToString() ?? "Other"} Parameters"
    };

    private static void AddParameters(IEnumerable<IOpenApiParameter> values, IDictionary<string, IOpenApiParameter> parameters)
    {
        foreach (var value in values)
        {
            var parameter = ResolveReference(value);
            var key = $"{parameter.In}:{parameter.Name}";
            parameters[key] = parameter;
        }
    }

    private static void AppendRequestBody(
        StringBuilder output,
        OpenApiOperation operation,
        IDictionary<string, IOpenApiSchema>? schemas,
        string operationKey)
    {
        if (operation.RequestBody is null) return;
        var requestBody = ResolveReference(operation.RequestBody);
        output.AppendLine(requestBody.Required
            ? "#### Request body <span class=\"openapi-badge openapi-badge-required\">Required</span> {.openapi-request-body}"
            : "#### Request body {.openapi-request-body}");
        AppendMarkdown(output, requestBody.Description);
        AppendMediaContent(
            output,
            requestBody.Content ?? new Dictionary<string, IOpenApiMediaType>(),
            schemas,
            $"{operationKey}-request",
            "openapi-request-media-type");
    }

    private static void AppendResponses(
        StringBuilder output,
        OpenApiOperation operation,
        IDictionary<string, IOpenApiSchema>? schemas,
        string operationKey)
    {
        if (operation.Responses is not { Count: > 0 }) return;
        foreach (var response in operation.Responses)
        {
            var responseObject = ResolveReference(response.Value);
            var statusName = ResponseStatusName(response.Key);
            var statusSuffix = string.IsNullOrWhiteSpace(statusName) ? string.Empty : $" - {EscapeMarkdown(statusName)}";
            output.AppendLine($"##### Response {CodeSpan(response.Key)}{statusSuffix} {{.openapi-response}}");
            AppendMarkdown(output, responseObject.Description);
            AppendResponseHeaders(output, responseObject.Headers, schemas);
            AppendResponseContent(
                output,
                responseObject.Content ?? new Dictionary<string, IOpenApiMediaType>(),
                schemas,
                $"{operationKey}-{response.Key}");
        }
    }

    private static void AppendResponseContent(
        StringBuilder output,
        IDictionary<string, IOpenApiMediaType> content,
        IDictionary<string, IOpenApiSchema>? schemas,
        string responseKey)
        => AppendMediaContent(output, content, schemas, responseKey, "openapi-media-type");

    private static void AppendMediaContent(
        StringBuilder output,
        IDictionary<string, IOpenApiMediaType> content,
        IDictionary<string, IOpenApiSchema>? schemas,
        string contentKey,
        string mediaHeadingClass)
    {
        foreach (var media in content)
        {
            var anchorPrefix = CreateSchemaAnchorPrefix($"{contentKey}-{media.Key}");
            output.AppendLine($"###### Media type {CodeSpan(media.Key)} {{.{mediaHeadingClass}}}");
            output.AppendLine("##### Example {.openapi-response-example}");
            AppendResponseSchemaName(output, media.Value.Schema, schemas);
            var example = media.Value.Example ?? (media.Value.Examples ?? new Dictionary<string, IOpenApiExample>()).Values
                .Select(ResolveReference)
                .Select(value => value.Value)
                .FirstOrDefault(value => value is not null);
            var exampleValue = example ?? (media.Value.Schema is null
                ? null
                : CreateSchemaExample(media.Value.Schema, mediaHeadingClass == "openapi-request-media-type"));
            if (exampleValue is null)
                output.AppendLine("No example is available.");
            else
                AppendCodeBlock(output, ExampleText(exampleValue, media.Key), ExampleLanguage(media.Key));

            output.AppendLine("##### Schema {.openapi-response-schema-table}");
            var flattenedArrayItemName = media.Value.Schema is null
                ? null
                : ArrayItemReferenceName(media.Value.Schema, schemas);
            var flattenedArrayItemAnchor = flattenedArrayItemName is null
                ? null
                : $"{anchorPrefix}-{SlugifySchemaName(flattenedArrayItemName)}";
            AppendResponseSchemaName(output, media.Value.Schema, schemas, flattenedArrayItemAnchor);
            AppendResponseSchemaTable(output, media.Value.Schema, schemas, anchorPrefix);
            AppendReferencedResponseSchemas(output, media.Value.Schema, schemas, anchorPrefix);

            output.AppendLine("##### Schema JSON {.openapi-response-schema-json}");
            AppendResponseSchemaName(output, media.Value.Schema, schemas);
            if (media.Value.Schema is null)
                output.AppendLine("No schema is defined.");
            else
            {
                AppendExample(output, SerializeSchema(media.Value.Schema));
                AppendReferencedResponseSchemaJson(output, media.Value.Schema, schemas);
            }
        }
    }

    private static string ExampleText(JsonNode example, string mediaType)
    {
        if (IsJsonMediaType(mediaType))
            return example.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        if (example is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        return example.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ExampleLanguage(string mediaType)
    {
        if (IsJsonMediaType(mediaType)) return "json";
        if (mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)) return "xml";
        if (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)) return "html";
        return string.Empty;
    }

    private static void AppendResponseSchemaName(
        StringBuilder output,
        IOpenApiSchema? schema,
        IDictionary<string, IOpenApiSchema>? components,
        string? anchor = null)
    {
        if (schema is null) return;
        if (anchor is not null)
            output.Append("<span id=\"").Append(HtmlEncoder.Default.Encode(anchor)).AppendLine("\"></span>");
        var deprecatedBadge = ResolveResponseSchemaReference(schema, components).Deprecated ? $" {DeprecatedBadge}" : string.Empty;
        output.AppendLine($"**{CodeSpan(SchemaName(schema, components))}**{deprecatedBadge}");
        output.AppendLine();
        AppendMarkdown(output, SchemaDescription(schema, components));
    }

    private static string? SchemaDescription(IOpenApiSchema schema, IDictionary<string, IOpenApiSchema>? components)
    {
        var resolved = ResolveResponseSchemaReference(schema, components);
        if (!string.IsNullOrWhiteSpace(resolved.Description)) return resolved.Description;
        return ArrayItemReferenceName(schema, components) is not null && resolved.Items is not null
            ? ResolveResponseSchemaReference(resolved.Items, components).Description
            : null;
    }

    private static void AppendResponseSchemaTable(
        StringBuilder output,
        IOpenApiSchema? schema,
        IDictionary<string, IOpenApiSchema>? components,
        string anchorPrefix)
    {
        if (schema is null)
        {
            output.AppendLine("No schema is defined.");
            return;
        }

        var tableSchema = ResolveResponseSchemaReference(schema, components);
        var isArrayOfReference = tableSchema.Type == JsonSchemaType.Array &&
                                 tableSchema.Items is not null &&
                                 GetReference(tableSchema.Items) is { IsLocal: true };
        if (isArrayOfReference) return;
        if (tableSchema.Items is not null && !isArrayOfReference)
            tableSchema = ResolveReference(tableSchema.Items);

        var properties = tableSchema.Properties ?? new Dictionary<string, IOpenApiSchema>();
        var required = tableSchema.Required ?? new HashSet<string>();
        var anchors = CreateSchemaAnchors(schema, components, anchorPrefix);
        output.AppendLine("| Property | Description |");
        output.AppendLine("| --- | --- |");
        if (properties.Count == 0)
        {
            output.AppendLine($"| {EscapeTableCell(DeclarationMarkup("value", SchemaTypeMarkup(schema), schema, components))} |  |");
            output.AppendLine();
            return;
        }

        AppendPropertyRows(output, properties, required, anchors, components);
        output.AppendLine();
    }

    private static void AppendPropertyRows(
        StringBuilder output,
        IDictionary<string, IOpenApiSchema> properties,
        ISet<string> required,
        IReadOnlyDictionary<string, string> anchors,
        IDictionary<string, IOpenApiSchema>? components)
    {
        foreach (var property in properties)
        {
            var propertySchema = ResolveReference(property.Value);
            var propertyName = HtmlEncoder.Default.Encode(property.Key) + (required.Contains(property.Key) ? RequiredMarker : string.Empty);
            var deprecated = property.Value.Deprecated || propertySchema.Deprecated;
            output.AppendLine($"| {EscapeTableCell(DeclarationMarkup(propertyName, SchemaTypeMarkup(property.Value, anchors), property.Value, components, deprecated))} | {EscapeTableCell(propertySchema.Description)} |");
        }
    }

    private static void AppendReferencedResponseSchemas(
        StringBuilder output,
        IOpenApiSchema? schema,
        IDictionary<string, IOpenApiSchema>? components,
        string anchorPrefix)
    {
        if (schema is null) return;
        var references = GetTableReferencedSchemas(schema, components);
        var flattenedArrayItemName = ArrayItemReferenceName(schema, components);
        var anchors = CreateSchemaAnchors(references.Keys, anchorPrefix);
        foreach (var (name, referencedSchema) in references)
        {
            var resolvedSchema = ResolveResponseSchemaReference(referencedSchema, components);
            if (name == flattenedArrayItemName)
            {
                if (resolvedSchema.Description != SchemaDescription(schema, components))
                    AppendMarkdown(output, resolvedSchema.Description);
                AppendSchemaPropertyTable(output, resolvedSchema, anchors, components);
                continue;
            }

            var deprecatedBadge = resolvedSchema.Deprecated ? $" {DeprecatedBadge}" : string.Empty;
            output.AppendLine($"###### {CodeSpan(name)}{deprecatedBadge} {{#{anchors[name]} .openapi-response-reference-schema}}");
            AppendMarkdown(output, resolvedSchema.Description);
            AppendSchemaPropertyTable(output, resolvedSchema, anchors, components);
        }
    }

    private static void AppendReferencedResponseSchemaJson(
        StringBuilder output,
        IOpenApiSchema schema,
        IDictionary<string, IOpenApiSchema>? components)
    {
        foreach (var (name, referencedSchema) in GetReferencedResponseSchemas(schema, components))
        {
            var resolvedSchema = ResolveResponseSchemaReference(referencedSchema, components);
            // A paragraph instead of a heading keeps automatic heading ids out, so repeated tabs stay identical.
            AppendParagraph(output, $"**{CodeSpan(name)}**");
            AppendMarkdown(output, resolvedSchema.Description);
            AppendExample(output, SerializeSchema(resolvedSchema));
        }
    }

    private static Dictionary<string, IOpenApiSchema> GetReferencedResponseSchemas(
        IOpenApiSchema schema,
        IDictionary<string, IOpenApiSchema>? components)
    {
        var references = CollectReferencedSchemas(schema, components);
        if (GetReference(schema) is { IsLocal: true, Id: { Length: > 0 } rootName })
            references.Remove(NormalizeSchemaReferenceName(rootName));
        return references;
    }

    private static string? ArrayItemReferenceName(
        IOpenApiSchema schema,
        IDictionary<string, IOpenApiSchema>? components)
    {
        var resolvedSchema = ResolveResponseSchemaReference(schema, components);
        return resolvedSchema.Type == JsonSchemaType.Array &&
               resolvedSchema.Items is not null &&
               GetReference(resolvedSchema.Items) is { IsLocal: true, Id: { Length: > 0 } id }
            ? NormalizeSchemaReferenceName(id)
            : null;
    }

    private static void AppendSchemaPropertyTable(
        StringBuilder output,
        IOpenApiSchema schema,
        IReadOnlyDictionary<string, string> anchors,
        IDictionary<string, IOpenApiSchema>? components)
    {
        var properties = schema.Properties ?? new Dictionary<string, IOpenApiSchema>();
        output.AppendLine("| Property | Description |");
        output.AppendLine("| --- | --- |");
        if (properties.Count == 0)
        {
            output.AppendLine($"| {EscapeTableCell(DeclarationMarkup("value", SchemaTypeMarkup(schema, anchors), schema, components))} |  |");
            output.AppendLine();
            return;
        }

        AppendPropertyRows(output, properties, schema.Required ?? new HashSet<string>(), anchors, components);
        output.AppendLine();
    }

    private static IReadOnlyDictionary<string, string> CreateSchemaAnchors(
        IOpenApiSchema? schema,
        IDictionary<string, IOpenApiSchema>? components,
        string anchorPrefix)
    {
        if (schema is null) return new Dictionary<string, string>();
        var references = GetTableReferencedSchemas(schema, components);
        return CreateSchemaAnchors(references.Keys, anchorPrefix);
    }

    // Enum values are listed on the rows that use them, so plain enums get no separate schema table.
    private static Dictionary<string, IOpenApiSchema> GetTableReferencedSchemas(
        IOpenApiSchema schema,
        IDictionary<string, IOpenApiSchema>? components)
    {
        var flattenedArrayItemName = ArrayItemReferenceName(schema, components);
        return GetReferencedResponseSchemas(schema, components)
            .Where(reference => reference.Key == flattenedArrayItemName ||
                                !IsEnumOnlySchema(ResolveResponseSchemaReference(reference.Value, components)))
            .ToDictionary(reference => reference.Key, reference => reference.Value, StringComparer.Ordinal);
    }

    private static bool IsEnumOnlySchema(IOpenApiSchema schema) =>
        schema.Enum is { Count: > 0 } && schema.Properties is not { Count: > 0 };

    private static IReadOnlyDictionary<string, string> CreateSchemaAnchors(
        IEnumerable<string> schemaNames,
        string anchorPrefix) => schemaNames.ToDictionary(
            name => name,
            name => $"{anchorPrefix}-{SlugifySchemaName(name)}",
            StringComparer.Ordinal);

    private static Dictionary<string, IOpenApiSchema> CollectReferencedSchemas(
        IOpenApiSchema schema,
        IDictionary<string, IOpenApiSchema>? components)
    {
        var references = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        var visited = new HashSet<IOpenApiSchema>(ReferenceEqualityComparer.Instance);
        Visit(schema);
        return references;

        void Visit(IOpenApiSchema current)
        {
            string? referenceName = null;
            if (GetReference(current) is { IsLocal: true, Id: { Length: > 0 } id })
            {
                referenceName = NormalizeSchemaReferenceName(id);
                references.TryAdd(referenceName, current);
            }

            current = ResolveResponseSchemaReference(current, components, referenceName);
            if (!visited.Add(current)) return;
            if (current.Items is not null) Visit(current.Items);
            if (current.AdditionalProperties is not null) Visit(current.AdditionalProperties);
            if (current.Not is not null) Visit(current.Not);
            foreach (var property in current.Properties?.Values ?? []) Visit(property);
            foreach (var nested in current.AllOf ?? []) Visit(nested);
            foreach (var nested in current.OneOf ?? []) Visit(nested);
            foreach (var nested in current.AnyOf ?? []) Visit(nested);
        }
    }

    private static IOpenApiSchema ResolveResponseSchemaReference(
        IOpenApiSchema schema,
        IDictionary<string, IOpenApiSchema>? components,
        string? referenceName = null)
    {
        var resolved = ResolveReference(schema);
        if (!ReferenceEquals(resolved, schema)) return resolved;
        if (referenceName is null && GetReference(schema) is { IsLocal: true, Id: { Length: > 0 } id })
            referenceName = NormalizeSchemaReferenceName(id);
        return referenceName is not null && components?.TryGetValue(referenceName, out var component) == true
            ? ResolveReference(component)
            : resolved;
    }

    private static string SchemaTypeMarkup(
        IOpenApiSchema? schema,
        IReadOnlyDictionary<string, string>? anchors = null)
    {
        if (schema is null) return TypeMarkup("any");
        if (GetReference(schema) is { IsLocal: true, Id: { Length: > 0 } id })
        {
            var name = NormalizeSchemaReferenceName(id);
            var display = TypeMarkup(name);
            return anchors is not null && anchors.TryGetValue(name, out var anchor)
                ? $"<a class=\"openapi-type-reference\" href=\"#{HtmlEncoder.Default.Encode(anchor)}\">{display}</a>"
                : display;
        }

        schema = ResolveReference(schema);
        if (schema.Type?.HasFlag(JsonSchemaType.Array) == true && schema.Items is not null)
        {
            var array = $"{TypeMarkup("[")}{SchemaTypeMarkup(schema.Items, anchors)}{TypeMarkup("]")}";
            return schema.Type.Value.HasFlag(JsonSchemaType.Null) ? AppendOptionalTypeMarker(array) : array;
        }
        // A map reads like the array notation: [string: Value].
        if (IsMapSchema(schema))
        {
            var map = $"{TypeMarkup("[string: ")}{SchemaTypeMarkup(schema.AdditionalProperties, anchors)}{TypeMarkup("]")}";
            return schema.Type?.HasFlag(JsonSchemaType.Null) == true ? AppendOptionalTypeMarker(map) : map;
        }
        if (schema.OneOf is { Count: > 0 } nullableOneOf && HasNullableUnion(nullableOneOf))
        {
            var nonNullable = nullableOneOf.Single(nested => SchemaType(nested) != "null");
            return AppendOptionalTypeMarker(SchemaTypeMarkup(nonNullable, anchors));
        }
        if (schema.AnyOf is { Count: > 0 } nullableAnyOf && HasNullableUnion(nullableAnyOf))
        {
            var nonNullable = nullableAnyOf.Single(nested => SchemaType(nested) != "null");
            return AppendOptionalTypeMarker(SchemaTypeMarkup(nonNullable, anchors));
        }
        if (schema.OneOf is { Count: > 0 } oneOf)
            return string.Join(" or ", oneOf.Select(nested => SchemaTypeMarkup(nested, anchors)));
        if (schema.AnyOf is { Count: > 0 } anyOf)
            return string.Join(" or ", anyOf.Select(nested => SchemaTypeMarkup(nested, anchors)));
        if (schema.AllOf is { Count: > 0 } allOf)
            return string.Join(" & ", allOf.Select(nested => SchemaTypeMarkup(nested, anchors)));
        return TypeMarkup(SchemaType(schema));
    }

    private const string RequiredMarker =
        "<span class=\"openapi-required-marker\" role=\"img\" aria-label=\"Required\" data-tooltip=\"Required\"><svg viewBox=\"0 0 10 10\" aria-hidden=\"true\" focusable=\"false\"><path d=\"M5 1.2v7.6M1.7 3.1l6.6 3.8M8.3 3.1 1.7 6.9\"></path></svg></span>";

    private static string DeclarationMarkup(
        string name,
        string type,
        IOpenApiSchema? schema = null,
        IDictionary<string, IOpenApiSchema>? components = null,
        bool deprecated = false,
        JsonNode? example = null)
    {
        var declaration = new StringBuilder(deprecated
                ? "<span class=\"openapi-declaration openapi-declaration-deprecated\"><span class=\"openapi-declaration-name\">"
                : "<span class=\"openapi-declaration\"><span class=\"openapi-declaration-name\">")
            .Append(name)
            .Append("</span><span class=\"openapi-declaration-colon\">:</span> <span class=\"openapi-declaration-type\">")
            .Append(type)
            .Append("</span>");
        var resolved = schema is null ? null : ResolveResponseSchemaReference(schema, components);
        if (resolved?.Default is { } defaultValue)
            declaration.Append("<span class=\"openapi-default\" data-tooltip=\"Default value\"> = ")
                .Append(HtmlEncoder.Default.Encode(defaultValue.ToJsonString()))
                .Append("</span>");
        if (deprecated) declaration.Append(' ').Append(DeprecatedBadge);
        declaration.Append("</span>");

        var facets = resolved is null ? new List<string>() : SchemaFacets(resolved);
        if (example is not null)
            facets.Add($"example <code>{HtmlEncoder.Default.Encode(EnumValueText(example))}</code>");
        if (facets.Count > 0)
            declaration.Append("<span class=\"openapi-facets\">")
                .AppendJoin(string.Empty, facets.Select(facet => $"<span class=\"openapi-facet\">{facet}</span>"))
                .Append("</span>");

        var values = schema is null ? null : EnumValues(schema, components);
        if (values is null) return declaration.ToString();
        declaration.Append("<span class=\"openapi-enum\" role=\"list\" aria-label=\"Allowed values\">");
        foreach (var value in values)
            declaration.Append("<code class=\"openapi-enum-value\" role=\"listitem\">")
                .Append(HtmlEncoder.Default.Encode(EnumValueText(value)))
                .Append("</code>");
        return declaration.Append("</span>").ToString();
    }

    private const string DeprecatedBadge = "<span class=\"openapi-badge openapi-badge-deprecated\">Deprecated</span>";

    private const string LockIcon =
        "<svg viewBox=\"0 0 20 20\" aria-hidden=\"true\" focusable=\"false\"><rect x=\"4.5\" y=\"9\" width=\"11\" height=\"8\" rx=\"1.8\"></rect><path d=\"M7 9V6.5a3 3 0 0 1 6 0V9\"></path></svg>";

    // Validation keywords and access modes, shortened to read like a type annotation.
    private static List<string> SchemaFacets(IOpenApiSchema schema)
    {
        var facets = new List<string>();
        if (schema.ReadOnly) facets.Add("read-only");
        if (schema.WriteOnly) facets.Add("write-only");
        var minLength = schema.MinLength is > 0 ? schema.MinLength : null;
        if (minLength is not null && schema.MaxLength is not null)
            facets.Add(minLength == schema.MaxLength ? $"length {minLength}" : $"length {minLength}–{schema.MaxLength}");
        else if (minLength is not null) facets.Add($"min length {minLength}");
        else if (schema.MaxLength is not null) facets.Add($"max length {schema.MaxLength}");
        AddBound(facets, schema.Minimum, schema.ExclusiveMinimum, "≥", ">");
        AddBound(facets, schema.Maximum, schema.ExclusiveMaximum, "≤", "<");
        if (schema.MultipleOf is { } multipleOf) facets.Add($"multiple of {FormatNumber(multipleOf)}");
        var minItems = schema.MinItems is > 0 ? schema.MinItems : null;
        if (minItems is not null && schema.MaxItems is not null)
            facets.Add(minItems == schema.MaxItems ? $"{minItems} items" : $"{minItems}–{schema.MaxItems} items");
        else if (minItems is not null) facets.Add($"at least {minItems} {(minItems == 1 ? "item" : "items")}");
        else if (schema.MaxItems is not null) facets.Add($"at most {schema.MaxItems} {(schema.MaxItems == 1 ? "item" : "items")}");
        if (schema.UniqueItems == true) facets.Add("unique items");
        if (!string.IsNullOrWhiteSpace(schema.Pattern))
            facets.Add($"pattern <code>{HtmlEncoder.Default.Encode(schema.Pattern)}</code>");
        return facets;
    }

    private static void AddBound(List<string> facets, object? inclusive, object? exclusive, string inclusiveSymbol, string exclusiveSymbol)
    {
        if (IsNumber(exclusive)) facets.Add($"{exclusiveSymbol} {FormatNumber(exclusive!)}");
        else if (IsNumber(inclusive)) facets.Add($"{inclusiveSymbol} {FormatNumber(inclusive!)}");
    }

    private static bool IsNumber(object? value) => ParseNumber(value) is not null;

    private static string FormatNumber(object value) =>
        HtmlEncoder.Default.Encode(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);

    private static bool SameText(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second) &&
        string.Equals(first.Trim().TrimEnd('.'), second.Trim().TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    private static string SecuritySchemeName(OpenApiSecuritySchemeReference scheme) =>
        scheme.Reference.Id ?? scheme.Name ?? "security scheme";

    private static string SecuritySchemeKind(IOpenApiSecurityScheme scheme) => scheme.Type switch
    {
        SecuritySchemeType.Http when string.Equals(scheme.Scheme, "bearer", StringComparison.OrdinalIgnoreCase) =>
            string.IsNullOrWhiteSpace(scheme.BearerFormat) ? "Bearer token" : $"Bearer token ({scheme.BearerFormat})",
        SecuritySchemeType.Http when string.Equals(scheme.Scheme, "basic", StringComparison.OrdinalIgnoreCase) => "HTTP Basic",
        SecuritySchemeType.Http => $"HTTP {scheme.Scheme}",
        SecuritySchemeType.ApiKey => $"API key {(string.IsNullOrWhiteSpace(scheme.Name) ? string.Empty : $"<code>{HtmlEncoder.Default.Encode(scheme.Name)}</code> ")}in {scheme.In?.ToString().ToLowerInvariant() ?? "request"}",
        SecuritySchemeType.OAuth2 => "OAuth 2.0",
        SecuritySchemeType.OpenIdConnect => "OpenID Connect",
        _ => HtmlEncoder.Default.Encode(scheme.Type?.ToString() ?? "Custom")
    };

    private static void AppendSecuritySchemes(StringBuilder output, OpenApiDocument document)
    {
        var schemes = document.Components?.SecuritySchemes;
        if (schemes is not { Count: > 0 }) return;
        output.Append("<div class=\"openapi-servers openapi-auth\"><span class=\"openapi-servers-label\">Authentication</span><ul>");
        foreach (var (name, scheme) in schemes)
        {
            output.Append("<li><code>").Append(HtmlEncoder.Default.Encode(name)).Append("</code>")
                .Append("<span class=\"openapi-security-kind\">").Append(SecuritySchemeKind(scheme)).Append("</span>");
            if (!string.IsNullOrWhiteSpace(scheme.Description))
                output.Append("<span class=\"openapi-server-description\">").Append(HtmlEncoder.Default.Encode(scheme.Description)).Append("</span>");
            output.Append("</li>");
        }
        output.AppendLine("</ul></div>");
        output.AppendLine();
    }

    private static void AppendSecurityRequirements(StringBuilder output, IReadOnlyList<OpenApiSecurityRequirement> requirements)
    {
        if (requirements.Count == 0) return;
        output.Append("<div class=\"openapi-security\"><span class=\"openapi-security-label\">")
            .Append(LockIcon)
            .Append("Authorization</span><ul>");
        foreach (var requirement in requirements)
        {
            output.Append("<li>");
            var first = true;
            foreach (var (scheme, scopes) in requirement)
            {
                if (!first) output.Append("<span class=\"openapi-security-and\">and</span>");
                first = false;
                output.Append("<code>").Append(HtmlEncoder.Default.Encode(SecuritySchemeName(scheme))).Append("</code>")
                    .Append("<span class=\"openapi-security-kind\">").Append(SecuritySchemeKind(scheme)).Append("</span>");
                foreach (var scope in scopes ?? [])
                    output.Append("<code class=\"openapi-enum-value\">").Append(HtmlEncoder.Default.Encode(scope)).Append("</code>");
            }
            output.Append("</li>");
        }
        output.AppendLine("</ul></div>");
        output.AppendLine();
    }

    private static void AppendInfoMeta(StringBuilder output, OpenApiInfo? info, string? specificationPageUrl = null)
    {
        var items = new List<string>();
        if (info?.License is { Name.Length: > 0 } license)
            items.Add($"License {Link(license.Url, license.Name)}");
        if (info?.Contact is { } contact && (contact.Name ?? contact.Email ?? contact.Url?.ToString()) is { Length: > 0 } contactName)
            items.Add($"Contact {Link(contact.Url ?? (contact.Email is null ? null : new Uri($"mailto:{contact.Email}")), contactName)}");
        if (info?.TermsOfService is { } terms)
            items.Add(Link(terms, "Terms of service"));
        if (!string.IsNullOrWhiteSpace(specificationPageUrl))
            items.Add($"<a class=\"openapi-spec-link\" href=\"{HtmlEncoder.Default.Encode(specificationPageUrl)}\">OpenAPI specification</a>");
        if (items.Count > 0)
            AppendParagraph(output, $"<p class=\"openapi-api-meta\">{string.Join("<span aria-hidden=\"true\"> · </span>", items)}</p>");
    }

    private static string Link(Uri? url, string text) => url is null
        ? HtmlEncoder.Default.Encode(text)
        : $"<a href=\"{HtmlEncoder.Default.Encode(url.ToString())}\">{HtmlEncoder.Default.Encode(text)}</a>";

    private static void AppendResponseHeaders(
        StringBuilder output,
        IDictionary<string, IOpenApiHeader>? headers,
        IDictionary<string, IOpenApiSchema>? components)
    {
        if (headers is not { Count: > 0 }) return;
        output.AppendLine("<p class=\"openapi-response-headers-label\">Response headers</p>");
        output.AppendLine();
        output.AppendLine("| Header | Description |");
        output.AppendLine("| --- | --- |");
        foreach (var (name, header) in headers)
        {
            var nameMarkup = HtmlEncoder.Default.Encode(name) + (header.Required ? RequiredMarker : string.Empty);
            output.AppendLine($"| {EscapeTableCell(DeclarationMarkup(nameMarkup, SchemaTypeMarkup(header.Schema), header.Schema, components, header.Deprecated))} | {EscapeTableCell(header.Description)} |");
        }
        output.AppendLine();
    }

    private static IList<JsonNode>? EnumValues(IOpenApiSchema schema, IDictionary<string, IOpenApiSchema>? components)
    {
        var resolved = ResolveResponseSchemaReference(schema, components);
        if (resolved.Enum is { Count: > 0 } values) return values;
        if (resolved.Type?.HasFlag(JsonSchemaType.Array) == true && resolved.Items is not null)
            return EnumValues(resolved.Items, components);
        var union = resolved.OneOf is { Count: > 0 } ? resolved.OneOf : resolved.AnyOf;
        return union is { Count: > 0 } && HasNullableUnion(union)
            ? EnumValues(union.Single(nested => SchemaType(nested) != "null"), components)
            : null;
    }

    private static string EnumValueText(JsonNode? value) =>
        value is JsonValue text && text.TryGetValue<string>(out var stringValue)
            ? stringValue
            : value?.ToJsonString() ?? "null";

    private static string TypeMarkup(string type)
    {
        var optional = type.EndsWith('?');
        var name = optional ? type[..^1] : type;
        var markup = $"<span class=\"openapi-type\"><span class=\"openapi-type-keyword\">{HtmlEncoder.Default.Encode(name)}</span>";
        return optional ? $"{markup}<span class=\"openapi-type-optional\">?</span></span>" : $"{markup}</span>";
    }

    private static string AppendOptionalTypeMarker(string markup) =>
        $"<span class=\"openapi-type\">{markup}<span class=\"openapi-type-optional\">?</span></span>";

    private static string SchemaName(IOpenApiSchema schema, IDictionary<string, IOpenApiSchema>? components)
    {
        var resolvedSchema = ResolveResponseSchemaReference(schema, components);
        if (resolvedSchema.Type == JsonSchemaType.Array && resolvedSchema.Items is not null)
            return $"[{SchemaName(resolvedSchema.Items, components)}]";
        if (GetReference(schema) is { IsLocal: true, Id: { Length: > 0 } id })
            return NormalizeSchemaReferenceName(id);
        return !string.IsNullOrWhiteSpace(resolvedSchema.Title) ? resolvedSchema.Title : SchemaType(resolvedSchema);
    }

    private static string CreateSchemaAnchorPrefix(string value)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..10].ToLowerInvariant();
        return $"openapi-response-schema-{hash}";
    }

    private static string NormalizeSchemaReferenceName(string name) =>
        name.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);

    private static string SlugifySchemaName(string name)
    {
        var slug = new StringBuilder(name.Length);
        foreach (var character in name.ToLowerInvariant())
            if (char.IsAsciiLetterOrDigit(character)) slug.Append(character);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        return slug.ToString().Trim('-');
    }

    private static bool IsJsonMediaType(string mediaType)
    {
        var type = mediaType.Split(';', 2)[0].Trim();
        return type.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
               type.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    private static string SerializeSchema(IOpenApiSchema schema)
    {
        using var text = new StringWriter();
        var writer = new OpenApiJsonWriter(text, new OpenApiJsonWriterSettings { Terse = false });
        ((OpenApiSchema)ResolveReference(schema)).SerializeAsV31(writer);
        text.Flush();
        return text.ToString();
    }

    // Request examples leave out read-only properties and response examples leave out write-only ones.
    private static JsonNode? CreateSchemaExample(IOpenApiSchema schema, bool forRequest) =>
        CreateSchemaExample(schema, new HashSet<IOpenApiSchema>(ReferenceEqualityComparer.Instance), forRequest);

    private static JsonNode? CreateSchemaExample(IOpenApiSchema schema, HashSet<IOpenApiSchema> ancestors, bool forRequest)
    {
        schema = ResolveReference(schema);
        if (!ancestors.Add(schema)) return null;

        try
        {
            var suppliedExample = schema.Examples?.FirstOrDefault();
#pragma warning disable CS0618 // OpenAPI 3.0 still uses the singular schema example field.
            suppliedExample ??= schema.Example;
#pragma warning restore CS0618
            suppliedExample ??= schema.Default ?? schema.Enum?.FirstOrDefault();
            if (suppliedExample is not null) return suppliedExample.DeepClone();
            if (schema.OneOf?.FirstOrDefault() is { } oneOf) return CreateSchemaExample(oneOf, ancestors, forRequest);
            if (schema.AnyOf?.FirstOrDefault() is { } anyOf) return CreateSchemaExample(anyOf, ancestors, forRequest);

            if (schema.Type?.HasFlag(JsonSchemaType.Array) == true || schema.Items is not null)
            {
                var example = new JsonArray();
                if (schema.Items is not null) example.Add(CreateSchemaExample(schema.Items, ancestors, forRequest));
                return example;
            }

            if (schema.AllOf is { Count: > 0 })
            {
                var example = new JsonObject();
                foreach (var part in schema.AllOf)
                {
                    if (CreateSchemaExample(part, ancestors, forRequest) is not JsonObject partExample) continue;
                    foreach (var property in partExample)
                        example[property.Key] = property.Value?.DeepClone();
                }
                AddSchemaProperties(example, schema, ancestors, forRequest);
                return example;
            }

            if (IsMapSchema(schema))
                return new JsonObject { ["additionalProp1"] = CreateSchemaExample(schema.AdditionalProperties!, ancestors, forRequest) };

            if (schema.Properties is { Count: > 0 } || schema.Type?.HasFlag(JsonSchemaType.Object) == true)
            {
                var example = new JsonObject();
                AddSchemaProperties(example, schema, ancestors, forRequest);
                return example;
            }

            var type = schema.Type ?? JsonSchemaType.Null;
            if (type.HasFlag(JsonSchemaType.String)) return JsonValue.Create(StringExample(schema.Format));
            if (type.HasFlag(JsonSchemaType.Integer)) return JsonValue.Create(NumberExample(schema, 1m));
            if (type.HasFlag(JsonSchemaType.Number)) return JsonValue.Create(NumberExample(schema, ParseNumber(schema.MultipleOf) ?? 1m));
            if (type.HasFlag(JsonSchemaType.Boolean)) return JsonValue.Create(false);
            return null;
        }
        finally
        {
            ancestors.Remove(schema);
        }
    }

    private static string StringExample(string? format) => format?.ToLowerInvariant() switch
    {
        "uuid" => "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "date-time" => "2025-01-31T12:00:00Z",
        "date" => "2025-01-31",
        "time" => "12:00:00",
        "duration" => "PT30M",
        "email" => "user@example.com",
        "uri" or "url" => "https://example.com",
        "hostname" => "example.com",
        "ipv4" => "192.0.2.1",
        "ipv6" => "2001:db8::1",
        "byte" => "U3RyaW5n",
        _ => "string"
    };

    private static void AddSchemaProperties(JsonObject example, IOpenApiSchema schema, HashSet<IOpenApiSchema> ancestors, bool forRequest)
    {
        foreach (var property in schema.Properties ?? new Dictionary<string, IOpenApiSchema>())
        {
            var propertySchema = ResolveReference(property.Value);
            if (forRequest ? propertySchema.ReadOnly : propertySchema.WriteOnly) continue;
            example[property.Key] = CreateSchemaExample(property.Value, ancestors, forRequest);
        }
    }

    // Generated numbers stay inside the declared bounds, so an exclusive minimum of 0 does not produce 0.
    private static decimal NumberExample(IOpenApiSchema schema, decimal step)
    {
        if (ParseNumber(schema.Minimum) is { } minimum) return minimum;
        if (ParseNumber(schema.ExclusiveMinimum) is { } exclusiveMinimum) return exclusiveMinimum + step;
        if (ParseNumber(schema.Maximum) is { } maximum && maximum < 0) return maximum;
        if (ParseNumber(schema.ExclusiveMaximum) is { } exclusiveMaximum && exclusiveMaximum <= 0) return exclusiveMaximum - step;
        return 0;
    }

    private static decimal? ParseNumber(object? value) =>
        value is not null && decimal.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static string? ResponseStatusName(string status)
    {
        var normalizedStatus = status.ToUpperInvariant();
        var name = normalizedStatus switch
        {
            "1XX" => "Informational",
            "2XX" => "Successful",
            "3XX" => "Redirection",
            "4XX" => "Client Error",
            "5XX" => "Server Error",
            "DEFAULT" => "Default",
            _ => null
        };

        if (name is not null) return name;
        if (int.TryParse(status, out var code) && code is >= 100 and <= 599)
        {
            using var response = new HttpResponseMessage((HttpStatusCode)code);
            return response.ReasonPhrase;
        }

        return null;
    }

    private static void AppendExample(StringBuilder output, string json)
        => AppendCodeBlock(output, json, "json");

    private static void AppendCodeBlock(StringBuilder output, string code, string language)
    {
        var fenceLength = Math.Max(3, LongestBacktickRun(code) + 1);
        var fence = new string('`', fenceLength);
        output.Append(fence).AppendLine(language);
        output.AppendLine(code);
        output.AppendLine(fence);
        output.AppendLine();
    }

    private static string SchemaType(IOpenApiSchema? schema)
    {
        if (schema is null) return "any";
        if (GetReference(schema) is { IsLocal: true, Id: { Length: > 0 } id })
            return NormalizeSchemaReferenceName(id);

        schema = ResolveReference(schema);
        if (schema.OneOf is { Count: > 0 } oneOf) return SchemaUnionType(oneOf);
        if (schema.AnyOf is { Count: > 0 } anyOf) return SchemaUnionType(anyOf);
        if (schema.AllOf is { Count: > 0 } allOf) return string.Join(" & ", allOf.Select(SchemaType));

        var type = schema.Type is { } schemaType ? FormatSchemaType(schemaType) : null;
        if (type is null) type = schema.Properties is { Count: > 0 } ? "object" : "any";
        var optional = type.EndsWith('?');
        if (optional) type = type[..^1];
        // A format such as uuid, int64, or date-time names a primitive more precisely than its JSON type.
        var display = string.IsNullOrWhiteSpace(schema.Format)
            ? type
            : type is "string" or "integer" or "number"
                ? FormatName(schema.Format)
                : $"{type} ({schema.Format})";
        if (schema.Type == JsonSchemaType.Array && schema.Items is not null)
            display = $"array of {SchemaType(schema.Items)}";
        if (IsMapSchema(schema))
            display = $"[string: {SchemaType(schema.AdditionalProperties)}]";
        if (optional) display += "?";
        return display;
    }

    private static bool IsMapSchema(IOpenApiSchema schema) =>
        schema.AdditionalProperties is not null && schema.Properties is not { Count: > 0 } &&
        schema.Type?.HasFlag(JsonSchemaType.Array) != true;

    private static string FormatName(string format)
    {
        var parts = format.Trim().ToLowerInvariant().Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select((part, index) => index == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string SchemaUnionType(IEnumerable<IOpenApiSchema> schemas)
    {
        var types = schemas.Select(SchemaType).ToArray();
        return HasNullableUnion(types)
            ? $"{types.Single(type => type != "null")}?"
            : string.Join(" or ", types);
    }

    private static bool HasNullableUnion(IEnumerable<IOpenApiSchema> schemas) =>
        HasNullableUnion(schemas.Select(SchemaType).ToArray());

    private static bool HasNullableUnion(IReadOnlyCollection<string> types) =>
        types.Count == 2 && types.Count(type => type == "null") == 1;

    private static string FormatSchemaType(JsonSchemaType type)
    {
        if (type.HasFlag(JsonSchemaType.Null) && type != JsonSchemaType.Null)
        {
            var nonNullType = (type & ~JsonSchemaType.Null).ToString()
                .Replace(", ", " or ", StringComparison.Ordinal)
                .ToLowerInvariant();
            return $"{nonNullType}?";
        }

        return type.ToString().Replace(", ", " or ", StringComparison.Ordinal).ToLowerInvariant();
    }

    private static IOpenApiPathItem ResolveReference(IOpenApiPathItem item) =>
        item is IOpenApiReferenceHolder<OpenApiPathItem, IOpenApiPathItem, OpenApiReferenceWithDescriptionAndSummary> reference
            ? reference.RecursiveTarget ?? item
            : item;

    private static IOpenApiParameter ResolveReference(IOpenApiParameter item) =>
        item is IOpenApiReferenceHolder<OpenApiParameter, IOpenApiParameter, OpenApiReferenceWithDescription> reference
            ? reference.RecursiveTarget ?? item
            : item;

    private static IOpenApiRequestBody ResolveReference(IOpenApiRequestBody item) =>
        item is IOpenApiReferenceHolder<OpenApiRequestBody, IOpenApiRequestBody, OpenApiReferenceWithDescription> reference
            ? reference.RecursiveTarget ?? item
            : item;

    private static IOpenApiResponse ResolveReference(IOpenApiResponse item) =>
        item is IOpenApiReferenceHolder<OpenApiResponse, IOpenApiResponse, OpenApiReferenceWithDescriptionAndSummary> reference
            ? reference.RecursiveTarget ?? item
            : item;

    private static IOpenApiSchema ResolveReference(IOpenApiSchema item) =>
        item is IOpenApiReferenceHolder<OpenApiSchema, IOpenApiSchema, JsonSchemaReference> reference
            ? reference.RecursiveTarget ?? item
            : item;

    private static IOpenApiExample ResolveReference(IOpenApiExample item) =>
        item is IOpenApiReferenceHolder<OpenApiExample, IOpenApiExample, OpenApiReferenceWithDescriptionAndSummary> reference
            ? reference.RecursiveTarget ?? item
            : item;

    private static BaseOpenApiReference? GetReference(IOpenApiSchema item) =>
        item is IOpenApiReferenceHolder<OpenApiSchema, IOpenApiSchema, JsonSchemaReference> reference
            ? reference.Reference
            : null;

    private static int TagOrder(IReadOnlyList<string> tags, string tag)
    {
        for (var index = 0; index < tags.Count; index++)
            if (tags[index].Equals(tag, StringComparison.Ordinal)) return index;
        return int.MaxValue;
    }

    private static string EscapeMarkdown(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("*", "\\*", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal)
        .Replace("]", "\\]", StringComparison.Ordinal)
        .Replace("`", "&#96;", StringComparison.Ordinal);

    private static IReadOnlyDictionary<string, string> GetPathParameterColors(string path)
    {
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in PathParameterRegex.Matches(path))
        {
            var name = match.Value[1..^1];
            if (!colors.ContainsKey(name))
                colors.Add(name, $"openapi-path-parameter-color-{colors.Count % 6 + 1}");
        }
        return colors;
    }

    private static string PathMarkup(string path, IReadOnlyDictionary<string, string> pathParameterColors)
    {
        var output = new StringBuilder("<span class=\"openapi-path\">");
        var position = 0;
        foreach (Match match in PathParameterRegex.Matches(path))
        {
            output.Append(EncodePath(path[position..match.Index]));
            var name = match.Value[1..^1];
            var colorClass = pathParameterColors.TryGetValue(name, out var color)
                ? color
                : "openapi-path-parameter-color-1";
            output.Append($"<span class=\"openapi-path-parameter {colorClass}\">")
                .Append(EncodePath(match.Value))
                .Append("</span>");
            position = match.Index + match.Length;
        }

        output.Append(EncodePath(path[position..]));
        return output.Append("</span>").ToString();
    }

    private static string EncodePath(string value) => HtmlEncoder.Default.Encode(value)
        .Replace("{", "&#123;", StringComparison.Ordinal)
        .Replace("}", "&#125;", StringComparison.Ordinal);

    private static string EscapeTableCell(string? value) =>
        (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string CodeSpan(string value)
    {
        var fence = new string('`', Math.Max(1, LongestBacktickRun(value) + 1));
        var content = value.StartsWith('`') || value.EndsWith('`') ? $" {value} " : value;
        return $"{fence}{content}{fence}";
    }

    private static int LongestBacktickRun(string value)
    {
        var longest = 0;
        var current = 0;
        foreach (var character in value)
        {
            current = character == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }
        return longest;
    }

    private static void AppendParagraph(StringBuilder output, string paragraph)
    {
        output.AppendLine(paragraph);
        output.AppendLine();
    }

    private static void AppendMarkdown(StringBuilder output, string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return;
        output.AppendLine(markdown.Trim());
        output.AppendLine();
    }

    private sealed record Operation(
        string Path,
        string Method,
        string Tag,
        IOpenApiPathItem PathItem,
        OpenApiOperation Data);

    private sealed record LoadedSpecification(OpenApiDocument Document, string RawText, string Format);

    private sealed record CachedDocument(string Hash, OpenApiDocument Document, string RawText);
}
