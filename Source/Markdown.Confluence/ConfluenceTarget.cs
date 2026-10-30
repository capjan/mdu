using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Markdown.Confluence;

public sealed class ConfluenceTarget
{
    public ConfluenceTarget(string url, string spaceKey, string? parentPageId = null, string exportId = "documentation")
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.AbsolutePath.Contains("%", StringComparison.Ordinal))
            throw new ArgumentException("Confluence URL must be an HTTPS base URL without credentials, query, fragment or encoded path segments.");
        if (!Regex.IsMatch(spaceKey, @"\A[A-Za-z0-9_-]+\z"))
            throw new ArgumentException("Confluence space key must contain only letters, digits, underscores or hyphens.");
        if (parentPageId is not null) ValidateId(parentPageId);
        if (!Regex.IsMatch(exportId, @"\A[A-Za-z0-9_-]{1,80}\z"))
            throw new ArgumentException("Confluence export ID must contain 1-80 letters, digits, underscores or hyphens.");

        BaseUri = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
        SpaceKey = spaceKey;
        ParentPageId = parentPageId;
        ExportId = exportId;
        Identity = Hash($"{BaseUri.AbsoluteUri}\n{spaceKey}\n{parentPageId}\n{exportId}");
    }

    public Uri BaseUri { get; }
    public string SpaceKey { get; }
    public string? ParentPageId { get; }
    public string ExportId { get; }
    public string Identity { get; }
    public string RootTitle => $"MDU: {ExportId} [{Identity[..12]}]";

    public string PageTitle(ConfluenceSource page)
    {
        var title = string.IsNullOrWhiteSpace(page.Title) ? page.Path : page.Title.Trim();
        var suffix = $" [MDU {Identity[..12]}/{Hash(page.Path)[..12]}]";
        return title[..Math.Min(title.Length, 250 - suffix.Length)] + suffix;
    }

    internal static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
    internal static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));

    internal static void ValidateId(string id)
    {
        if (!Regex.IsMatch(id, @"\A[1-9][0-9]*\z"))
            throw new InvalidOperationException("Confluence returned or received an invalid content ID.");
    }
}

public sealed record ConfluenceSource(string Path, string? ParentPath, string Title, string Markdown, string FilePath, string SourceRoot);
public sealed record ConfluenceAsset(string Name, byte[] Content, bool IsImage);
public sealed record ConfluenceDocument(string Storage, IReadOnlyList<ConfluenceAsset> Assets, IReadOnlyList<string> Diagnostics);
