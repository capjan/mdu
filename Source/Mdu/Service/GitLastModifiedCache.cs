using System.Collections;

namespace Mdu.Service;

/// <summary>One preview's commit dates, invalidated by repository metadata rather than uncommitted body edits.</summary>
public sealed class GitLastModifiedCache
{
    private string? _root, _signature;
    private IReadOnlyDictionary<string, DateTimeOffset>? _dates;

    public IReadOnlyDictionary<string, DateTimeOffset> Read(string rootPath)
    {
        var signature = Signature(rootPath);
        if (signature is not null && _root == rootPath && _signature == signature && _dates is not null) return _dates;
        var dates = GitLastModified.Read(rootPath);
        _root = rootPath;
        _signature = signature is not null && signature == Signature(rootPath) ? signature : null;
        return _dates = dates;
    }

    private static string? Signature(string rootPath)
    {
        // History/path/configuration overrides retain Git's interpretation; pager and prompt settings do not affect dates.
        if (Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .Any(entry => entry.Key.ToString() is { } name &&
                (name.StartsWith("GIT_CONFIG", StringComparison.Ordinal) || name is
                    "GIT_DIR" or "GIT_WORK_TREE" or "GIT_COMMON_DIR" or "GIT_NAMESPACE" or
                    "GIT_OBJECT_DIRECTORY" or "GIT_ALTERNATE_OBJECT_DIRECTORIES" or "GIT_REPLACE_REF_BASE" or
                    "GIT_NO_REPLACE_OBJECTS" or "GIT_SHALLOW_FILE" or "GIT_GRAFT_FILE" or
                    "GIT_CEILING_DIRECTORIES" or "GIT_DISCOVERY_ACROSS_FILESYSTEM" or
                    "GIT_LITERAL_PATHSPECS" or "GIT_GLOB_PATHSPECS" or "GIT_NOGLOB_PATHSPECS" or "GIT_ICASE_PATHSPECS"))) return null;
        try
        {
            string? git = null;
            for (var directory = new DirectoryInfo(rootPath); directory is not null; directory = directory.Parent)
            {
                var marker = Path.Combine(directory.FullName, ".git");
                if (Directory.Exists(marker)) { git = marker; break; }
                if (!File.Exists(marker))
                {
                    if (File.Exists(Path.Combine(directory.FullName, "HEAD")) &&
                        Directory.Exists(Path.Combine(directory.FullName, "objects"))) return null;
                    continue;
                }
                var value = Metadata(marker);
                if (value is null || !value.StartsWith("gitdir: ", StringComparison.Ordinal)) return null;
                git = Path.GetFullPath(value[8..].Trim(), directory.FullName);
                break;
            }
            if (git is null) return "outside-repository";
            var commonName = Metadata(Path.Combine(git, "commondir"));
            if (commonName is null) return null;
            var common = commonName.Length == 0 ? git : Path.GetFullPath(commonName.Trim(), git);
            if (Directory.Exists(Path.Combine(common, "reftable"))) return null;
            if (new[] { git, common }.Any(path =>
                    File.Exists(Path.Combine(path, "info", "grafts")) ||
                    Directory.Exists(Path.Combine(path, "refs", "replace")) &&
                    Directory.EnumerateFiles(Path.Combine(path, "refs", "replace"), "*", SearchOption.AllDirectories).Any()))
                return null;
            var head = Metadata(Path.Combine(git, "HEAD"));
            if (head is null) return null;
            var files = new List<string>
            {
                Path.Combine(common, "packed-refs"), Path.Combine(common, "shallow"),
                Path.Combine(common, "config"), Path.Combine(git, "config.worktree")
            };
            if (head.StartsWith("ref: ", StringComparison.Ordinal))
            {
                var reference = head[5..].Trim();
                var local = Path.Combine(git, reference);
                files.Add(File.Exists(local) ? local : Path.Combine(common, reference));
            }
            var values = files.Select(Metadata).ToArray();
            if (values.Any(value => value is null || value.StartsWith("ref: ", StringComparison.Ordinal) ||
                                    value.Contains(" refs/replace/", StringComparison.Ordinal))) return null;
            return string.Join('\0', new[] { git, common, head }.Concat(values!));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? Metadata(string path)
    {
        var file = new FileInfo(path);
        // Large metadata is handled by Git without retaining another large process-local copy.
        return !file.Exists ? string.Empty : file.Length > 64 * 1024 ? null : File.ReadAllText(path);
    }
}
