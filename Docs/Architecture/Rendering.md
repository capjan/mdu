---
navigation.order: 3
---

# Rendering

MDU interprets Markdown and generates the content at build time. Readers receive ordinary static files rather than running the documentation generator in their browser.

## Website output

| Input | Generated output |
| --- | --- |
| Markdown and front matter | HTML pages with navigation and page metadata |
| Code blocks | Highlighted code, diagrams, formulas, or API references |
| Icon shortcodes | SVG icons |
| Page headings and text | A search index linking to matching sections |
| Site settings and linked assets | Styles, logos, images, and other static files |

Unsupported diagram syntax remains readable as a code block. Feature-specific syntax is described in the [feature documentation](../Features/README.md).

## In the browser

Local JavaScript adds search, theme selection, navigation controls, and interactions such as copy buttons. An external video player is loaded only after the reader consents. Published pages do not depend on the local preview server.

## PDF output

PDF generation reuses the same content interpretation and navigation order. It removes explicitly hidden content, lays out pages, and preserves links where supported. Browser-only interactions are omitted.

[`mdu pdf`](../Usage/pdf.md) generates a selected export. PDF links can also request downloads as part of `render` or `serve`.

## Optional details

The [rendering implementation](Details/Rendering.md) explains the Markdown extensions, code-block dispatch, template, assets, browser effects, and PDF layout.
