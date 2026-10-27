using System.Text.Encodings.Web;
using System.Text.Json;
using Markdown.Renderer.MarkdigExtension;
using StaticCode.Icons;

namespace Markdown.Renderer;

/// <summary>
/// The search for all icon and emoji shortcodes that an <c>icon-search</c> code block adds to a page.
/// Its script and index are written next to the site's other scripts.
/// </summary>
public static class IconSearch
{
    public const string CodeBlockLanguage = "icon-search";
    public const string ScriptFileName = "icon-search.js";
    public const string IndexFileName = "icon-search-index.json";

    private static CachedIndex? _index;

    public const string Html =
        """
        <div class="icon-search">
            <div class="icon-search-controls">
                <input class="icon-search-input" type="search" autocomplete="off" spellcheck="false" placeholder="Search icons and emojis…" aria-label="Search icons and emojis" disabled>
                <div class="icon-search-filters" role="group" aria-label="Show">
                    <button type="button" data-filter="all" aria-pressed="true">All</button>
                    <button type="button" data-filter="icons" aria-pressed="false">Icons</button>
                    <button type="button" data-filter="emojis" aria-pressed="false">Emojis</button>
                </div>
            </div>
            <p class="icon-search-status" role="status" aria-live="polite">The icon search requires JavaScript.</p>
            <ul class="icon-search-results" aria-label="Icons and emojis"></ul>
            <button class="icon-search-more" type="button" hidden>Show more</button>
        </div>
        """;

    public static IReadOnlyList<string> WriteAssetsTo(string outputPath)
    {
        var scriptDirectory = Path.Combine(outputPath, "js");
        Directory.CreateDirectory(scriptDirectory);

        var scriptPath = Path.Combine(scriptDirectory, ScriptFileName);
        File.WriteAllText(scriptPath, Resources.IconSearchScriptContent());
        var indexPath = Path.Combine(scriptDirectory, IndexFileName);
        File.WriteAllBytes(indexPath, GetIndex());
        return new[] { scriptPath, indexPath };
    }

    // The index is created again only when the custom icon sets change.
    private static byte[] GetIndex()
    {
        var sets = IconCatalog.Sets;
        if (_index is { } cached && ReferenceEquals(cached.Sets, sets)) return cached.Index;
        var index = CreateIndex(sets);
        _index = new CachedIndex(sets, index);
        return index;
    }

    private sealed record CachedIndex(IReadOnlyList<IconSet> Sets, byte[] Index);

    private static byte[] CreateIndex(IReadOnlyList<IconSet> sets)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteStartArray("sets");
            foreach (var set in sets)
            {
                json.WriteStartObject();
                json.WriteString("prefix", set.ShortcodePrefix);
                json.WriteString("name", set.Name);
                json.WriteStartArray("icons");
                foreach (var icon in set.VisibleIcons)
                {
                    // [name, viewBox, body] or, for themed icons, [name, viewBox, body, dark viewBox, dark body]
                    json.WriteStartArray();
                    json.WriteStringValue(icon.Shortcode[set.ShortcodePrefix.Length..]);
                    json.WriteStringValue(icon.Glyph.ViewBox);
                    json.WriteStringValue(icon.Glyph.Body);
                    if (icon.DarkGlyph is not null)
                    {
                        json.WriteStringValue(icon.DarkGlyph.ViewBox);
                        json.WriteStringValue(icon.DarkGlyph.Body);
                    }
                    json.WriteEndArray();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("emojis");
            foreach (var (shortcode, emoji) in ShortcodeExtension.Emojis)
            {
                json.WriteStartArray();
                json.WriteStringValue(shortcode.Trim(':'));
                json.WriteStringValue(emoji);
                json.WriteEndArray();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }

        return stream.ToArray();
    }
}
