using Core.Collections.NodeGraph.Extensions;
using Markdown.Linter;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;
using Mdu.Commands.Settings;
using Mdu.Model;
using Mdu.Service;
using Mdu.Service.Visitors;

namespace Mdu.Commands;

public class FixCommand
{
    private static readonly Regex FrontMatterBlock = new(
        @"\A---[ \t]*\r?\n(?<yaml>[\s\S]*?)\r?\n---[ \t]*(?:\r?\n|$)",
        RegexOptions.Compiled);
    private readonly GraphBuilder _graphBuilder;
    private readonly LinterVisitor _linterVisitor;

    public FixCommand(GraphBuilder graphBuilder, LinterVisitor linterVisitor)
    {
        _graphBuilder = graphBuilder;
        _linterVisitor = linterVisitor;
    }

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

        var pathComparer = MarkdownLinks.PathComparer;
        var migrationChanges = MigrateMkDocsContent(rootPath, settings.IncludeHidden, pathComparer);
        if (migrationChanges < 0) return 1;
        var glossaryMigrationChanges = MigrateMkDocsGlossary(rootPath);
        migrationChanges += glossaryMigrationChanges;

        var graph = _graphBuilder.CreateGraph(new VisitorSettings
        {
            RootPath = rootPath,
            SearchPattern = settings.SearchPattern,
            IncludeHidden = settings.IncludeHidden
        }, SiteConfigurationReader.TryResolveGlossaryPath(rootPath));
        _linterVisitor.Prepare(graph, rootPath);
        graph.Visit(_linterVisitor);

        var moves = _linterVisitor.ErrorReceiver.Diagnostics
            .Where(diagnostic => diagnostic.IsWarning &&
                                 diagnostic.Message.StartsWith($"{RuleCodes.ReadmeWithoutSubpages}:", StringComparison.Ordinal))
            .Select(diagnostic => CreateMove(Path.GetFullPath(diagnostic.FilePath)))
            .ToArray();

        if (moves.Length == 0)
        {
            Console.WriteLine(migrationChanges == 0
                ? "No fixable issues found."
                : $"Applied {migrationChanges} MkDocs compatibility and structure fix{(migrationChanges == 1 ? "" : "es")}.");
            return 0;
        }

        var movedPaths = moves.ToDictionary(move => move.SourcePath, move => move.TargetPath, pathComparer);
        var duplicateTargets = moves
            .GroupBy(move => move.TargetPath, pathComparer)
            .FirstOrDefault(group => group.Count() > 1);
        var existingTarget = moves.FirstOrDefault(move =>
            File.Exists(move.TargetPath) && !pathComparer.Equals(move.SourcePath, move.TargetPath));
        if (duplicateTargets is not null || existingTarget is not null)
        {
            var target = duplicateTargets?.Key ?? existingTarget!.TargetPath;
            Console.Error.WriteLine($"Cannot fix MDU006 because the target already exists: {target}");
            return 1;
        }

        var enumerationOptions = new EnumerationOptions
        {
            AttributesToSkip = settings.IncludeHidden
                ? FileAttributes.System
                : FileAttributes.Hidden | FileAttributes.System,
            RecurseSubdirectories = true
        };
        var glossarySourcePaths = GetGlossarySourcePaths(rootPath).ToHashSet(pathComparer);
        var markdownFiles = Directory.EnumerateFiles(rootPath, "*", enumerationOptions)
            .Where(MarkdownLinks.IsMarkdownFile)
            .Where(path => !glossarySourcePaths.Contains(Path.GetFullPath(path)))
            .ToArray();
        var changedDocuments = new Dictionary<string, string>(pathComparer);
        var updatedLinkCount = 0;

        foreach (var markdownFile in markdownFiles)
        {
            var sourcePath = Path.GetFullPath(markdownFile);
            var newSourcePath = movedPaths.GetValueOrDefault(sourcePath, sourcePath);
            var content = File.ReadAllText(sourcePath);
            var updatedContent = MarkdownLinks.Rewrite(content, sourcePath, newSourcePath, movedPaths, pathComparer, out var changedLinkCount);
            if (updatedContent != content)
            {
                changedDocuments[sourcePath] = updatedContent;
                updatedLinkCount += changedLinkCount;
            }
        }

        foreach (var move in moves)
            File.Move(move.SourcePath, move.TargetPath);

        foreach (var (sourcePath, content) in changedDocuments)
        {
            var targetPath = movedPaths.GetValueOrDefault(sourcePath, sourcePath);
            File.WriteAllText(targetPath, content);
        }

        foreach (var move in moves)
        {
            var sectionDirectory = Path.GetDirectoryName(move.SourcePath)!;
            if (Directory.Exists(sectionDirectory) && !Directory.EnumerateFileSystemEntries(sectionDirectory).Any())
                Directory.Delete(sectionDirectory);

            Console.WriteLine($"Fixed MDU006: {Path.GetRelativePath(rootPath, move.SourcePath)} -> {Path.GetRelativePath(rootPath, move.TargetPath)}");
        }

        Console.WriteLine($"Updated {updatedLinkCount} Markdown link{(updatedLinkCount == 1 ? "" : "s")}.");
        Console.WriteLine($"Applied {migrationChanges} MkDocs compatibility and structure fix{(migrationChanges == 1 ? "" : "es")}.");
        return 0;
    }

    private static int MigrateMkDocsContent(string rootPath, bool includeHidden, StringComparer pathComparer)
    {
        var enumerationOptions = new EnumerationOptions
        {
            AttributesToSkip = includeHidden ? FileAttributes.System : FileAttributes.Hidden | FileAttributes.System,
            RecurseSubdirectories = true
        };
        var glossarySourcePaths = GetGlossarySourcePaths(rootPath).ToHashSet(pathComparer);
        var markdownFiles = Directory.EnumerateFiles(rootPath, "*", enumerationOptions)
            .Where(MarkdownLinks.IsMarkdownFile)
            .Select(Path.GetFullPath)
            .Where(path => !glossarySourcePaths.Contains(path))
            .ToArray();
        var indexMoves = markdownFiles
            .Where(path => Path.GetFileName(path).Equals("index.md", StringComparison.OrdinalIgnoreCase))
            .Select(path => new PageMove(path, Path.Combine(Path.GetDirectoryName(path)!, "README.md")))
            .ToArray();
        var duplicateTarget = indexMoves.FirstOrDefault(move => File.Exists(move.TargetPath));
        if (duplicateTarget is not null)
        {
            Console.Error.WriteLine($"Cannot rename '{duplicateTarget.SourcePath}' because '{duplicateTarget.TargetPath}' already exists.");
            return -1;
        }
        var duplicateMoveTarget = indexMoves.GroupBy(move => move.TargetPath, pathComparer).FirstOrDefault(group => group.Count() > 1);
        if (duplicateMoveTarget is not null)
        {
            Console.Error.WriteLine($"Cannot rename multiple index pages to '{duplicateMoveTarget.Key}'.");
            return -1;
        }

        var movedPaths = indexMoves.ToDictionary(move => move.SourcePath, move => move.TargetPath, pathComparer);
        var updated = new Dictionary<string, string>(pathComparer);
        var changes = 0;
        foreach (var path in markdownFiles)
        {
            var content = File.ReadAllText(path);
            var fixedContent = MarkdownCompatibility.FixMarkdown(content);
            var targetPath = movedPaths.GetValueOrDefault(path, path);
            if (fixedContent != content || !pathComparer.Equals(path, targetPath))
            {
                updated[targetPath] = fixedContent;
                if (fixedContent != content) changes++;
            }
        }

        var pagesFiles = Directory.EnumerateFiles(rootPath, ".pages", new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.System,
            RecurseSubdirectories = true
        }).ToArray();
        Dictionary<string, Dictionary<string, string>> pageSettings;
        try
        {
            pageSettings = ReadPagesSettings(pagesFiles, rootPath);
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return -1;
        }

        try
        {
            CreateMissingSectionReadmes(rootPath, enumerationOptions, movedPaths, updated, pathComparer, ref changes);
            foreach (var (path, settings) in pageSettings)
            {
                var sourcePath = movedPaths.FirstOrDefault(move => pathComparer.Equals(move.Value, path)).Key ?? path;
                if (!updated.TryGetValue(path, out var content))
                {
                    if (!File.Exists(sourcePath)) continue;
                    content = File.ReadAllText(sourcePath);
                }

                foreach (var (key, value) in settings)
                    content = SetFrontMatterValue(content, key, value, path);
                updated[path] = content;
                changes++;
            }

            WriteStaticBlogAndTags(rootPath, updated, movedPaths, pathComparer, ref changes);
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return -1;
        }

        var changedLinkCount = 0;
        foreach (var path in markdownFiles)
        {
            var sourcePath = Path.GetFullPath(path);
            var newSourcePath = movedPaths.GetValueOrDefault(sourcePath, sourcePath);
            var content = updated.GetValueOrDefault(newSourcePath) ?? File.ReadAllText(sourcePath);
            var rewritten = MarkdownLinks.Rewrite(content, sourcePath, newSourcePath, movedPaths, pathComparer, out var linksChanged);
            if (rewritten != content)
            {
                updated[newSourcePath] = rewritten;
                changedLinkCount += linksChanged;
            }
        }

        foreach (var move in indexMoves) changes++;

        // All Markdown and YAML transformations are prepared before the first filesystem mutation.
        foreach (var move in indexMoves)
            File.Move(move.SourcePath, move.TargetPath);

        foreach (var (targetPath, content) in updated)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, content);
        }

        foreach (var pagesFile in pagesFiles)
        {
            File.Delete(pagesFile);
            changes++;
        }

        if (changedLinkCount > 0)
        {
            changes++;
            Console.WriteLine($"Updated {changedLinkCount} Markdown link{(changedLinkCount == 1 ? "" : "s")} during migration.");
        }
        return changes;
    }

    private static int MigrateMkDocsGlossary(string rootPath)
    {
        var source = FindMkDocsGlossary(rootPath, out var diagnostic, out _);
        if (diagnostic is not null)
        {
            Console.Error.WriteLine(diagnostic);
            return 0;
        }
        if (source is null) return 0;
        if (!Path.GetExtension(source.Path).Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Could not migrate MkDocs glossary: '{source.Path}' is not a Markdown file.");
            return 0;
        }
        if (!File.Exists(source.Path))
        {
            Console.Error.WriteLine($"Could not migrate MkDocs glossary: '{source.Path}' does not exist.");
            return 0;
        }

        var mduPath = Path.Combine(rootPath, "mdu.yml");
        var contents = File.Exists(mduPath) ? File.ReadAllText(mduPath) : string.Empty;
        var hasFlowMapping = false;
        if (contents.Length > 0)
        {
            var yaml = new YamlStream();
            try
            {
                yaml.Load(new StringReader(contents));
            }
            catch (YamlDotNet.Core.YamlException exception)
            {
                Console.Error.WriteLine($"Could not migrate MkDocs glossary because mdu.yml is invalid: {exception.Message}");
                return 0;
            }

            var root = yaml.Documents.FirstOrDefault()?.RootNode;
            if (root is YamlMappingNode mapping)
            {
                if (mapping.Children.ContainsKey(new YamlScalarNode("glossary"))) return 0;
                if (mapping.Style == YamlDotNet.Core.Events.MappingStyle.Flow)
                    hasFlowMapping = true;
            }
            else if (root is not null && root is not YamlScalarNode { Value: null or "" })
            {
                Console.Error.WriteLine("Could not migrate MkDocs glossary because mdu.yml must contain a YAML mapping.");
                return 0;
            }
        }

        string relativePath;
        try
        {
            relativePath = Path.GetRelativePath(rootPath, source.Path)
                .Replace(Path.DirectorySeparatorChar, '/');
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"Could not migrate MkDocs glossary because it is on a different volume: {exception.Message}");
            return 0;
        }
        var newline = contents.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var glossaryEntry = $"glossary: '{relativePath.Replace("'", "''", StringComparison.Ordinal)}'";
        if (hasFlowMapping)
        {
            var openingBrace = contents.IndexOf('{');
            var closingBrace = contents.LastIndexOf('}');
            if (openingBrace < 0 || closingBrace <= openingBrace ||
                contents.AsSpan(openingBrace, closingBrace - openingBrace).Contains('\n') ||
                contents.AsSpan(openingBrace, closingBrace - openingBrace).Contains('\r'))
            {
                Console.Error.WriteLine("Could not migrate MkDocs glossary because mdu.yml uses an unsupported multi-line flow mapping.");
                return 0;
            }

            var beforeClosingBrace = contents[..closingBrace].TrimEnd();
            var separator = beforeClosingBrace.EndsWith('{') ? "" : beforeClosingBrace.EndsWith(',') ? " " : ", ";
            contents = contents.Insert(closingBrace, separator + glossaryEntry);
        }
        else
        {
            if (contents.Length > 0 && !contents.EndsWith('\n')) contents += newline;
            contents += glossaryEntry + newline;
        }
        try
        {
            var migratedYaml = new YamlStream();
            migratedYaml.Load(new StringReader(contents));
            var migratedRoot = migratedYaml.Documents.FirstOrDefault()?.RootNode;
            if (migratedRoot is not YamlMappingNode migratedMapping ||
                !migratedMapping.Children.TryGetValue(new YamlScalarNode("glossary"), out var migratedGlossary) ||
                migratedGlossary is not YamlScalarNode { Value: var migratedGlossaryPath } ||
                migratedGlossaryPath != relativePath)
            {
                Console.Error.WriteLine("Could not migrate MkDocs glossary because the glossary entry could not be verified in mdu.yml.");
                return 0;
            }
        }
        catch (YamlDotNet.Core.YamlException exception)
        {
            Console.Error.WriteLine($"Could not migrate MkDocs glossary because the updated mdu.yml would be invalid: {exception.Message}");
            return 0;
        }
        File.WriteAllText(mduPath, contents);
        Console.WriteLine($"Migrated MkDocs glossary configuration to mdu.yml: {relativePath}");
        return 1;
    }

    private static IReadOnlyList<string> GetGlossarySourcePaths(string rootPath)
    {
        try
        {
            var configuration = SiteConfigurationReader.ReadForContent(rootPath);
            if (configuration.Glossary is not null)
                return [Path.GetFullPath(configuration.Glossary, rootPath)];
        }
        catch (Exception exception) when (exception is IOException or YamlDotNet.Core.YamlException or InvalidOperationException or ArgumentException)
        {
        }

        FindMkDocsGlossary(rootPath, out _, out var sourcePaths);
        return sourcePaths;
    }

    private static MkDocsGlossary? FindMkDocsGlossary(
        string rootPath,
        out string? diagnostic,
        out IReadOnlyList<string> sourcePaths)
    {
        diagnostic = null;
        sourcePaths = Array.Empty<string>();
        var projectRoot = File.Exists(Path.Combine(rootPath, "mkdocs.yml"))
            ? rootPath
            : Directory.GetParent(rootPath)?.FullName;
        if (projectRoot is null) return null;
        var mkdocsPath = Path.Combine(projectRoot, "mkdocs.yml");
        if (!File.Exists(mkdocsPath)) return null;

        var yaml = new YamlStream();
        try
        {
            yaml.Load(new StringReader(File.ReadAllText(mkdocsPath)));
        }
        catch (YamlDotNet.Core.YamlException exception)
        {
            diagnostic = $"Could not migrate MkDocs glossary because {mkdocsPath} is invalid: {exception.Message}";
            return null;
        }

        var configDirectory = Path.GetDirectoryName(mkdocsPath)!;
        if (yaml.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root) return null;
        var snippets = FindMkDocsSnippets(root);
        var appendSettings = snippets
            .Where(options => options.Children.ContainsKey(new YamlScalarNode("auto_append")))
            .ToArray();
        if (appendSettings.Length == 0) return null;
        if (appendSettings.Length > 1)
        {
            diagnostic = $"Could not migrate MkDocs glossary from {mkdocsPath}: configure auto_append in only one pymdownx.snippets configuration.";
            return null;
        }

        if (!ReadSnippetBasePaths(appendSettings[0], root, configDirectory, out var basePaths, out diagnostic))
            return null;

        var appendNode = appendSettings[0].Children[new YamlScalarNode("auto_append")];
        string?[] configuredPaths;
        if (appendNode is YamlScalarNode scalar)
        {
            configuredPaths = [scalar.Value];
        }
        else if (appendNode is YamlSequenceNode sequence)
        {
            if (sequence.Children.Any(node => node is not YamlScalarNode))
            {
                diagnostic = $"Could not migrate MkDocs glossary from {mkdocsPath}: auto_append entries must be paths.";
                return null;
            }
            configuredPaths = sequence.Children.Cast<YamlScalarNode>().Select(node => node.Value).ToArray();
        }
        else
        {
            diagnostic = $"Could not migrate MkDocs glossary from {mkdocsPath}: auto_append must be a path or a list of paths.";
            return null;
        }

        if (configuredPaths.Length == 0) return null;
        if (configuredPaths.Any(string.IsNullOrWhiteSpace))
        {
            diagnostic = $"Could not migrate MkDocs glossary from {mkdocsPath}: auto_append contains an empty path.";
            return null;
        }

        var resolvedPaths = new List<string>();
        foreach (var configuredPath in configuredPaths)
        {
            try
            {
                var resolvedPath = ResolveAutoAppendPath(configuredPath!, basePaths);
                if (resolvedPath is not null) resolvedPaths.Add(resolvedPath);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                diagnostic = $"Could not migrate MkDocs glossary from {mkdocsPath}: {exception.Message}";
                return null;
            }
        }

        sourcePaths = resolvedPaths.Distinct(MarkdownLinks.PathComparer).ToArray();
        if (configuredPaths.Length > 1)
        {
            diagnostic = $"Could not migrate MkDocs glossary from {mkdocsPath}: auto_append must contain exactly one glossary path.";
            return null;
        }
        if (sourcePaths.Count == 0)
        {
            diagnostic = $"Could not migrate MkDocs glossary from {mkdocsPath}: auto_append file was not found under the configured base_path.";
            return null;
        }

        return new MkDocsGlossary(sourcePaths[0]);
    }

    private static IReadOnlyList<YamlMappingNode> FindMkDocsSnippets(YamlMappingNode root)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode("markdown_extensions"), out var extensions))
            return Array.Empty<YamlMappingNode>();

        if (extensions is YamlMappingNode mapping)
            return TryGetMapping(mapping, "pymdownx.snippets", out var mappedOptions)
                ? [mappedOptions]
                : Array.Empty<YamlMappingNode>();

        if (extensions is not YamlSequenceNode sequence) return Array.Empty<YamlMappingNode>();

        var result = new List<YamlMappingNode>();
        foreach (var extension in sequence.Children.OfType<YamlMappingNode>())
            if (TryGetMapping(extension, "pymdownx.snippets", out var options)) result.Add(options);
        return result;
    }

    private static bool TryGetMapping(YamlMappingNode mapping, string key, out YamlMappingNode value)
    {
        if (mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlMappingNode child)
        {
            value = child;
            return true;
        }
        value = null!;
        return false;
    }

    private static bool ReadSnippetBasePaths(
        YamlMappingNode snippets,
        YamlMappingNode mkdocs,
        string configDirectory,
        out string[] paths,
        out string? diagnostic)
    {
        diagnostic = null;
        if (!snippets.Children.TryGetValue(new YamlScalarNode("base_path"), out var node))
        {
            paths = [configDirectory];
            return true;
        }

        var values = node switch
        {
            YamlScalarNode scalar => new[] { scalar },
            YamlSequenceNode sequence when sequence.Children.All(child => child is YamlScalarNode) =>
                sequence.Children.Cast<YamlScalarNode>().ToArray(),
            _ => Array.Empty<YamlScalarNode>()
        };
        if (values.Length == 0 || values.Any(value => string.IsNullOrWhiteSpace(value.Value)))
        {
            paths = [];
            diagnostic = "Could not migrate MkDocs glossary: base_path must contain one or more non-empty paths.";
            return false;
        }

        try
        {
            paths = values.Select(value => ResolveSnippetBasePath(value, mkdocs, configDirectory)).ToArray();
            return true;
        }
        catch (InvalidOperationException exception)
        {
            paths = [];
            diagnostic = exception.Message;
            return false;
        }
    }

    private static string ResolveSnippetBasePath(YamlScalarNode value, YamlMappingNode mkdocs, string configDirectory)
    {
        var path = value.Value!;
        if (value.Tag == "!relative")
        {
            if (path == "$config_dir") return configDirectory;
            if (path.StartsWith("$config_dir/", StringComparison.Ordinal))
                return Path.GetFullPath(path["$config_dir/".Length..], configDirectory);

            var docsDirectory = mkdocs.Children.TryGetValue(new YamlScalarNode("docs_dir"), out var docsNode) &&
                                docsNode is YamlScalarNode docsScalar && !string.IsNullOrWhiteSpace(docsScalar.Value)
                ? docsScalar.Value
                : "docs";
            var fullDocsDirectory = Path.GetFullPath(docsDirectory!, configDirectory);
            if (path == "$docs_dir") return fullDocsDirectory;
            if (path.StartsWith("$docs_dir/", StringComparison.Ordinal))
                return Path.GetFullPath(path["$docs_dir/".Length..], fullDocsDirectory);

            throw new InvalidOperationException(
                "Could not migrate MkDocs glossary: base_path uses !relative to the current Markdown file, which cannot be mapped to one shared glossary path.");
        }

        return Path.GetFullPath(path, configDirectory);
    }

    private static string? ResolveAutoAppendPath(string configuredPath, IEnumerable<string> basePaths)
    {
        foreach (var basePath in basePaths)
        {
            if (File.Exists(basePath) &&
                Path.GetFileName(configuredPath).Equals(Path.GetFileName(basePath), StringComparison.Ordinal))
                return Path.GetFullPath(basePath);

            var path = Path.GetFullPath(configuredPath, basePath);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private sealed record MkDocsGlossary(string Path);

    private static Dictionary<string, Dictionary<string, string>> ReadPagesSettings(
        IEnumerable<string> pagesFiles,
        string rootPath)
    {
        var settings = new Dictionary<string, Dictionary<string, string>>(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
        foreach (var pagesFile in pagesFiles)
        {
            var yaml = new YamlStream();
            try
            {
                yaml.Load(new StringReader(File.ReadAllText(pagesFile)));
            }
            catch (YamlDotNet.Core.YamlException exception)
            {
                throw new InvalidOperationException($"Cannot migrate {pagesFile}: {exception.Message}");
            }

            var rootNode = yaml.Documents.FirstOrDefault()?.RootNode;
            if (rootNode is not YamlMappingNode mapping)
            {
                if (rootNode is YamlScalarNode empty && string.IsNullOrWhiteSpace(empty.Value)) continue;
                throw new InvalidOperationException($"Cannot migrate {pagesFile}: expected a YAML mapping.");
            }
            var directory = Path.GetDirectoryName(pagesFile)!;
            if (mapping.Children.TryGetValue(new YamlScalarNode("title"), out var titleNode))
            {
                if (titleNode is not YamlScalarNode title)
                    throw new InvalidOperationException($"Cannot migrate {pagesFile}: 'title' must be a string.");
                Add(Path.Combine(directory, "README.md"), "navigation.title", title.Value ?? string.Empty);
            }

            if (mapping.Children.TryGetValue(new YamlScalarNode("arrange"), out var arrangeNode))
            {
                if (arrangeNode is not YamlSequenceNode arrange)
                    throw new InvalidOperationException($"Cannot migrate {pagesFile}: 'arrange' must be a list.");
                for (var index = 0; index < arrange.Children.Count; index++)
                {
                    if (arrange.Children[index] is not YamlScalarNode entry || string.IsNullOrWhiteSpace(entry.Value))
                        throw new InvalidOperationException($"Cannot migrate {pagesFile}: 'arrange' entries must be non-empty strings.");
                    var value = entry.Value.Trim();
                    string target;
                    if (value.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    {
                        target = Path.GetFullPath(Path.Combine(directory, value));
                    }
                    else
                    {
                        var section = Path.GetFullPath(Path.Combine(directory, value));
                        target = Path.Combine(section, "README.md");
                    }

                    if (Path.GetFileName(target).Equals("index.md", StringComparison.OrdinalIgnoreCase))
                        target = Path.Combine(Path.GetDirectoryName(target)!, "README.md");
                    Add(target, "navigation.order", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }

        return settings;

        void Add(string path, string key, string value)
        {
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(Path.GetFullPath(rootPath) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
                !fullPath.Equals(Path.GetFullPath(rootPath), OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return;
            if (!settings.TryGetValue(fullPath, out var values)) settings[fullPath] = values = new Dictionary<string, string>(StringComparer.Ordinal);
            values[key] = value;
        }
    }

    private static void CreateMissingSectionReadmes(
        string rootPath,
        EnumerationOptions options,
        IReadOnlyDictionary<string, string> movedPaths,
        Dictionary<string, string> updated,
        StringComparer pathComparer,
        ref int changes)
    {
        var directOptions = new EnumerationOptions
        {
            AttributesToSkip = options.AttributesToSkip,
            RecurseSubdirectories = false
        };
        var directories = Directory.EnumerateDirectories(rootPath, "*", options).Append(rootPath)
            .OrderBy(path => path.Length).ToArray();
        foreach (var directory in directories)
        {
            var directMarkdown = Directory.EnumerateFiles(directory, "*", directOptions).Where(MarkdownLinks.IsMarkdownFile).ToArray();
            var hasMarkdownChildDirectory = Directory.EnumerateDirectories(directory, "*", directOptions).Any(ContainsMarkdown);
            if (!hasMarkdownChildDirectory && directMarkdown.Length < 2) continue;
            if (directMarkdown.Any(path => Path.GetFileName(path).Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
                                           movedPaths.Values.Any(target => pathComparer.Equals(target, Path.Combine(directory, "README.md"))))) continue;

            var readmePath = Path.Combine(directory, "README.md");
            updated[readmePath] = $"# {Path.GetFileName(directory)}{Environment.NewLine}";
            changes++;
        }

        bool ContainsMarkdown(string directory)
        {
            return Directory.EnumerateFiles(directory, "*", directOptions).Any(MarkdownLinks.IsMarkdownFile) ||
                   Directory.EnumerateDirectories(directory, "*", directOptions).Any(ContainsMarkdown);
        }
    }

    private static string SetFrontMatterValue(string content, string key, string value, string path)
    {
        YamlMappingNode mapping;
        string body;
        var match = FrontMatterBlock.Match(content);
        if (match.Success)
        {
            var yaml = new YamlStream();
            try
            {
                yaml.Load(new StringReader(match.Groups["yaml"].Value));
            }
            catch (YamlDotNet.Core.YamlException exception)
            {
                throw new InvalidOperationException($"Cannot migrate malformed Markdown front matter in '{path}': {exception.Message}");
            }

            mapping = yaml.Documents.FirstOrDefault()?.RootNode as YamlMappingNode
                ?? throw new InvalidOperationException($"Cannot migrate Markdown front matter in '{path}' because it must be a YAML mapping.");
            body = content[match.Length..];
        }
        else
        {
            if (StartsWithFrontMatter(content))
                throw new InvalidOperationException($"Cannot migrate Markdown front matter in '{path}' because its closing '---' delimiter is missing.");
            mapping = new YamlMappingNode();
            body = content;
        }

        var keyParts = key.Split('.');
        var destination = mapping;
        for (var index = 0; index < keyParts.Length - 1; index++)
        {
            var part = new YamlScalarNode(keyParts[index]);
            if (!destination.Children.TryGetValue(part, out var nested) || nested is not YamlMappingNode nestedMapping)
            {
                nestedMapping = new YamlMappingNode();
                destination.Children[part] = nestedMapping;
            }
            destination = nestedMapping;
        }

        destination.Children[new YamlScalarNode(keyParts[^1])] = new YamlScalarNode(value);
        var output = new YamlStream(new YamlDocument(mapping));
        using var writer = new StringWriter();
        output.Save(writer, assignAnchors: false);
        var serialized = writer.ToString().Trim();
        if (serialized.StartsWith("---", StringComparison.Ordinal))
            serialized = serialized[3..].TrimStart();
        if (serialized.EndsWith("...", StringComparison.Ordinal))
            serialized = serialized[..^3].TrimEnd();
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        serialized = serialized.Replace("\n", newline, StringComparison.Ordinal);
        return $"---{newline}{serialized}{newline}---{newline}{newline}{body}";
    }

    private static string ReplaceMarkdownBody(string content, string body, string path)
    {
        var match = FrontMatterBlock.Match(content);
        if (!match.Success)
        {
            if (StartsWithFrontMatter(content))
                throw new InvalidOperationException($"Cannot migrate malformed Markdown front matter in '{path}' because its closing '---' delimiter is missing.");
            return body;
        }
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return content[..match.Length].TrimEnd('\r', '\n') + newline + newline + body;
    }

    private static void WriteStaticBlogAndTags(
        string rootPath,
        Dictionary<string, string> updated,
        IReadOnlyDictionary<string, string> movedPaths,
        StringComparer pathComparer,
        ref int changes)
    {
        var blogDirectory = Path.Combine(rootPath, "blog");
        var blogIndex = Path.Combine(blogDirectory, "README.md");
        if (File.Exists(blogIndex) || updated.ContainsKey(blogIndex))
        {
            var existing = updated.GetValueOrDefault(blogIndex) ??
                           (movedPaths.FirstOrDefault(move => pathComparer.Equals(move.Value, blogIndex)).Key is { } oldBlogIndex
                               ? File.ReadAllText(oldBlogIndex)
                               : File.ReadAllText(blogIndex));
            var body = RemoveSimpleFrontMatter(existing).Trim();
            if (Regex.IsMatch(body, @"^# Blog\s*$", RegexOptions.IgnoreCase))
            {
                var posts = Directory.Exists(Path.Combine(blogDirectory, "posts"))
                    ? Directory.EnumerateFiles(Path.Combine(blogDirectory, "posts"), "*.md")
                        .Where(path => !Path.GetFileName(path).Equals("README.md", StringComparison.OrdinalIgnoreCase))
                        .Select(path =>
                        {
                            var fullPath = Path.GetFullPath(path);
                            return (Path: path, TargetPath: movedPaths.GetValueOrDefault(fullPath, fullPath), Metadata: ReadMetadata(path));
                        })
                        .OrderByDescending(item => item.Metadata.GetValueOrDefault("date"), StringComparer.Ordinal)
                        .ToArray()
                    : Array.Empty<(string Path, string TargetPath, Dictionary<string, string> Metadata)>();
                var lines = posts.Select(post => $"- [{post.Metadata.GetValueOrDefault("title", Path.GetFileNameWithoutExtension(post.Path))}]({EncodeRelativePath(Path.GetRelativePath(blogDirectory, post.TargetPath))})" +
                                                  (post.Metadata.TryGetValue("date", out var date) ? $" — {date}" : string.Empty));
                var generatedBody = "# Blog" + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines) + Environment.NewLine;
                updated[blogIndex] = ReplaceMarkdownBody(existing, generatedBody, blogIndex);
                changes++;
            }
        }

        var tagsPath = Path.Combine(rootPath, "Tags.md");
        if (File.Exists(tagsPath))
        {
            var existing = updated.GetValueOrDefault(tagsPath) ?? File.ReadAllText(tagsPath);
            if (existing.Contains("material/tags", StringComparison.OrdinalIgnoreCase))
            {
                var tagGroups = new SortedDictionary<string, List<(string RelativePath, string Title)>>(StringComparer.OrdinalIgnoreCase);
                foreach (var page in Directory.EnumerateFiles(rootPath, "*.md", SearchOption.AllDirectories))
                {
                    var metadata = ReadMetadata(page);
                    if (metadata.TryGetValue("tags", out var tags))
                    {
                        var fullPath = Path.GetFullPath(page);
                        var targetPath = movedPaths.GetValueOrDefault(fullPath, fullPath);
                        foreach (var tag in tags.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            if (!tagGroups.TryGetValue(tag, out var entries)) tagGroups[tag] = entries = new List<(string, string)>();
                            entries.Add((Path.GetRelativePath(rootPath, targetPath).Replace(Path.DirectorySeparatorChar, '/'),
                                metadata.GetValueOrDefault("title", Path.GetFileNameWithoutExtension(page))));
                        }
                    }
                }

                var lines = new List<string> { "# Tags", string.Empty };
                foreach (var (tag, entries) in tagGroups)
                {
                    lines.Add($"## {tag}");
                    lines.AddRange(entries.OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                        .Select(entry => $"- [{entry.Title}]({EncodeRelativePath(entry.RelativePath)})"));
                    lines.Add(string.Empty);
                }
                updated[tagsPath] = ReplaceMarkdownBody(existing, string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine, tagsPath);
                changes++;
            }
        }
    }

    private static Dictionary<string, string> ReadMetadata(string path)
    {
        var content = File.ReadAllText(path);
        var frontMatter = FrontMatterBlock.Match(content);
        if (!frontMatter.Success) return new Dictionary<string, string>();
        var yaml = new YamlStream();
        try { yaml.Load(new StringReader(frontMatter.Groups["yaml"].Value)); }
        catch (YamlDotNet.Core.YamlException) { return new Dictionary<string, string>(); }
        if (yaml.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode mapping) return new Dictionary<string, string>();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in mapping.Children)
        {
            if (key is not YamlScalarNode name) continue;
            if (value is YamlScalarNode scalar) values[name.Value ?? string.Empty] = scalar.Value ?? string.Empty;
            else if (name.Value == "tags" && value is YamlSequenceNode sequence)
                values["tags"] = string.Join('|', sequence.Children.OfType<YamlScalarNode>().Select(item => item.Value));
        }
        return values;
    }

    private static string RemoveSimpleFrontMatter(string content)
    {
        var match = FrontMatterBlock.Match(content);
        return match.Success ? content[match.Length..] : content;
    }

    private static bool StartsWithFrontMatter(string content)
    {
        var newline = content.IndexOfAny(['\r', '\n']);
        var firstLine = newline < 0 ? content : content[..newline];
        return firstLine.Trim() == "---";
    }

    private static string EncodeRelativePath(string path) => string.Join('/',
        path.Replace(Path.DirectorySeparatorChar, '/').Split('/').Select(Uri.EscapeDataString));

    private static PageMove CreateMove(string sourcePath)
    {
        var sectionDirectory = new DirectoryInfo(Path.GetDirectoryName(sourcePath)!);
        var targetPath = Path.Combine(sectionDirectory.Parent!.FullName, $"{sectionDirectory.Name}.md");
        return new PageMove(sourcePath, targetPath);
    }

    private static IEnumerable<MarkdownFile> Flatten(IEnumerable<MarkdownFile> pages)
    {
        foreach (var page in pages)
        {
            yield return page;
            foreach (var child in Flatten(page.Children)) yield return child;
        }
    }

    private sealed record PageMove(string SourcePath, string TargetPath);
}
