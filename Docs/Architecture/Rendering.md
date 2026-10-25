---
navigation.order: 3
---

# Rendering

`Markdown.Renderer` turns the Markdown of one page into a complete HTML page, or into PDF content. The page tree supplies everything around the content, such as navigation and breadcrumbs; the renderer supplies the content itself and the page frame.

Pages with `openapi` fenced code blocks have each block expanded into a generated API reference before rendering. `OpenApiMarkdownRenderer` reads a local JSON or YAML specification file or parses an inline specification, emits a companion static specification page with the full syntax-highlighted source, and hands the combined content to the normal HTML and PDF renderers. Large specifications repeat the same examples and schemas across many responses, so the HTML renderer emits a tab content that occurs more than once only once as a `<template>`. The page script copies it into an Example, Schema, or Schema JSON tab when that tab becomes visible in an open endpoint, and restores the schema anchor prefix of each media type in the copy. See the [OpenAPI feature](../Features/OpenAPI.md) for its usage and supported content.

## From Markdown to HTML

```mermaid
flowchart LR
    accTitle: Rendering one page
    Source([Markdown]) --> Parse[Markdig with<br>MDU extensions]
    Parse --> Post[Post-processing]
    Post --> Template[[HTML template]]
    Template --> Page([HTML page])
```

1. **Parse.** Markdig parses the page with the extensions listed below.
2. **Rewrite links.** Relative links to `.md` files point to the rendered `.html` pages; `README.md` becomes `index.html`. Anchors and query strings are kept.
3. **Render.** Markdig writes the HTML. Code blocks go through MDU's own [code block renderer](#code-blocks).
4. **Post-process.** Standalone video links become consent placeholders, a full-width hero moves above the page layout, headings get permalinks, and the *On this page* list is collected.
5. **Fill the template.** The embedded `main.html` contains placeholders such as `%%MARKDOWN%%`, `%%NAVIGATION_ITEMS%%`, and `%%PAGE_BREADCRUMBS%%`. `HtmlRenderer` replaces them directly with HTML-encoded values.

## Markdig extensions

| Extension | Adds |
| --- | --- |
| Markdig advanced extensions | Pipe and grid tables, footnotes, task lists, emphasis extras, heading IDs, automatic links, and attribute lists |
| `HeadingIdExtension` | Keeps a leading number in heading IDs, as GitHub and MkDocs do: `## 3.1 Overview` gets `31-overview` |
| `ScrollableTableExtension` | Wraps wide tables in a horizontally scrollable container |
| `RenderedMathExtension` | [Math](../Features/Math.md) as inline SVG |
| `GitHubAlertExtension` | [Callouts](../Features/Callouts.md) with GitHub's alert syntax |
| `ShortcodeExtension` | [Icons and emojis](../Features/Icons-and-Emojis.md) from shortcodes |
| `KeysExtension` | [Keyboard keys](../Features/Keyboard-Keys/README.md) such as ++ctrl+alt+del++ |
| `HighlighterExtension` | The code block renderer below |

## Code blocks

Every fenced code block passes through one renderer, which picks the output by the block's language:

```mermaid
flowchart TD
    accTitle: Code block dispatch
    Block[Fenced code block] --> Lang{Language}
    Lang -->|icon-search| Search[Icon search widget]
    Lang -->|hero| Hero[Animated hero banner]
    Lang -->|openapi| OpenApi[API reference and spec page]
    Lang -->|math| Math[Formula as SVG]
    Lang -->|mermaid, plantuml, context| Diagram{Supported syntax?}
    Diagram -->|Yes| Svg[Diagram as SVG]
    Diagram -->|No| Code
    Lang -->|anything else| Code[Highlighted code with copy button]
    click Search href "../Features/Icons-and-Emojis.md#search"
    click Hero href "../Features/Hero.md"
    click OpenApi href "../Features/OpenAPI.md"
    click Math href "../Features/Math.md"
    click Svg href "../Features/Diagrams.md"
    click Code href "../Features/CodeBlocks.md"
```

Because a diagram that cannot be rendered stays a code block, a page never breaks on unknown syntax. `mdu lint` reports unsupported Mermaid syntax as `MDU010`.

## Page assets

Every page links the shared `css/main.css`. Everything else is added only to the pages that need it:

| Asset | Added when |
| --- | --- |
| `js/search.js` and `search-index.json` | Always; the search runs directly in the browser |
| Icon search script and index in `js/` | A page contains an `icon-search` block |
| Hero styles and WebGL scene, inline | A page contains a `hero` block |
| Media consent script, inline | A page contains a standalone YouTube or Vimeo link |
| Logo and favicon assets from `mdu.yml` | A logo or favicon is configured |

A rendered site looks like this:

```text
site/
├── index.html
├── search-index.json
├── css/
│   └── main.css
├── js/
│   └── search.js
├── assets/
│   └── logo.svg
└── Usage/
    ├── index.html
    └── render.html
```

## In the browser

The generated pages are self-contained static HTML. Small inline scripts add:

- light and dark themes, with an automatic mode that follows the operating system;
- the collapsible sidebar, top navigation tabs, and a drawer on narrow screens;
- client-side search with links to the matching section and highlighted hits;
- copy buttons for code blocks, image lightboxes, and footnote previews;
- the consent dialog for embedded videos.

Theme colors from `mdu.yml` become CSS custom properties. Secondary colors, lines, and hover backgrounds are mixed from them in CSS, so a custom palette stays consistent.

## PDF output

`PdfRenderer` reuses the HTML pipeline for `mdu pdf` and for the `documentation.pdf` generated by `mdu render` and `mdu serve` when the project contains a PDF Link. It renders every page to an HTML fragment, reads that fragment as XML, and lays out its blocks on A4 pages. Tables keep their inline styles and links, and diagrams and formulas are drawn from their SVG paths. Content pages repeat the document title in a running header and show page numbers with the total count in the footer. The writer uses the standard fonts built into every PDF reader for compact files; emojis appear as their shortcodes. Links between included pages become internal PDF links.
