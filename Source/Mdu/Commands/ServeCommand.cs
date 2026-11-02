using System.ComponentModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Mdu.Service.PageTree.Extensions;
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
    private DependencyIndex _dependencyIndex = DependencyIndex.CreateEmpty();
    private readonly HashSet<string> _generatedFiles = new(PathComparer);
    private readonly HashSet<string> _configurationAssets = new(PathComparer);
    private FileWatcherSet? _watchers;
    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private readonly Dictionary<string, MarkdownFile> _pendingPages = new(PathComparer);
    private readonly HashSet<string> _failedPendingPages = new(PathComparer);
    private readonly HashSet<string> _unfinishedPages = new(PathComparer);
    private bool _rebuildAfterFailure;
    private readonly ConcurrentDictionary<string, MarkdownFile> _pendingOutputs = new(PathComparer);
    private MarkdigVisitor? _pendingVisitor;
    private PreviewServer? _server;
    private readonly Queue<PdfCommand.SitePdfJob> _pendingPdfs = new();
    private bool _pdfFailed;
    private bool _siteCompletionPending;
    private bool _pdfRefreshRequired;
    private readonly GitLastModifiedCache _gitDates = new();

    private MarkdownFile[] _navigationRoots = Array.Empty<MarkdownFile>();
    private IReadOnlyCollection<string> _siteMediaProviders = Array.Empty<string>();
    private RenderCommandSettings _settings = new();
    private string _rootPath = string.Empty;
    private string _outputPath = string.Empty;
    private string? _glossaryPath;
    private string? _iconsPath;

    private Dictionary<string, MarkdownFile> _pages => _dependencyIndex.Pages;
    private Dictionary<string, HashSet<string>> _assetsByPage => _dependencyIndex.AssetsByPage;
    private Dictionary<string, HashSet<string>> _pagesByAsset => _dependencyIndex.PagesByAsset;
    private Dictionary<string, IReadOnlyList<string>> _openApiSpecsByPage => _dependencyIndex.OpenApiSpecsByPage;
    private Dictionary<string, IReadOnlyList<string>> _openApiSpecPagesByPage => _dependencyIndex.OpenApiSpecPagesByPage;
    private Dictionary<string, string> _pdfTitlesByPage => _dependencyIndex.PdfTitlesByPage;

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

        using var server = new PreviewServer(_outputPath, port, PrepareOutputAsync);
        _server = server;
        server.SetBuilding(_pendingPages.Count > 0 || _pendingPdfs.Count > 0);
        UpdatePdfStatus();
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
        _siteCompletionPending = true;
        InvalidatePdfs();
        _server?.SetBuilding(true);
        var previousFiles = new HashSet<string>(_generatedFiles, PathComparer);
        var siteConfiguration = SiteConfigurationReader.Read(_rootPath);
        _glossaryPath = SiteConfigurationReader.GetGlossaryPath(_rootPath, siteConfiguration);
        _watchers?.UpdateExternalFiles(_openApiSpecsByPage.Values.SelectMany(paths => paths), _glossaryPath);
        var glossaryPath = SiteConfigurationReader.ResolveGlossaryPath(_rootPath, siteConfiguration);
        var glossaryMarkdown = glossaryPath is null ? null : File.ReadAllText(glossaryPath);
        _renderer.SetGlossary(glossaryMarkdown);
        _iconsPath = SiteConfigurationReader.GetIconsPath(_rootPath, siteConfiguration);
        SiteConfigurationReader.UseCustomIcons(_rootPath, siteConfiguration);
        SiteConfigurationReader.UseDiagramLimits(siteConfiguration);
        var graph = _graphBuilder.CreateGraph(_settings, glossaryPath, requireImportedGlossaries: true);
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
        // Register external OpenAPI watchers before PDF generation and page rendering.
        RebuildDependencyIndex(graph);
        _siteMediaProviders = GetSiteMediaProviders();
        graph.Visit(new LocalAssetsCopy(_rootPath, _outputPath));
        var rendererVisitor = new MarkdigVisitor(
            _rootPath,
            _outputPath,
            _renderer,
            _settings.EditPageRoot,
            _searchIndexBuilder,
            _navigationRoots,
            _siteMediaProviders,
            _gitDates.Read(_rootPath));
        rendererVisitor.PrepareSearchIndex();
        SchedulePdfs(reuseUnchanged: false);
        // Keep the last usable generation available until the replacement is prepared successfully.
        _pendingPages.Clear();
        _failedPendingPages.Clear();
        _pendingOutputs.Clear();
        _unfinishedPages.Clear();
        _rebuildAfterFailure = false;
        _pendingVisitor = rendererVisitor;
        foreach (var (pagePath, page) in _pages)
        {
            _pendingPages[pagePath] = page;
            _pendingOutputs[GetPageOutputPath(page)] = page;
            var alias = GetAliasOutputPath(page);
            if (alias is not null) _pendingOutputs[alias] = page;
            var specificationPages = rendererVisitor.GetPreparedSpecificationPagePaths(page);
            _openApiSpecPagesByPage[pagePath] = specificationPages;
            foreach (var path in specificationPages) _pendingOutputs[path] = page;
        }
        _searchIndexBuilder.WriteIndex(_outputPath);
        var initialPage = _pendingOutputs.GetValueOrDefault(Path.Combine(_outputPath, "index.html")) ??
            _pendingOutputs.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => entry.Value).FirstOrDefault();
        if (initialPage is not null) CompletePendingPage(initialPage);
        else FinishPendingSite();
        ReconcileGeneratedFiles(previousFiles);
        if (_pendingPages.Count > 0) Console.WriteLine("Preview ready; completing remaining pages.");
    }

    private async Task PrepareOutputAsync(string path, CancellationToken cancellationToken)
    {
        // Assets and unavailable PDFs need no rendering. Existing HTML still shares the lock with its writer.
        if (!Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase) &&
            !_pendingOutputs.ContainsKey(path) && !_pendingOutputs.ContainsKey(Path.Combine(path, "index.html")))
            return;
        await _renderLock.WaitAsync(cancellationToken);
        try
        {
            if (_unfinishedPages.Count > 0)
            {
                foreach (var (pagePath, candidate) in _pages)
                    if (_unfinishedPages.Contains(pagePath) &&
                        (PathComparer.Equals(path, GetPageOutputPath(candidate)) ||
                         PathComparer.Equals(path, GetAliasOutputPath(candidate)) ||
                         _openApiSpecPagesByPage.GetValueOrDefault(pagePath)?.Contains(path, PathComparer) == true))
                        throw new InvalidOperationException("The page update failed. Fix the source and save again.");
            }
            var page = _pendingOutputs.GetValueOrDefault(path) ??
                       _pendingOutputs.GetValueOrDefault(Path.Combine(path, "index.html"));
            if (page is not null) CompletePendingPage(page);
        }
        finally
        {
            _renderLock.Release();
        }
    }

    private void CompletePendingPage(MarkdownFile page)
    {
        var pageKey = GetPageKey(page);
        if (!_pendingPages.ContainsKey(pageKey)) return;
        var visitor = _pendingVisitor!;
        try
        {
            visitor.Process(page, page.CountDepth());
            _openApiSpecPagesByPage[pageKey] = visitor.OpenApiSpecPagePaths.GetValueOrDefault(page) ?? Array.Empty<string>();
            if (_failedPendingPages.Contains(pageKey)) _searchIndexBuilder.WriteIndex(_outputPath);
            _pendingPages.Remove(pageKey);
            _failedPendingPages.Remove(pageKey);
        }
        catch
        {
            _failedPendingPages.Add(pageKey);
            visitor.InvalidatePreparedPage(page);
            throw;
        }
        FinishPendingSite();
    }

    private void FinishPendingSite()
    {
        if (_pendingPages.Count != 0 || _rebuildAfterFailure) return;
        _pendingVisitor = null;
        _pendingOutputs.Clear();
        if (_pendingPdfs.Count != 0 || !_siteCompletionPending) return;
        _siteCompletionPending = false;
        _server?.SetBuilding(false);
        Console.WriteLine("Site updated.");
    }

    private void SchedulePdfs(bool reuseUnchanged)
    {
        // All rendering is serialized. Dropping these sessions prevents an older save from publishing a PDF.
        _pendingPdfs.Clear();
        _pdfFailed = !TryWriteSitePdf(() => _pdfCommand.WriteSitePdfIfLinked(
            _rootPath, Path.Combine(_outputPath, "documentation.pdf"), _navigationRoots, reuseUnchanged,
            (path, sections) => _pendingPdfs.Enqueue(_pdfCommand.CreateSitePdfJob(path, sections))));
        _pdfRefreshRequired = false;
        if (_pdfFailed) ReportPdfFailure();
        if (_pendingPdfs.Count > 0)
        {
            _siteCompletionPending = true;
            _server?.SetBuilding(true);
        }
        UpdatePdfStatus();
    }

    private void InvalidatePdfs()
    {
        _pendingPdfs.Clear();
        _pdfRefreshRequired = _pdfCommand.GeneratedSitePdfPaths.Count > 0;
        if (_pdfCommand.GeneratedSitePdfPaths.Count > 0)
            _server?.SetDownloads(_pdfCommand.GeneratedSitePdfPaths, "pending");
    }

    private void UpdatePdfStatus() => _server?.SetDownloads(_pdfCommand.GeneratedSitePdfPaths,
        _pendingPdfs.Count > 0 ? "pending" : _pdfFailed ? "failed" :
        _pdfCommand.GeneratedSitePdfPaths.Count > 0 ? "ready" : "none");

    private void RebuildDependencyIndex(IEnumerable<MarkdownFile> graph)
    {
        var index = DependencyIndex.CreateEmpty();

        foreach (var page in Flatten(graph))
        {
            var pagePath = GetPageKey(page);
            index.Pages[pagePath] = page;
        }

        foreach (var (pagePath, page) in index.Pages)
        {
            AddPageDependencies(index, pagePath, ReadPageDependencies(page));
            // Keep the specification pages last written for a page until rendering records the new ones.
            if (_openApiSpecPagesByPage.TryGetValue(pagePath, out var specPages))
                index.OpenApiSpecPagesByPage[pagePath] = specPages;
        }

        // Swap the index only after every page was read, so a failed read keeps the previous index intact.
        _watchers?.UpdateExternalFiles(index.OpenApiSpecsByPage.Values.SelectMany(paths => paths), _glossaryPath);
        _dependencyIndex = index;
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

    private void UpdatePageAssets(string pagePath, PageDependencies dependencies)
    {
        RemovePageAssets(pagePath);
        AddPageDependencies(_dependencyIndex, pagePath, dependencies);
    }

    private PageDependencies ReadPageDependencies(MarkdownFile page)
    {
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
        }

        var body = RemoveFrontMatter(markdown, page.FrontMatter);
        var referencedSpecs = OpenApiMarkdownRenderer.FindReferencedSpecPaths(body)
            .Select(specPath => OpenApiMarkdownRenderer.ResolveSpecPath(
                page.FileInfo.FullName,
                specPath,
                page.SourceRootPath))
            .ToArray();

        var pdfTitle = PdfSelection.GetPdfTitle(_rootPath, page, body, _renderer.ExcludesTitleHeadingFromPdf);
        return new PageDependencies(pageAssets, referencedSpecs, pdfTitle, _renderer.FindMediaProviders(body));
    }

    private IReadOnlyCollection<string> GetSiteMediaProviders()
    {
        var providers = _dependencyIndex.MediaProvidersByPage.Values.SelectMany(values => values).ToHashSet(StringComparer.Ordinal);
        return new[] { "YouTube", "Vimeo" }.Where(providers.Contains).ToArray();
    }

    private static void AddPageDependencies(
        DependencyIndex index,
        string pagePath,
        PageDependencies dependencies)
    {
        index.AssetsByPage[pagePath] = dependencies.Assets;
        foreach (var assetPath in dependencies.Assets)
        {
            if (!index.PagesByAsset.TryGetValue(assetPath, out var dependentPages))
            {
                dependentPages = new HashSet<string>(PathComparer);
                index.PagesByAsset.Add(assetPath, dependentPages);
            }

            dependentPages.Add(pagePath);
        }

        index.OpenApiSpecsByPage[pagePath] = dependencies.OpenApiSpecs;
        index.PdfTitlesByPage[pagePath] = dependencies.PdfTitle;
        index.MediaProvidersByPage[pagePath] = dependencies.MediaProviders;
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
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!changes.TryPeek(out _))
            {
                var completedPage = false;
                await _renderLock.WaitAsync(cancellationToken);
                try
                {
                    if (_pendingPages.FirstOrDefault(entry => !_failedPendingPages.Contains(entry.Key)).Value is { } pendingPage)
                    {
                        CompletePendingPage(pendingPage);
                        completedPage = true;
                    }
                    else if (_pendingPdfs.Count > 0)
                    {
                        var job = _pendingPdfs.Peek();
                        if (_pdfCommand.AdvanceSitePdfJob(job))
                        {
                            _pendingPdfs.Dequeue();
                            _pdfFailed |= job.Failed;
                            UpdatePdfStatus();
                            FinishPendingSite();
                        }
                        completedPage = true;
                    }
                    else FinishPendingSite();
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"Could not update the site: {exception.Message}");
                    completedPage = true;
                }
                finally
                {
                    _renderLock.Release();
                }
                if (completedPage)
                {
                    await Task.Yield();
                    continue;
                }
                if (!await changes.WaitToReadAsync(cancellationToken)) return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
            var batch = new List<FileChange>();
            while (changes.TryRead(out var change)) batch.Add(change);
            // Structural saves often rename the old file before creating its replacement.
            // Give those events more time to settle without delaying ordinary content edits.
            if (batch.Any(change => change.Kind is (FileChangeKind.Deleted or FileChangeKind.Renamed) &&
                                    (_pages.ContainsKey(change.Path) && !File.Exists(change.Path) ||
                                     change.OldPath is not null && _pages.ContainsKey(change.OldPath) && !File.Exists(change.OldPath))))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(75), cancellationToken);
                while (changes.TryRead(out var change)) batch.Add(change);
            }

            await _renderLock.WaitAsync(cancellationToken);
            try
            {
                ProcessChangeBatch(batch, server);
            }
            catch (Exception exception)
            {
                // A partial batch can leave metadata/dependencies unfinished. Rebuild from fresh sources on the next save.
                _rebuildAfterFailure = true;
                _siteCompletionPending = true;
                server.SetBuilding(true);
                Console.Error.WriteLine($"Could not update the site: {exception.Message}");
                if (_pdfRefreshRequired)
                {
                    _pdfFailed = true;
                    UpdatePdfStatus();
                    FinishPendingSite();
                }
            }
            finally
            {
                _renderLock.Release();
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

        var changedPaths = relevantChanges
            .SelectMany(change => change.OldPath is null ? new[] { change.Path } : new[] { change.Path, change.OldPath })
            .ToHashSet(PathComparer);
        var specificationPages = _openApiSpecsByPage
            .Where(entry => entry.Value.Any(changedPaths.Contains))
            .Select(entry => entry.Key);
        var changedPages = relevantChanges
            .Where(change => change.Kind is FileChangeKind.Changed or FileChangeKind.Created or FileChangeKind.Renamed &&
                             _pages.ContainsKey(change.Path) && File.Exists(change.Path))
            .Select(change => change.Path)
            .Concat(specificationPages)
            .Distinct(PathComparer)
            .ToArray();

        // Invalidate the complete batch before any parsing can fail, including configuration-triggered rebuilds.
        foreach (var pagePath in changedPages)
        {
            var page = _pages[pagePath];
            _unfinishedPages.Add(pagePath);
            page.InvalidateMarkdown();
            _pendingVisitor?.InvalidatePreparedPage(page);
            _pendingPages.Remove(pagePath);
            _failedPendingPages.Remove(pagePath);
        }
        if (changedPages.Length > 0) InvalidatePdfs();

        var siteConfigurationPath = Path.Combine(_rootPath, "mdu.yml");
        if (_rebuildAfterFailure || relevantChanges.Any(change => PathComparer.Equals(change.Path, siteConfigurationPath) ||
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

        var defaultIconsPath = Path.Combine(_rootPath, SiteConfigurationReader.DefaultIconsFolder);
        if (relevantChanges.Any(change => IsIconsChange(change.Path, defaultIconsPath) ||
                                          change.OldPath is not null && IsIconsChange(change.OldPath, defaultIconsPath)))
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
            var dependencies = ReadPageDependencies(page);
            // Pages that already import a repository are handled above, so a set source means a newly added import.
            if (page.ImportSource is not null ||
                !previousTitle.Equals(page.Title, StringComparison.Ordinal) ||
                !previousNavigationTitle.Equals(page.NavigationTitle, StringComparison.Ordinal) ||
                previousNavigationOrder != page.NavigationOrder ||
                previousNavigationVisible != page.NavigationVisible ||
                previousNavigationExpanded != page.NavigationExpanded ||
                previousNavigationPlacement != page.NavigationPlacement ||
                previousInvalidNavigationPlacement != page.InvalidNavigationPlacement ||
                !_pdfTitlesByPage.TryGetValue(pagePath, out var previousPdfTitle) ||
                !previousPdfTitle.Equals(dependencies.PdfTitle, StringComparison.Ordinal))
            {
                RebuildSite();
                server.Publish(reloadAll: true);
                return;
            }

            var previousSpecPages = _openApiSpecPagesByPage.GetValueOrDefault(pagePath) ?? Array.Empty<string>();
            UpdatePageAssets(pagePath, dependencies);
            var providers = GetSiteMediaProviders();
            if (!new HashSet<string>(_siteMediaProviders, StringComparer.Ordinal).SetEquals(providers))
            {
                RebuildSite();
                server.Publish(reloadAll: true);
                return;
            }
            _watchers?.UpdateExternalFiles(_openApiSpecsByPage.Values.SelectMany(paths => paths), _glossaryPath);
            new LocalAssetsCopy(_rootPath, _outputPath).Process(page, depth);
            var rendererVisitor = new MarkdigVisitor(
                _rootPath,
                _outputPath,
                _renderer,
                _settings.EditPageRoot,
                _searchIndexBuilder,
                _navigationRoots,
                _siteMediaProviders,
                _gitDates.Read(_rootPath));

            rendererVisitor.Process(page, depth);
            var specPages = rendererVisitor.OpenApiSpecPagePaths.GetValueOrDefault(page) ?? Array.Empty<string>();
            _openApiSpecPagesByPage[pagePath] = specPages;
            var specPagesChanged = !previousSpecPages.SequenceEqual(specPages, PathComparer);
            affectedPageUrls.Add(PreviewServer.ToUrlPath(_outputPath, GetPageOutputPath(page)));
            foreach (var specPage in previousSpecPages.Concat(specPages).Distinct(PathComparer))
                affectedPageUrls.Add(PreviewServer.ToUrlPath(_outputPath, specPage));

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

                    // Only pages that wrote specification pages have names that can shift.
                    var previousSiblingSpecPages = _openApiSpecPagesByPage.GetValueOrDefault(siblingKey) ?? Array.Empty<string>();
                    if (previousSiblingSpecPages.Count == 0) continue;

                    _pendingVisitor?.InvalidatePreparedPage(siblingPage);
                    rendererVisitor.Process(siblingPage, siblingPage.CountDepth());
                    _pendingPages.Remove(siblingKey);
                    _failedPendingPages.Remove(siblingKey);
                    var siblingSpecPages = rendererVisitor.OpenApiSpecPagePaths.GetValueOrDefault(siblingPage) ?? Array.Empty<string>();
                    _openApiSpecPagesByPage[siblingKey] = siblingSpecPages;
                    if (!previousSiblingSpecPages.SequenceEqual(siblingSpecPages, PathComparer))
                        affectedPageUrls.Add(PreviewServer.ToUrlPath(_outputPath, siblingOutputPath));
                }
            }
        }

        if (changedPages.Length > 0)
        {
            _searchIndexBuilder.WriteIndex(_outputPath);
            SchedulePdfs(reuseUnchanged: true);
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
        _unfinishedPages.ExceptWith(changedPages);
        FinishPendingSite();
    }

    // An invalid PDF root throws instead of returning an error code.
    private static bool TryWriteSitePdf(Func<int> write)
    {
        try
        {
            return write() == 0;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine($"Could not write PDF: {exception.Message}");
            return false;
        }
    }

    // While serving, a PDF error must not block page updates, because a page is often briefly invalid while it is edited.
    private static void ReportPdfFailure() =>
        Console.Error.WriteLine("Could not render the documentation PDF. Pages are still updated; the PDF is available again once the error is fixed.");

    private bool ChangesPageSet(string path) =>
        _pages.ContainsKey(path) ? !File.Exists(path) : File.Exists(path) && IsMarkdownCandidate(path);

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
        currentFiles.UnionWith(_pdfCommand.GeneratedSitePdfPaths);
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

    // The default folder counts even before it exists, so that creating it adds the icons.
    private bool IsIconsChange(string path, string defaultIconsPath) =>
        PathComparer.Equals(path, defaultIconsPath) || IsInsideDirectory(defaultIconsPath, path) ||
        _iconsPath is not null && (PathComparer.Equals(path, _iconsPath) || IsInsideDirectory(_iconsPath, path));

    private bool IsGeneratedOutputPath(string path) => IsInsideDirectory(_outputPath, path);

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

    private sealed record PageDependencies(
        HashSet<string> Assets,
        IReadOnlyList<string> OpenApiSpecs,
        string PdfTitle,
        IReadOnlyCollection<string> MediaProviders);

    private sealed record DependencyIndex(
        Dictionary<string, MarkdownFile> Pages,
        Dictionary<string, HashSet<string>> AssetsByPage,
        Dictionary<string, HashSet<string>> PagesByAsset,
        Dictionary<string, IReadOnlyList<string>> OpenApiSpecsByPage,
        Dictionary<string, IReadOnlyList<string>> OpenApiSpecPagesByPage,
        Dictionary<string, string> PdfTitlesByPage,
        Dictionary<string, IReadOnlyCollection<string>> MediaProvidersByPage)
    {
        public static DependencyIndex CreateEmpty() => new(
            new Dictionary<string, MarkdownFile>(PathComparer),
            new Dictionary<string, HashSet<string>>(PathComparer),
            new Dictionary<string, HashSet<string>>(PathComparer),
            new Dictionary<string, IReadOnlyList<string>>(PathComparer),
            new Dictionary<string, IReadOnlyList<string>>(PathComparer),
            new Dictionary<string, string>(PathComparer),
            new Dictionary<string, IReadOnlyCollection<string>>(PathComparer));
    }

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
            // External watchers can see the output folder too, for example a specification next to it.
            if (IsInsideDirectory(_outputPath, change.Path) ||
                change.OldPath is not null && IsInsideDirectory(_outputPath, change.OldPath)) return;

            _writer.TryWrite(change);
        }

    }
}
