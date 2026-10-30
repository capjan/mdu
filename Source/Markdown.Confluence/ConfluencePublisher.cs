using System.Text.Json.Nodes;

namespace Markdown.Confluence;

public sealed class ConfluencePublisher(ConfluenceClient client, TextWriter output)
{
    public async Task Publish(IReadOnlyList<ConfluenceSource> sources, bool dryRun, string? glossary = null,
        CancellationToken cancellationToken = default, ConfluenceAsset? pdf = null)
    {
        if (sources.Count == 0) throw new InvalidOperationException("No Markdown pages selected for Confluence.");
        var sourceMap = sources.ToDictionary(source => source.Path, StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (source.Path == "$root" || source.ParentPath == source.Path ||
                source.ParentPath is not null && !sourceMap.ContainsKey(source.ParentPath))
                throw new InvalidOperationException("Invalid Confluence source hierarchy.");
            var ancestors = new HashSet<string>(StringComparer.Ordinal) { source.Path };
            for (var parent = source.ParentPath; parent is not null; parent = sourceMap[parent].ParentPath)
                if (!ancestors.Add(parent)) throw new InvalidOperationException("Cyclic Confluence source hierarchy.");
        }
        var renderer = new ConfluenceStorageRenderer(client.Target, sources, glossary, pdf);
        var documents = sources.ToDictionary(source => source.Path, renderer.Render, StringComparer.Ordinal);
        foreach (var source in sources)
            foreach (var diagnostic in documents[source.Path].Diagnostics)
                await output.WriteLineAsync($"Warning: {source.Path}: {diagnostic}");

        await client.Preflight(cancellationToken);
        var root = await client.FindRoot(cancellationToken);
        var pages = root is null ? new Dictionary<string, JsonObject>(StringComparer.Ordinal) :
            (await client.ReadManagedPages(cancellationToken)).ToDictionary(page =>
                ConfluenceClient.RequiredText(ConfluenceClient.Owner(page)!, "path"), StringComparer.Ordinal);

        // Validate the entire plan before the first write, including collisions with unmanaged titles.
        foreach (var source in sources)
        {
            var title = client.Target.PageTitle(source);
            var matches = await client.FindByTitle(title, cancellationToken);
            if (matches.Any(page => !pages.TryGetValue(source.Path, out var owned) ||
                                    ConfluenceClient.RequiredText(owned, "id") != ConfluenceClient.RequiredText(page, "id")))
                throw new InvalidOperationException($"Confluence title collision for {source.Path}; no foreign page will be adopted.");
            if (pages.TryGetValue(source.Path, out var existing))
            {
                var parentId = source.ParentPath is null ? ConfluenceClient.RequiredText(root!, "id") :
                    pages.TryGetValue(source.ParentPath, out var parent) ? ConfluenceClient.RequiredText(parent, "id") : null;
                if (parentId is null) throw new InvalidOperationException("Existing page has an unexpected source hierarchy.");
                await client.VerifyPage(ConfluenceClient.RequiredText(existing, "id"), source.Path, parentId, cancellationToken);
            }
        }
        if (root is not null)
            await client.VerifyPage(ConfluenceClient.RequiredText(root, "id"), "$root", client.Target.ParentPageId, cancellationToken);

        await output.WriteLineAsync($"Target: {client.Target.BaseUri} space={client.Target.SpaceKey} parent={client.Target.ParentPageId ?? "(top level)"} export={client.Target.ExportId}");
        await output.WriteLineAsync("Edit protection: publisher only. Comments and other actions remain governed by space permissions.");
        if (root is null) await output.WriteLineAsync("Create managed export root.");
        foreach (var source in sources)
        {
            var action = !pages.TryGetValue(source.Path, out var existing) ? "Create" :
                ConfluenceClient.Storage(existing) == documents[source.Path].Storage &&
                ConfluenceClient.Text(existing, "title") == client.Target.PageTitle(source) ? "Check" : "Update";
            await output.WriteLineAsync($"{action}: {source.Path} ({documents[source.Path].Assets.Count} attachments)");
        }
        foreach (var stale in pages.Keys.Except(sourceMap.Keys, StringComparer.Ordinal))
            await output.WriteLineAsync($"Warning: stale page retained: {stale}");
        if (dryRun)
        {
            await output.WriteLineAsync("Dry run complete. No write requests were sent.");
            return;
        }

        root ??= await client.CreatePage(client.Target.RootTitle, client.Target.ParentPageId, "$root", cancellationToken);
        var rootId = ConfluenceClient.RequiredText(root, "id");
        foreach (var source in Ordered(sources))
        {
            if (!pages.TryGetValue(source.Path, out var page))
            {
                var parentId = source.ParentPath is null ? rootId : ConfluenceClient.RequiredText(pages[source.ParentPath], "id");
                page = await client.CreatePage(client.Target.PageTitle(source), parentId, source.Path, cancellationToken);
                pages.Add(source.Path, page);
            }
            await client.RegisterPage(page, cancellationToken);
        }
        foreach (var source in Ordered(sources))
        {
            var page = pages[source.Path];
            var id = ConfluenceClient.RequiredText(page, "id");
            var parentId = source.ParentPath is null ? rootId : ConfluenceClient.RequiredText(pages[source.ParentPath], "id");
            var document = documents[source.Path];
            foreach (var asset in document.Assets)
                await client.UploadAsset(id, source.Path, asset, cancellationToken);
            if (ConfluenceClient.Storage(page) != document.Storage || ConfluenceClient.Text(page, "title") != client.Target.PageTitle(source))
                await client.UpdatePage(id, source.Path, client.Target.PageTitle(source), document.Storage, parentId, cancellationToken);
            await client.VerifyPage(id, source.Path, parentId, cancellationToken);
            await client.PublishVisibility(id, source.Path, cancellationToken);
        }
        var rootStorage = "<p>Read-only documentation mirror generated by MDU. Pages are listed in the Confluence page tree. Edit the Markdown sources to propose changes.</p>";
        if (ConfluenceClient.Storage(root) != rootStorage)
            await client.UpdatePage(rootId, "$root", client.Target.RootTitle, rootStorage, client.Target.ParentPageId, cancellationToken);
        await client.PublishVisibility(rootId, "$root", cancellationToken);
        await output.WriteLineAsync($"Published {sources.Count} pages. No pages or attachments were deleted.");
    }

    private static IEnumerable<ConfluenceSource> Ordered(IReadOnlyList<ConfluenceSource> sources)
    {
        foreach (var source in sources.Where(source => source.ParentPath is null))
        {
            yield return source;
            foreach (var child in Children(source.Path)) yield return child;
        }
        IEnumerable<ConfluenceSource> Children(string path)
        {
            foreach (var child in sources.Where(source => source.ParentPath == path))
            {
                yield return child;
                foreach (var descendant in Children(child.Path)) yield return descendant;
            }
        }
    }
}
