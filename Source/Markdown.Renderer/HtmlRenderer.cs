using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Net;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Abbreviations;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdown.Renderer.MarkdigExtension;
using Markdown.Renderer.Res.Model;

namespace Markdown.Renderer;

public class HtmlRenderer
{
    private static readonly Regex TemplateTokenRegex = new(@"%%(?<name>[A-Z_]+)%%", RegexOptions.Compiled);
    private static readonly Regex HeadingRegex = new(
        @"<h(?<level>[1-6])(?<attributes>\s[^>]*)?>(?<content>.*?)</h\k<level>>",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex HeadingIdRegex = new(@"\bid=""(?<id>[^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ClassAttributeRegex = new(@"\bclass=""(?<class>[^""]*)""", RegexOptions.Compiled);
    private static readonly Regex OpenApiResponseCodeRegex = new(
        @"^Response\s*<code>(?<status>[^<]+)</code>(?<suffix>.*?)$",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex OpenApiMediaTypeCodeRegex = new(
        @"^Media type\s*<code>(?<mediaType>[^<]+)</code>",
        RegexOptions.Compiled);
    private static readonly Regex OpenApiTagEndpointRegex = new(
        "<details class=\"openapi-endpoint (?<method>openapi-method-[a-z]+)(?<deprecated> openapi-endpoint-deprecated)?\"",
        RegexOptions.Compiled);
    private static readonly Regex OpenApiOperationSummaryRegex = new(
        @"\A\s*<p class=""openapi-operation-summary"">(?<summary>.*?)</p>\s*",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private const string OpenApiSharedStart = "<!--openapi-shared-->";
    private const string OpenApiSharedEnd = "<!--/openapi-shared-->";
    private const string OpenApiSharedScopeToken = "openapi-shared-scope";
    private static readonly Regex OpenApiSharedPanelRegex = new(
        "<!--openapi-shared-->(?<content>.*?)<!--/openapi-shared-->",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex OpenApiSchemaAnchorPrefixRegex = new(
        "openapi-response-schema-[0-9a-f]{10}",
        RegexOptions.Compiled);
    private static readonly Regex OpenApiTableHeaderRegex = new(@"<th>(?<label>.*?)</th>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly string[] OpenApiTagMethodOrder = ["GET", "POST", "PUT", "PATCH", "DELETE"];
    private static readonly Regex OpenApiPermalinkRegex = new(
        @"<a\b[^>]*\bclass=""headerlink""[^>]*>.*?</a>",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex FootnoteReferenceRegex = new(
        @"<a\b[^>]*\bclass=""footnote-ref""[^>]*>.*?</a>",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private readonly MarkdownPipeline _basePipeline;
    private MarkdownPipeline _pipeline;
    private readonly LinkReplacer _linkReplacer = new LinkReplacer();
    private string? _basePath;
    private IReadOnlyList<string> _iconSearchAssets = Array.Empty<string>();
    private SiteConfiguration _siteConfiguration = new();

    public HtmlRenderer()
    {
        _basePipeline = CreatePipeline();
        _pipeline = _basePipeline;
    }

    private static MarkdownPipeline CreatePipeline(SharedAbbreviationsExtension? sharedAbbreviations = null)
    {
        var pipelineBuilder = new MarkdownPipelineBuilder();
        pipelineBuilder
            .UseGridTables()
            .UsePipeTables()
            .UseAdvancedExtensions();
        if (sharedAbbreviations is not null) pipelineBuilder.Extensions.Add(sharedAbbreviations);
        return pipelineBuilder
            .UseScrollableTables()
            .UseRenderedMath()
            .UseGitHubAlerts()
            .UseIconsAndEmojis()
            .UseKeyboardKeys()
            .UseStaticCodeHighlighter()
            .Build();
    }

    public void SetGlossary(string? markdown)
    {
        if (markdown is null)
        {
            _pipeline = _basePipeline;
            return;
        }

        var sharedAbbreviations = new SharedAbbreviationsExtension();
        var pipeline = CreatePipeline(sharedAbbreviations);
        var document = Markdig.Markdown.Parse(markdown, pipeline);
        var definitions = document.GetAbbreviations();
        var sharedDefinitions = definitions?.Select(pair => new SharedAbbreviation(
            pair.Key,
            pair.Value.Label ?? pair.Key,
            pair.Value.Text.ToString())).ToArray() ?? [];
        if (sharedDefinitions.Length == 0)
            throw new InvalidOperationException("The configured glossary does not contain any abbreviation definitions.");

        sharedAbbreviations.SetDefinitions(sharedDefinitions);
        _pipeline = pipeline;
    }
    /// <summary>
    /// The icon search files written to the base path because a rendered page contains an icon search.
    /// </summary>
    public IReadOnlyList<string> IconSearchAssets => _iconSearchAssets;

    public void PrepareBasePath(string basePath, SiteConfiguration? siteConfiguration = null)
    {
        _basePath = basePath;
        _siteConfiguration = siteConfiguration ?? new SiteConfiguration();
        _iconSearchAssets = Array.Empty<string>();
        var cssFolder = GetEnsuredCssFolderPath(basePath);
        var cssFilePath = Path.Combine(cssFolder, "main.css");

        WriteCssFileTo(cssFilePath);
    }

    private MarkdownDocument ParseMarkdown(string markdown, string? pdfDownloadUrl = null)
    {
        var document = Markdig.Markdown.Parse(markdown, _pipeline);
        _linkReplacer.ReplaceAll(document, pdfDownloadUrl);
        return document;
    }

    public bool ContainsPdfLink(string markdown) =>
        Markdig.Markdown.Parse(markdown, _pipeline)
            .Descendants<LinkInline>()
            .Any(link => LinkReplacer.IsPdfLink(link.Url));

    public IReadOnlyCollection<string> FindMediaProviders(string markdown)
    {
        var document = ParseMarkdown(markdown);
        var html = document.ToHtml(_pipeline);
        MediaLinkEmbedder.ReplaceStandaloneLinks(html, out var providers);
        return providers;
    }

    public string RenderMarkdownFragment(string markdown)
    {
        var document = ParseMarkdown(markdown);
        var html = document.ToHtml(_pipeline)
            .Replace(OpenApiMarkdownRenderer.BlockStartMarker, string.Empty, StringComparison.Ordinal)
            .Replace(OpenApiMarkdownRenderer.BlockEndMarker, string.Empty, StringComparison.Ordinal);
        return MediaLinkEmbedder.ReplaceStandaloneLinks(html, out _);
    }

    public async Task WriteHtml(
        TextWriter writer,
        string title,
        string description,
        int nestingDeep,
        string markdown,
        string editPageLink,
        IReadOnlyList<NavigationItem>? navigationItems = null,
        IReadOnlyList<BreadcrumbItem>? breadcrumbs = null,
        IReadOnlyCollection<string>? siteMediaProviders = null,
        string pageIcon = "",
        IReadOnlyList<NavigationItem>? topNavigationItems = null,
        DateTimeOffset? lastModified = null,
        string? pdfDownloadUrl = null,
        DashboardOptions? dashboard = null)
    {
        var prefix = string.Concat(Enumerable.Repeat("../", nestingDeep));
        var cssFileLink = prefix + "css/main.css";
        var searchScriptLink = prefix + "js/search.js";
        var searchIndexLink = prefix + "search-index.json";
        
        var document = ParseMarkdown(markdown, pdfDownloadUrl);
        var hasIconSearch = document.Descendants<FencedCodeBlock>()
            .Any(block => block.Info == IconSearch.CodeBlockLanguage);
        if (hasIconSearch) EnsureIconSearchAssets();
        var hasHero = document.Descendants<FencedCodeBlock>()
            .Any(block => block.Info == Hero.CodeBlockLanguage);
        var renderedMarkdown = dashboard is null ? document.ToHtml(_pipeline) : Dashboard.Render(document, _pipeline, dashboard);
        renderedMarkdown = MediaLinkEmbedder.ReplaceStandaloneLinks(renderedMarkdown, out var mediaProviders);
        var pageHero = hasHero ? Hero.ExtractFullWidth(ref renderedMarkdown) : string.Empty;
        renderedMarkdown = InsertFootnotesTitle(renderedMarkdown);
        if (!string.IsNullOrWhiteSpace(pageIcon))
        {
            var iconName = pageIcon.Trim().Trim(':').Replace('/', '-');
            var iconHtml = Markdig.Markdown.Parse($":{iconName}:", _pipeline).ToHtml(_pipeline)
                .Replace("<p>", string.Empty, StringComparison.Ordinal)
                .Replace("</p>\n", string.Empty, StringComparison.Ordinal)
                .Trim();
            renderedMarkdown = HeadingRegex.Replace(renderedMarkdown, match =>
            {
                if (match.Groups["level"].Value != "1") return match.Value;
                var openEnd = match.Value.IndexOf('>');
                return match.Value.Insert(openEnd + 1, iconHtml + " ");
            }, 1);
        }
        var providersForSettings = siteMediaProviders is { Count: > 0 } ? siteMediaProviders : mediaProviders;
        var tocHeadings = GetTableOfContentsHeadings(renderedMarkdown);
        var pageHeading = GetPageHeadingTitle(renderedMarkdown);
        renderedMarkdown = InsertHeadingPermalinks(renderedMarkdown);
        renderedMarkdown = CollapseOpenApiBlocks(renderedMarkdown);
        renderedMarkdown = ShareOpenApiTabPanels(renderedMarkdown);
        var hasOpenApiInteractiveContent = renderedMarkdown.Contains("class=\"openapi-responses\"", StringComparison.Ordinal) ||
                                           renderedMarkdown.Contains("class=\"openapi-tag-list\"", StringComparison.Ordinal) ||
                                           renderedMarkdown.Contains("class=\"openapi-request-body\"", StringComparison.Ordinal);
        renderedMarkdown = InsertEditPageLink(renderedMarkdown, editPageLink, out var editLinkInserted);
        var breadcrumbMarkup = RenderBreadcrumbs(renderedMarkdown, breadcrumbs ?? Array.Empty<BreadcrumbItem>());
        var model = new MainModel
        {
            Title = title,
            Description = description,
            RenderedMarkdown = renderedMarkdown,
            NavigationItems = navigationItems ?? Array.Empty<NavigationItem>(),
            TopNavigationItems = topNavigationItems ?? Array.Empty<NavigationItem>(),
            CssLink = cssFileLink,
            EditPageLink = editPageLink
        };
        var hasTopNavigation = model.TopNavigationItems.Count > 0;
        var hasCurrentTopSection = model.NavigationItems.Any(item =>
            item.IsTopNavigationItem && item.IsInCurrentTopSection);
        var currentTopNavigationItem = model.TopNavigationItems.FirstOrDefault(item => item.IsCurrent || item.IsCurrentSection);
        var hasUnassignedSidebarPages = model.NavigationItems.Any(item =>
            item.IsOutsideTopNavigationSection && !item.IsAncestorOfCurrentTopSection);
        var hasWideSidebar = !hasTopNavigation || (hasCurrentTopSection
            ? hasUnassignedSidebarPages || model.NavigationItems.Any(item => item.IsInCurrentTopSection && !item.IsTopNavigationItem)
            : model.NavigationItems.Any(item => item.IsCurrent && item.Depth > 0));

        var values = new Dictionary<string, string>
        {
            ["TITLE"] = HtmlEncoder.Default.Encode(model.Title),
            ["DESCRIPTION"] = HtmlEncoder.Default.Encode(model.Description),
            ["CSS_LINK"] = HtmlEncoder.Default.Encode(model.CssLink),
            ["SITE_NAME"] = HtmlEncoder.Default.Encode(_siteConfiguration.SiteName),
            ["SITE_BRAND"] = RenderSiteBrand(prefix, _siteConfiguration, pageHeading),
            ["FAVICON_LINK"] = RenderFavicon(prefix, _siteConfiguration),
            ["SITE_THEME_STYLE"] = RenderThemeStyles(_siteConfiguration),
            ["SEARCH_SCRIPT_LINK"] = HtmlEncoder.Default.Encode(searchScriptLink),
            ["SEARCH_INDEX_LINK"] = HtmlEncoder.Default.Encode(searchIndexLink),
            ["TOC_LAYOUT_CLASS"] = dashboard is null ? " has-on-this-page" : string.Empty,
            ["NAVIGATION_ITEMS"] = RenderNavigationItems(model.NavigationItems),
            ["TOP_NAVIGATION_ITEMS"] = RenderTopNavigationItems(model.TopNavigationItems),
            ["SIDEBAR_SECTION_TITLE"] = currentTopNavigationItem is not null && hasWideSidebar && !hasUnassignedSidebarPages
                ? $"<div class=\"site-nav-section-title\">{HtmlEncoder.Default.Encode(currentTopNavigationItem.Title)}</div>"
                : string.Empty,
            ["TOP_NAVIGATION_CLASS"] = hasTopNavigation ? string.Empty : " is-empty",
            ["SITE_LAYOUT_CLASS"] = (hasTopNavigation
                ? hasWideSidebar
                    ? hasCurrentTopSection
                        ? " has-top-navigation has-current-top-section has-wide-sidebar"
                        : " has-top-navigation has-wide-sidebar"
                    : " has-top-navigation no-wide-sidebar"
                : " has-wide-sidebar")
                + (dashboard is null ? string.Empty : " is-dashboard"),
            ["MEDIA_SETTINGS_BUTTON"] = providersForSettings.Count > 0 ? MediaLinkEmbedder.SettingsButtonHtml : string.Empty,
            ["MEDIA_SETTINGS_DIALOG"] = MediaLinkEmbedder.RenderSettingsDialog(providersForSettings),
            ["MARKDOWN"] = model.RenderedMarkdown,
            ["PAGE_HERO"] = pageHero,
            ["PAGE_STYLES"] = (hasHero ? $"<style>{Hero.Styles}</style>" : string.Empty)
                + (dashboard is null ? string.Empty : $"<style>{Dashboard.Styles}</style>"),
            ["PAGE_BREADCRUMBS"] = dashboard is null ? breadcrumbMarkup : string.Empty,
            ["PAGE_TOC"] = dashboard is null ? RenderOnThisPage(tocHeadings) : string.Empty,
            ["PAGE_FOOTER"] = RenderPageFooter(model.NavigationItems, editPageLink, !editLinkInserted, lastModified),
            ["BACK_TO_TOP"] = "<a class=\"back-to-top\" id=\"back-to-top\" href=\"#top\" aria-label=\"Back to top\" title=\"Back to top\"><svg viewBox=\"0 0 20 20\" aria-hidden=\"true\"><path d=\"M10 16V4m-5 5 5-5 5 5\"></path></svg><span>Back to top</span></a>",
            ["GITHUB_REPOSITORY_LINK"] = RenderGitHubRepositoryLink(editPageLink),
            ["PAGE_SCRIPTS"] =
                (hasIconSearch
                    ? $"<script defer src=\"{HtmlEncoder.Default.Encode(prefix + "js/" + IconSearch.ScriptFileName)}\"></script>{Environment.NewLine}"
                    : string.Empty)
                + (providersForSettings.Count > 0 ? $"<script>{MediaLinkEmbedder.Script}</script>{Environment.NewLine}" : string.Empty)
                + (hasOpenApiInteractiveContent ? $"<script>{OpenApiResponseScript}</script>{Environment.NewLine}" : string.Empty)
                + (hasHero ? $"<script>{Hero.Script}</script>{Environment.NewLine}" : string.Empty)
                + (dashboard is null ? string.Empty : $"<script>{Dashboard.Script}</script>{Environment.NewLine}")
        };
        var renderedHtml = TemplateTokenRegex.Replace(Resources.HtmlTemplateContent(), match =>
            values.TryGetValue(match.Groups["name"].Value, out var value) ? value : match.Value);
        
        await writer.WriteAsync(renderedHtml);
    }

    private void EnsureIconSearchAssets()
    {
        if (_basePath is null || _iconSearchAssets.Count > 0) return;
        _iconSearchAssets = IconSearch.WriteAssetsTo(_basePath);
    }

    private static string RenderSiteBrand(string prefix, SiteConfiguration configuration, string pageHeading)
    {
        var name = HtmlEncoder.Default.Encode(configuration.SiteName);
        var heading = HtmlEncoder.Default.Encode(pageHeading.Length > 0 ? pageHeading : configuration.SiteName);
        var light = ResolveAssetUrl(prefix, configuration.LogoLight);
        var dark = ResolveAssetUrl(prefix, configuration.LogoDark);
        var logo = light.Length == 0 ? string.Empty :
            $"<img class=\"site-brand-logo\" src=\"{HtmlEncoder.Default.Encode(light)}\" data-light-src=\"{HtmlEncoder.Default.Encode(light)}\" data-dark-src=\"{HtmlEncoder.Default.Encode(dark)}\" alt=\"\">";
        return $"<div class=\"site-header-leading\"><div class=\"site-header-icon-slot{(light.Length > 0 ? " has-logo" : string.Empty)}\">{logo}<button class=\"navigation-toggle\" id=\"navigation-toggle\" type=\"button\" aria-expanded=\"true\" aria-controls=\"site-navigation\" aria-label=\"Collapse navigation\" title=\"Collapse navigation\"><svg viewBox=\"0 0 20 20\" aria-hidden=\"true\" focusable=\"false\"><rect x=\"2.5\" y=\"3.5\" width=\"15\" height=\"13\" rx=\"2\"></rect><path d=\"M8 4v12M5 7h1M5 10h1M5 13h1\"></path></svg></button></div><a class=\"site-brand\" href=\"{HtmlEncoder.Default.Encode(prefix + "index.html")}\"><span id=\"site-brand-title\" data-site-title=\"{name}\" data-page-title=\"{heading}\">{name}</span></a></div>";
    }

    private static string RenderFavicon(string prefix, SiteConfiguration configuration)
    {
        var light = ResolveAssetUrl(prefix, configuration.FaviconLight);
        var dark = ResolveAssetUrl(prefix, configuration.FaviconDark);
        if (light.Length == 0)
        {
            return dark.Length == 0 ? string.Empty :
                $"<link rel=\"icon\" href=\"{HtmlEncoder.Default.Encode(dark)}\" media=\"(prefers-color-scheme: dark)\">";
        }
        var lightLink = $"<link rel=\"icon\" href=\"{HtmlEncoder.Default.Encode(light)}\">";
        if (dark.Length == 0 || string.Equals(light, dark, StringComparison.Ordinal)) return lightLink;
        return lightLink + $"<link rel=\"icon\" href=\"{HtmlEncoder.Default.Encode(dark)}\" media=\"(prefers-color-scheme: dark)\">";
    }

    private static string ResolveAssetUrl(string prefix, string? asset)
    {
        if (string.IsNullOrWhiteSpace(asset)) return string.Empty;
        if (Uri.TryCreate(asset, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return asset;
        return prefix + string.Join('/', asset.Replace('\\', '/').TrimStart('/').Split('/').Select(Uri.EscapeDataString));
    }

    private static string RenderThemeStyles(SiteConfiguration configuration)
    {
        var light = GetPaletteVariables(configuration.Light);
        var dark = GetPaletteVariables(configuration.Dark);
        if (light.Length == 0 && dark.Length == 0) return string.Empty;
        var css = new StringBuilder("<style>\n");
        if (light.Length > 0) css.Append(":root,:root[data-theme=\"light\"]{").Append(light).Append("}\n");
        if (dark.Length > 0)
        {
            css.Append(":root[data-theme=\"dark\"]{").Append(dark).Append("}\n")
                .Append("@media(prefers-color-scheme:dark){:root[data-theme=\"auto\"]{").Append(dark).Append("}}\n");
        }
        return css.Append("</style>").ToString();
    }

    private static string GetPaletteVariables(SitePalette? palette)
    {
        if (palette is null) return string.Empty;
        var values = new List<string>();
        Add("--page-background", palette.Background);
        Add("--surface-header", palette.Background);
        Add("--text-primary", palette.Text);
        Add("--link", palette.Primary);
        // Secondary text, lines and surfaces follow a custom palette instead of staying on the default neutrals.
        if (palette.Background is not null || palette.Text is not null)
        {
            values.Add($"--text-secondary:{MixText(63)}");
            values.Add($"--border:{MixText(16)}");
            values.Add($"--border-subtle:{MixText(11)}");
            values.Add($"--header-divider:{MixText(11)}");
            values.Add($"--code-block-border:{MixText(11)}");
            values.Add($"--surface-current:{MixText(5)}");
            values.Add($"--surface-code:{MixText(8)}");
            values.Add($"--surface-code-block:{MixText(5)}");
            values.Add($"--surface-code-block-body:{MixText(2)}");
        }
        if (palette.Primary is not null) values.Add("--nav-hover:color-mix(in oklab,var(--link) 16%,var(--page-background))");
        return string.Join(';', values);

        static string MixText(int percent) =>
            $"color-mix(in oklab,var(--text-primary) {percent}%,var(--page-background))";

        void Add(string name, string? value)
        {
            if (value is not null && Regex.IsMatch(value, @"^[#a-zA-Z0-9(),.% ]+$")) values.Add($"{name}:{value}");
        }
    }

    private static string RenderGitHubRepositoryLink(string editPageLink)
    {
        if (!Uri.TryCreate(editPageLink, UriKind.Absolute, out var editPageUri) ||
            (editPageUri.Scheme != Uri.UriSchemeHttp && editPageUri.Scheme != Uri.UriSchemeHttps))
            return string.Empty;

        var pathSegments = editPageUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Length < 4 || !pathSegments[2].Equals("edit", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var repositoryName = $"{pathSegments[0]}/{pathSegments[1]}";
        var repositoryUrl = $"{editPageUri.GetLeftPart(UriPartial.Authority)}/{repositoryName}";
        var encodedRepositoryUrl = HtmlEncoder.Default.Encode(repositoryUrl);
        var encodedRepositoryName = HtmlEncoder.Default.Encode(repositoryName);

        return $"<a class=\"repository-link\" href=\"{encodedRepositoryUrl}\" target=\"_blank\" rel=\"noopener noreferrer\" title=\"GitHub repository: {encodedRepositoryName}\" aria-label=\"GitHub repository: {encodedRepositoryName}\"><svg viewBox=\"0 0 24 24\" aria-hidden=\"true\" focusable=\"false\"><path d=\"M12 .5C5.65.5.5 5.65.5 12c0 5.1 3.31 9.28 7.94 10.78.58.11.79-.25.79-.56v-2.02c-3.23.7-3.91-1.37-3.91-1.37-.53-1.34-1.29-1.7-1.29-1.7-1.05-.72.08-.71.08-.71 1.16.08 1.77 1.19 1.77 1.19 1.03 1.76 2.7 1.25 3.36.96.1-.75.4-1.25.73-1.54-2.58-.29-5.29-1.29-5.29-5.75 0-1.27.45-2.31 1.19-3.13-.12-.29-.52-1.48.11-3.08 0 0 .97-.31 3.17 1.19a11.1 11.1 0 0 1 5.77 0c2.2-1.5 3.17-1.19 3.17-1.19.63 1.6.23 2.79.11 3.08.74.82 1.19 1.86 1.19 3.13 0 4.47-2.72 5.45-5.31 5.74.42.36.78 1.07.78 2.16v3.2c0 .31.21.67.8.56A11.5 11.5 0 0 0 23.5 12C23.5 5.65 18.35.5 12 .5Z\"></path></svg><span>{encodedRepositoryName}</span></a>";
    }

    private static string RenderNavigationItems(IReadOnlyList<NavigationItem> navigationItems)
    {
        var output = new StringBuilder();
        var index = 0;
        RenderItems(0);
        return output.ToString();

        void RenderItems(int depth)
        {
            while (index < navigationItems.Count && navigationItems[index].Depth == depth)
            {
                var itemIndex = index++;
                var item = navigationItems[itemIndex];
                var hasChildren = index < navigationItems.Count && navigationItems[index].Depth > depth;
                var hasToggle = hasChildren && depth > 0;
                var indentDepth = Math.Max(0, item.Depth - 1);
                var encodedTitle = HtmlEncoder.Default.Encode(item.Title);
                var encodedUrl = HtmlEncoder.Default.Encode(item.Url);
                var childrenId = $"site-nav-children-{itemIndex}";
                var expanded = !hasToggle || item.IsExpandedByDefault;
                var itemIndent = new string(' ', 16 + depth * 8);
                var rowIndent = itemIndent + "    ";
                var contentIndent = rowIndent + "    ";

                var itemClass = item.IsTopNavigationItem ? "site-nav-item top-navigation-entry" : "site-nav-item";
                if (item.IsCurrentSection) itemClass += " is-current-section";
                if (item.IsOutsideTopNavigationSection) itemClass += " is-outside-top-navigation-section";
                if (item.IsInCurrentTopSection) itemClass += " is-in-current-top-section";
                if (item.IsAncestorOfCurrentTopSection) itemClass += " is-top-navigation-ancestor";
                output.Append(itemIndent).AppendLine($"<li class=\"{itemClass}\" style=\"--indent-depth: {indentDepth}; --wide-indent-depth: {Math.Max(0, item.TopNavigationDepth)}\">");
                var rowClass = item.IsCurrent ? " is-current" : item.IsCurrentSection ? " is-current-section" : string.Empty;
                var currentValue = item.IsCurrent ? "page" : item.IsCurrentSection ? "location" : "false";
                output.Append(rowIndent).AppendLine($"<div class=\"site-nav-row{rowClass}\">");
                if (hasToggle)
                {
                    var label = expanded ? "Collapse" : "Expand";
                    var expandedValue = expanded ? "true" : "false";
                    output.Append(contentIndent).AppendLine($"<button class=\"site-nav-toggle\" type=\"button\" aria-expanded=\"{expandedValue}\" aria-controls=\"{childrenId}\" aria-label=\"{label} {encodedTitle}\"><svg viewBox=\"0 0 12 12\" aria-hidden=\"true\" focusable=\"false\"><path d=\"M3 2l4 4-4 4\"></path></svg></button>");
                }
                else if (depth > 0)
                {
                    output.Append(contentIndent).AppendLine("<span class=\"site-nav-toggle-spacer\" aria-hidden=\"true\"></span>");
                }

                output.Append(contentIndent).AppendLine($"<a class=\"site-nav-link{(item.IsCurrent ? " is-current" : item.IsCurrentSection ? " is-current-section" : string.Empty)}\" href=\"{encodedUrl}\" aria-current=\"{currentValue}\">{encodedTitle}</a>");
                output.Append(rowIndent).AppendLine("</div>");

                if (hasChildren)
                {
                    var childClass = item.IsTopNavigationItem ? "site-nav-list site-nav-children top-navigation-children" : "site-nav-list site-nav-children";
                    output.Append(rowIndent).AppendLine($"<ul class=\"{childClass}\" id=\"{childrenId}\"{(expanded ? string.Empty : " hidden")}>");
                    RenderItems(depth + 1);
                    output.Append(rowIndent).AppendLine("</ul>");
                }

                output.Append(itemIndent).AppendLine("</li>");
            }
        }
    }

    private static string RenderTopNavigationItems(IReadOnlyList<NavigationItem> navigationItems)
    {
        var output = new StringBuilder();
        foreach (var item in navigationItems)
        {
            var title = HtmlEncoder.Default.Encode(item.Title);
            var url = HtmlEncoder.Default.Encode(item.Url);
            var isCurrentSection = item.IsCurrent || item.IsCurrentSection;
            output.AppendLine($"            <a class=\"top-nav-link{(isCurrentSection ? " is-current" : string.Empty)}\" href=\"{url}\" aria-current=\"{(item.IsCurrent ? "page" : isCurrentSection ? "location" : "false")}\" data-label=\"{title}\">{title}</a>");
        }

        return output.ToString();
    }

    private static IReadOnlyList<TocHeading> GetTableOfContentsHeadings(string renderedMarkdown)
    {
        var openApiRanges = GetOpenApiBlockRanges(renderedMarkdown);
        var matches = HeadingRegex.Matches(renderedMarkdown).Cast<Match>().ToArray();
        var isUnmarkedOpenApiPage = openApiRanges.Count == 0 && matches.Any(match =>
            HasClass(match, "openapi-overview") ||
            HasClass(match, "openapi-tag") ||
            HasClass(match, "openapi-operation"));
        var headings = new List<TocHeading>();
        foreach (var match in matches)
        {
            var inOpenApiBlock = isUnmarkedOpenApiPage ||
                                 openApiRanges.Any(range => match.Index >= range.Start && match.Index < range.End);
            if (inOpenApiBlock && !HasClass(match, "openapi-tag")) continue;
            if (HasClass(match, "openapi-response") ||
                HasClass(match, "openapi-media-type") ||
                HasClass(match, "openapi-response-example") ||
                HasClass(match, "openapi-response-schema-table") ||
                HasClass(match, "openapi-response-schema-json") ||
                HasClass(match, "openapi-response-reference-schema")) continue;
            var level = match.Groups["level"].Value[0] - '0';
            if (level < 2) continue;

            var idMatch = HeadingIdRegex.Match(match.Groups["attributes"].Value);
            if (!idMatch.Success) continue;

            var title = GetHeadingText(match.Groups["content"].Value);
            if (title.Length == 0) continue;

            headings.Add(new TocHeading(level, idMatch.Groups["id"].Value, title));
        }

        return headings;
    }

    private static IReadOnlyList<(int Start, int End)> GetOpenApiBlockRanges(string renderedMarkdown)
    {
        var ranges = new List<(int Start, int End)>();
        var position = 0;
        while (position < renderedMarkdown.Length)
        {
            var start = renderedMarkdown.IndexOf(OpenApiMarkdownRenderer.BlockStartMarker, position, StringComparison.Ordinal);
            if (start < 0) break;
            var end = renderedMarkdown.IndexOf(OpenApiMarkdownRenderer.BlockEndMarker, start + OpenApiMarkdownRenderer.BlockStartMarker.Length, StringComparison.Ordinal);
            if (end < 0) break;
            ranges.Add((start, end + OpenApiMarkdownRenderer.BlockEndMarker.Length));
            position = end + OpenApiMarkdownRenderer.BlockEndMarker.Length;
        }

        return ranges;
    }

    private static string CollapseOpenApiBlocks(string renderedMarkdown)
    {
        var ranges = GetOpenApiBlockRanges(renderedMarkdown);
        if (ranges.Count == 0)
            return CollapseOpenApiTagGroups(CollapseOpenApiOperations(renderedMarkdown));

        var output = new StringBuilder(renderedMarkdown.Length);
        var copiedUntil = 0;
        foreach (var (start, end) in ranges)
        {
            output.Append(renderedMarkdown, copiedUntil, start - copiedUntil);
            var innerStart = start + OpenApiMarkdownRenderer.BlockStartMarker.Length;
            var innerEnd = end - OpenApiMarkdownRenderer.BlockEndMarker.Length;
            var block = renderedMarkdown[innerStart..innerEnd];
            output.Append(CollapseOpenApiTagGroups(CollapseOpenApiOperations(block)));
            copiedUntil = end;
        }

        output.Append(renderedMarkdown, copiedUntil, renderedMarkdown.Length - copiedUntil);
        return output.ToString();
    }

    private static string RenderOnThisPage(IReadOnlyList<TocHeading> headings)
    {
        if (headings.Count == 0) return string.Empty;
        var output = new StringBuilder();
        output.AppendLine("        <aside class=\"on-this-page\" id=\"on-this-page\" aria-label=\"On this Page\">");
        output.AppendLine("            <h2 class=\"toc-heading\">On this Page</h2>");
        output.AppendLine("            <nav class=\"toc-nav\" id=\"toc-nav\" aria-label=\"On this Page\">");
        output.AppendLine("                <ul class=\"toc-list\">");
        for (var index = 0; index < headings.Count; index++)
        {
            var heading = headings[index];
            var id = HtmlEncoder.Default.Encode(heading.Id);
            var title = HtmlEncoder.Default.Encode(heading.Title);
            var depth = heading.Level - 2;
            output.AppendLine($"                    <li style=\"--toc-depth: {depth}\"><a class=\"toc-link\" href=\"#{id}\">{title}</a></li>");
        }
        output.AppendLine("                </ul>");
        output.AppendLine("            </nav>");
        output.AppendLine("        </aside>");
        return output.ToString();
    }

    private sealed record TocHeading(int Level, string Id, string Title);

    // The heading has no id, so it stays out of the page contents and gets no permalink.
    private static string InsertFootnotesTitle(string renderedMarkdown) => renderedMarkdown.Replace(
        "<div class=\"footnotes\">\n<hr />\n",
        "<div class=\"footnotes\">\n<hr />\n<h2 class=\"footnotes-title\">Footnotes</h2>\n",
        StringComparison.Ordinal);

    private static string RenderPageFooter(
        IReadOnlyList<NavigationItem> navigationItems,
        string editPageLink,
        bool showEditPageLink,
        DateTimeOffset? lastModified)
    {
        var meta = new List<string>();
        if (lastModified is { } date)
        {
            var isoDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var displayDate = date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
            meta.Add($"<span>Last updated on <time datetime=\"{isoDate}\">{displayDate}</time></span>");
        }
        if (showEditPageLink && !string.IsNullOrWhiteSpace(editPageLink))
            meta.Add($"<a href=\"{HtmlEncoder.Default.Encode(editPageLink)}\" target=\"_blank\" rel=\"noopener noreferrer\">Edit this page</a>");

        // The sidebar order is the reading order, so its neighbors are the previous and next pages.
        var currentIndex = -1;
        for (var index = 0; index < navigationItems.Count; index++)
        {
            if (!navigationItems[index].IsCurrent) continue;
            currentIndex = index;
            break;
        }
        var previous = currentIndex > 0 ? navigationItems[currentIndex - 1] : null;
        var next = currentIndex >= 0 && currentIndex < navigationItems.Count - 1 ? navigationItems[currentIndex + 1] : null;
        if (meta.Count == 0 && previous is null && next is null) return string.Empty;

        var output = new StringBuilder();
        output.AppendLine("<footer class=\"page-footer\">");
        if (meta.Count > 0)
            output.AppendLine($"    <div class=\"page-meta\">{string.Join(string.Empty, meta)}</div>");
        if (previous is not null || next is not null)
        {
            output.AppendLine("    <nav class=\"page-pagination\" aria-label=\"Previous and next page\">");
            if (previous is not null)
                output.AppendLine(RenderPaginationLink(previous, "is-previous", "prev", "Previous"));
            if (next is not null)
                output.AppendLine(RenderPaginationLink(next, "is-next", "next", "Next"));
            output.AppendLine("    </nav>");
        }
        output.AppendLine("</footer>");
        return output.ToString();

        static string RenderPaginationLink(NavigationItem item, string className, string rel, string label) =>
            $"        <a class=\"page-pagination-link {className}\" href=\"{HtmlEncoder.Default.Encode(item.Url)}\" rel=\"{rel}\">" +
            $"<span class=\"page-pagination-label\">{label}</span>" +
            $"<span class=\"page-pagination-title\">{HtmlEncoder.Default.Encode(item.Title)}</span></a>";
    }

    private static string RenderBreadcrumbs(
        string renderedMarkdown,
        IReadOnlyList<BreadcrumbItem> breadcrumbs)
    {
        // The current page is left out because the page heading below the trail already names it.
        var ancestors = breadcrumbs.Where(item => item.Url is not null).ToList();
        if (ancestors.Count == 0) return string.Empty;
        var headingStart = renderedMarkdown.IndexOf("<h1", StringComparison.Ordinal);
        if (headingStart < 0) return string.Empty;

        const string separator =
            """<span class="breadcrumb-separator" aria-hidden="true">/</span>""";
        var breadcrumbItems = ancestors.Select(item =>
            $"<a href=\"{HtmlEncoder.Default.Encode(item.Url!)}\">{HtmlEncoder.Default.Encode(item.Title)}</a>");
        return $"<nav class=\"breadcrumbs\" aria-label=\"Breadcrumb\">{string.Join(separator, breadcrumbItems)}</nav>";
    }

    private static string GetPageHeadingTitle(string renderedMarkdown)
    {
        var heading = HeadingRegex.Match(renderedMarkdown);
        if (!heading.Success || heading.Groups["level"].Value != "1") return string.Empty;
        return GetHeadingText(heading.Groups["content"].Value);
    }

    private static string GetHeadingText(string headingContent)
    {
        var text = FootnoteReferenceRegex.Replace(headingContent, string.Empty);
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        return WebUtility.HtmlDecode(text).Trim();
    }

    private static string InsertHeadingPermalinks(string renderedMarkdown) =>
        HeadingRegex.Replace(renderedMarkdown, match =>
        {
            // The page URL already links to the page heading.
            if (match.Groups["level"].Value == "1") return match.Value;
            var idMatch = HeadingIdRegex.Match(match.Groups["attributes"].Value);
            if (!idMatch.Success) return match.Value;

            var encodedId = HtmlEncoder.Default.Encode(idMatch.Groups["id"].Value);
            var permalink = $"<a class=\"headerlink\" href=\"#{encodedId}\" title=\"Permanent link\" aria-label=\"Permanent link\"><svg viewBox=\"0 0 20 20\" aria-hidden=\"true\" focusable=\"false\"><path d=\"M8.5 6H6a4 4 0 0 0 0 8h2.5M11.5 6H14a4 4 0 0 1 0 8h-2.5M7 10h6\" transform=\"rotate(-45 10 10)\"></path></svg></a>";
            var contentEnd = match.Groups["content"].Index + match.Groups["content"].Length - match.Index;
            return match.Value.Insert(contentEnd, permalink);
        });

    private static string CollapseOpenApiOperations(string renderedMarkdown)
    {
        var markerHeadings = HeadingRegex.Matches(renderedMarkdown)
            .Cast<Match>()
            .Where(match => HasClass(match, "openapi-operation") ||
                            HasClass(match, "openapi-tag"))
            .ToArray();
        if (!markerHeadings.Any(match => HasClass(match, "openapi-operation"))) return renderedMarkdown;

        var output = new StringBuilder(renderedMarkdown.Length);
        var copiedUntil = 0;
        for (var index = 0; index < markerHeadings.Length; index++)
        {
            var heading = markerHeadings[index];
            if (!HasClass(heading, "openapi-operation")) continue;

            var attributes = heading.Groups["attributes"].Value;
            var id = HeadingIdRegex.Match(attributes);
            var classAttribute = ClassAttributeRegex.Match(attributes);
            var methodClass = classAttribute.Success
                ? classAttribute.Groups["class"].Value.Split(' ').FirstOrDefault(value => value.StartsWith("openapi-method-", StringComparison.Ordinal))
                : null;
            if (!id.Success || methodClass is null) continue;

            var contentEnd = index + 1 < markerHeadings.Length
                ? markerHeadings[index + 1].Index
                : renderedMarkdown.Length;
            var contentStart = heading.Index + heading.Length;
            var operationScopeId = HtmlEncoder.Default.Encode(id.Groups["id"].Value);
            output.Append(renderedMarkdown, copiedUntil, heading.Index - copiedUntil);
            output.Append("<details class=\"openapi-endpoint ")
                .Append(HtmlEncoder.Default.Encode(methodClass))
                .Append(HasClass(heading, "openapi-deprecated") ? " openapi-endpoint-deprecated" : string.Empty)
                .AppendLine("\">");
            output.Append("<summary id=\"")
                .Append(HtmlEncoder.Default.Encode(id.Groups["id"].Value))
                .AppendLine("\" class=\"openapi-operation\">");
            output.Append(OpenApiPermalinkRegex.Replace(heading.Groups["content"].Value, string.Empty));
            // The summary moves into the collapsed row so endpoints can be scanned without opening them.
            var operationMarkup = renderedMarkdown[contentStart..contentEnd];
            var summary = OpenApiOperationSummaryRegex.Match(operationMarkup);
            if (summary.Success)
            {
                output.Append("<span class=\"openapi-operation-summary\">").Append(summary.Groups["summary"].Value).Append("</span>");
                operationMarkup = operationMarkup[(summary.Index + summary.Length)..];
            }
            output.AppendLine("</summary>");
            output.AppendLine("<div class=\"openapi-operation-content\">");
            // Tables are wrapped first so tab contents are complete before ShareOpenApiTabPanels compares them.
            var operationContent = WrapOpenApiRequestBodies(WrapOpenApiDescriptionTables(operationMarkup), operationScopeId);
            output.Append(WrapOpenApiResponses(operationContent, operationScopeId));
            output.AppendLine("</div>");
            output.AppendLine("</details>");
            copiedUntil = contentEnd;
        }

        output.Append(renderedMarkdown, copiedUntil, renderedMarkdown.Length - copiedUntil);
        return output.ToString();
    }

    private static string WrapOpenApiDescriptionTables(string content) =>
        Regex.Replace(content, @"<table>.*?</table>", match =>
        {
            if (!match.Value.Contains("class=\"openapi-declaration\"", StringComparison.Ordinal)) return match.Value;
            var hasDescriptions = Regex.Matches(match.Value, @"<tr>\s*<td>.*?</td>\s*<td>(?<description>.*?)</td>\s*</tr>", RegexOptions.Singleline)
                .Cast<Match>().Any(row => !string.IsNullOrWhiteSpace(WebUtility.HtmlDecode(
                    Regex.Replace(row.Groups["description"].Value, "<[^>]+>", string.Empty))));
            if (!hasDescriptions) return $"<div class=\"openapi-schema-table openapi-schema-table-plain\">{match.Value}</div>";
            // The toggle sits in the description header cell, which stays visible while the column is collapsed.
            var headers = OpenApiTableHeaderRegex.Matches(match.Value);
            var header = headers[^1];
            var table = match.Value.Remove(header.Index, header.Length).Insert(header.Index,
                $"<th class=\"openapi-description-header\"><span class=\"openapi-schema-table-header\"><span class=\"openapi-description-label\">{header.Groups["label"].Value}</span>" +
                "<button type=\"button\" class=\"openapi-description-toggle\" aria-expanded=\"false\">Show descriptions</button></span></th>");
            return $"<div class=\"openapi-schema-table\">{table}</div>";
        }, RegexOptions.Singleline);

    private static string WrapOpenApiResponses(string content, string operationScopeId)
    {
        var responseHeadings = HeadingRegex.Matches(content)
            .Cast<Match>()
            .Where(match => match.Groups["level"].Value == "5" && HasClass(match, "openapi-response"))
            .Select(match => new
            {
                Heading = match,
                Code = OpenApiResponseCodeRegex.Match(match.Groups["content"].Value)
            })
            .Where(response => response.Code.Success)
            .Select(response => new
            {
                response.Heading,
                Status = WebUtility.HtmlDecode(response.Code.Groups["status"].Value),
                Label = OpenApiResponseLabel(WebUtility.HtmlDecode(response.Code.Groups["status"].Value +
                    OpenApiPermalinkRegex.Replace(response.Code.Groups["suffix"].Value, string.Empty)).Trim())
            })
            .ToArray();
        if (responseHeadings.Length == 0) return content;

        var responses = new List<OpenApiResponseContent>();
        for (var index = 0; index < responseHeadings.Length; index++)
        {
            var response = responseHeadings[index];
            var end = index + 1 < responseHeadings.Length
                ? responseHeadings[index + 1].Heading.Index
                : content.Length;
            var bodyStart = response.Heading.Index + response.Heading.Length;
            var body = content[bodyStart..end];
            var mediaTypes = HeadingRegex.Matches(body)
                .Cast<Match>()
                .Where(match => match.Groups["level"].Value == "6" && HasClass(match, "openapi-media-type"))
                .Select(match => new
                {
                    Heading = match,
                    Code = OpenApiMediaTypeCodeRegex.Match(match.Groups["content"].Value)
                })
                .Where(media => media.Code.Success)
                .Select(media => new OpenApiMediaTypeHeading(
                    media.Heading,
                    WebUtility.HtmlDecode(media.Code.Groups["mediaType"].Value)))
                .ToArray();
            responses.Add(new OpenApiResponseContent(response.Status, response.Label, body, mediaTypes));
        }

        var output = new StringBuilder(content.Length + responses.Count * 200);
        output.Append(content, 0, responseHeadings[0].Heading.Index);
        output.AppendLine("<div class=\"openapi-responses\">");
        output.AppendLine("<div class=\"openapi-response-heading\">Response <span class=\"openapi-response-position\">1 of " + responses.Count + "</span></div>");
        output.AppendLine("<div class=\"openapi-response-control\">");
        output.Append("<select class=\"openapi-response-select ")
            .Append(GetOpenApiResponseStatusClass(responses[0].Status))
            .AppendLine("\" aria-label=\"Response status\">");
        for (var index = 0; index < responses.Count; index++)
        {
            var response = responses[index];
            output.Append("<option value=\"")
                .Append(HtmlEncoder.Default.Encode(response.Status))
                .Append("\" data-response-position=\"")
                .Append(index + 1)
                .Append("\" class=\"")
                .Append(GetOpenApiResponseStatusClass(response.Status))
                .Append('"');
            if (index == 0) output.Append(" selected");
            output.Append('>').Append(HtmlEncoder.Default.Encode(response.Label)).AppendLine("</option>");
        }
        output.AppendLine("</select>");
        output.AppendLine("</div>");

        for (var index = 0; index < responses.Count; index++)
        {
            var response = responses[index];
            output.Append("<section class=\"openapi-response-panel ")
                .Append(GetOpenApiResponseStatusClass(response.Status))
                .Append("\" data-response-status=\"")
                .Append(HtmlEncoder.Default.Encode(response.Status))
                .Append('"');
            if (index > 0) output.Append(" hidden");
            output.AppendLine(">");
            var descriptionEnd = response.MediaTypes.Count > 0 ? response.MediaTypes[0].Heading.Index : response.Body.Length;
            output.AppendLine("<div class=\"openapi-response-description\">");
            output.Append(response.Body, 0, descriptionEnd);
            output.AppendLine("</div>");
            AppendMediaTypeControl(output, response.MediaTypes);

            for (var mediaIndex = 0; mediaIndex < response.MediaTypes.Count; mediaIndex++)
            {
                var media = response.MediaTypes[mediaIndex];
                var mediaEnd = mediaIndex + 1 < response.MediaTypes.Count
                    ? response.MediaTypes[mediaIndex + 1].Heading.Index
                    : response.Body.Length;
                var mediaContentStart = media.Heading.Index + media.Heading.Length;
                output.Append("<section class=\"openapi-media-panel\" data-media-index=\"")
                    .Append(mediaIndex)
                    .Append("\" data-media-type=\"")
                    .Append(HtmlEncoder.Default.Encode(media.Name))
                    .Append('"');
                if (mediaIndex > 0) output.Append(" hidden");
                output.AppendLine(">");
                output.Append(WrapOpenApiResponseTabs(
                    response.Body[mediaContentStart..mediaEnd], $"openapi-{operationScopeId}-response-{index}-media-{mediaIndex}", "Response details"));
                output.AppendLine("</section>");
            }

            output.AppendLine("</section>");
        }

        output.AppendLine("</div>");
        return output.ToString();
    }

    // "default - Default" says the same twice, so a name that only repeats the status code stands alone.
    private static string OpenApiResponseLabel(string label)
    {
        var separator = label.IndexOf(" - ", StringComparison.Ordinal);
        return separator > 0 && label[..separator].Equals(label[(separator + 3)..], StringComparison.OrdinalIgnoreCase)
            ? label[(separator + 3)..]
            : label;
    }

    private static string WrapOpenApiRequestBodies(string content, string operationScopeId)
    {
        var requestHeadings = HeadingRegex.Matches(content)
            .Cast<Match>()
            .Where(match => match.Groups["level"].Value == "4" && HasClass(match, "openapi-request-body"))
            .ToArray();
        if (requestHeadings.Length == 0) return content;

        var responseHeadings = HeadingRegex.Matches(content)
            .Cast<Match>()
            .Where(match => match.Groups["level"].Value == "5" && HasClass(match, "openapi-response"))
            .ToArray();
        var output = new StringBuilder(content.Length + requestHeadings.Length * 200);
        var copiedUntil = 0;
        for (var index = 0; index < requestHeadings.Length; index++)
        {
            var heading = requestHeadings[index];
            var end = index + 1 < requestHeadings.Length ? requestHeadings[index + 1].Index : content.Length;
            var responseHeading = responseHeadings.FirstOrDefault(match => match.Index > heading.Index);
            if (responseHeading is not null) end = Math.Min(end, responseHeading.Index);
            var bodyStart = heading.Index + heading.Length;
            var body = content[bodyStart..end];
            var mediaTypes = HeadingRegex.Matches(body)
                .Cast<Match>()
                .Where(match => match.Groups["level"].Value == "6" && HasClass(match, "openapi-request-media-type"))
                .Select(match => new
                {
                    Heading = match,
                    Code = OpenApiMediaTypeCodeRegex.Match(match.Groups["content"].Value)
                })
                .Where(media => media.Code.Success)
                .Select(media => new OpenApiMediaTypeHeading(
                    media.Heading,
                    WebUtility.HtmlDecode(media.Code.Groups["mediaType"].Value)))
                .ToArray();

            output.Append(content, copiedUntil, heading.Index - copiedUntil);
            output.AppendLine("<section class=\"openapi-request-body\">");
            output.AppendLine(heading.Value);
            var descriptionEnd = mediaTypes.Length > 0 ? mediaTypes[0].Heading.Index : body.Length;
            output.AppendLine("<div class=\"openapi-request-description\">");
            output.Append(body, 0, descriptionEnd);
            output.AppendLine("</div>");
            AppendMediaTypeControl(output, mediaTypes);

            for (var mediaIndex = 0; mediaIndex < mediaTypes.Length; mediaIndex++)
            {
                var media = mediaTypes[mediaIndex];
                var mediaEnd = mediaIndex + 1 < mediaTypes.Length
                    ? mediaTypes[mediaIndex + 1].Heading.Index
                    : body.Length;
                var mediaContentStart = media.Heading.Index + media.Heading.Length;
                output.Append("<section class=\"openapi-media-panel\" data-media-index=\"")
                    .Append(mediaIndex)
                    .Append("\" data-media-type=\"")
                    .Append(HtmlEncoder.Default.Encode(media.Name))
                    .Append('"');
                if (mediaIndex > 0) output.Append(" hidden");
                output.AppendLine(">");
                output.Append(WrapOpenApiResponseTabs(
                    body[mediaContentStart..mediaEnd], $"openapi-{operationScopeId}-request-{index}-media-{mediaIndex}", "Request body details"));
                output.AppendLine("</section>");
            }

            output.AppendLine("</section>");
            copiedUntil = end;
        }

        output.Append(content, copiedUntil, content.Length - copiedUntil);
        return output.ToString();
    }

    private static void AppendMediaTypeControl(StringBuilder output, IReadOnlyList<OpenApiMediaTypeHeading> mediaTypes)
    {
        if (mediaTypes.Count == 0) return;
        output.AppendLine("<div class=\"openapi-media-type-control\">");
        output.Append("<div class=\"openapi-media-type-label\"><span class=\"openapi-media-type-heading\">Media Type <span class=\"openapi-media-type-position\">1 of ")
            .Append(mediaTypes.Count)
            .AppendLine("</span></span>");
        output.AppendLine("<select class=\"openapi-media-type-select\" aria-label=\"Media type\">");
        for (var index = 0; index < mediaTypes.Count; index++)
        {
            output.Append("<option value=\"").Append(index).Append('"');
            if (index == 0) output.Append(" selected");
            output.Append('>').Append(HtmlEncoder.Default.Encode(mediaTypes[index].Name)).AppendLine("</option>");
        }
        output.AppendLine("</select>");
        output.AppendLine("</div>");
        output.AppendLine("</div>");
    }

    private sealed record OpenApiResponseContent(
        string Status,
        string Label,
        string Body,
        IReadOnlyList<OpenApiMediaTypeHeading> MediaTypes);

    private sealed record OpenApiMediaTypeHeading(Match Heading, string Name);

    private static string WrapOpenApiResponseTabs(string content, string groupId, string tabListLabel)
    {
        var tabs = HeadingRegex.Matches(content)
            .Cast<Match>()
            .Where(match => match.Groups["level"].Value == "5")
            .Select(match => new
            {
                Heading = match,
                Name = HasClass(match, "openapi-response-example") ? "Example" :
                    HasClass(match, "openapi-response-schema-table") ? "Schema" :
                    HasClass(match, "openapi-response-schema-json") ? "Schema JSON" : null,
                Key = HasClass(match, "openapi-response-example") ? "example" :
                    HasClass(match, "openapi-response-schema-table") ? "schema" :
                    HasClass(match, "openapi-response-schema-json") ? "schema-json" : null
            })
            .Where(tab => tab.Name is not null && tab.Key is not null)
            .ToArray();
        if (tabs.Length == 0) return content;

        var output = new StringBuilder(content.Length + tabs.Length * 200);
        output.AppendLine("<div class=\"openapi-response-tabs\">");
        output.Append("<div class=\"openapi-response-tab-list\" role=\"tablist\" aria-label=\"")
            .Append(HtmlEncoder.Default.Encode(tabListLabel))
            .AppendLine("\">");
        for (var index = 0; index < tabs.Length; index++)
        {
            var tabId = $"{groupId}-{tabs[index].Key}-tab";
            var panelId = $"{groupId}-{tabs[index].Key}-panel";
            output.Append("<button type=\"button\" class=\"openapi-response-tab\" role=\"tab\" id=\"")
                .Append(tabId)
                .Append("\" aria-controls=\"")
                .Append(panelId)
                .Append("\" aria-selected=\"")
                .Append(index == 0 ? "true" : "false")
                .Append("\" tabindex=\"")
                .Append(index == 0 ? "0" : "-1")
                .Append("\">")
                .Append(tabs[index].Name)
                .AppendLine("</button>");
        }
        output.AppendLine("</div>");

        for (var index = 0; index < tabs.Length; index++)
        {
            var tab = tabs[index];
            var contentEnd = index + 1 < tabs.Length ? tabs[index + 1].Heading.Index : content.Length;
            var tabContentStart = tab.Heading.Index + tab.Heading.Length;
            var tabId = $"{groupId}-{tab.Key}-tab";
            var panelId = $"{groupId}-{tab.Key}-panel";
            output.Append("<div class=\"openapi-response-tabpanel\" role=\"tabpanel\" id=\"")
                .Append(panelId)
                .Append("\" aria-labelledby=\"")
                .Append(tabId)
                .Append("\" tabindex=\"0\"");
            if (index > 0) output.Append(" hidden");
            output.AppendLine(">");
            output.Append(OpenApiSharedStart)
                .Append(content, tabContentStart, contentEnd - tabContentStart)
                .Append(OpenApiSharedEnd);
            output.AppendLine("</div>");
        }
        output.AppendLine("</div>");
        return output.ToString();
    }

    // Large specifications repeat the same examples and schemas across many responses. Tab contents that occur
    // more than once are emitted once as a template, and the page script copies a template into its tab when the
    // tab becomes visible. Each media type scopes its schema anchors with its own prefix, so a template stores the
    // prefix as a token and every copy gets its original anchors back.
    private static string ShareOpenApiTabPanels(string renderedMarkdown)
    {
        var panels = OpenApiSharedPanelRegex.Matches(renderedMarkdown);
        if (panels.Count == 0) return renderedMarkdown;

        var contents = panels.Select(panel => SharedOpenApiPanelContent.From(panel.Groups["content"].Value)).ToArray();
        var counts = contents.Where(content => content.Shareable)
            .GroupBy(content => content.Normalized, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var templateIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var templates = new StringBuilder();
        var output = new StringBuilder(renderedMarkdown.Length);
        var copiedUntil = 0;
        for (var index = 0; index < panels.Count; index++)
        {
            var panel = panels[index];
            var content = contents[index];
            output.Append(renderedMarkdown, copiedUntil, panel.Index - copiedUntil);
            copiedUntil = panel.Index + panel.Length;
            if (!content.Shareable || counts[content.Normalized] < 2)
            {
                output.Append(content.Original);
                continue;
            }

            if (!templateIds.TryGetValue(content.Normalized, out var id))
            {
                id = $"openapi-shared-{templateIds.Count + 1}";
                templateIds.Add(content.Normalized, id);
                templates.Append("<template data-openapi-shared=\"").Append(id).Append("\">")
                    .Append(content.Normalized)
                    .AppendLine("</template>");
            }

            output.Append("<div class=\"openapi-shared-content\" data-openapi-shared=\"")
                .Append(id)
                .Append("\" data-openapi-scope=\"")
                .Append(content.Scope)
                .Append("\"></div>");
        }

        output.Append(renderedMarkdown, copiedUntil, renderedMarkdown.Length - copiedUntil);
        return output.Append(templates).ToString();
    }

    private sealed record SharedOpenApiPanelContent(string Original, string Normalized, string Scope, bool Shareable)
    {
        public static SharedOpenApiPanelContent From(string content)
        {
            var scopes = OpenApiSchemaAnchorPrefixRegex.Matches(content)
                .Select(match => match.Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return scopes.Length switch
            {
                0 => new SharedOpenApiPanelContent(content, content, string.Empty, true),
                1 => new SharedOpenApiPanelContent(content, content.Replace(scopes[0], OpenApiSharedScopeToken, StringComparison.Ordinal), scopes[0], true),
                _ => new SharedOpenApiPanelContent(content, content, string.Empty, false)
            };
        }
    }

    private static string CollapseOpenApiTagGroups(string renderedMarkdown)
    {
        var groupHeadings = HeadingRegex.Matches(renderedMarkdown)
            .Cast<Match>()
            .Where(match => HasClass(match, "openapi-tag"))
            .ToArray();
        if (!groupHeadings.Any(match => HasClass(match, "openapi-tag"))) return renderedMarkdown;

        var deprecatedEndpoints = OpenApiTagEndpointRegex.Matches(renderedMarkdown).Count(match => match.Groups["deprecated"].Success);
        var output = new StringBuilder(renderedMarkdown.Length);
        var copiedUntil = 0;
        for (var index = 0; index < groupHeadings.Length; index++)
        {
            var heading = groupHeadings[index];
            if (!HasClass(heading, "openapi-tag")) continue;

            var id = HeadingIdRegex.Match(heading.Groups["attributes"].Value);
            if (!id.Success) continue;

            var groupEnd = index + 1 < groupHeadings.Length
                ? groupHeadings[index + 1].Index
                : renderedMarkdown.Length;
            var contentStart = heading.Index + heading.Length;
            var groupContent = renderedMarkdown[contentStart..groupEnd];
            var firstEndpoint = groupContent.IndexOf("<details class=\"openapi-endpoint ", StringComparison.Ordinal);
            var descriptionMarkup = firstEndpoint < 0 ? groupContent : groupContent[..firstEndpoint];
            var endpointContent = firstEndpoint < 0 ? string.Empty : groupContent[firstEndpoint..];
            var description = WebUtility.HtmlDecode(Regex.Replace(descriptionMarkup, "<[^>]+>", " "));
            description = Regex.Replace(description, @"\s+", " ").Trim();
            output.Append(renderedMarkdown, copiedUntil, heading.Index - copiedUntil);
            if (index == 0)
            {
                output.AppendLine("<section class=\"openapi-tag-list\" aria-label=\"API endpoints\">");
                output.AppendLine("<div class=\"openapi-filter-bar\">");
                output.AppendLine("<label class=\"openapi-path-filter-label\"><span class=\"openapi-path-filter-text\">Filter endpoints</span><svg class=\"openapi-path-filter-icon\" viewBox=\"0 0 20 20\" aria-hidden=\"true\" focusable=\"false\"><circle cx=\"8.5\" cy=\"8.5\" r=\"5.5\"></circle><path d=\"m12.6 12.6 4.4 4.4\"></path></svg><input type=\"search\" class=\"openapi-path-filter\" placeholder=\"Search endpoint paths…\" autocomplete=\"off\" spellcheck=\"false\"></label>");
                if (deprecatedEndpoints > 0)
                    output.Append("<label class=\"openapi-deprecated-filter\"><input type=\"checkbox\" class=\"openapi-show-deprecated\" checked>")
                        .Append("<span class=\"openapi-switch\" aria-hidden=\"true\"></span>Show deprecated <span class=\"openapi-deprecated-filter-count\">")
                        .Append(deprecatedEndpoints)
                        .AppendLine("</span></label>");
                output.AppendLine("</div>");
                output.AppendLine("<p class=\"openapi-filter-status\" role=\"status\" hidden></p>");
                output.AppendLine("<div class=\"openapi-tag-rows\">");
            }
            output.AppendLine("<details class=\"openapi-tag-group\">");
            output.Append("<summary id=\"")
                .Append(HtmlEncoder.Default.Encode(id.Groups["id"].Value))
                .AppendLine("\" class=\"openapi-tag\">");
            output.Append("<span class=\"openapi-tag-summary-main\"><span class=\"openapi-tag-title\">")
                .Append(OpenApiPermalinkRegex.Replace(heading.Groups["content"].Value, string.Empty))
                .Append("</span>");
            AppendOpenApiTagMethodCounts(output, groupContent);
            output.AppendLine("</span>");
            if (description.Length > 0)
                output.Append("<span class=\"openapi-tag-description\">")
                    .Append(HtmlEncoder.Default.Encode(description))
                    .AppendLine("</span>");
            output.AppendLine("</summary>");
            output.AppendLine("<div class=\"openapi-tag-body\">");
            output.Append(endpointContent);
            output.AppendLine("</div>");
            output.AppendLine("</details>");
            copiedUntil = groupEnd;
        }

        output.AppendLine("</div></section>");
        output.Append(renderedMarkdown, copiedUntil, renderedMarkdown.Length - copiedUntil);
        return output.ToString();
    }

    private static void AppendOpenApiTagMethodCounts(StringBuilder output, string groupContent)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var deprecatedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match match in OpenApiTagEndpointRegex.Matches(groupContent))
        {
            var method = match.Groups["method"].Value;
            counts[method] = counts.GetValueOrDefault(method) + 1;
            if (match.Groups["deprecated"].Success) deprecatedCounts[method] = deprecatedCounts.GetValueOrDefault(method) + 1;
        }

        if (counts.Count == 0) return;
        output.Append("<span class=\"openapi-tag-method-counts\">");
        foreach (var (method, count) in counts
                     .OrderBy(pair => TagMethodOrder(pair.Key["openapi-method-".Length..]))
                     .ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var methodName = method["openapi-method-".Length..].ToUpperInvariant();
            var deprecated = deprecatedCounts.GetValueOrDefault(method);
            var label = $"{count} {methodName} {(count == 1 ? "endpoint" : "endpoints")}" +
                        (deprecated > 0 ? $", {deprecated} deprecated" : string.Empty);
            output.Append("<span class=\"openapi-tag-method-count ")
                .Append(method)
                .Append("\" data-method=\"")
                .Append(method)
                .Append("\" data-count=\"")
                .Append(count)
                .Append("\" data-deprecated=\"")
                .Append(deprecated)
                .Append("\" data-tooltip=\"")
                .Append(label)
                .Append("\" aria-label=\"")
                .Append(label)
                .Append("\">")
                .Append(count)
                .AppendLine("</span>");
        }
        output.AppendLine("</span>");
    }

    private static int TagMethodOrder(string method)
    {
        var index = Array.IndexOf(OpenApiTagMethodOrder, method.ToUpperInvariant());
        return index < 0 ? OpenApiTagMethodOrder.Length : index;
    }

    private static string GetOpenApiResponseStatusClass(string status)
    {
        var normalizedStatus = status.ToUpperInvariant();
        if (normalizedStatus == "2XX" || IsNumericStatusInFamily(normalizedStatus, '2')) return "openapi-status-2xx";
        if (normalizedStatus == "4XX" || IsNumericStatusInFamily(normalizedStatus, '4')) return "openapi-status-4xx";
        if (normalizedStatus == "5XX" || IsNumericStatusInFamily(normalizedStatus, '5')) return "openapi-status-5xx";
        return "openapi-status-neutral";
    }

    private static bool IsNumericStatusInFamily(string status, char family) =>
        status.Length == 3 && status[0] == family && char.IsAsciiDigit(status[1]) && char.IsAsciiDigit(status[2]);

    private const string OpenApiResponseScript = """
        // Repeated tab contents arrive as templates; copy one in once its tab is visible in an open endpoint.
        const fillSharedContent = endpoint => {
            if (!endpoint?.open) return;
            endpoint.querySelectorAll(".openapi-shared-content").forEach(placeholder => {
                for (let element = placeholder; element && element !== endpoint; element = element.parentElement)
                    if (element.hidden) return;
                const template = document.querySelector(`template[data-openapi-shared="${placeholder.dataset.openapiShared}"]`);
                if (!template) return;
                const container = document.createElement("div");
                container.innerHTML = template.innerHTML.replaceAll("openapi-shared-scope", placeholder.dataset.openapiScope);
                placeholder.replaceWith(...container.childNodes);
            });
        };
        document.addEventListener("toggle", event => {
            if (!(event.target instanceof HTMLDetailsElement) || !event.target.matches(".openapi-endpoint")) return;
            fillSharedContent(event.target);
            if (event.target.open) enhanceSelects(event.target);
        }, true);

        const openApiFilterStates = new WeakMap();
        const applyEndpointFilter = list => {
            const input = list.querySelector(".openapi-path-filter");
            const query = input.value.trim().toLowerCase();
            const showDeprecated = list.querySelector(".openapi-show-deprecated")?.checked ?? true;
            const filtered = query.length > 0 || !showDeprecated;
            const groups = [...list.querySelectorAll(".openapi-tag-group")];
            if (query && !openApiFilterStates.has(input))
                openApiFilterStates.set(input, new Map(groups.map(group => [group, group.open])));
            let matches = 0;
            let hiddenDeprecated = 0;
            groups.forEach(group => {
                let visible = 0;
                const visibleByMethod = new Map();
                const deprecatedByMethod = new Map();
                group.querySelectorAll(".openapi-endpoint").forEach(endpoint => {
                    const path = endpoint.querySelector(".openapi-path").textContent.toLowerCase();
                    const deprecated = endpoint.classList.contains("openapi-endpoint-deprecated");
                    const matchesQuery = path.includes(query);
                    endpoint.hidden = !matchesQuery || (deprecated && !showDeprecated);
                    if (matchesQuery && endpoint.hidden) hiddenDeprecated++;
                    if (endpoint.hidden) return;
                    visible++;
                    const method = [...endpoint.classList].find(name => name.startsWith("openapi-method-"));
                    visibleByMethod.set(method, (visibleByMethod.get(method) ?? 0) + 1);
                    if (deprecated) deprecatedByMethod.set(method, (deprecatedByMethod.get(method) ?? 0) + 1);
                });
                group.querySelectorAll(".openapi-tag-method-count").forEach(counter => {
                    const count = filtered ? visibleByMethod.get(counter.dataset.method) ?? 0 : Number(counter.dataset.count);
                    const deprecated = filtered ? deprecatedByMethod.get(counter.dataset.method) ?? 0 : Number(counter.dataset.deprecated);
                    const method = counter.dataset.method.slice("openapi-method-".length).toUpperCase();
                    const label = `${count} ${method} ${count === 1 ? "endpoint" : "endpoints"}${deprecated > 0 ? `, ${deprecated} deprecated` : ""}`;
                    counter.hidden = count === 0;
                    counter.textContent = count;
                    counter.dataset.tooltip = label;
                    counter.setAttribute("aria-label", label);
                });
                group.hidden = visible === 0;
                if (query) group.open = visible > 0;
                else if (openApiFilterStates.has(input)) group.open = openApiFilterStates.get(input).get(group);
                matches += visible;
            });
            if (!query) openApiFilterStates.delete(input);
            const status = list.querySelector(".openapi-filter-status");
            status.hidden = !filtered;
            const found = matches === 0
                ? "No matching endpoints"
                : `${matches} ${query ? "matching " : ""}${matches === 1 ? "endpoint" : "endpoints"}`;
            status.textContent = `${found}${hiddenDeprecated > 0 ? `, ${hiddenDeprecated} deprecated hidden` : ""}.`;
        };
        document.addEventListener("input", event => {
            if (event.target.matches(".openapi-path-filter")) applyEndpointFilter(event.target.closest(".openapi-tag-list"));
        });

        // Response and media type choices use a listbox instead of the native select, which stays hidden as
        // the source of truth so the change handler below keeps working and the page still works without script.
        const statusClasses = ["openapi-status-2xx", "openapi-status-4xx", "openapi-status-5xx", "openapi-status-neutral"];
        let openApiSelectCount = 0;
        const selectOptionMarkup = option => {
            const text = option.textContent;
            const separator = text.indexOf(" - ");
            const label = separator < 0 || !option.closest(".openapi-response-select")
                ? `<span class="openapi-select-text"></span>`
                : `<span class="openapi-select-code"></span> <span class="openapi-select-text"></span>`;
            const container = document.createElement("span");
            container.innerHTML = label;
            const parts = container.children.length === 2 ? [text.slice(0, separator), text.slice(separator + 3)] : [text];
            [...container.children].forEach((child, index) => child.textContent = parts[index]);
            return container.innerHTML;
        };
        const syncSelect = select => {
            const control = select.nextElementSibling;
            if (!control?.matches(".openapi-select")) return;
            control.querySelector(".openapi-select-current").innerHTML = selectOptionMarkup(select.selectedOptions[0]);
            control.classList.remove(...statusClasses);
            const statusClass = statusClasses.find(name => select.classList.contains(name));
            if (statusClass) control.classList.add(statusClass);
            control.querySelectorAll("[role=option]").forEach((option, index) =>
                option.setAttribute("aria-selected", index === select.selectedIndex ? "true" : "false"));
        };
        const enhanceSelects = root => {
            root.querySelectorAll("select.openapi-response-select, select.openapi-media-type-select").forEach(select => {
                if (select.dataset.enhanced) return;
                select.dataset.enhanced = "true";
                const id = `openapi-select-${++openApiSelectCount}`;
                const label = select.getAttribute("aria-label") ?? "";
                const control = document.createElement("div");
                control.className = "openapi-select";
                control.innerHTML = `<button type="button" class="openapi-select-button" aria-haspopup="listbox" aria-expanded="false" aria-controls="${id}-list" aria-labelledby="${id}-label ${id}-current"><span class="openapi-select-label" id="${id}-label"></span><span class="openapi-select-value"><span class="openapi-select-current" id="${id}-current"></span><span class="openapi-select-sizer" aria-hidden="true"></span></span><svg class="openapi-select-chevron" viewBox="0 0 20 20" aria-hidden="true" focusable="false"><path d="m6 8 4 4 4-4"></path></svg></button><ul class="openapi-select-list" id="${id}-list" role="listbox" tabindex="-1" hidden></ul>`;
                control.querySelector(".openapi-select-label").textContent = `${label}: `;
                const list = control.querySelector(".openapi-select-list");
                list.setAttribute("aria-label", label);
                // Every label sits invisibly in the button, so it is as wide as its longest option and the list.
                const sizer = control.querySelector(".openapi-select-sizer");
                [...select.options].forEach(option => {
                    const label = document.createElement("span");
                    label.innerHTML = selectOptionMarkup(option);
                    sizer.append(label);
                });
                [...select.options].forEach((option, index) => {
                    const item = document.createElement("li");
                    item.id = `${id}-option-${index}`;
                    item.className = `openapi-select-option ${option.className}`.trim();
                    item.setAttribute("role", "option");
                    item.dataset.index = index;
                    item.innerHTML = `${selectOptionMarkup(option)}<svg class="openapi-select-check" viewBox="0 0 20 20" aria-hidden="true" focusable="false"><path d="m5 10.5 3.2 3.2L15 7"></path></svg>`;
                    list.append(item);
                });
                select.after(control);
                select.hidden = true;
                select.tabIndex = -1;
                syncSelect(select);
            });
        };
        const setActiveOption = (control, index) => {
            const options = [...control.querySelectorAll("[role=option]")];
            const active = options[Math.max(0, Math.min(index, options.length - 1))];
            options.forEach(option => option.classList.toggle("is-active", option === active));
            control.querySelector("[role=listbox]").setAttribute("aria-activedescendant", active.id);
            active.scrollIntoView({ block: "nearest" });
        };
        const activeOptionIndex = control =>
            Number(control.querySelector("[role=option].is-active")?.dataset.index ?? 0);
        const closeSelect = (control, focusButton) => {
            const list = control.querySelector("[role=listbox]");
            if (list.hidden) return;
            list.hidden = true;
            control.classList.remove("is-open", "opens-up");
            control.querySelector(".openapi-select-button").setAttribute("aria-expanded", "false");
            if (focusButton) control.querySelector(".openapi-select-button").focus();
        };
        const openSelect = control => {
            document.querySelectorAll(".openapi-select.is-open").forEach(other => closeSelect(other, false));
            const select = control.previousElementSibling;
            const list = control.querySelector("[role=listbox]");
            list.hidden = false;
            control.classList.add("is-open");
            control.querySelector(".openapi-select-button").setAttribute("aria-expanded", "true");
            const bounds = control.getBoundingClientRect();
            const spaceBelow = document.documentElement.clientHeight - bounds.bottom;
            control.classList.toggle("opens-up", spaceBelow < list.offsetHeight + 12 && bounds.top > spaceBelow);
            list.focus({ preventScroll: true });
            setActiveOption(control, select.selectedIndex);
        };
        const chooseOption = (control, index) => {
            const select = control.previousElementSibling;
            if (select.selectedIndex !== index) {
                select.selectedIndex = index;
                select.dispatchEvent(new Event("change", { bubbles: true }));
            }
            syncSelect(select);
            closeSelect(control, true);
        };
        let typeahead = "";
        let typeaheadTimer;
        document.addEventListener("click", event => {
            const button = event.target.closest(".openapi-select-button");
            const option = event.target.closest(".openapi-select-option");
            document.querySelectorAll(".openapi-select.is-open").forEach(control => {
                if (!control.contains(event.target)) closeSelect(control, false);
            });
            if (option) chooseOption(option.closest(".openapi-select"), Number(option.dataset.index));
            else if (button) {
                const control = button.closest(".openapi-select");
                if (control.classList.contains("is-open")) closeSelect(control, true);
                else openSelect(control);
            }
        });
        document.addEventListener("pointermove", event => {
            const option = event.target.closest?.(".openapi-select-option");
            if (option && !option.classList.contains("is-active")) setActiveOption(option.closest(".openapi-select"), Number(option.dataset.index));
        });
        document.addEventListener("focusout", event => {
            const control = event.target.closest?.(".openapi-select");
            if (control && !control.contains(event.relatedTarget)) closeSelect(control, false);
        });
        document.addEventListener("keydown", event => {
            const control = event.target.closest?.(".openapi-select");
            if (!control) return;
            const open = control.classList.contains("is-open");
            if (!open) {
                if (event.target.matches(".openapi-select-button") && ["ArrowDown", "ArrowUp", "Enter", " "].includes(event.key)) {
                    event.preventDefault();
                    openSelect(control);
                }
                return;
            }
            const count = control.querySelectorAll("[role=option]").length;
            const index = activeOptionIndex(control);
            if (event.key === "ArrowDown") setActiveOption(control, index + 1);
            else if (event.key === "ArrowUp") setActiveOption(control, index - 1);
            else if (event.key === "Home" || event.key === "PageUp") setActiveOption(control, 0);
            else if (event.key === "End" || event.key === "PageDown") setActiveOption(control, count - 1);
            else if (event.key === "Enter" || event.key === " ") chooseOption(control, index);
            else if (event.key === "Escape") closeSelect(control, true);
            else if (event.key === "Tab") { closeSelect(control, false); return; }
            else if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
                window.clearTimeout(typeaheadTimer);
                typeahead += event.key.toLowerCase();
                typeaheadTimer = window.setTimeout(() => typeahead = "", 600);
                const match = [...control.querySelectorAll("[role=option]")]
                    .find(option => option.textContent.trim().toLowerCase().startsWith(typeahead));
                if (match) setActiveOption(control, Number(match.dataset.index));
            }
            else return;
            event.preventDefault();
            event.stopPropagation();
        });

        document.addEventListener("change", event => {
            if (event.target.matches?.(".openapi-show-deprecated")) {
                applyEndpointFilter(event.target.closest(".openapi-tag-list"));
                return;
            }
            const select = event.target;
            if (!(select instanceof HTMLSelectElement)) return;
            if (select.matches(".openapi-response-select")) {
                const responses = select.closest(".openapi-responses");
                const activeResponse = [...responses.querySelectorAll(".openapi-response-panel")]
                    .find(panel => panel.dataset.responseStatus === select.value);
                responses.querySelectorAll(".openapi-response-panel").forEach(panel => {
                    panel.hidden = panel !== activeResponse;
                });
                const position = select.selectedOptions[0].dataset.responsePosition;
                responses.querySelector(".openapi-response-position").textContent = `${position} of ${select.options.length}`;
                select.classList.remove("openapi-status-2xx", "openapi-status-4xx", "openapi-status-5xx", "openapi-status-neutral");
                const status = select.value.toUpperCase();
                const statusClass = /^2(?:\d{2}|XX)$/.test(status) ? "openapi-status-2xx"
                    : /^4(?:\d{2}|XX)$/.test(status) ? "openapi-status-4xx"
                    : /^5(?:\d{2}|XX)$/.test(status) ? "openapi-status-5xx"
                    : "openapi-status-neutral";
                select.classList.add(statusClass);
                fillSharedContent(select.closest(".openapi-endpoint"));
                return;
            }
            if (select.matches(".openapi-media-type-select")) {
                const content = select.closest(".openapi-response-panel, .openapi-request-body");
                content.querySelectorAll(".openapi-media-panel").forEach(panel => {
                    panel.hidden = panel.dataset.mediaIndex !== select.value;
                });
                select.closest(".openapi-media-type-control").querySelector(".openapi-media-type-position").textContent = `${select.selectedIndex + 1} of ${select.options.length}`;
                fillSharedContent(select.closest(".openapi-endpoint"));
            }
        });

        const activateResponseTab = tab => {
            const tabs = tab.closest(".openapi-response-tabs");
            tabs.querySelectorAll(".openapi-response-tab").forEach(button => {
                const selected = button === tab;
                button.setAttribute("aria-selected", selected ? "true" : "false");
                button.tabIndex = selected ? 0 : -1;
            });
            tabs.querySelectorAll(".openapi-response-tabpanel").forEach(panel => {
                panel.hidden = panel.id !== tab.getAttribute("aria-controls");
            });
            fillSharedContent(tab.closest(".openapi-endpoint"));
        };

        document.addEventListener("click", event => {
            const descriptionToggle = event.target.closest(".openapi-description-toggle");
            if (descriptionToggle) {
                const expanded = descriptionToggle.getAttribute("aria-expanded") !== "true";
                descriptionToggle.setAttribute("aria-expanded", String(expanded));
                descriptionToggle.textContent = expanded ? "Hide descriptions" : "Show descriptions";
                descriptionToggle.closest(".openapi-schema-table").classList.toggle("descriptions-visible", expanded);
            }
            const tab = event.target.closest(".openapi-response-tab");
            if (tab) activateResponseTab(tab);
            const reference = event.target.closest("a.openapi-type-reference");
            if (!reference) return;
            const target = document.getElementById(reference.hash.slice(1));
            if (!target) return;
            const color = getComputedStyle(target).getPropertyValue("--link").trim();
            const highlight = `color-mix(in srgb, ${color} 16%, transparent)`;
            const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)").matches;
            for (let element = target; element; element = element.nextElementSibling) {
                if (element !== target && (element.matches("h6") || element.id)) break;
                element.getAnimations().filter(animation => animation.id === "openapi-schema-highlight")
                    .forEach(animation => animation.cancel());
                element.animate([
                    { backgroundColor: highlight },
                    { backgroundColor: reducedMotion ? highlight : "transparent" }
                ], { id: "openapi-schema-highlight", duration: 1600, easing: "ease-out" });
            }
        });

        document.addEventListener("keydown", event => {
            const tab = event.target.closest(".openapi-response-tab");
            if (!tab || !["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
            const tabs = [...tab.closest(".openapi-response-tabs").querySelectorAll(".openapi-response-tab")];
            const index = tabs.indexOf(tab);
            const nextIndex = event.key === "Home" ? 0
                : event.key === "End" ? tabs.length - 1
                : (index + (event.key === "ArrowRight" ? 1 : -1) + tabs.length) % tabs.length;
            event.preventDefault();
            tabs[nextIndex].focus();
            activateResponseTab(tabs[nextIndex]);
        });
        """;

    private static bool HasClass(Match heading, string className)
    {
        var attribute = ClassAttributeRegex.Match(heading.Groups["attributes"].Value);
        return attribute.Success && attribute.Groups["class"].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(className, StringComparer.Ordinal);
    }

    private static string InsertEditPageLink(string renderedMarkdown, string editPageLink, out bool inserted)
    {
        inserted = false;
        if (string.IsNullOrWhiteSpace(editPageLink)) return renderedMarkdown;
        var editLinkInserted = false;
        var result = HeadingRegex.Replace(renderedMarkdown, match =>
        {
            if (editLinkInserted || match.Groups["level"].Value != "1") return match.Value;
            editLinkInserted = true;
            var editLink = $"<a class=\"page-edit-link\" href=\"{HtmlEncoder.Default.Encode(editPageLink)}\" target=\"_blank\" rel=\"noopener noreferrer\" aria-label=\"Edit this page\" title=\"Edit this page\"><svg viewBox=\"0 0 20 20\" aria-hidden=\"true\"><path d=\"m3 14.5-.8 3.3 3.3-.8L16.8 5.7a1.8 1.8 0 0 0-2.5-2.5L3 14.5Z\"></path><path d=\"m11.8 5.7 2.5 2.5\"></path></svg></a>";
            var contentEnd = match.Groups["content"].Index + match.Groups["content"].Length - match.Index;
            return match.Value.Insert(contentEnd, editLink);
        });
        inserted = editLinkInserted;
        return result;
    }
    
    private string GetEnsuredCssFolderPath(string basePath)
    {
        var cssFolder = Path.Combine(basePath, "css");
        Directory.CreateDirectory(cssFolder);
        return cssFolder;
    }

    private static void WriteCssFileTo(string cssFilePath)
    {
        var mainCssContent = Resources.CssContent();
        var syntaxCssContent = StaticCode.Highlighter.Res.Resources.CssContent();
        
        var cssContentBuilder = new StringBuilder();
        cssContentBuilder.AppendLine(mainCssContent);
        cssContentBuilder.AppendLine(syntaxCssContent);

        var cssContent = cssContentBuilder.ToString();
        File.WriteAllText(cssFilePath, cssContent, Encoding.UTF8);
    }
    


}
