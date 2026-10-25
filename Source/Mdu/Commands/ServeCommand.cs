using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Core.Collections.NodeGraph.Extensions;
using Markdown.Renderer;
using Markdown.Search;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Commands;

public sealed class ServeCommand
{
    private static readonly StringComparer PathComparer = MarkdownLinks.PathComparer;

    private readonly GraphBuilder _graphBuilder;
    private readonly DataPreparationVisitor _dataPreparationVisitor;
    private readonly HtmlRenderer _renderer;
    private readonly SearchIndexBuilder _searchIndexBuilder;
    private readonly PdfCommand _pdfCommand;
    private readonly Dictionary<string, MarkdownFile> _pages = new(PathComparer);
    private readonly Dictionary<string, HashSet<string>> _assetsByPage = new(PathComparer);
    private readonly Dictionary<string, HashSet<string>> _pagesByAsset = new(PathComparer);
    private readonly Dictionary<string, IReadOnlyList<string>> _openApiSpecsByPage = new(PathComparer);
    private readonly Dictionary<string, IReadOnlyList<string>> _openApiSpecPagesByPage = new(PathComparer);
    private readonly HashSet<string> _generatedFiles = new(PathComparer);
    private readonly HashSet<string> _configurationAssets = new(PathComparer);
    private FileWatcherSet? _watchers;

    private MarkdownFile[] _navigationRoots = Array.Empty<MarkdownFile>();
    private IReadOnlyCollection<string> _siteMediaProviders = Array.Empty<string>();
    private RenderCommandSettings _settings = new();
    private string _rootPath = string.Empty;
    private string _outputPath = string.Empty;
    private string? _glossaryPath;
    private bool _hasPdfLink;

    public ServeCommand(
        GraphBuilder graphBuilder,
        DataPreparationVisitor dataPreparationVisitor,
        HtmlRenderer renderer,
        SearchIndexBuilder searchIndexBuilder,
        PdfCommand? pdfCommand = null)
    {
        _graphBuilder = graphBuilder;
        _dataPreparationVisitor = dataPreparationVisitor;
        _renderer = renderer;
        _searchIndexBuilder = searchIndexBuilder;
        _pdfCommand = pdfCommand ?? new PdfCommand(graphBuilder, new PdfRenderer(renderer));
    }

    public int Execute(RenderCommandSettings settings, int port, bool noBrowser) =>
        ExecuteAsync(settings, port, noBrowser).GetAwaiter().GetResult();

    private async Task<int> ExecuteAsync(RenderCommandSettings settings, int port, bool noBrowser)
    {
        if (port is < 1 or > 65535)
        {
            Console.Error.WriteLine("Port must be between 1 and 65535.");
            return 1;
        }

        try
        {
            _rootPath = CommandPathDefaults.ResolveRootPath(settings.RootPath);
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }

        _outputPath = CommandPathDefaults.ResolveOutputPath(settings.OutputPath);
        if (PathComparer.Equals(_rootPath, _outputPath))
        {
            Console.Error.WriteLine("The output folder must be different from the Markdown root folder.");
            return 1;
        }
        if (IsInsideDirectory(_outputPath, _rootPath))
        {
            Console.Error.WriteLine("The output folder cannot contain the Markdown root folder.");
            return 1;
        }

        _settings = new RenderCommandSettings
        {
            RootPath = _rootPath,
            OutputPath = _outputPath,
            SearchPattern = settings.SearchPattern,
            IncludeHidden = settings.IncludeHidden,
            EditPageRoot = settings.EditPageRoot
        };

        var changes = Channel.CreateUnbounded<FileChange>();
        using var watcher = new FileWatcherSet(_rootPath, _outputPath, changes.Writer);
        _watchers = watcher;
        try
        {
            RebuildSite();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or YamlDotNet.Core.YamlException)
        {
            _watchers = null;
            Console.Error.WriteLine($"Could not render the site: {exception.Message}");
            return 1;
        }

        using var server = new PreviewServer(_outputPath, port);
        try
        {
            server.Start();
        }
        catch (HttpListenerException exception)
        {
            _watchers = null;
            Console.Error.WriteLine($"Could not start the preview server on port {port}: {exception.Message}");
            return 1;
        }

        var url = server.BaseUrl;
        Console.WriteLine($"Serving {_rootPath} at {url} (press Ctrl+C to stop)");
        if (!noBrowser) OpenBrowser(url);

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var serverTask = server.RunAsync(cancellation.Token);
            var changesTask = ProcessChangesAsync(changes.Reader, server, cancellation.Token);
            await Task.WhenAny(serverTask, changesTask);
            var stoppedByUser = cancellation.IsCancellationRequested;
            if (!stoppedByUser) cancellation.Cancel();

            await Task.WhenAll(serverTask, changesTask);
            if (!stoppedByUser)
            {
                Console.Error.WriteLine("The preview server stopped unexpectedly.");
                return 1;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is HttpListenerException or SocketException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            Console.Error.WriteLine($"The preview server stopped: {exception.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            _watchers = null;
        }

        return 0;
    }

    private void RebuildSite()
    {
        var previousFiles = new HashSet<string>(_generatedFiles, PathComparer);
        var siteConfiguration = SiteConfigurationReader.Read(_rootPath);
        _glossaryPath = SiteConfigurationReader.GetGlossaryPath(_rootPath, siteConfiguration);
        _watchers?.UpdateExternalFiles(_openApiSpecsByPage.Values.SelectMany(paths => paths), _glossaryPath);
        var glossaryPath = SiteConfigurationReader.ResolveGlossaryPath(_rootPath, siteConfiguration);
        var glossaryMarkdown = glossaryPath is null ? null : File.ReadAllText(glossaryPath);
        _renderer.SetGlossary(glossaryMarkdown);
        var graph = _graphBuilder.CreateGraph(_settings, glossaryPath);
        _navigationRoots = graph;

        _renderer.PrepareBasePath(_outputPath, siteConfiguration);
        SiteConfigurationReader.CopyAssets(_rootPath, _outputPath, siteConfiguration);
        _configurationAssets.Clear();
        foreach (var asset in new[] { siteConfiguration.LogoLight, siteConfiguration.LogoDark, siteConfiguration.FaviconLight, siteConfiguration.FaviconDark }
                     .Where(asset => !string.IsNullOrWhiteSpace(asset)))
        {
            if (Uri.TryCreate(asset, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") continue;
            _configurationAssets.Add(Path.GetFullPath(asset!, _rootPath));
        }
        SearchClientAsset.WriteTo(_outputPath);
        _searchIndexBuilder.Clear();
        graph.Visit(new LocalAssetsCopy(_rootPath, _outputPath));
        // Register external OpenAPI watchers before PDF generation and page rendering.
        RebuildDependencyIndex(graph);
        var pdfOutputPath = Path.Combine(_outputPath, "documentation.pdf");
        if (_pdfCommand.WriteSitePdfIfLinked(_rootPath, pdfOutputPath, graph) != 0)
            throw new InvalidOperationException("Could not render the documentation PDF.");
        _hasPdfLink = File.Exists(pdfOutputPath);
        var rendererVisitor = new MarkdigVisitor(
            _rootPath,
            _outputPath,
            _renderer,
            _settings.EditPageRoot,
            _searchIndexBuilder,
            _navigationRoots);
        _siteMediaProviders = rendererVisitor.SiteMediaProviders;
        graph.Visit(rendererVisitor);
        _searchIndexBuilder.WriteIndex(_outputPath);
        ReconcileGeneratedFiles(previousFiles);
        Console.WriteLine("Site updated.");
    }

    private void RebuildDependencyIndex(IEnumerable<MarkdownFile> graph)
    {
        _pages.Clear();
        _assetsByPage.Clear();
        _pagesByAsset.Clear();
        _openApiSpecsByPage.Clear();
        _openApiSpecPagesByPage.Clear();

        foreach (var page in Flatten(graph))
        {
            var pagePath = GetPageKey(page);
            _pages[pagePath] = page;
        }

        foreach (var (pagePath, page) in _pages)
            UpdatePageAssets(pagePath, page);

        _watchers?.UpdateExternalFiles(_openApiSpecsByPage.Values.SelectMany(paths => paths), _glossaryPath);
    }

    private static IEnumerable<MarkdownFile> Flatten(IEnumerable<MarkdownFile> pages)
    {
        foreach (var page in pages)
        {
            yield return page;
            foreach (var child in Flatten(page.Children))
                yield return child;
        }
    }

    private void UpdatePageAssets(string pagePath, MarkdownFile page)
    {
        RemovePageAssets(pagePath);

        var pageAssets = new HashSet<string>(PathComparer);
        var markdown = page.ReadMarkdown();
        var assetRoot = page.SourceRootPath ?? _rootPath;
        foreach (var assetPath in LocalAssetsCopy.GetLinkedAssetPaths(
                     markdown,
                     page.FileInfo.DirectoryName ?? _rootPath)
                 .Where(assetPath => IsInsideDirectory(assetRoot, assetPath)))
        {
            if (GetAssetOutputPath(page, assetPath) is null) continue;
            pageAssets.Add(assetPath);
            if (!_pagesByAsset.TryGetValue(assetPath, out var dependentPages))
            {
                dependentPages = new HashSet<string>(PathComparer);
                _pagesByAsset.Add(assetPath, dependentPages);
            }

            dependentPages.Add(pagePath);
        }

        _assetsByPage[pagePath] = pageAssets;
        UpdatePageOpenApiDependencies(pagePath, page, markdown);
    }

    private void UpdatePageOpenApiDependencies(string pagePath, MarkdownFile page, string markdown)
    {
        var body = RemoveFrontMatter(markdown, page.FrontMatter);
        var referencedSpecs = OpenApiMarkdownRenderer.FindReferencedSpecPaths(body)
            .Select(specPath => OpenApiMarkdownRenderer.ResolveSpecPath(
                page.FileInfo.FullName,
                specPath,
                page.SourceRootPath))
            .ToArray();
        _openApiSpecsByPage[pagePath] = referencedSpecs;

        var pageOutputPath = GetPageOutputPath(page);
        var outputDirectory = Path.GetDirectoryName(pageOutputPath)!;
        var outputSiblings = MarkdigVisitor.GetOutputSiblings(
            _pages.Values.Select(candidate => (Page: candidate, OutputPath: GetPageOutputPath(candidate))),
            outputDirectory);
        var specPages = OpenApiMarkdownRenderer.GetSpecificationPageFileNames(
                body,
                page.FileInfo.FullName,
                page.SourceRootPath,
                Path.GetFileName(pageOutputPath),
                outputSiblings)
            .Select(specFileName => Path.Combine(outputDirectory, specFileName))
            .ToArray();
        _openApiSpecPagesByPage[pagePath] = specPages;
    }

    private void RemovePageAssets(string pagePath)
    {
        if (!_assetsByPage.Remove(pagePath, out var previousAssets)) return;

        foreach (var assetPath in previousAssets)
        {
            if (!_pagesByAsset.TryGetValue(assetPath, out var dependentPages)) continue;
            dependentPages.Remove(pagePath);
            if (dependentPages.Count == 0) _pagesByAsset.Remove(assetPath);
        }
    }

    private async Task ProcessChangesAsync(
        ChannelReader<FileChange> changes,
        PreviewServer server,
        CancellationToken cancellationToken)
    {
        while (await changes.WaitToReadAsync(cancellationToken))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(75), cancellationToken);
            var batch = new List<FileChange>();
            while (changes.TryRead(out var change)) batch.Add(change);

            try
            {
                ProcessChangeBatch(batch, server);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or YamlDotNet.Core.YamlException)
            {
                Console.Error.WriteLine($"Could not update the site: {exception.Message}");
            }
        }
    }

    private void ProcessChangeBatch(IReadOnlyCollection<FileChange> changes, PreviewServer server)
    {
        var relevantChanges = changes
            .Where(change => change.Kind == FileChangeKind.Rescan ||
                             !IsGeneratedOutputPath(change.Path) &&
                             (change.OldPath is null || !IsGeneratedOutputPath(change.OldPath)))
            .ToArray();
        if (relevantChanges.Length == 0) return;

        var siteConfigurationPath = Path.Combine(_rootPath, "mdu.yml");
        if (relevantChanges.Any(change => PathComparer.Equals(change.Path, siteConfigurationPath) ||
                                          _configurationAssets.Contains(change.Path) ||
                                          change.OldPath is not null && _configurationAssets.Contains(change.OldPath)))
        {
            RebuildSite();
            server.Publish(reloadAll: true);
            return;
        }

        if (_glossaryPath is not null && relevantChanges.Any(change =>
                IsGlossaryPathChange(change, _glossaryPath)))
        {
            RebuildSite();
            server.Publish(reloadAll: true);
            return;
        }

        var importMarkers = _pages.Values
            .Where(page => page.ImportMarkerPath is not null)
            .Select(page => Path.GetFullPath(page.ImportMarkerPath!))
            .ToHashSet(PathComparer);
        if (relevantChanges.Any(change => importMarkers.Contains(change.Path) ||
                                          change.OldPath is not null && importMarkers.Contains(change.OldPath)))
        {
            RebuildSite();
            server.Publish(reloadAll: true);
            return;
        }

        if (relevantChanges.Any(IsOpenApiSpecChange))
        {
            RebuildSite();
            server.Publish(reloadAll: true);
            return;
        }

        // Editors that save through a temporary file report creations, renames, and deletions for a page whose
        // content only changed. Only the state after the batch decides whether pages were added or removed.
        var requiresRebuild = relevantChanges.Any(change =>
                change.Kind == FileChangeKind.Rescan ||
                change.Kind is (FileChangeKind.Created or FileChangeKind.Deleted or FileChangeKind.Renamed) &&
                IsDirectoryChange(change)) ||
            relevantChanges
                .Where(change => change.Kind is FileChangeKind.Created or FileChangeKind.Deleted or FileChangeKind.Renamed)
                .SelectMany(change => change.OldPath is null ? new[] { change.Path } : new[] { change.Path, change.OldPath })
                .Distinct(PathComparer)
                .Any(ChangesPageSet);

        if (requiresRebuild)
        {
            RebuildSite();
            server.Publish(reloadAll: true);
            return;
        }

        var changedPages = relevantChanges
            .Where(change => change.Kind is FileChangeKind.Changed or FileChangeKind.Created or FileChangeKind.Renamed &&
                             _pages.ContainsKey(change.Path) && File.Exists(change.Path))
            .Select(change => change.Path)
            .Distinct(PathComparer)
            .ToArray();

        var affectedPageUrls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pagePath in changedPages)
        {
            var page = _pages[pagePath];
            var depth = page.CountDepth();
            var previousTitle = page.Title;
            var previousNavigationTitle = page.NavigationTitle;
            var previousNavigationOrder = page.NavigationOrder;
            var previousNavigationVisible = page.NavigationVisible;
            var previousNavigationExpanded = page.NavigationExpanded;
            var previousNavigationPlacement = page.NavigationPlacement;
            var previousInvalidNavigationPlacement = page.InvalidNavigationPlacement;
            _dataPreparationVisitor.Process(page, depth);
            if (!previousTitle.Equals(page.Title, StringComparison.Ordinal) ||
                !previousNavigationTitle.Equals(page.NavigationTitle, StringComparison.Ordinal) ||
                previousNavigationOrder != page.NavigationOrder ||
                previousNavigationVisible != page.NavigationVisible ||
                previousNavigationExpanded != page.NavigationExpanded ||
                previousNavigationPlacement != page.NavigationPlacement ||
                previousInvalidNavigationPlacement != page.InvalidNavigationPlacement)
            {
                RebuildSite();
                server.Publish(reloadAll: true);
                return;
            }

            var previousSpecPages = _openApiSpecPagesByPage.GetValueOrDefault(pagePath) ?? Array.Empty<string>();
            UpdatePageAssets(pagePath, page);
            var specPagesChanged = !previousSpecPages.SequenceEqual(_openApiSpecPagesByPage[pagePath], PathComparer);
            _watchers?.UpdateExternalFiles(_openApiSpecsByPage.Values.SelectMany(paths => paths), _glossaryPath);
            new LocalAssetsCopy(_rootPath, _outputPath).Process(page, depth);
            var rendererVisitor = new MarkdigVisitor(
                _rootPath,
                _outputPath,
                _renderer,
                _settings.EditPageRoot,
                _searchIndexBuilder,
                _navigationRoots);
            if (!new HashSet<string>(_siteMediaProviders, StringComparer.Ordinal).SetEquals(rendererVisitor.SiteMediaProviders))
            {
                RebuildSite();
                server.Publish(reloadAll: true);
                return;
            }

            rendererVisitor.Process(page, depth);
            affectedPageUrls.Add(PreviewServer.ToUrlPath(_outputPath, GetPageOutputPath(page)));

            if (specPagesChanged)
            {
                var outputDirectory = Path.GetDirectoryName(GetPageOutputPath(page))!;
                foreach (var (siblingKey, siblingPage) in _pages)
                {
                    if (PathComparer.Equals(siblingKey, pagePath) || !File.Exists(siblingPage.FileInfo.FullName))
                        continue;
                    var siblingOutputPath = GetPageOutputPath(siblingPage);
                    if (!PathComparer.Equals(Path.GetDirectoryName(siblingOutputPath), outputDirectory))
                        continue;

                    var previousSiblingSpecPages = _openApiSpecPagesByPage.GetValueOrDefault(siblingKey) ?? Array.Empty<string>();
                    try
                    {
                        UpdatePageOpenApiDependencies(siblingKey, siblingPage, siblingPage.ReadMarkdown());
                        if (previousSiblingSpecPages.SequenceEqual(_openApiSpecPagesByPage[siblingKey], PathComparer))
                            continue;

                        rendererVisitor.Process(siblingPage, siblingPage.CountDepth());
                        affectedPageUrls.Add(PreviewServer.ToUrlPath(_outputPath, siblingOutputPath));
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or YamlDotNet.Core.YamlException)
                    {
                        Console.Error.WriteLine($"Could not update the site: {exception.Message}");
                    }
                }
            }
        }

        if (changedPages.Length > 0)
        {
            _searchIndexBuilder.WriteIndex(_outputPath);
            var pdfOutputPath = Path.Combine(_outputPath, "documentation.pdf");
            if (_pdfCommand.WriteSitePdfIfLinked(_rootPath, pdfOutputPath, _navigationRoots) != 0)
                throw new InvalidOperationException("Could not render the documentation PDF.");
            _hasPdfLink = File.Exists(pdfOutputPath);
        }

        var assetChanges = relevantChanges
            .Where(change => change.Kind is FileChangeKind.Changed or FileChangeKind.Created or FileChangeKind.Deleted or FileChangeKind.Renamed)
            .SelectMany(change => change.OldPath is null ? new[] { change.Path } : new[] { change.Path, change.OldPath })
            .Distinct(PathComparer)
            .Where(_pagesByAsset.ContainsKey)
            .ToArray();

        foreach (var assetPath in assetChanges)
        {
            var dependentPages = _pagesByAsset[assetPath];
            foreach (var pagePath in dependentPages)
            {
                if (_pages.TryGetValue(pagePath, out var page))
                {
                    CopyOrRemoveAsset(page, assetPath);
                    affectedPageUrls.Add(PreviewServer.ToUrlPath(_outputPath, GetPageOutputPath(page)));
                }
            }
        }

        ReconcileGeneratedFiles(new HashSet<string>(_generatedFiles, PathComparer));
        if (affectedPageUrls.Count > 0)
        {
            Console.WriteLine($"Updated {affectedPageUrls.Count} page(s).");
            server.Publish(affectedPageUrls);
        }
    }

    private bool ChangesPageSet(string path) =>
        _pages.ContainsKey(path) ? !File.Exists(path) : File.Exists(path) && IsMarkdownCandidate(path);

    private bool IsOpenApiSpecChange(FileChange change)
    {
        foreach (var specPaths in _openApiSpecsByPage.Values)
        {
            foreach (var specPath in specPaths)
            {
                if (PathComparer.Equals(specPath, change.Path) ||
                    change.OldPath is not null && PathComparer.Equals(specPath, change.OldPath)) return true;
            }
        }
        return false;
    }

    private bool IsMarkdownCandidate(string path)
    {
        if (_pages.ContainsKey(path)) return true;
        if (Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase)) return true;

        if (!File.Exists(path)) return false;
        var pattern = _settings.SearchPattern ?? Default.SearchPattern;
        var directory = Path.GetDirectoryName(path);
        return directory is not null && Directory.GetFiles(directory, pattern)
            .Any(file => PathComparer.Equals(Path.GetFullPath(file), path));
    }

    private static bool IsDirectoryChange(FileChange change) =>
        Directory.Exists(change.Path) ||
        change.OldPath is not null && !Path.HasExtension(change.Path) && !Path.HasExtension(change.OldPath);

    private void CopyOrRemoveAsset(MarkdownFile page, string assetPath)
    {
        var source = Path.GetFullPath(assetPath);
        var output = GetAssetOutputPath(page, source);
        if (output is null) return;
        if (!File.Exists(source))
        {
            if (File.Exists(output)) File.Delete(output);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temporaryPath = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporaryPath);
            File.Move(temporaryPath, output, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private void ReconcileGeneratedFiles(HashSet<string> previousFiles)
    {
        var currentFiles = new HashSet<string>(PathComparer)
        {
            Path.Combine(_outputPath, "css", "main.css"),
            Path.Combine(_outputPath, "js", "search.js"),
            Path.Combine(_outputPath, "search-index.json")
        };
        if (_hasPdfLink) currentFiles.Add(Path.Combine(_outputPath, "documentation.pdf"));
        currentFiles.UnionWith(_renderer.IconSearchAssets);

        foreach (var page in _pages.Values)
        {
            currentFiles.Add(GetPageOutputPath(page));
            var aliasPath = GetAliasOutputPath(page);
            if (aliasPath is not null) currentFiles.Add(aliasPath);
        }

        foreach (var specPages in _openApiSpecPagesByPage.Values)
            currentFiles.UnionWith(specPages);

        foreach (var assetPath in _configurationAssets)
            if (File.Exists(assetPath)) currentFiles.Add(GetAssetOutputPath(assetPath));

        foreach (var (assetPath, dependentPages) in _pagesByAsset)
        {
            if (!File.Exists(assetPath)) continue;
            foreach (var pagePath in dependentPages)
                if (_pages.TryGetValue(pagePath, out var page))
                {
                    var outputPath = GetAssetOutputPath(page, assetPath);
                    if (outputPath is not null) currentFiles.Add(outputPath);
                }
        }

        foreach (var staleFile in previousFiles.Except(currentFiles, PathComparer))
        {
            if (File.Exists(staleFile)) File.Delete(staleFile);
        }

        _generatedFiles.Clear();
        _generatedFiles.UnionWith(currentFiles);
    }

    private string GetPageOutputPath(MarkdownFile page)
    {
        var pathInfo = FilePathInfo.CreateRenderFileInfo(_rootPath, _outputPath, page.FileInfo, ".html", page.SiteRelativePath);
        var outputPath = pathInfo.OutputFullFilePath;
        if (outputPath.EndsWith("readme.html", StringComparison.OrdinalIgnoreCase))
            outputPath = outputPath[..^"readme.html".Length] + "index.html";
        return Path.GetFullPath(outputPath);
    }

    private string? GetAliasOutputPath(MarkdownFile page)
    {
        if (!string.IsNullOrWhiteSpace(page.AliasRelativePath))
            return Path.GetFullPath(Path.Combine(_outputPath, page.AliasRelativePath));
        if (page.FileInfo.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase)) return null;
        var aliasDirectory = Path.Combine(page.FileInfo.DirectoryName!, Path.GetFileNameWithoutExtension(page.FileInfo.Name));
        if (Directory.Exists(aliasDirectory) && Directory.EnumerateFiles(aliasDirectory).Any(filePath =>
                Path.GetFileName(filePath).Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(filePath).Equals("index.md", StringComparison.OrdinalIgnoreCase))) return null;
        var pageOutputPath = GetPageOutputPath(page);
        return Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(pageOutputPath)!,
            Path.GetFileNameWithoutExtension(pageOutputPath),
            "index.html"));
    }

    private string GetAssetOutputPath(string assetPath)
    {
        var relativePath = Path.GetRelativePath(_rootPath, assetPath);
        return Path.GetFullPath(Path.Combine(_outputPath, relativePath));
    }

    private string? GetAssetOutputPath(MarkdownFile page, string assetPath)
    {
        var sourceDirectory = page.FileInfo.DirectoryName ?? _rootPath;
        var pageDirectory = Path.GetDirectoryName(page.SiteRelativePath ?? Path.GetRelativePath(_rootPath, page.FileInfo.FullName)) ?? string.Empty;
        var relativeAssetPath = Path.GetRelativePath(sourceDirectory, assetPath);
        var outputPath = Path.GetFullPath(Path.Combine(_outputPath, pageDirectory, relativeAssetPath));
        return IsInsideDirectory(_outputPath, outputPath) ? outputPath : null;
    }

    private string GetPageKey(MarkdownFile page)
    {
        var sourcePath = Path.GetFullPath(page.FileInfo.FullName);
        return page.SourceRootPath is null
            ? sourcePath
            : $"{sourcePath}|{page.SiteRelativePath}";
    }

    private static bool IsInsideDirectory(string directory, string path)
    {
        var relativePath = Path.GetRelativePath(directory, path);
        return relativePath == "." ||
               !Path.IsPathRooted(relativePath) &&
               relativePath != ".." &&
               !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsGlossaryPathChange(FileChange change, string glossaryPath)
    {
        if (PathComparer.Equals(change.Path, glossaryPath) ||
            change.OldPath is not null && PathComparer.Equals(change.OldPath, glossaryPath))
            return true;

        return change.Kind is (FileChangeKind.Created or FileChangeKind.Renamed) &&
               Directory.Exists(change.Path) && IsInsideDirectory(change.Path, glossaryPath);
    }

    private bool IsGeneratedOutputPath(string path) =>
        IsInsideDirectory(_rootPath, _outputPath) && IsInsideDirectory(_outputPath, path);

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            Console.Error.WriteLine($"Could not open a browser automatically. Open {url}");
        }
    }

    private static string RemoveFrontMatter(string markdown, string frontMatter) =>
        !string.IsNullOrEmpty(frontMatter) && markdown.StartsWith(frontMatter, StringComparison.Ordinal)
            ? markdown[frontMatter.Length..].TrimStart('\r', '\n')
            : markdown;

    private sealed record FileChange(string Path, string? OldPath, FileChangeKind Kind);

    private enum FileChangeKind
    {
        Changed,
        Created,
        Deleted,
        Renamed,
        Rescan
    }

    private sealed class FileWatcherSet : IDisposable
    {
        private readonly string _rootPath;
        private readonly string _outputPath;
        private readonly ChannelWriter<FileChange> _writer;
        private readonly FileSystemWatcher _rootWatcher;
        private readonly Dictionary<string, FileSystemWatcher> _externalWatchers = new(PathComparer);

        public FileWatcherSet(string rootPath, string outputPath, ChannelWriter<FileChange> writer)
        {
            _rootPath = rootPath;
            _outputPath = outputPath;
            _writer = writer;
            _rootWatcher = Create(_rootPath, includeSubdirectories: true, internalBufferSize: 64 * 1024);
        }

        public void UpdateExternalFiles(IEnumerable<string> specPaths, string? glossaryPath)
        {
            var directories = specPaths
                .Where(path => !IsInsideDirectory(_rootPath, path))
                .Select(path => Path.GetDirectoryName(path)!)
                .Where(Directory.Exists)
                .ToHashSet(PathComparer);
            if (glossaryPath is not null && !IsInsideDirectory(_rootPath, glossaryPath))
            {
                var directory = Path.GetDirectoryName(glossaryPath)!;
                while (!Directory.Exists(directory))
                {
                    var parent = Directory.GetParent(directory);
                    if (parent is null || PathComparer.Equals(parent.FullName, Path.GetPathRoot(parent.FullName))) break;
                    directory = parent.FullName;
                }
                if (Directory.Exists(directory) &&
                    !PathComparer.Equals(directory, Path.GetPathRoot(directory)))
                    directories.Add(directory);
            }

            foreach (var directory in _externalWatchers.Keys
                         .Where(directory => !directories.Contains(directory))
                         .ToArray())
            {
                _externalWatchers[directory].Dispose();
                _externalWatchers.Remove(directory);
            }

            foreach (var directory in directories)
            {
                if (_externalWatchers.ContainsKey(directory)) continue;
                _externalWatchers.Add(directory, Create(directory, includeSubdirectories: false));
            }
        }

        public void Dispose()
        {
            _rootWatcher.Dispose();
            foreach (var watcher in _externalWatchers.Values) watcher.Dispose();
            _externalWatchers.Clear();
        }

        private FileSystemWatcher Create(string path, bool includeSubdirectories, int? internalBufferSize = null)
        {
            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = includeSubdirectories,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = false
            };
            if (internalBufferSize.HasValue) watcher.InternalBufferSize = internalBufferSize.Value;

            watcher.Changed += (_, eventArgs) => WriteChange(new(eventArgs.FullPath, null, FileChangeKind.Changed));
            watcher.Created += (_, eventArgs) => WriteChange(new(eventArgs.FullPath, null, FileChangeKind.Created));
            watcher.Deleted += (_, eventArgs) => WriteChange(new(eventArgs.FullPath, null, FileChangeKind.Deleted));
            watcher.Renamed += (_, eventArgs) => WriteChange(new(eventArgs.FullPath, eventArgs.OldFullPath, FileChangeKind.Renamed));
            watcher.Error += (_, _) => _writer.TryWrite(new(_rootPath, null, FileChangeKind.Rescan));
            watcher.EnableRaisingEvents = true;
            return watcher;
        }

        private void WriteChange(FileChange change)
        {
            if (IsInsideDirectory(_rootPath, _outputPath) &&
                (IsInsideDirectory(_outputPath, change.Path) ||
                 change.OldPath is not null && IsInsideDirectory(_outputPath, change.OldPath))) return;

            _writer.TryWrite(change);
        }

    }
}
