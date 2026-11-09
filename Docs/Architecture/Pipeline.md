---
navigation.order: 2
---

# Processing pipeline

Most commands read the same sources and page structure. They then inspect the pages, update sources, or generate output.

## Page tree

The folder structure determines the page tree. A folder's `README.md`, or otherwise its `index.md`, introduces that section. Front matter controls titles, ordering, and visibility. Imported documentation joins the same tree.

That tree supplies the sidebar, breadcrumbs, previous/next links, and PDF ordering. `mdu tree` shows it directly.

## Rendering a site

1. Read the Markdown files, site settings, and linked content.
2. Prepare the page tree, navigation, and search data.
3. Generate pages, assets, and any linked PDF downloads.
4. Finish successfully once the required output is complete.

Use [`mdu render`](../Usage/render.md) for output you intend to publish. [Rendering](Rendering.md) describes the generated files.

## Live preview

[`mdu serve`](../Usage/serve.md) makes the first usable page available early and completes remaining work in the background.

- Saving a page normally updates that page and its affected dependencies.
- Shared navigation or configuration changes can rebuild the site.
- Pending or failed PDF downloads are visibly marked and are not served as current.
- Invalid intermediate saves can recover after correction. Failed page updates return an error instead of an old successful response.

This keeps preview readiness separate from completion of the whole site. [Performance benchmarks](Benchmarks.md) shows the targets and measured behavior.

## Other commands

| Command | Outcome |
| --- | --- |
| `new` | Creates a starter documentation project |
| `lint` | Reports content and structure problems |
| `fix` | Applies supported source migrations and automatic fixes |
| `pdf` | Generates a complete PDF from the selected pages |
| `page move` | Moves pages and updates their links |
| `page links` | Lists the links that point to a file |
| `check-site` | Checks local links, assets, and anchors in a rendered site, including content added after rendering |

The [implementation details](Details/Pipeline.md) explain visitors, dependencies, caching, recovery, and the remaining scheduling limits.
