using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Mdu.Service.Assets;

/// <summary>An image on the asset server with its ready-made snippets.</summary>
public sealed record AssetSnippets(string FileName, string Url, string Markdown, string Html);

/// <summary>The uploaded image, or the image the project already had with the same content.</summary>
public sealed record UploadOutcome(AssetSnippets Asset, bool AlreadyUploaded);

/// <summary>The state of one public link: ok, trash, deleted, unknown or invalid (API-06 of mdu-assets).</summary>
public sealed record LinkStatus(string Url, string Status);

/// <summary>A refusal or failure of the asset server, with a message for the reader.</summary>
public sealed class AssetServerException(string message) : Exception(message);

/// <summary>
/// Talks to an MDU Asset Server with <see cref="HttpClient"/> only (CLI-04): uploads images and checks public links.
/// JSON is read and written without reflection, like the rest of MDU.
/// </summary>
public sealed class AssetServerClient(HttpClient http)
{
    /// <summary>The most links the server checks in one request.</summary>
    public const int CheckBatchSize = 100;

    public async Task<UploadOutcome> UploadAsync(
        AssetsSettings settings, string token, string project, string name, string? alt, IReadOnlyList<string> tags, byte[] content,
        CancellationToken cancellationToken = default)
    {
        // A token over plain HTTP could be read on the way; only a server on this machine may use it.
        if (settings.Server.Scheme != Uri.UriSchemeHttps && !settings.Server.IsLoopback)
            throw new AssetServerException($"The asset server {settings.Server} is not https; MDU sends the token only over https.");

        var query = new StringBuilder($"name={Uri.EscapeDataString(name)}&duplicates=reject");
        if (!string.IsNullOrWhiteSpace(alt)) query.Append("&alt=").Append(Uri.EscapeDataString(alt));
        foreach (var tag in tags) query.Append("&tag=").Append(Uri.EscapeDataString(tag));
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(settings.Server, $"api/v1/projects/{Uri.EscapeDataString(project)}/assets?{query}"))
        {
            Content = new ByteArrayContent(content)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await Send(request, settings, cancellationToken);
        using var body = await ReadJson(response, cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.Created => new UploadOutcome(ReadAsset(body?.RootElement), false),
            // The project already has this content; the existing image and its link are what the author needs.
            HttpStatusCode.Conflict when body?.RootElement.TryGetProperty("existing", out var existing) == true =>
                new UploadOutcome(ReadAsset(existing), true),
            HttpStatusCode.Unauthorized => throw new AssetServerException(
                $"The asset server refused the token. Check {AssetsSettings.TokenVariable}; tokens are created on the Account page."),
            _ => throw new AssetServerException(Describe(response, body))
        };
    }

    /// <summary>Asks the server about public links, in batches; no token is needed.</summary>
    public async Task<IReadOnlyList<LinkStatus>> CheckAsync(AssetsSettings settings, IReadOnlyList<string> urls, CancellationToken cancellationToken = default)
    {
        var results = new List<LinkStatus>();
        foreach (var batch in urls.Chunk(CheckBatchSize))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.Server, "api/v1/public/check"))
            {
                Content = new ByteArrayContent(WriteUrls(batch))
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await Send(request, settings, cancellationToken);
            using var body = await ReadJson(response, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK || body?.RootElement.TryGetProperty("results", out var items) != true ||
                items.ValueKind != JsonValueKind.Array)
                throw new AssetServerException(Describe(response, body));
            foreach (var item in items.EnumerateArray())
                results.Add(new LinkStatus(Text(item, "url"), Text(item, "status")));
        }

        return results;
    }

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, AssetsSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new AssetServerException($"The asset server {settings.Server} cannot be reached: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AssetServerException($"The asset server {settings.Server} did not answer in time.");
        }
    }

    private static async Task<JsonDocument?> ReadJson(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AssetSnippets ReadAsset(JsonElement? asset)
    {
        if (asset is not { ValueKind: JsonValueKind.Object } value)
            throw new AssetServerException("The asset server answered without the image.");
        return new AssetSnippets(Text(value, "fileName"), Text(value, "url"), Text(value, "markdown"), Text(value, "html"));
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

    // Problem details (RFC 9457) explain the refusal; the status is the fallback.
    private static string Describe(HttpResponseMessage response, JsonDocument? body)
    {
        var status = $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim();
        if (body?.RootElement is { ValueKind: JsonValueKind.Object } problem)
        {
            var detail = Text(problem, "detail");
            var title = Text(problem, "title");
            if (detail.Length > 0) return $"{detail} ({status})";
            if (title.Length > 0) return $"{title} ({status})";
        }
        return response.StatusCode == HttpStatusCode.TooManyRequests
            ? $"The asset server asks to slow down ({status}); try again in a minute."
            : $"The asset server answered {status}.";
    }

    private static byte[] WriteUrls(IEnumerable<string> urls)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("urls");
            foreach (var url in urls) writer.WriteStringValue(url);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }
}
