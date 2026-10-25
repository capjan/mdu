using System.Text;
using Markdown.Renderer;

namespace Markdown.Renderer.Test;

public class OpenApiMarkdownRendererTest
{
    [Fact]
    public void ResolveSpecPathRejectsPathsOutsideAnAllowedRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-openapi-root-{Guid.NewGuid():N}");
        var repository = Path.Combine(root, "repository");
        var pagePath = Path.Combine(repository, "Docs", "page.md");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            OpenApiMarkdownRenderer.ResolveSpecPath(pagePath, "../../../../outside.yaml", repository));

        Assert.Contains("inside the imported repository", exception.Message);
    }

    [Fact]
    public void ResolveSpecPathRejectsRemoteUrls()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            OpenApiMarkdownRenderer.ResolveSpecPath("/tmp/page.md", "https://example.com/openapi.yaml"));

        Assert.Contains("local file path", exception.Message);
    }

    [Fact]
    public async Task RendersNullableArrayItemsAsLinkedCompactTypes()
    {
        var html = await RenderHtml("""
            {
              "openapi": "3.1.0",
              "info": { "title": "Locations", "version": "1" },
              "paths": { "/locations": { "get": { "responses": {
                "200": { "description": "Locations", "content": { "application/json": {
                  "schema": { "type": "object", "properties": {
                    "metaInfoAssets": { "type": ["null", "array"], "items": { "$ref": "#/components/schemas/LocationAssetDto" } }
                  } }
                } } }
              } } } },
              "components": { "schemas": { "LocationAssetDto": {
                "type": "object", "properties": { "id": { "type": "string" } }
              } } }
            }
            """);
        var cell = System.Text.RegularExpressions.Regex.Match(html,
            @"<td>(?<type><span[^>]+><span[^>]+>metaInfoAssets.*?)</td>").Groups["type"].Value;
        Assert.Equal("metaInfoAssets: [LocationAssetDto]?", System.Text.RegularExpressions.Regex.Replace(cell, "<[^>]+>", ""));
        var link = System.Text.RegularExpressions.Regex.Match(cell,
            "class=\"openapi-type-reference\" href=\"#(?<anchor>[^\"]+)\"");
        Assert.True(link.Success);
        Assert.Contains($"id=\"{link.Groups["anchor"].Value}\"", html);
        Assert.Contains("class=\"openapi-type-optional\">?</span>", cell);
        Assert.DoesNotContain("class=\"openapi-description-toggle\"", html);
        Assert.Contains("<div class=\"openapi-schema-table openapi-schema-table-plain\">", html);
    }

    [Fact]
    public async Task ListsOperationsBelowTheirTagsInThePageContents()
    {
        var html = await RenderHtml("""
            {
              "openapi": "3.1.0",
              "info": { "title": "Orders", "version": "1" },
              "paths": {
                "/orders": { "get": { "tags": ["Orders"], "summary": "List orders", "responses": { "200": { "description": "OK" } } } },
                "/health": { "get": { "summary": "Health", "responses": { "200": { "description": "OK" } } } }
              }
            }
            """);
        var toc = html.Split("class=\"toc-list\"", 2)[1].Split("</ul>", 2)[0];
        var entries = System.Text.RegularExpressions.Regex.Matches(toc,
                "--toc-depth: (?<depth>\\d)\"><a class=\"toc-link\" href=\"#(?<id>[^\"]+)\">(?<title>[^<]*)</a>")
            .Select(match => (Depth: match.Groups["depth"].Value, Id: match.Groups["id"].Value, Title: match.Groups["title"].Value))
            .ToArray();

        var orders = entries.Single(entry => entry.Title == "GET /orders");
        var health = entries.Single(entry => entry.Title == "GET /health");
        Assert.Equal("1", orders.Depth);
        Assert.Equal("1", health.Depth);
        Assert.Contains(entries, entry => entry is { Depth: "0", Title: "Orders" });
        Assert.Contains($"<summary id=\"{orders.Id}\" class=\"openapi-operation\">", html);
        Assert.Contains($"<summary id=\"{health.Id}\" class=\"openapi-operation\">", html);
    }

    [Fact]
    public async Task CollapsesEndpointsAndRendersResponseMediaTypesAndTabs()
    {
        var html = await RenderHtml("""
            {
              "openapi": "3.1.0",
              "info": { "title": "Orders", "version": "1" },
              "tags": [{ "name": "Orders", "description": "Order operations" }],
              "paths": {
                "/orders/{id}": {
                  "get": {
                    "tags": ["Orders"],
                    "responses": {
                      "200": {
                        "description": "Found",
                        "content": {
                          "application/json": {
                            "schema": {
                              "type": "object",
                              "required": ["id"],
                              "properties": { "id": { "type": "integer", "description": "Order identifier" } }
                            },
                            "example": { "id": 42 }
                          },
                          "text/plain": { "schema": { "type": "string" }, "example": "Found" }
                        }
                      },
                      "404": { "description": "Not found" }
                    }
                  }
                }
              }
            }
            """);

        var operationHeading = System.Text.RegularExpressions.Regex.Match(
            html,
            "<summary(?<attributes>[^>]*)>\\s*<code>GET</code> <span class=\"openapi-path\">/orders/<span class=\"openapi-path-parameter openapi-path-parameter-color-1\">\\{id\\}</span></span>");
        Assert.True(operationHeading.Success, html);
        Assert.Contains("class=\"openapi-operation\"", operationHeading.Groups["attributes"].Value);
        Assert.Contains("<details class=\"openapi-endpoint openapi-method-get\">", html);
        Assert.Contains("<span class=\"openapi-tag-title\"><code>Orders</code></span>", html);
        Assert.Contains("<span class=\"openapi-tag-description\">Order operations</span>", html);
        Assert.Contains("class=\"openapi-tag-method-count openapi-method-get\" data-method=\"openapi-method-get\" data-count=\"1\" data-deprecated=\"0\" data-tooltip=\"1 GET endpoint\" aria-label=\"1 GET endpoint\">1", html);
        Assert.DoesNotContain("<input type=\"checkbox\" class=\"openapi-show-deprecated\"", html);
        Assert.Contains("<div class=\"openapi-media-type-label\">", html);
        Assert.Contains("const enhanceSelects = root =>", html);
        var operationId = System.Text.RegularExpressions.Regex.Match(
            operationHeading.Groups["attributes"].Value,
            "id=\"(?<id>[^\"]+)\"").Groups["id"].Value;
        Assert.Contains($"id=\"{operationId}\"", html);
        Assert.DoesNotContain("<details class=\"openapi-endpoint openapi-method-get\" open", html);
        var detailsStart = html.IndexOf("<details class=\"openapi-endpoint openapi-method-get\">", StringComparison.Ordinal);
        var detailsEnd = html.IndexOf("</details>", detailsStart, StringComparison.Ordinal);
        var responsePanel = html.IndexOf("<section class=\"openapi-response-panel openapi-status-2xx\" data-response-status=\"200\">", detailsStart, StringComparison.Ordinal);
        Assert.True(detailsEnd > responsePanel, html);
        Assert.DoesNotContain("id=\"get-orders\" id=\"\"", html);
        Assert.Contains("<div class=\"openapi-response-heading\">Response <span class=\"openapi-response-position\">1 of 2</span></div>", html);
        Assert.DoesNotContain("2 Responses", html);
        Assert.Contains("<option value=\"200\" data-response-position=\"1\" class=\"openapi-status-2xx\" selected>200 - OK</option>", html);
        Assert.Contains("<option value=\"404\" data-response-position=\"2\" class=\"openapi-status-4xx\">404 - Not Found</option>", html);
        Assert.DoesNotContain("Response <code>200</code>", html);
        Assert.Contains("Media Type <span class=\"openapi-media-type-position\">1 of 2</span>", html);
        Assert.Contains("<option value=\"0\" selected>application/json</option>", html);
        Assert.Contains("<option value=\"1\">text/plain</option>", html);
        Assert.Contains("<button type=\"button\" class=\"openapi-response-tab\" role=\"tab\"", html);
        Assert.Contains(">Example</button>", html);
        Assert.Contains(">Schema</button>", html);
        Assert.Contains(">Schema JSON</button>", html);
        Assert.Equal(6, System.Text.RegularExpressions.Regex.Matches(html, "role=\"tab\"").Count);
        Assert.Contains("<th>Property</th>", html);
        Assert.Contains("<th class=\"openapi-description-header\"><span class=\"openapi-schema-table-header\"><span class=\"openapi-description-label\">Description</span><button type=\"button\" class=\"openapi-description-toggle\" aria-expanded=\"false\">Show descriptions</button></span></th>", html);
        Assert.Contains("<div class=\"openapi-schema-table\">", html);
        Assert.DoesNotContain("<th>Type</th>", html);
        Assert.DoesNotContain("<th>Required</th>", html);
        Assert.Contains("<span class=\"openapi-required-marker\" role=\"img\" aria-label=\"Required\" data-tooltip=\"Required\">", html);
        Assert.DoesNotContain("title=\"Required\"", html);
        Assert.Contains("<td>Order identifier</td>", html);
        Assert.Contains("<div class=\"openapi-response-description\">", html);
        Assert.True(
            html.IndexOf("class=\"openapi-response-select", StringComparison.Ordinal) <
            html.IndexOf("class=\"openapi-response-description", StringComparison.Ordinal) &&
            html.IndexOf("class=\"openapi-response-description", StringComparison.Ordinal) <
            html.IndexOf("class=\"openapi-media-type-select", StringComparison.Ordinal) &&
            html.IndexOf("class=\"openapi-media-type-select", StringComparison.Ordinal) <
            html.IndexOf("role=\"tablist\"", StringComparison.Ordinal), html);
        Assert.Contains("&quot;type&quot;", html);
        Assert.Contains("&quot;properties&quot;", html);
        Assert.DoesNotContain("class=\"openapi-schema\"", html);
    }

    [Fact]
    public async Task SeparatesAndEscapesOpenApiTitlesAndSummaries()
    {
        var html = await RenderHtml("""
            {
              "openapi": "3.1.0",
              "info": { "title": "Orders <script>", "version": "1.0" },
              "paths": {
                "/orders": {
                  "get": {
                    "tags": ["Orders"],
                    "summary": "Get `order` <details>",
                    "operationId": "getOrder",
                    "responses": {
                      "204": {
                        "description": "Done",
                        "content": { "text/plain": { "schema": { "type": "string" } } }
                      }
                    }
                  }
                }
              }
            }
            """);

        Assert.Contains("<p class=\"openapi-api-title\"><strong>Orders &lt;script&gt;</strong></p>", html);
        Assert.Contains("<p class=\"openapi-api-version\">Version <code>1.0</code></p>", html);
        Assert.Contains("<span class=\"openapi-operation-summary\">Get `order` &lt;details&gt;</span></summary>", html);
        Assert.Contains("<p class=\"openapi-operation-id\">Operation ID <code>getOrder</code></p>", html);
        Assert.Contains("Response <span class=\"openapi-response-position\">1 of 1</span>", html);
        Assert.Contains("Media Type <span class=\"openapi-media-type-position\">1 of 1</span>", html);
        Assert.DoesNotContain("<p><strong>Orders <script>", html);
    }

    [Theory]
    [InlineData("`1.0")]
    [InlineData("1.0`")]
    public async Task RendersCodeSpansWhenValuesStartOrEndWithBackticks(string version)
    {
        var html = await RenderHtml($$"""
            {
              "openapi": "3.1.0",
              "info": { "title": "Orders", "version": "{{version}}" },
              "paths": {}
            }
            """);

        Assert.Contains($"<p class=\"openapi-api-version\">Version <code>{version}</code></p>", html);
    }

    [Fact]
    public void ResolvesInternalReferencesAndRefreshesCachedSpecifications()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mdu-openapi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var markdownPath = Path.Combine(directory, "orders.md");
        var specPath = Path.Combine(directory, "orders.json");
        try
        {
            const string pageContent = "# Orders\n\n```openapi\norders.json\n```\n";
            File.WriteAllBytes(specPath, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(ReferencedSpecification("Order"))]);
            var firstRender = OpenApiMarkdownRenderer.ExpandCodeBlocks(pageContent, markdownPath, out var specPages);
            Assert.Single(specPages);
            Assert.Equal("orders-spec.html", specPages[0].FileName);
            Assert.Contains("class=\"openapi-spec-link\" href=\"orders-spec.html\">OpenAPI specification</a>", firstRender);
            Assert.Contains("```json title=\"orders.json\"", specPages[0].Markdown);
            Assert.DoesNotContain("\uFEFF", specPages[0].Markdown, StringComparison.Ordinal);

            var pdfRender = OpenApiMarkdownRenderer.ExpandCodeBlocks(pageContent, markdownPath, includeSpecificationPageLink: false);
            Assert.DoesNotContain("openapi-spec-link", pdfRender);
            Assert.Contains("#### Path Parameters", firstRender);
            Assert.DoesNotContain("openapi-api-title", firstRender);
            Assert.Contains("<span class=\"openapi-path-parameter openapi-path-parameter-color-1\">id</span>", firstRender);
            var plainMarkdown = System.Text.RegularExpressions.Regex.Replace(firstRender, "<[^>]+>", "");
            Assert.Contains("| id: string |", plainMarkdown);
            Assert.Contains("id<span class=\"openapi-required-marker\"", firstRender);
            Assert.Contains("#### Request body", firstRender);
            Assert.Contains("##### Response `201`", firstRender);
            Assert.Contains("**`Order`**", firstRender);
            Assert.Contains($"**`Order`**{Environment.NewLine}{Environment.NewLine}Order description.", firstRender);
            Assert.Contains("| id: string | Identifier |", plainMarkdown);
            Assert.DoesNotContain("## Schemas", firstRender);

            File.WriteAllText(specPath, ReferencedSpecification("UpdatedOrder"));
            var secondRender = OpenApiMarkdownRenderer.ExpandCodeBlocks(pageContent, markdownPath);
            Assert.Contains("**`UpdatedOrder`**", secondRender);
            Assert.DoesNotContain("**`Order`**", secondRender);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExpandsMultipleInlineAndFileOpenApiBlocksInPlace()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mdu-openapi-multi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var markdownPath = Path.Combine(directory, "guide.md");
        File.WriteAllText(Path.Combine(directory, "launches.yaml"), """
            openapi: 3.1.0
            info:
              title: Launches API
              version: 1.0.0
            paths:
              /launches:
                get:
                  tags: [Launches]
                  summary: List launches
                  responses:
                    '200':
                      description: OK
            """);
        try
        {
            const string markdown = """
                # Spaceport Guide

                ## API overview

                ## Launch control

                ```openapi
                ./launches.yaml
                ```

                ## Beacon ping

                Inline specifications work anywhere on the page:

                ```openapi
                openapi: 3.1.0
                info:
                  title: Beacon API
                  version: 2.0.0
                paths:
                  /ping:
                    get:
                      tags: [Beacon]
                      summary: Ping orbital beacon
                      responses:
                        '204':
                          description: Pong
                ```

                ## Next steps

                Continue reading the mission handbook.
                """;

            var expanded = OpenApiMarkdownRenderer.ExpandCodeBlocks(markdown, markdownPath, out var specPages);
            Assert.Equal(2, specPages.Count);
            Assert.Equal("guide-launches-spec.html", specPages[0].FileName);
            Assert.Equal("guide-beacon-api-spec.html", specPages[1].FileName);
            Assert.Contains("[← Back to API reference](guide.html#openapi-overview-1)", specPages[0].Markdown);
            Assert.Contains("[← Back to API reference](guide.html#openapi-overview-2)", specPages[1].Markdown);

            var output = new StringBuilder();
            using var writer = new StringWriter(output);
            await new HtmlRenderer().WriteHtml(writer, "Spaceport Guide", string.Empty, 0, expanded, string.Empty);
            var html = output.ToString();

            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "<section class=\"openapi-tag-list\"").Count);
            Assert.Contains("<h2 id=\"api-overview\">API overview", html);
            Assert.Contains("<h2 id=\"openapi-overview-1\" class=\"openapi-overview\">API overview", html);
            Assert.Contains("<h2 id=\"openapi-overview-2\" class=\"openapi-overview\">API overview", html);
            Assert.Contains("href=\"guide-launches-spec.html\">OpenAPI specification</a>", html);
            Assert.Contains("href=\"guide-beacon-api-spec.html\">OpenAPI specification</a>", html);
            Assert.Contains("<h2 id=\"next-steps\">Next steps", html);
            Assert.True(html.LastIndexOf("</section>", StringComparison.Ordinal) < html.IndexOf("<h2 id=\"next-steps\">", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SpecificationPageFileNamesAvoidCollidingWithSiblingMarkdownPages()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mdu-openapi-collision-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var markdownPath = Path.Combine(directory, "guide.md");
            File.WriteAllText(markdownPath, "# Guide\n\n```openapi\n./orders.yaml\n```\n");
            File.WriteAllText(Path.Combine(directory, "guide-orders-spec.md"), "# Existing Spec Page\n");
            File.WriteAllText(Path.Combine(directory, "orders.yaml"), """
                openapi: 3.1.0
                info:
                  title: Orders API
                  version: 1.0.0
                paths: {}
                """);

            var expanded = OpenApiMarkdownRenderer.ExpandCodeBlocks(
                File.ReadAllText(markdownPath),
                markdownPath,
                out var specPages);

            Assert.Single(specPages);
            Assert.Equal("guide-orders-spec-2.html", specPages[0].FileName);
            Assert.Contains("href=\"guide-orders-spec-2.html\">OpenAPI specification</a>", expanded);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SpecificationPageFileNamesAvoidCollidingAcrossSiblingOpenApiPages()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mdu-openapi-sibling-collision-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var firstPage = Path.Combine(directory, "a.md");
            var secondPage = Path.Combine(directory, "a-b.md");
            File.WriteAllText(firstPage, """
                # Page A

                ```openapi
                openapi: 3.1.0
                info:
                  title: B C
                  version: 1.0.0
                paths: {}
                ```
                """);
            File.WriteAllText(secondPage, """
                # Page A-B

                ```openapi
                openapi: 3.1.0
                info:
                  title: C
                  version: 1.0.0
                paths: {}
                ```
                """);

            var expandedSecond = OpenApiMarkdownRenderer.ExpandCodeBlocks(
                File.ReadAllText(secondPage),
                secondPage,
                out var secondSpecPages);
            var expandedFirst = OpenApiMarkdownRenderer.ExpandCodeBlocks(
                File.ReadAllText(firstPage),
                firstPage,
                out var firstSpecPages);

            Assert.Single(firstSpecPages);
            Assert.Single(secondSpecPages);
            Assert.Equal("a-b-c-spec.html", firstSpecPages[0].FileName);
            Assert.Equal("a-b-c-spec-2.html", secondSpecPages[0].FileName);
            Assert.Contains("href=\"a-b-c-spec.html\">OpenAPI specification</a>", expandedFirst);
            Assert.Contains("href=\"a-b-c-spec-2.html\">OpenAPI specification</a>", expandedSecond);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SpecificationPageFileNamesAvoidCollidingAcrossOutputSiblingsInDifferentSourceFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdu-openapi-cross-folder-{Guid.NewGuid():N}");
        var importedDirectory = Path.Combine(root, "imported");
        var localDirectory = Path.Combine(root, "local");
        Directory.CreateDirectory(importedDirectory);
        Directory.CreateDirectory(localDirectory);
        try
        {
            var importedPage = Path.Combine(importedDirectory, "a.md");
            var localPage = Path.Combine(localDirectory, "a-b.md");
            File.WriteAllText(importedPage, """
                # Imported Page A

                ```openapi
                openapi: 3.1.0
                info:
                  title: B C
                  version: 1.0.0
                paths: {}
                ```
                """);
            File.WriteAllText(localPage, """
                # Local Page A-B

                ```openapi
                openapi: 3.1.0
                info:
                  title: C
                  version: 1.0.0
                paths: {}
                ```
                """);

            var outputSiblings = new[]
            {
                new OpenApiSiblingPage(importedPage, "a.html", importedDirectory),
                new OpenApiSiblingPage(localPage, "a-b.html", null)
            };

            var expandedLocal = OpenApiMarkdownRenderer.ExpandCodeBlocks(
                File.ReadAllText(localPage),
                localPage,
                out var localSpecPages,
                allowedRootPath: null,
                pageOutputFileName: "a-b.html",
                siblingPages: outputSiblings);
            var expandedImported = OpenApiMarkdownRenderer.ExpandCodeBlocks(
                File.ReadAllText(importedPage),
                importedPage,
                out var importedSpecPages,
                allowedRootPath: importedDirectory,
                pageOutputFileName: "a.html",
                siblingPages: outputSiblings);

            Assert.Single(importedSpecPages);
            Assert.Single(localSpecPages);
            Assert.Equal("a-b-c-spec.html", importedSpecPages[0].FileName);
            Assert.Equal("a-b-c-spec-2.html", localSpecPages[0].FileName);
            Assert.Contains("href=\"a-b-c-spec.html\">OpenAPI specification</a>", expandedImported);
            Assert.Contains("href=\"a-b-c-spec-2.html\">OpenAPI specification</a>", expandedLocal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExpandsOpenApiBlocksInsideBlockquotesAndListItems()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mdu-openapi-containers-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var markdownPath = Path.Combine(directory, "guide.md");
            File.WriteAllText(Path.Combine(directory, "orders.yaml"), """
                openapi: 3.1.0
                info:
                  title: Orders API
                  version: 1.0.0
                paths:
                  /orders:
                    get:
                      tags: [Orders]
                      summary: List orders
                      responses:
                        '200':
                          description: OK
                """);

            const string markdown = """
                # Container Guide

                > Quoted intro
                >
                > ```openapi
                > ./orders.yaml
                > ```
                >
                > Quoted outro

                - List item intro

                  ```openapi
                  openapi: 3.1.0
                  info:
                    title: Inline List API
                    version: 2.0.0
                  paths:
                    /status:
                      get:
                        tags: [Status]
                        summary: Check status
                        responses:
                          '204':
                            description: Healthy
                  ```

                  List item outro
                """;

            var expanded = OpenApiMarkdownRenderer.ExpandCodeBlocks(markdown, markdownPath, out var specPages);
            Assert.Equal(2, specPages.Count);

            var output = new StringBuilder();
            using var writer = new StringWriter(output);
            await new HtmlRenderer().WriteHtml(writer, "Container Guide", string.Empty, 0, expanded, string.Empty);
            var html = output.ToString();

            var blockquoteStart = html.IndexOf("<blockquote>", StringComparison.Ordinal);
            var blockquoteEnd = html.IndexOf("</blockquote>", blockquoteStart, StringComparison.Ordinal);
            Assert.True(blockquoteStart >= 0 && blockquoteEnd > blockquoteStart, html);
            var blockquoteHtml = html[blockquoteStart..blockquoteEnd];
            Assert.Contains("Quoted intro", blockquoteHtml);
            Assert.Contains("<section class=\"openapi-tag-list\"", blockquoteHtml);
            Assert.Contains("Quoted outro", blockquoteHtml);

            var listStart = html.IndexOf("<ul>", blockquoteEnd, StringComparison.Ordinal);
            var listEnd = html.IndexOf("</ul>", listStart, StringComparison.Ordinal);
            Assert.True(listStart >= 0 && listEnd > listStart, html);
            var listHtml = html[listStart..listEnd];
            Assert.Contains("List item intro", listHtml);
            Assert.Contains("<section class=\"openapi-tag-list\"", listHtml);
            Assert.Contains("List item outro", listHtml);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ListsEnumValuesAndDefaultsOnTheRowsThatUseThem()
    {
        var html = await RenderHtml("""
            {
              "openapi": "3.0.1",
              "info": { "title": "Features", "version": "1" },
              "servers": [{ "url": "https://api.example.com", "description": "Production" }],
              "paths": { "/features": { "get": {
                "parameters": [
                  { "name": "IsActive", "in": "query", "schema": { "$ref": "#/components/schemas/ActiveSearchOption" } },
                  { "name": "Limit", "in": "query", "required": true, "schema": { "type": "integer", "format": "int32", "default": 20 } }
                ],
                "responses": { "200": { "description": "Features", "content": { "application/json": { "schema": {
                  "type": "object", "properties": {
                    "contactType": { "$ref": "#/components/schemas/ContactType" },
                    "length": { "type": "string", "format": "date-span" },
                    "file": { "type": "string", "format": "binary" },
                    "size": { "type": "integer", "format": "int64" }
                  }
                } } } } }
              } } },
              "components": { "schemas": {
                "ActiveSearchOption": { "type": "string", "enum": ["NonActive", "Active"] },
                "ContactType": { "type": "string", "description": "Contact type.", "enum": ["Request", "All"] }
              } }
            }
            """);

        var plain = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "");
        Assert.Contains("<span class=\"openapi-enum\" role=\"list\" aria-label=\"Allowed values\"><code class=\"openapi-enum-value\" role=\"listitem\">NonActive</code><code class=\"openapi-enum-value\" role=\"listitem\">Active</code></span>", html);
        Assert.Contains("<code class=\"openapi-enum-value\" role=\"listitem\">Request</code>", html);
        Assert.Contains("<span class=\"openapi-default\" data-tooltip=\"Default value\"> = 20</span>", html);
        Assert.Contains("length: dateSpan", plain);
        Assert.Contains("file: binary", plain);
        Assert.Contains("size: int64", plain);
        Assert.DoesNotContain("string: Request", plain);
        Assert.DoesNotContain("openapi-response-reference-schema", html);
        Assert.DoesNotContain("class=\"openapi-type-reference\"", html);
        Assert.Contains("<div class=\"openapi-servers\"><span class=\"openapi-servers-label\">Server</span><ul><li><code>https://api.example.com</code><span class=\"openapi-server-description\">Production</span></li></ul></div>", html);
    }

    [Fact]
    public async Task EmitsRepeatedTabContentsOnceWithScopedAnchors()
    {
        var html = await RenderHtml("""
            {
              "openapi": "3.1.0",
              "info": { "title": "Orders", "version": "1" },
              "paths": { "/orders": { "get": { "responses": {
                "200": { "description": "Found", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Order" } } } },
                "400": { "description": "Bad", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Problem" } } } },
                "500": { "description": "Error", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Problem" } } } }
              } } } },
              "components": { "schemas": {
                "Order": { "type": "object", "properties": { "id": { "type": "string" } } },
                "Problem": { "type": "object", "properties": { "detail": { "$ref": "#/components/schemas/Detail" } } },
                "Detail": { "type": "object", "properties": { "title": { "type": "string" } } }
              } }
            }
            """);

        var templates = System.Text.RegularExpressions.Regex.Matches(html, "<template data-openapi-shared=\"(?<id>[^\"]+)\">");
        Assert.Equal(3, templates.Count);
        var placeholders = System.Text.RegularExpressions.Regex.Matches(html,
            "<div class=\"openapi-shared-content\" data-openapi-shared=\"(?<id>[^\"]+)\" data-openapi-scope=\"(?<scope>[^\"]*)\"></div>");
        Assert.Equal(6, placeholders.Count);
        var schemaScopes = placeholders.Select(match => match.Groups["scope"].Value).Where(scope => scope.Length > 0).Distinct().ToArray();
        Assert.Equal(2, schemaScopes.Length);
        Assert.All(schemaScopes, scope => Assert.StartsWith("openapi-response-schema-", scope));
        Assert.Contains("href=\"#openapi-shared-scope-detail\"", html);
        Assert.DoesNotContain("openapi-shared-->", html);
        Assert.Contains("<td><span class=\"openapi-declaration\"><span class=\"openapi-declaration-name\">id</span>", html);
        Assert.Contains("template[data-openapi-shared=", html);
    }

    [Fact]
    public async Task MarksDeprecationSecurityConstraintsAndHeaders()
    {
        var html = await RenderHtml("""
            {
              "openapi": "3.1.0",
              "info": { "title": "Items", "version": "1", "license": { "name": "MIT", "identifier": "MIT" } },
              "security": [{ "bearer": [] }],
              "paths": {
                "/items": { "get": {
                  "summary": "List items", "description": "List items.",
                  "parameters": [
                    { "name": "q", "in": "query", "deprecated": true, "schema": { "type": "string", "minLength": 3, "maxLength": 64 } },
                    { "name": "limit", "in": "query", "example": 50, "schema": { "type": "integer", "minimum": 1, "maximum": 100 } }
                  ],
                  "responses": { "200": {
                    "description": "Items",
                    "headers": { "X-Rate-Limit": { "required": true, "schema": { "type": "integer", "format": "int32" } } },
                    "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Item" } } }
                  } }
                } },
                "/items/{id}": { "delete": {
                  "deprecated": true, "security": [],
                  "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
                  "responses": { "204": { "description": "Deleted" } }
                } }
              },
              "components": {
                "securitySchemes": { "bearer": { "type": "http", "scheme": "bearer", "bearerFormat": "JWT" } },
                "schemas": { "Item": { "type": "object", "properties": {
                  "id": { "type": "string", "readOnly": true },
                  "password": { "type": "string", "writeOnly": true },
                  "price": { "type": "number", "exclusiveMinimum": 0 },
                  "labels": { "type": "object", "additionalProperties": { "type": "string" } },
                  "legacyCode": { "type": "string", "deprecated": true },
                  "legacy": { "$ref": "#/components/schemas/Legacy" }
                } },
                "Legacy": { "type": "object", "deprecated": true, "properties": { "code": { "type": "string" } } } }
              }
            }
            """);

        var plain = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "");
        Assert.Contains("<details class=\"openapi-endpoint openapi-method-delete openapi-endpoint-deprecated\">", html);
        Assert.Contains("This operation is deprecated and should no longer be used.", html);
        Assert.Contains("data-deprecated=\"1\" data-tooltip=\"1 DELETE endpoint, 1 deprecated\"", html);
        Assert.Contains("<input type=\"checkbox\" class=\"openapi-show-deprecated\" checked><span class=\"openapi-switch\" aria-hidden=\"true\"></span>Show deprecated <span class=\"openapi-deprecated-filter-count\">1</span>", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "class=\"openapi-lock\""));
        Assert.Contains("data-tooltip=\"Requires bearer\"", html);
        Assert.Contains("<span class=\"openapi-security-kind\">Bearer token (JWT)</span>", html);
        Assert.Contains("License MIT", plain);
        Assert.DoesNotContain("<p>List items.</p>", html);
        Assert.Contains("openapi-declaration openapi-declaration-deprecated", html);
        Assert.Contains("<span class=\"openapi-facet\">length 3–64</span>", html);
        Assert.Contains("<span class=\"openapi-facet\">≥ 1</span><span class=\"openapi-facet\">≤ 100</span><span class=\"openapi-facet\">example <code>50</code></span>", html);
        Assert.Contains("<span class=\"openapi-facet\">read-only</span>", html);
        Assert.Contains("<span class=\"openapi-facet\">&gt; 0</span>", html);
        Assert.Contains("labels: [string: string]", plain);
        Assert.Contains("X-Rate-Limit: int32", plain);
        Assert.Contains("Response headers", html);
        Assert.Contains("&quot;additionalProp1&quot;", html);
        Assert.Matches("<h6 id=\"[^\"]+-legacy\" class=\"openapi-response-reference-schema\"><code>Legacy</code> <span class=\"openapi-badge openapi-badge-deprecated\">Deprecated</span>", html);
    }

    [Theory]
    [InlineData(true, "password", "id")]
    [InlineData(false, "id", "password")]
    public async Task GeneratedExamplesFollowAccessModesAndBounds(bool request, string included, string excluded)
    {
        var html = await RenderHtml($$"""
            {
              "openapi": "3.1.0",
              "info": { "title": "Accounts", "version": "1" },
              "paths": { "/accounts": { "post": {
                "requestBody": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Account" } } } },
                "responses": { "201": { "description": "Created", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Account" } } } } }
              } } },
              "components": { "schemas": { "Account": { "type": "object", "properties": {
                "id": { "type": "string", "readOnly": true },
                "password": { "type": "string", "writeOnly": true },
                "balance": { "type": "number", "exclusiveMinimum": 0 },
                "level": { "type": "integer", "minimum": 3 }
              } } } }
            }
            """);

        var section = request
            ? html[html.IndexOf("class=\"openapi-request-body\"", StringComparison.Ordinal)..html.IndexOf("class=\"openapi-responses\"", StringComparison.Ordinal)]
            : html[html.IndexOf("class=\"openapi-responses\"", StringComparison.Ordinal)..];
        var example = System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Match(section, "<pre.*?</pre>", System.Text.RegularExpressions.RegexOptions.Singleline).Value, "<[^>]+>", "");
        Assert.Contains($"&quot;{included}&quot;", example);
        Assert.DoesNotContain($"&quot;{excluded}&quot;", example);
        Assert.Contains("&quot;balance&quot;: 1", example);
        Assert.Contains("&quot;level&quot;: 3", example);
    }

    private static async Task<string> RenderHtml(string specification)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mdu-openapi-html-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var markdownPath = Path.Combine(directory, "orders.md");
            File.WriteAllText(Path.Combine(directory, "orders.json"), specification);
            var markdown = OpenApiMarkdownRenderer.ExpandCodeBlocks("# Orders\n\n```openapi\norders.json\n```\n", markdownPath);
            var output = new StringBuilder();
            using var writer = new StringWriter(output);
            await new HtmlRenderer().WriteHtml(writer, "Orders", string.Empty, 0, markdown, string.Empty);
            return output.ToString();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string ReferencedSpecification(string schemaName) => $$"""
        {
          "openapi": "3.1.0",
          "info": { "title": "Orders", "version": "1" },
          "paths": {
            "/orders/{id}": {
              "parameters": [ { "$ref": "#/components/parameters/id" } ],
              "post": {
                "tags": ["Orders"],
                "requestBody": { "$ref": "#/components/requestBodies/createOrder" },
                "responses": { "201": { "$ref": "#/components/responses/created" } }
              }
            }
          },
          "components": {
            "parameters": {
              "id": { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }
            },
            "requestBodies": {
              "createOrder": { "required": true, "content": { "application/json": { "schema": { "$ref": "#/components/schemas/{{schemaName}}" } } } }
            },
            "responses": {
              "created": { "description": "Created", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/{{schemaName}}" } } } }
            },
            "schemas": {
              "{{schemaName}}": {
                "type": "object",
                "description": "Order description.",
                "required": ["id"],
                "properties": { "id": { "type": "string", "description": "Identifier" } }
              }
            }
          }
        }
        """;
}
