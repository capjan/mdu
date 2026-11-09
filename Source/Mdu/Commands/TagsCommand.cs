using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;

namespace Mdu.Commands;

/// <summary>Lists the tags of a site with the number of pages and the areas that use them.</summary>
public sealed class TagsCommand(GraphBuilder graphBuilder)
{
    public int Execute(VisitorSettings settings)
    {
        string rootPath;
        try
        {
            rootPath = CommandPathDefaults.ResolveRootPath(settings.RootPath);
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }

        SiteConfigurationReader.WarnAboutRenamedSettings(rootPath);
        MarkdownFile[] graph;
        try
        {
            graph = graphBuilder.CreateGraph(new VisitorSettings
            {
                RootPath = rootPath,
                SearchPattern = settings.SearchPattern,
                IncludeHidden = settings.IncludeHidden
            }, SiteConfigurationReader.TryResolveGlossaryPath(rootPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"Could not resolve Markdown sources: {exception.Message}");
            return 1;
        }

        var tags = SiteTags.TryLoad(rootPath, graph, out var vocabularyError);
        if (vocabularyError is not null)
        {
            Console.Error.WriteLine($"Could not read the tag vocabulary: {vocabularyError}");
            return 1;
        }

        var rows = tags.Tags
            .Select(tag => (tag.Name, Count: tag.Pages.Count, Detail: string.Join(", ", tag.Pages
                .Select(page => NavigationPage.GetArea(page, graph))
                .Distinct()
                .Select(area => NavigationPage.RemoveIconShortcodes(NavigationPage.GetTitle(area)).Trim()))))
            .ToList();
        var vocabularyName = tags.VocabularyPath is null ? null : Path.GetFileName(tags.VocabularyPath);
        var used = tags.Tags.Select(tag => tag.Name).ToHashSet(StringComparer.Ordinal);
        if (tags.Catalog is not null)
            rows.AddRange(tags.Catalog.Definitions.Where(definition => !used.Contains(definition.Name))
                .Select(definition => (definition.Name, 0, $"only in {vocabularyName}")));
        // Most used first: those are the tags readers meet most often.
        rows = rows.OrderByDescending(row => row.Count).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToList();

        if (rows.Count == 0)
        {
            Console.WriteLine("No tags found. Add 'tags' to the front matter of a page.");
            return 0;
        }
        var nameWidth = rows.Max(row => row.Name.Length);
        var countWidth = rows.Max(row => row.Count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        foreach (var (name, count, detail) in rows)
            Console.WriteLine($"{name.PadRight(nameWidth)}  {count.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(countWidth)}  {detail}".TrimEnd());

        var problems = tags.Problems.Count;
        if (problems > 0)
            Console.Error.WriteLine($"{problems} tag problem{(problems == 1 ? "" : "s")} found. Run 'mdu lint' for details.");
        return 0;
    }
}
