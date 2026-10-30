using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Markdown.Confluence.Test;

public class ConfluencePublisherTest
{
    private static ConfluenceTarget Target(string? parent = "10") => new("https://wiki.example.test/confluence", "DOC", parent);
    private static ConfluenceSource[] Sources() =>
    [
        new("README.md", null, "Home", "# Home\n\n[Next](Next.md#details)", "/docs/README.md", "/docs"),
        new("Next.md", "README.md", "Next", "# Next\n\n## Details\n\nText", "/docs/Next.md", "/docs")
    ];

    [Fact]
    public async Task PublishesProtectedSubtreeAndSecondRunDoesNotWrite()
    {
        var server = new FakeConfluence();
        using var client = new ConfluenceClient(Target(), "test-pat", server);
        var output = new StringWriter();
        await new ConfluencePublisher(client, output).Publish(Sources(), false);
        Assert.Equal(4, server.Pages.Count);
        Assert.All(server.Pages.Values.Where(page => page.Id != "10"), page =>
        {
            Assert.False(page.Private);
            Assert.True(page.EditProtected);
            Assert.NotNull(page.Owner);
        });
        Assert.Equal("Existing parent", server.Pages["10"].Title);
        Assert.Equal("parent content", server.Pages["10"].Storage);
        Assert.DoesNotContain(server.Writes, entry => entry.Contains("/content/10/", StringComparison.Ordinal));
        server.Writes.Clear();
        await new ConfluencePublisher(client, output).Publish(Sources(), false);
        Assert.Empty(server.Writes);
        Assert.Contains("Comments and other actions remain governed by space permissions", output.ToString());
    }

    [Fact]
    public async Task DryRunUsesOnlyReads()
    {
        var server = new FakeConfluence();
        using var client = new ConfluenceClient(Target(), "test-pat", server);
        await new ConfluencePublisher(client, TextWriter.Null).Publish(Sources(), true);
        Assert.Empty(server.Writes);
        Assert.Single(server.Pages);
    }

    [Fact]
    public async Task WrongParentSpaceCausesNoWrites()
    {
        var server = new FakeConfluence();
        server.Pages["10"].Space = "OTHER";
        using var client = new ConfluenceClient(Target(), "test-pat", server);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ConfluencePublisher(client, TextWriter.Null).Publish(Sources(), false));
        Assert.Empty(server.Writes);
    }

    [Fact]
    public async Task UnmanagedRootTitleCollisionCausesNoWrites()
    {
        var server = new FakeConfluence();
        server.Seed("20", Target().RootTitle, "10");
        using var client = new ConfluenceClient(Target(), "test-pat", server);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ConfluencePublisher(client, TextWriter.Null).Publish(Sources(), false));
        Assert.Empty(server.Writes);
    }

    [Fact]
    public async Task UnmanagedPageTitleCollisionCausesNoWrites()
    {
        var server = new FakeConfluence();
        server.Seed("20", Target().PageTitle(Sources()[1]), "10");
        using var client = new ConfluenceClient(Target(), "test-pat", server);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ConfluencePublisher(client, TextWriter.Null).Publish(Sources(), false));
        Assert.Empty(server.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MovedRegisteredPageCausesNoWrites(bool anotherSpace)
    {
        var server = new FakeConfluence();
        using var client = new ConfluenceClient(Target(), "test-pat", server);
        await new ConfluencePublisher(client, TextWriter.Null).Publish(Sources(), false);
        var page = server.Pages.Values.Single(page => page.Owner?["path"]?.GetValue<string>() == "Next.md");
        if (anotherSpace) page.Space = "OTHER";
        else page.ParentId = "10";
        server.Writes.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ConfluencePublisher(client, TextWriter.Null).Publish(Sources(), false));
        Assert.Empty(server.Writes);
    }

    [Fact]
    public async Task ServerIgnoringInitialRestrictionsNeverReceivesDocumentation()
    {
        var server = new FakeConfluence { IgnoreCreationRestrictions = true, IgnoreRestrictionUpdates = true };
        using var client = new ConfluenceClient(Target(), "test-pat", server);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ConfluencePublisher(client, TextWriter.Null).Publish(Sources(), false));
        Assert.All(server.Pages.Values.Where(page => page.Id != "10"), page => Assert.Equal("<p>MDU publication in progress.</p>", page.Storage));
        Assert.Equal(2, server.Writes.Count);
    }

    [Fact]
    public async Task WeakenedEditProtectionFailsBeforeWriting()
    {
        var server = new FakeConfluence();
        using var client = new ConfluenceClient(Target(), "test-pat", server);
        await new ConfluencePublisher(client, TextWriter.Null).Publish(Sources(), false);
        server.Pages.Values.Single(page => page.Owner?["path"]?.GetValue<string>() == "Next.md").EditProtected = false;
        server.Writes.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ConfluencePublisher(client, TextWriter.Null).Publish(Sources(), false));
        Assert.Empty(server.Writes);
    }

    [Fact]
    public async Task FailedUploadCanResumeWithoutDuplicates()
    {
        var server = new FakeConfluence { FailNextUpdate = true };
        using var client = new ConfluenceClient(Target(), "test-pat", server);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ConfluencePublisher(client, TextWriter.Null).Publish(Sources(), false));
        Assert.True(server.Pages.Values.Single(page => page.Owner?["path"]?.GetValue<string>() == "$root").Private);
        await new ConfluencePublisher(client, TextWriter.Null).Publish(Sources(), false);
        Assert.Equal(4, server.Pages.Count);
        Assert.All(server.Pages.Values.Where(page => page.Id != "10"), page => Assert.False(page.Private));
    }

    [Fact]
    public async Task RemovedSourcesAreRetainedWithoutDeletes()
    {
        var server = new FakeConfluence();
        using var client = new ConfluenceClient(Target(), "test-pat", server);
        var publisher = new ConfluencePublisher(client, TextWriter.Null);
        await publisher.Publish(Sources(), false);
        server.Writes.Clear();
        await publisher.Publish([Sources()[0] with { Markdown = "# Home" }], false);
        Assert.Equal(4, server.Pages.Count);
        Assert.DoesNotContain(server.Writes, entry => entry.StartsWith("DELETE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AttachmentsAreScopedVerifiedAndNotUploadedTwice()
    {
        var server = new FakeConfluence();
        using var client = new ConfluenceClient(Target(), "test-pat", server);
        var source = Sources()[0] with { Markdown = "# Home\n\n[PDF](mdu:pdf)" };
        var pdf = new ConfluenceAsset("mdu-test.pdf", [1, 2, 3], false);
        var publisher = new ConfluencePublisher(client, TextWriter.Null);
        await publisher.Publish([source], false, pdf: pdf);
        Assert.Single(server.Attachments);
        server.Writes.Clear();
        await publisher.Publish([source], false, pdf: pdf);
        Assert.Empty(server.Writes);
    }

    [Fact]
    public async Task VersionConflictDoesNotBlindlyRetry()
    {
        var server = new FakeConfluence();
        using var client = new ConfluenceClient(Target(), "test-pat", server);
        var publisher = new ConfluencePublisher(client, TextWriter.Null);
        await publisher.Publish(Sources(), false);
        server.FailNextUpdate = true;
        server.Writes.Clear();
        var changed = Sources();
        changed[0] = changed[0] with { Markdown = "# Changed" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.Publish(changed, false));
        Assert.Single(server.Writes);
    }

    [Fact]
    public async Task HandlesPaginationWithoutFollowingServerProvidedUrls()
    {
        var server = new FakeConfluence { PageSize = 1 };
        using var client = new ConfluenceClient(Target(), "test-pat", server);
        var publisher = new ConfluencePublisher(client, TextWriter.Null);
        await publisher.Publish(Sources(), false);
        server.Writes.Clear();
        await publisher.Publish(Sources(), true);
        Assert.Empty(server.Writes);
    }

    [Fact]
    public async Task RedirectsFailWithoutFollowingOrLeakingResponseBody()
    {
        var handler = new RedirectHandler();
        using var client = new ConfluenceClient(Target(), "test-pat", handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.Preflight(default));
        Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain("test-pat", error.Message);
        Assert.Contains("HTTP 302", error.Message);
    }

    [Theory]
    [InlineData("http://wiki.example.test", "DOC", "10")]
    [InlineData("https://user:password@wiki.example.test", "DOC", "10")]
    [InlineData("https://wiki.example.test?redirect=x", "DOC", "10")]
    [InlineData("https://wiki.example.test", "../OTHER", "10")]
    [InlineData("https://wiki.example.test", "DOC", "../20")]
    public void RejectsInvalidTargets(string url, string space, string parent)
    {
        Assert.ThrowsAny<Exception>(() => new ConfluenceTarget(url, space, parent));
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://other.example.test/steal") },
                Content = new StringContent("test-pat")
            });
        }
    }
}

internal sealed class FakeConfluence : HttpMessageHandler
{
    internal sealed class Page(string id, string title, string? parentId)
    {
        public string Id = id;
        public string Title = title;
        public string? ParentId = parentId;
        public string Space = "DOC";
        public string Storage = "parent content";
        public int Version = 1;
        public bool Private;
        public bool EditProtected;
        public JsonObject? Owner;
        public Dictionary<string, JsonObject> Properties = new(StringComparer.Ordinal);
    }

    public Dictionary<string, Page> Pages { get; } = new(StringComparer.Ordinal);
    public List<string> Writes { get; } = [];
    public Dictionary<string, JsonObject> Attachments { get; } = new(StringComparer.Ordinal);
    public bool IgnoreCreationRestrictions;
    public bool IgnoreRestrictionUpdates;
    public bool FailNextUpdate;
    public int PageSize = 100;
    private int _id = 100;

    public FakeConfluence() => Seed("10", "Existing parent", null);
    public void Seed(string id, string title, string? parent) => Pages[id] = new Page(id, title, parent);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal("wiki.example.test", request.RequestUri!.Host);
        Assert.StartsWith("/confluence/rest/api/", request.RequestUri.AbsolutePath);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("test-pat", request.Headers.Authorization?.Parameter);
        var route = request.RequestUri.AbsolutePath["/confluence/rest/api/".Length..].Split('/');
        var query = request.RequestUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)).ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts.ElementAtOrDefault(1) ?? ""));
        var read = request.Method == HttpMethod.Get;
        if (!read) Writes.Add(request.Method.Method + " " + request.RequestUri.AbsolutePath);
        JsonNode? payload = null;
        if (request.Content is not null && request.Content.Headers.ContentType?.MediaType == "application/json")
            payload = JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
        var body = payload as JsonObject;
        if (route[0] == "space") return Ok(new JsonObject { ["key"] = route[1] });
        if (route[0] == "user") return Ok(new JsonObject { ["userKey"] = "publisher" });
        Assert.Equal("content", route[0]);
        if (route.Length == 1)
        {
            if (read)
                return Collection(Pages.Values.Where(page => page.Space == query["spaceKey"] &&
                    (!query.TryGetValue("title", out var title) || page.Title == title)).Select(Json), query);
            var id = (++_id).ToString();
            var page = new Page(id, body!["title"]!.GetValue<string>(), body["ancestors"]!.AsArray().LastOrDefault()?["id"]?.GetValue<string>())
            {
                Storage = body["body"]!["storage"]!["value"]!.GetValue<string>(),
                Private = !IgnoreCreationRestrictions,
                EditProtected = !IgnoreCreationRestrictions
            };
            Assert.Equal("DOC", body["space"]!["key"]!.GetValue<string>());
            Assert.NotNull(body["restrictions"]?["read"]);
            Assert.NotNull(body["restrictions"]?["update"]);
            Pages[id] = page;
            return Ok(Json(page));
        }
        if (!Pages.TryGetValue(route[1], out var current)) return new HttpResponseMessage(HttpStatusCode.NotFound);
        if (route.Length == 2)
        {
            if (read) return Ok(Json(current));
            if (FailNextUpdate)
            {
                FailNextUpdate = false;
                return new HttpResponseMessage(HttpStatusCode.Conflict);
            }
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.True(current.EditProtected);
            Assert.NotNull(current.Owner);
            Assert.Equal(current.Version + 1, body!["version"]!["number"]!.GetValue<int>());
            current.Title = body["title"]!.GetValue<string>();
            current.Storage = body["body"]!["storage"]!["value"]!.GetValue<string>();
            current.Version++;
            return Ok(Json(current));
        }
        if (route[2] == "descendant")
            return Collection(Pages.Values.Where(page => Ancestors(page).Contains(current.Id)).Select(Json), query);
        if (route[2] == "child" && route[3] == "attachment")
        {
            if (read)
                return Collection(Attachments.Values.Where(value => value["container"]?["id"]?.GetValue<string>() == current.Id &&
                    value["title"]?.GetValue<string>() == query["filename"]), query);
            Assert.True(current.EditProtected);
            Assert.NotNull(current.Owner);
            Assert.Equal("nocheck", request.Headers.GetValues("X-Atlassian-Token").Single());
            var form = Assert.IsType<MultipartFormDataContent>(request.Content);
            var file = form.Single(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "file");
            var comment = await form.Single(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "comment").ReadAsStringAsync(cancellationToken);
            var name = file.Headers.ContentDisposition!.FileName!.Trim('"');
            var attachment = new JsonObject
            {
                ["id"] = (++_id).ToString(), ["title"] = name,
                ["container"] = new JsonObject { ["id"] = current.Id },
                ["metadata"] = new JsonObject { ["comment"] = comment }
            };
            Attachments[current.Id + "/" + name] = attachment;
            return Ok(Result([attachment]));
        }
        if (route[2] == "restriction")
        {
            if (!read)
            {
                Assert.Equal(HttpMethod.Put, request.Method);
                Assert.Equal(3, route.Length);
                foreach (var restriction in Assert.IsType<JsonArray>(payload))
                {
                    if (IgnoreRestrictionUpdates) continue;
                    var applyRestriction = restriction!["restrictions"]!["user"]!.AsArray().Count > 0;
                    if (restriction["operation"]!.GetValue<string>() == "read") current.Private = applyRestriction;
                    else current.EditProtected = applyRestriction;
                }
                return Ok(Result([]));
            }
            var restricted = route[4] == "read" ? current.Private : current.EditProtected;
            return Ok(new JsonObject
            {
                ["operation"] = route[4],
                ["restrictions"] = new JsonObject
                {
                    ["user"] = Result(restricted ? [new JsonObject { ["userKey"] = "publisher" }] : []),
                    ["group"] = Result([])
                }
            });
        }
        if (route[2] == "property")
        {
            if (route.Length == 3 && read) return Collection(current.Properties.Values.Select(value => (JsonObject)value.DeepClone()), query);
            var key = route.Length > 3 ? Uri.UnescapeDataString(route[3]) : body!["key"]!.GetValue<string>();
            if (read)
                return current.Properties.TryGetValue(key, out var property) ? Ok(property) : new HttpResponseMessage(HttpStatusCode.NotFound);
            var entry = new JsonObject
            {
                ["key"] = key, ["value"] = body!["value"]!.DeepClone(),
                ["version"] = new JsonObject { ["number"] = body["version"]?["number"]?.GetValue<int>() ?? 1 }
            };
            current.Properties[key] = entry;
            if (key == ConfluenceClient.OwnerKey) current.Owner = entry["value"]!.DeepClone().AsObject();
            return Ok(entry);
        }
        throw new InvalidOperationException("Unexpected fake Confluence request: " + request.RequestUri.AbsolutePath);
    }

    private string[] Ancestors(Page page)
    {
        var result = new List<string>();
        for (var parent = page.ParentId; parent is not null; parent = Pages[parent].ParentId) result.Insert(0, parent);
        return result.ToArray();
    }

    private JsonObject Json(Page page) => new()
    {
        ["id"] = page.Id, ["title"] = page.Title, ["type"] = "page", ["status"] = "current",
        ["space"] = new JsonObject { ["key"] = page.Space },
        ["ancestors"] = new JsonArray(Ancestors(page).Select(id => (JsonNode)new JsonObject { ["id"] = id }).ToArray()),
        ["version"] = new JsonObject { ["number"] = page.Version },
        ["body"] = new JsonObject { ["storage"] = new JsonObject { ["value"] = page.Storage } },
        ["metadata"] = new JsonObject
        {
            ["properties"] = new JsonObject { [ConfluenceClient.OwnerKey] = new JsonObject { ["value"] = page.Owner?.DeepClone() } }
        }
    };

    private static JsonObject Result(IEnumerable<JsonObject> values, int start = 0)
    {
        var results = values.ToArray();
        return new JsonObject
        {
            ["results"] = new JsonArray(results.Select(value => (JsonNode)value.DeepClone()).ToArray()),
            ["size"] = results.Length, ["start"] = start, ["limit"] = 100, ["_links"] = new JsonObject()
        };
    }
    private HttpResponseMessage Collection(IEnumerable<JsonObject> values, Dictionary<string, string> query)
    {
        var start = int.Parse(query.GetValueOrDefault("start", "0"));
        var all = values.ToArray();
        var result = Result(all.Skip(start).Take(PageSize), start);
        if (all.Length > start + PageSize)
            result["_links"]!["next"] = "https://untrusted.example.test/never-follow";
        return Ok(result);
    }
    private static HttpResponseMessage Ok(JsonObject value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value.ToJsonString(), Encoding.UTF8, "application/json")
    };
}
