using System.Text.RegularExpressions;
using Markdown.Document;
using Mdu.Model;

namespace Mdu.Service.Visitors;

public class LocalAssetsCopy : BaseVisitor
{
    private static readonly Regex SchemePattern = new(@"^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.Compiled);

    public LocalAssetsCopy(string rootPath, string outPath) : base(rootPath, outPath)
    {
    }

    public static IEnumerable<string> GetLinkedAssetPaths(string markdown, string inputDirectoryPath) =>
        MarkdownDocument.ReadLinks(markdown)
            .Select(link => GetLocalAssetPath(link.Destination, inputDirectoryPath))
            .Where(path => path is not null)
            .Select(path => path!)
            .Distinct(StringComparer.Ordinal);

    protected override bool ProcessFile(MarkdownFile entity, int graphDepth, FilePathInfo io)
    {
        var fileContent = entity.ReadMarkdown();

        var assetPaths = GetLinkedAssetPaths(fileContent, io.InputFullDirectoryPath).ToArray();
        if (assetPaths.Length > 0)
        {
            Console.WriteLine($"Found {assetPaths.Length} Assets in {io.InputFullFilePath}");
            var pending = new Queue<string>(assetPaths);
            var visited = new HashSet<string>(assetPaths, StringComparer.Ordinal);
            while (pending.TryDequeue(out var fullPath))
            {
                var assetRootPath = entity.SourceRootPath ?? io.RootDirectoryPath;
                if (!IsInsideDirectory(assetRootPath, fullPath)) continue;

                var fileInfo = new FileInfo(fullPath);
                if (!fileInfo.Exists)
                {
                    Console.WriteLine($"Missing File: {fullPath}");
                    continue;
                }
                if (fileInfo.LinkTarget is not null)
                {
                    Console.WriteLine($"Skipping symbolic link asset: {fullPath}");
                    continue;
                }
                
                var outputAssetPath = Path.GetFullPath(Path.Combine(
                    io.OutputFullDirectoryPath,
                    Path.GetRelativePath(io.InputFullDirectoryPath, fullPath)));
                if (!IsInsideDirectory(io.OutputRootDirectoryPath, outputAssetPath)) continue;
                var relativeOutputAssetPath = Path.GetRelativePath(io.OutputRootDirectoryPath, outputAssetPath);
                var assetIO = FilePathInfo.CreateRenderFileInfo(
                    io.RootDirectoryPath,
                    io.OutputRootDirectoryPath,
                    fileInfo,
                    siteRelativePath: relativeOutputAssetPath);
                assetIO.CopyFile();
                Console.WriteLine($"Copied: {assetIO.InputRelativeFilePathToRootDirectory}");

                // An embedded HTML page, such as an iframe demo, needs the scripts, styles and images it loads.
                foreach (var referencedPath in GetHtmlAssetPaths(fileInfo))
                {
                    if (visited.Add(referencedPath)) pending.Enqueue(referencedPath);
                }
            }
        }
        return true;
    }

    private static IEnumerable<string> GetHtmlAssetPaths(FileInfo fileInfo)
    {
        if (!fileInfo.Extension.Equals(".html", StringComparison.OrdinalIgnoreCase) &&
            !fileInfo.Extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
            return [];

        return MarkdownDocument.ReadHtmlUrls(File.ReadAllText(fileInfo.FullName))
            .Select(url => GetLocalAssetPath(url, fileInfo.DirectoryName!))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string? GetLocalAssetPath(string destination, string inputDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(destination) || destination.StartsWith('#') ||
            destination.StartsWith("//", StringComparison.Ordinal) || SchemePattern.IsMatch(destination))
            return null;

        var suffixStart = destination.IndexOfAny(['?', '#']);
        var relativePath = suffixStart < 0 ? destination : destination[..suffixStart];
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        try
        {
            relativePath = Uri.UnescapeDataString(relativePath);
            if (Path.IsPathRooted(relativePath) ||
                Path.GetExtension(relativePath).Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(relativePath).Equals(".markdown", StringComparison.OrdinalIgnoreCase))
                return null;

            return Path.GetFullPath(relativePath, inputDirectoryPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or UriFormatException)
        {
            return null;
        }
    }

    private static bool IsInsideDirectory(string directory, string path)
    {
        var relativePath = Path.GetRelativePath(directory, path);
        return relativePath == "." ||
               !Path.IsPathRooted(relativePath) &&
               relativePath != ".." &&
               !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}
