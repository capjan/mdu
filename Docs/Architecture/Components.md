---
navigation.order: 1
---

# Components

MDU separates command handling, document processing, and output generation. They run together in one .NET tool; a published website does not need these libraries or an application server.

| Building block | Responsibility | Projects |
| --- | --- | --- |
| Commands | Interpret options, select inputs, and coordinate each operation | `Mdu` |
| Document processing | Read page structure, check content and links, and build search data | `Markdown.Document`, `Markdown.Linter`, `Markdown.Search` |
| Rendering | Generate HTML/PDF and render code, diagrams, math, and icons | `Markdown.Renderer`, `StaticCode.*` |

## Boundaries

- Markdown files and site settings are the inputs. Commands that change sources, such as `fix` and `page move`, do so explicitly.
- A shared page tree supplies navigation, breadcrumbs, and PDF ordering.
- Rendered pages and search data are static output. The local preview server adds development-only live reload and readiness information.

See the [processing pipeline](Pipeline.md) for the flow between these blocks.

## Optional details

The [project map and library reference](Details/Components.md) show dependencies, classes, third-party libraries, and the individual renderers.
