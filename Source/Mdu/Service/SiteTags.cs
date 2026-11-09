using Markdown.Renderer;
using Mdu.Model;

namespace Mdu.Service;

/// <summary>A tag of the site with the pages that carry it, in navigation order.</summary>
public sealed record SiteTag(string Name, string Slug, TagDefinition? Definition, IReadOnlyList<MarkdownFile> Pages)
{
    /// <summary>The comparison key of the tag, see <see cref="TagCatalog.Normalize"/>.</summary>
    internal string Key { get; init; } = TagCatalog.Normalize(Name);
}

/// <summary>A tag that a page uses wrongly. Errors stop mdu render and mdu pdf; warnings only inform.</summary>
/// <param name="Line">The line of a <c>tags</c> code block the problem is in; null for the front matter.</param>
public sealed record TagProblem(MarkdownFile Page, string Message, bool IsWarning, string? Tag = null, int? Line = null);

/// <summary>
/// Collects the tags of all pages and checks them against the vocabulary of <c>content.tags</c>. With a vocabulary,
/// every tag must be one of its names or aliases; spellings that differ only in letter case are warnings.
/// </summary>
public sealed class SiteTags
{
    /// <summary>The folder of the generated tag pages in the output.</summary>
    public const string Folder = "tags";

    private readonly Dictionary<MarkdownFile, IReadOnlyList<SiteTag>> _pageTags;

    private SiteTags(IReadOnlyList<SiteTag> tags, Dictionary<MarkdownFile, IReadOnlyList<SiteTag>> pageTags,
        IReadOnlyList<TagProblem> problems, TagCatalog? catalog, string? vocabularyPath, MarkdownFile? vocabularyPage)
    {
        Tags = tags;
        _pageTags = pageTags;
        Problems = problems;
        Catalog = catalog;
        VocabularyPath = vocabularyPath;
        VocabularyPage = vocabularyPage;
    }

    public static SiteTags Empty { get; } = new([], new Dictionary<MarkdownFile, IReadOnlyList<SiteTag>>(), [], null, null, null);

    /// <summary>The tags that at least one page carries, sorted by name.</summary>
    public IReadOnlyList<SiteTag> Tags { get; }
    public IReadOnlyList<TagProblem> Problems { get; }
    public TagCatalog? Catalog { get; }
    public string? VocabularyPath { get; }
    /// <summary>The vocabulary file when it is a page of the site; it then also serves as the tag overview.</summary>
    public MarkdownFile? VocabularyPage { get; }
    public bool HasErrors => Problems.Any(problem => !problem.IsWarning);

    public IReadOnlyList<SiteTag> For(MarkdownFile page) => _pageTags.GetValueOrDefault(page) ?? [];

    /// <summary>The tag a name or alias stands for, or null when no page carries it.</summary>
    public SiteTag? Find(string name)
    {
        var key = TagCatalog.Normalize(Catalog?.Find(name)?.Name ?? name);
        return Tags.FirstOrDefault(tag => tag.Key == key);
    }

    /// <summary>
    /// The pages of a <c>tags</c> code block in navigation order: they carry every tag of <c>tags</c> and none of
    /// <c>exclude</c>. The page that shows the list is left out.
    /// </summary>
    public IReadOnlyList<MarkdownFile> Select(TagListOptions options, MarkdownFile listingPage)
    {
        var required = options.Tags.Select(Find).ToArray();
        if (required.Any(tag => tag is null)) return [];
        var excluded = options.Exclude.Select(Find).OfType<SiteTag>().SelectMany(tag => tag.Pages).ToHashSet();
        return required[0]!.Pages
            .Where(page => !ReferenceEquals(page, listingPage) && !excluded.Contains(page) &&
                           required.Skip(1).All(tag => tag!.Pages.Contains(page)))
            .ToArray();
    }

    /// <summary>The reason a <c>tags</c> code block cannot list pages, such as an unknown tag; null when it can.</summary>
    public string? Check(TagListBlockSource block) =>
        block.Error ?? (Catalog is null ? null : block.Options!.Tags.Concat(block.Options.Exclude)
            .Where(name => Catalog.Find(name) is null)
            .Select(name => $"The tags block uses tag '{name}', which is not defined in {VocabularyName}." +
                            (Catalog.Suggest(name) is { } suggestion ? $" Did you mean '{suggestion}'?" : string.Empty))
            .FirstOrDefault());

    private string VocabularyName => VocabularyPath is null ? "the tag vocabulary" : Path.GetFileName(VocabularyPath);

    /// <summary>Reads the vocabulary of the site, if any, and collects the tags of the pages.</summary>
    /// <exception cref="InvalidOperationException">The vocabulary does not exist or is not valid.</exception>
    public static SiteTags Load(string rootPath, Markdown.Renderer.Res.Model.SiteConfiguration configuration, IEnumerable<MarkdownFile> roots)
    {
        var vocabularyPath = SiteConfigurationReader.ResolveTagsPath(rootPath, configuration);
        TagCatalog? catalog = null;
        if (vocabularyPath is not null)
        {
            try
            {
                catalog = TagCatalog.Parse(File.ReadAllText(vocabularyPath));
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException($"{Path.GetRelativePath(rootPath, vocabularyPath)}: {exception.Message}", exception);
            }
        }
        return Build(rootPath, roots, catalog, vocabularyPath);
    }

    /// <summary>
    /// Like <see cref="Load"/>, for mdu lint, which also runs when mdu.yml has errors: those are not tag problems, so the
    /// tags are then checked without a vocabulary. Only a vocabulary that is set but unusable is reported.
    /// </summary>
    public static SiteTags TryLoad(string rootPath, IEnumerable<MarkdownFile> roots, out string? vocabularyError)
    {
        vocabularyError = null;
        var pages = roots.ToArray();
        Markdown.Renderer.Res.Model.SiteConfiguration configuration;
        try
        {
            configuration = SiteConfigurationReader.ReadForContent(rootPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or YamlDotNet.Core.YamlException)
        {
            return Build(rootPath, pages, null, null);
        }
        try
        {
            return Load(rootPath, configuration, pages);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            vocabularyError = exception.Message;
            return Build(rootPath, pages, null, null);
        }
    }

    public static SiteTags Build(string rootPath, IEnumerable<MarkdownFile> roots, TagCatalog? catalog, string? vocabularyPath)
    {
        var pages = NavigationPage.Flatten(roots).ToArray();
        var vocabularyPage = vocabularyPath is null ? null : pages.FirstOrDefault(page =>
            MarkdownLinks.PathComparer.Equals(Path.GetFullPath(page.FileInfo.FullName), vocabularyPath));
        var vocabularyName = vocabularyPath is null ? "the tag vocabulary" : Path.GetRelativePath(rootPath, vocabularyPath).Replace('\\', '/');
        var problems = new List<TagProblem>();
        var uses = new List<(MarkdownFile Page, string Spelling, string Key, TagDefinition? Definition)>();

        foreach (var page in pages)
        {
            if (page.HasInvalidTags)
                problems.Add(new TagProblem(page, "The 'tags' front matter must be one tag or a list of tags.", false));
            foreach (var spelling in page.Tags)
            {
                var definition = catalog?.Find(spelling);
                if (catalog is not null && definition is null)
                {
                    var suggestion = catalog.Suggest(spelling);
                    problems.Add(new TagProblem(page,
                        $"Tag '{spelling}' is not defined in {vocabularyName}." +
                        (suggestion is null ? $" Add it as a '## {spelling}' heading there." : $" Did you mean '{suggestion}'?"),
                        false, spelling));
                }
                uses.Add((page, spelling, definition is null ? TagCatalog.Normalize(spelling) : TagCatalog.Normalize(definition.Name), definition));
            }
        }

        var tags = new List<SiteTag>();
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in uses.GroupBy(use => use.Key))
        {
            var definition = group.First().Definition;
            // Without a vocabulary, the spelling most pages use names the tag; the first one wins a tie.
            var name = definition?.Name ?? group.GroupBy(use => use.Spelling, StringComparer.Ordinal)
                .OrderByDescending(spelling => spelling.Count()).First().Key;
            foreach (var use in group.Where(use => !IsAccepted(use.Spelling, name, definition)).DistinctBy(use => use.Page))
                problems.Add(new TagProblem(use.Page,
                    $"Tag '{use.Spelling}' differs from '{name}' ({Pages(group.Count(other => other.Spelling == name))}). Write it as '{name}'.",
                    true, use.Spelling));
            tags.Add(new SiteTag(name, string.Empty, definition, group.Select(use => use.Page).Distinct().ToArray()) { Key = group.Key });
        }

        tags.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name) is var order and not 0
            ? order : StringComparer.Ordinal.Compare(left.Name, right.Name));
        for (var index = 0; index < tags.Count; index++)
        {
            var slug = TagCatalog.Slug(tags[index].Name);
            for (var suffix = 2; !slugs.Add(slug); suffix++) slug = $"{TagCatalog.Slug(tags[index].Name)}-{suffix}";
            tags[index] = tags[index] with { Slug = slug };
        }

        // A page lists its tags in the order its front matter gives them.
        var byKey = tags.ToDictionary(tag => tag.Key);
        var ordered = uses.GroupBy(use => use.Page).ToDictionary(group => group.Key,
            group => (IReadOnlyList<SiteTag>)group.Select(use => byKey[use.Key]).Distinct().ToArray());
        var siteTags = new SiteTags(tags, ordered, problems, catalog, vocabularyPath, vocabularyPage);
        // The tags code blocks follow the same vocabulary as the front matter.
        foreach (var page in pages)
        foreach (var block in TagListBlock.Find(page.ReadMarkdown()))
            if (siteTags.Check(block) is { } message)
                problems.Add(new TagProblem(page, message, false, Line: block.Line));
        return siteTags;

        static bool IsAccepted(string spelling, string name, TagDefinition? definition) =>
            spelling == name || definition?.Aliases.Contains(spelling, StringComparer.Ordinal) == true;

        static string Pages(int count) => count == 1 ? "1 page" : $"{count} pages";
    }

    /// <summary>Formats a problem with the page path for the console, such as <c>Setup.md: Tag 'x' is not defined…</c>.</summary>
    public static string Describe(string rootPath, TagProblem problem) =>
        $"{(problem.Page.SiteRelativePath ?? Path.GetRelativePath(rootPath, problem.Page.FileInfo.FullName)).Replace('\\', '/')}: {problem.Message}";

    /// <summary>
    /// Writes the problems to standard error and returns whether one of them is an error. With <paramref name="pages"/>,
    /// only the problems of those pages count, such as the pages of one PDF export.
    /// </summary>
    public bool Report(string rootPath, IEnumerable<MarkdownFile>? pages = null)
    {
        var selected = pages?.ToHashSet();
        var problems = Problems.Where(problem => selected?.Contains(problem.Page) != false).ToArray();
        foreach (var problem in problems)
            Console.Error.WriteLine($"{(problem.IsWarning ? "Warning" : "Error")}: {Describe(rootPath, problem)}");
        return problems.Any(problem => !problem.IsWarning);
    }
}
