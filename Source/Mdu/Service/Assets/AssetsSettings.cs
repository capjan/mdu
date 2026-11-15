namespace Mdu.Service.Assets;

/// <summary>
/// The <c>assets</c> section of <c>mdu.yml</c>: where the MDU Asset Server of the site runs (CLI-01 of mdu-assets).
/// <c>Server</c> is the base URL of its web interface and API, <c>Public</c> the base URL of its public image links
/// (the server's own address when it serves both), <c>Project</c> the default project for uploads. The API token never
/// comes from the file, only from the <c>MDU_ASSETS_TOKEN</c> environment variable.
/// </summary>
public sealed record AssetsSettings(Uri Server, Uri Public, string? Project)
{
    public const string TokenVariable = "MDU_ASSETS_TOKEN";

    /// <summary>The API token from the environment, or null when it is not set.</summary>
    public static string? ReadToken() => Environment.GetEnvironmentVariable(TokenVariable) is { Length: > 0 } token ? token.Trim() : null;

    /// <summary>Whether a link points to the public host of the asset server, so its image can be checked there.</summary>
    public bool IsPublicLink(string destination) =>
        Uri.TryCreate(destination, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
        string.Equals(uri.Host, Public.Host, StringComparison.OrdinalIgnoreCase) && uri.Port == Public.Port &&
        uri.AbsolutePath.StartsWith(Public.AbsolutePath.TrimEnd('/') + "/", StringComparison.Ordinal);
}
