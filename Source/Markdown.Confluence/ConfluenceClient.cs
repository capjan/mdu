using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Markdown.Confluence;

public sealed class ConfluenceClient : IDisposable
{
    public const string OwnerKey = "mdu-export";
    private const string Expansion = "space,ancestors,version,body.storage,metadata.properties.mdu-export";
    private readonly HttpClient _http;
    private readonly string _pat;
    private readonly HashSet<string> _created = new(StringComparer.Ordinal);
    private string? _rootId;
    private string? _userKey;

    public ConfluenceClient(ConfluenceTarget target, string pat)
        : this(target, pat, new HttpClientHandler { AllowAutoRedirect = false })
    {
    }

    // The injected handler is for offline contract tests; production never follows redirects.
    public ConfluenceClient(ConfluenceTarget target, string pat, HttpMessageHandler handler)
    {
        if (string.IsNullOrWhiteSpace(pat) || pat.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
            throw new ArgumentException("Set MDU_CONFLUENCE_PAT to a valid personal access token.");
        Target = target;
        _pat = pat;
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(100) };
    }

    public ConfluenceTarget Target { get; }
    public string UserKey => _userKey ?? throw new InvalidOperationException("Confluence preflight has not run.");

    public async Task Preflight(CancellationToken cancellationToken)
    {
        _rootId = null;
        _created.Clear();
        var space = await Get($"space/{Escape(Target.SpaceKey)}", cancellationToken);
        if (Text(space, "key") != Target.SpaceKey)
            throw new InvalidOperationException("Confluence returned a different target space.");
        var user = await Get("user/current", cancellationToken);
        _userKey = RequiredText(user, "userKey");
        if (Target.ParentPageId is not null)
            CheckPage(await ReadPage(Target.ParentPageId, cancellationToken), Target.ParentPageId);
    }

    public Task<JsonObject> ReadPage(string id, CancellationToken cancellationToken)
    {
        ConfluenceTarget.ValidateId(id);
        return Get($"content/{id}?expand={Expansion}", cancellationToken);
    }

    public async Task<JsonObject?> FindRoot(CancellationToken cancellationToken)
    {
        var matches = await List($"content?type=page&spaceKey={Escape(Target.SpaceKey)}&title={Escape(Target.RootTitle)}&expand={Expansion}", cancellationToken);
        if (matches.Count > 1) throw new InvalidOperationException("Multiple Confluence export roots match this target.");
        if (matches.Count == 0) return null;
        var root = matches[0];
        var id = RequiredText(root, "id");
        CheckPage(root, id);
        CheckOwner(root, "$root");
        CheckParent(root, Target.ParentPageId);
        _rootId = id;
        return root;
    }

    public async Task<IReadOnlyList<JsonObject>> ReadManagedPages(CancellationToken cancellationToken)
    {
        var root = RootId;
        var descendants = await List($"content/{root}/descendant/page?expand={Expansion}", cancellationToken);
        var managed = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var page in descendants)
        {
            CheckPage(page, RequiredText(page, "id"));
            if (Owner(page)?["identity"]?.GetValue<string>() == Target.Identity)
            {
                var path = RequiredText(Owner(page)!, "path");
                CheckOwner(page, path);
                if (!managed.TryAdd(path, page))
                    throw new InvalidOperationException($"Duplicate managed Confluence path: {path}");
            }
        }

        // A registry on the root detects managed pages moved outside the subtree or into another space.
        foreach (var property in await List($"content/{root}/property", cancellationToken))
        {
            if (!RequiredText(property, "key").StartsWith("mdu-page-", StringComparison.Ordinal)) continue;
            var value = property["value"] as JsonObject ?? throw new InvalidOperationException("Invalid Confluence page registry.");
            var path = RequiredText(value, "path");
            var id = RequiredText(value, "id");
            var page = await ReadPage(id, cancellationToken);
            CheckPage(page, id);
            CheckOwner(page, path);
            if (managed.TryGetValue(path, out var found) && RequiredText(found, "id") != id)
                throw new InvalidOperationException($"Conflicting Confluence page registry for {path}.");
            managed[path] = page;
        }
        return managed.Values.ToArray();
    }

    public Task<IReadOnlyList<JsonObject>> FindByTitle(string title, CancellationToken cancellationToken) =>
        List($"content?type=page&spaceKey={Escape(Target.SpaceKey)}&title={Escape(title)}&expand={Expansion}", cancellationToken);

    public async Task<JsonObject> CreatePage(string title, string? parentId, string path, CancellationToken cancellationToken)
    {
        if (parentId is not null) await AssertParent(parentId, path == "$root", cancellationToken);
        else if (path != "$root" || Target.ParentPageId is not null)
            throw new InvalidOperationException("Only a top-level export root may be created without a parent.");

        var owner = new JsonObject { ["identity"] = Target.Identity, ["path"] = path, ["format"] = 1 };
        var payload = new JsonObject
        {
            ["type"] = "page", ["title"] = title,
            ["space"] = new JsonObject { ["key"] = Target.SpaceKey },
            ["ancestors"] = parentId is null ? new JsonArray() : new JsonArray(new JsonObject { ["id"] = parentId }),
            ["body"] = Body("<p>MDU publication in progress.</p>"),
            ["restrictions"] = new JsonObject
            {
                ["read"] = Restriction("read"), ["update"] = Restriction("update")
            }
        };
        var result = await Send(HttpMethod.Post, "content", payload, cancellationToken);
        var id = RequiredText(result, "id");
        ConfluenceTarget.ValidateId(id);
        _created.Add(id);
        if (path == "$root") _rootId = id;
        var page = await ReadPage(id, cancellationToken);
        CheckPage(page, id);
        CheckParent(page, parentId);
        await Send(HttpMethod.Put, $"content/{id}/restriction",
            new JsonArray(Restriction("read"), Restriction("update")), cancellationToken);
        await AssertRestrictions(id, requirePrivate: true, cancellationToken);
        await WriteProperty(id, OwnerKey, owner, path, cancellationToken);
        page = await ReadPage(id, cancellationToken);
        CheckPage(page, id);
        CheckOwner(page, path);
        return page;
    }

    public async Task RegisterPage(JsonObject page, CancellationToken cancellationToken)
    {
        var path = RequiredText(Owner(page)!, "path");
        var value = new JsonObject { ["id"] = RequiredText(page, "id"), ["path"] = path };
        await WriteProperty(RootId, "mdu-page-" + ConfluenceTarget.Hash(path), value, "$root", cancellationToken);
    }

    public async Task<JsonObject> UpdatePage(string id, string path, string title, string storage, string? parentId, CancellationToken cancellationToken)
    {
        var page = await AssertManaged(id, path, cancellationToken);
        await AssertRestrictions(id, requirePrivate: false, cancellationToken);
        if (parentId is not null) await AssertParent(parentId, path == "$root", cancellationToken);
        CheckParent(page, parentId);
        var version = page["version"]?["number"]?.GetValue<int>() ??
                      throw new InvalidOperationException("Missing Confluence page version.");
        await Send(HttpMethod.Put, $"content/{id}", new JsonObject
        {
            ["id"] = id, ["type"] = "page", ["title"] = title,
            ["version"] = new JsonObject { ["number"] = version + 1, ["minorEdit"] = true },
            ["body"] = Body(storage)
        }, cancellationToken);
        page = await AssertManaged(id, path, cancellationToken);
        CheckParent(page, parentId);
        if (Text(page, "title") != title || page["body"]?["storage"]?["value"]?.GetValue<string>() != storage)
            throw new InvalidOperationException($"Confluence did not preserve the published content of page {id}.");
        return page;
    }

    public async Task VerifyPage(string id, string path, string? parentId, CancellationToken cancellationToken)
    {
        var page = await AssertManaged(id, path, cancellationToken);
        CheckParent(page, parentId);
        await AssertRestrictions(id, requirePrivate: false, cancellationToken);
    }

    public async Task PublishVisibility(string id, string path, CancellationToken cancellationToken)
    {
        await AssertManaged(id, path, cancellationToken);
        var restricted = await AssertRestrictions(id, requirePrivate: false, cancellationToken);
        if (!restricted) return;
        await Send(HttpMethod.Put, $"content/{id}/restriction", new JsonArray(new JsonObject
        {
            ["operation"] = "read",
            ["restrictions"] = new JsonObject { ["user"] = new JsonArray(), ["group"] = new JsonArray() }
        }), cancellationToken);
        await AssertManaged(id, path, cancellationToken);
        if (await AssertRestrictions(id, requirePrivate: false, cancellationToken))
            throw new InvalidOperationException($"Confluence page {id} is still restricted to the publisher for reading.");
    }

    public async Task UploadAsset(string pageId, string path, ConfluenceAsset asset, CancellationToken cancellationToken)
    {
        await AssertManaged(pageId, path, cancellationToken);
        await AssertRestrictions(pageId, requirePrivate: false, cancellationToken);
        var endpointBase = $"content/{pageId}/child/attachment?filename={Escape(asset.Name)}&expand=container,version,metadata";
        var existing = await List(endpointBase, cancellationToken);
        if (existing.Count > 1) throw new InvalidOperationException("Duplicate Confluence attachment name.");
        var checksum = "mdu-sha256:" + ConfluenceTarget.Hash(asset.Content);
        string endpoint;
        if (existing.Count == 1)
        {
            var attachment = existing[0];
            var attachmentId = RequiredText(attachment, "id");
            ConfluenceTarget.ValidateId(attachmentId);
            if (attachment["container"]?["id"]?.GetValue<string>() != pageId)
                throw new InvalidOperationException("Confluence attachment is outside its expected page.");
            if (attachment["metadata"]?["comment"]?.GetValue<string>() == checksum) return;
            if (!(attachment["metadata"]?["comment"]?.GetValue<string>() ?? "").StartsWith("mdu-sha256:", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing to overwrite an attachment not owned by MDU.");
            endpoint = $"content/{pageId}/child/attachment/{attachmentId}/data";
        }
        else endpoint = $"content/{pageId}/child/attachment";

        await AssertManaged(pageId, path, cancellationToken);
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(asset.Content), "file", asset.Name);
        form.Add(new StringContent(checksum), "comment");
        form.Add(new StringContent("true"), "minorEdit");
        await Request(HttpMethod.Post, endpoint, form, cancellationToken, attachment: true);
        await AssertManaged(pageId, path, cancellationToken);
        var uploaded = await List(endpointBase, cancellationToken);
        if (uploaded.Count != 1 || uploaded[0]["container"]?["id"]?.GetValue<string>() != pageId ||
            uploaded[0]["metadata"]?["comment"]?.GetValue<string>() != checksum)
            throw new InvalidOperationException("Confluence attachment upload could not be verified.");
    }

    internal static JsonObject? Owner(JsonObject page) => page["metadata"]?["properties"]?[OwnerKey]?["value"] as JsonObject;
    internal static string RequiredText(JsonObject value, string key) =>
        Text(value, key) is { Length: > 0 } text ? text : throw new InvalidOperationException($"Confluence response is missing '{key}'.");
    internal static string? Text(JsonObject value, string key) => value[key]?.GetValue<string>();
    internal static string Storage(JsonObject page) => page["body"]?["storage"]?["value"]?.GetValue<string>() ??
        throw new InvalidOperationException("Missing Confluence page storage.");
    internal static void CheckParent(JsonObject page, string? parentId)
    {
        var ancestors = page["ancestors"] as JsonArray ?? throw new InvalidOperationException("Missing Confluence ancestors.");
        var actual = ancestors.LastOrDefault()?["id"]?.GetValue<string>();
        if (actual != parentId) throw new InvalidOperationException("Confluence page has moved from its expected parent; no automatic moves are performed.");
    }

    private string RootId => _rootId ?? throw new InvalidOperationException("Confluence export root has not been resolved.");
    private static string Escape(string value) => Uri.EscapeDataString(value);
    private static JsonObject Body(string storage) =>
        new() { ["storage"] = new JsonObject { ["value"] = storage, ["representation"] = "storage" } };
    private JsonObject Restriction(string operation) => new()
    {
        ["operation"] = operation,
        ["restrictions"] = new JsonObject
        {
            ["user"] = new JsonArray(new JsonObject { ["type"] = "known", ["userKey"] = UserKey }),
            ["group"] = new JsonArray()
        }
    };

    private void CheckPage(JsonObject page, string expectedId)
    {
        ConfluenceTarget.ValidateId(expectedId);
        if (RequiredText(page, "id") != expectedId || Text(page, "type") != "page" ||
            Text(page, "status") != "current" || page["space"]?["key"]?.GetValue<string>() != Target.SpaceKey)
            throw new InvalidOperationException("Confluence content is not a current page in the configured space.");
        if (page["ancestors"] is not JsonArray ancestors)
            throw new InvalidOperationException("Confluence response has no ancestor information.");
        if (Target.ParentPageId is not null && expectedId != Target.ParentPageId &&
            !ancestors.Any(item => item?["id"]?.GetValue<string>() == Target.ParentPageId))
            throw new InvalidOperationException("Confluence content is outside the configured parent subtree.");
        if (_rootId is not null && expectedId != _rootId && expectedId != Target.ParentPageId &&
            !ancestors.Any(item => item?["id"]?.GetValue<string>() == _rootId))
            throw new InvalidOperationException("Confluence content is outside the managed export subtree.");
    }

    private void CheckOwner(JsonObject page, string path)
    {
        var owner = Owner(page);
        if (owner?["identity"]?.GetValue<string>() != Target.Identity ||
            owner["path"]?.GetValue<string>() != path || owner["format"]?.GetValue<int>() != 1)
            throw new InvalidOperationException("Refusing to modify Confluence content not owned by this export.");
    }

    private async Task<JsonObject> AssertManaged(string id, string path, CancellationToken cancellationToken)
    {
        var page = await ReadPage(id, cancellationToken);
        CheckPage(page, id);
        CheckOwner(page, path);
        return page;
    }

    private async Task AssertParent(string id, bool rootParent, CancellationToken cancellationToken)
    {
        var page = await ReadPage(id, cancellationToken);
        CheckPage(page, id);
        if (id == Target.ParentPageId && rootParent) return;
        if (Owner(page)?["identity"]?.GetValue<string>() != Target.Identity)
            throw new InvalidOperationException("Refusing to create content under an unmanaged parent.");
    }

    private async Task<bool> AssertRestrictions(string id, bool requirePrivate, CancellationToken cancellationToken)
    {
        var update = await Get($"content/{id}/restriction/byOperation/update?expand=restrictions.user,restrictions.group&limit=200", cancellationToken);
        if (!OnlyPublisher(update))
            throw new InvalidOperationException($"Page {id} is not edit-restricted to the publisher. Fix restrictions before retrying.");
        var read = await Get($"content/{id}/restriction/byOperation/read?expand=restrictions.user,restrictions.group&limit=200", cancellationToken);
        var privatePage = OnlyPublisher(read);
        if (!privatePage && (!EmptySubjects(read) || requirePrivate))
            throw new InvalidOperationException($"Page {id} has unexpected read restrictions; publication stopped.");
        return privatePage;
    }

    private bool OnlyPublisher(JsonObject restriction)
    {
        var users = restriction["restrictions"]?["user"] as JsonObject;
        var groups = restriction["restrictions"]?["group"] as JsonObject;
        return Complete(users, 1) && Complete(groups, 0) &&
               users!["results"]![0]?["userKey"]?.GetValue<string>() == UserKey;
    }

    private static bool EmptySubjects(JsonObject restriction) =>
        Complete(restriction["restrictions"]?["user"] as JsonObject, 0) &&
        Complete(restriction["restrictions"]?["group"] as JsonObject, 0);

    private static bool Complete(JsonObject? collection, int count) =>
        collection?["results"] is JsonArray results && results.Count == count &&
        collection["size"]?.GetValue<int>() == count &&
        (collection["start"]?.GetValue<int>() ?? 0) == 0 &&
        collection["_links"]?["next"] is null;

    private async Task WriteProperty(string id, string key, JsonObject value, string path, CancellationToken cancellationToken)
    {
        if (_created.Contains(id))
        {
            var page = await ReadPage(id, cancellationToken);
            CheckPage(page, id);
            if (key != OwnerKey) CheckOwner(page, path);
        }
        else await AssertManaged(id, path, cancellationToken);
        var existing = await Request(HttpMethod.Get, $"content/{id}/property/{Escape(key)}", null, cancellationToken, allowNotFound: true);
        if (existing?["value"]?.ToJsonString() == value.ToJsonString()) return;
        var payload = new JsonObject { ["key"] = key, ["value"] = value.DeepClone() };
        if (existing is not null)
        {
            payload["version"] = new JsonObject { ["number"] = (existing["version"]?["number"]?.GetValue<int>() ??
                throw new InvalidOperationException("Missing Confluence property version.")) + 1 };
        }
        await Send(existing is null ? HttpMethod.Post : HttpMethod.Put,
            existing is null ? $"content/{id}/property" : $"content/{id}/property/{Escape(key)}", payload, cancellationToken);
    }

    private Task<JsonObject> Get(string path, CancellationToken cancellationToken) => Send(HttpMethod.Get, path, null, cancellationToken);
    private async Task<JsonObject> Send(HttpMethod method, string path, JsonNode? payload, CancellationToken cancellationToken)
    {
        using var content = payload is null ? null : new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        return await Request(method, path, content, cancellationToken) ??
            throw new InvalidOperationException("Confluence returned no JSON response.");
    }

    private async Task<IReadOnlyList<JsonObject>> List(string path, CancellationToken cancellationToken)
    {
        var all = new List<JsonObject>();
        var start = 0;
        while (true)
        {
            var result = await Get($"{path}{(path.Contains('?') ? '&' : '?')}start={start}&limit=100", cancellationToken);
            var results = result["results"] as JsonArray ?? throw new InvalidOperationException("Invalid Confluence collection.");
            if (result["start"]?.GetValue<int>() != start || result["size"]?.GetValue<int>() != results.Count)
                throw new InvalidOperationException("Invalid Confluence pagination.");
            all.AddRange(results.Select(item => item?.DeepClone() as JsonObject ??
                throw new InvalidOperationException("Invalid Confluence collection item.")));
            if (result["_links"]?["next"] is null) return all;
            if (results.Count == 0) throw new InvalidOperationException("Confluence pagination did not advance.");
            start = checked(start + results.Count);
        }
    }

    private async Task<JsonObject?> Request(HttpMethod method, string path, HttpContent? content,
        CancellationToken cancellationToken, bool allowNotFound = false, bool attachment = false)
    {
        var uri = new Uri(Target.BaseUri, "rest/api/" + path);
        if (uri.GetLeftPart(UriPartial.Authority) != Target.BaseUri.GetLeftPart(UriPartial.Authority) ||
            !uri.AbsolutePath.StartsWith(Target.BaseUri.AbsolutePath + "rest/api/", StringComparison.Ordinal))
            throw new InvalidOperationException("Confluence request escaped the configured base URL.");
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _pat);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (attachment) request.Headers.Add("X-Atlassian-Token", "nocheck");
        using var response = await _http.SendAsync(request, cancellationToken);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Confluence {method.Method} failed (HTTP {(int)response.StatusCode}). Redirects are not followed; response bodies and credentials are not logged.");
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("Confluence returned an empty JSON response.");
        return JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("Confluence returned an invalid JSON object.");
    }

    public void Dispose() => _http.Dispose();
}
