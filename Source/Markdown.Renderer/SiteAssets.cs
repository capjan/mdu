using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

namespace Markdown.Renderer;

// One publisher per site generation. Ordinary preview edits reuse the already published assets.
internal sealed class SiteAssets(string? outputPath)
{
    internal static readonly string[] TemplateScripts =
    [
        "theme", "panels-init", "header-init", "navigation-controls", "tables",
        "panels", "scroll-navigation", "code-copy", "content-previews"
    ];

    // Site-relative path to content version; an empty version marks an asset without content.
    private readonly Dictionary<string, string> _versions = new(StringComparer.Ordinal);

    /// <summary>Full paths of the asset files published in this generation.</summary>
    internal IEnumerable<string> PublishedPaths => outputPath is null
        ? []
        : _versions.Where(entry => entry.Value.Length > 0).Select(entry => Path.Combine(outputPath, entry.Key));

    internal string Script(string name, Func<string> content, string prefix)
    {
        if (outputPath is null) return $"<script>{content()}</script>";
        var url = Publish("js/" + name + ".js", content);
        return url is null ? string.Empty : $"<script src=\"{HtmlEncoder.Default.Encode(prefix + url)}\"></script>";
    }

    internal string Styles(string name, Func<string> content, string prefix)
    {
        if (outputPath is null)
        {
            var css = content();
            return css.Length == 0 ? string.Empty : $"<style>{css}</style>";
        }
        var url = Publish("css/" + name + ".css", content);
        return url is null ? string.Empty : $"<link rel=\"stylesheet\" href=\"{HtmlEncoder.Default.Encode(prefix + url)}\">";
    }

    // Returns the versioned site-relative URL, so a cached copy from an earlier deployment is never reused.
    private string? Publish(string relativePath, Func<string> content)
    {
        if (!_versions.TryGetValue(relativePath, out var version))
        {
            var text = content();
            version = text.Length == 0 ? string.Empty : Version(text);
            if (version.Length > 0) WriteAtomically(Path.Combine(outputPath!, relativePath), text);
            _versions[relativePath] = version;
        }
        return version.Length == 0 ? null : relativePath + "?v=" + version;
    }

    internal static string Version(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..12];

    internal static void WriteAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
