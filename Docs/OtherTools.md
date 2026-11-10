---
navigation.order: 40
navigation.placement: top
---

# Other Tools

MDU focuses on one job: publishing a folder of plain Markdown with a single tool, a built-in theme, and easy configuration. If you are looking for a plugin ecosystem, custom UI components, versioned documentation, or translations, one of these generators is a great fit.

| Tool | Built on | Typical use |
| --- | --- | --- |
| [DocFX](https://dotnet.github.io/docfx/) | .NET | API reference generated from .NET code, combined with conceptual Markdown docs |
| [Docusaurus](https://docusaurus.io/) | React, Node.js | Product documentation with MDX components, versions, and translations |
| [Material for MkDocs](https://squidfunk.github.io/mkdocs-material/) | MkDocs, Python | Feature-rich MkDocs theme; end of life on **May 5, 2027** |
| [MkDocs](https://www.mkdocs.org/) | Python | Simple project documentation configured in one YAML file |
| [Rspress](https://rspress.rs/) | React, Rsbuild | Fast documentation sites with MDX |
| [Starlight](https://starlight.astro.build/) | Astro | Documentation sites that mix Markdown with Astro components |
| [VuePress](https://vuepress.vuejs.org/) | Vue | Documentation with Vue components in Markdown |
| [Zensical](https://zensical.org/) | Python, Rust | Successor to Material for MkDocs from the same team |

## How MDU compares

What each tool needs and brings without extra plugins:

| | MDU | Material for MkDocs | Docusaurus | Starlight | DocFX |
| --- | --- | --- | --- | --- | --- |
| Runtime to build the site | None (standalone executable) or .NET | Python | Node.js | Node.js | .NET |
| Broken links | Error in `mdu lint`, including anchors | Build warning, error with `--strict` | Build error | Plugins | Build warning |
| Path casing and structure checks | Built in (`mdu lint`) | — | — | — | — |
| Diagrams | Static SVG at build time, no JavaScript | Mermaid in the browser | Mermaid in the browser | Plugins | Mermaid in the browser |
| PDF export | Built in (`mdu pdf`) | Plugins | Plugins | Plugins | Built in |
| Page format | Markdown with extensions | Markdown with extensions | Markdown and MDX | Markdown and MDX | Markdown with extensions |

> [!TIP]
> Coming from Material for MkDocs? MDU understands the same icon shortcodes, and [`mdu fix`](./Usage/fix.md#migrating-mkdocs-material-markdown) converts admonitions, tabs, card grids, and `.pages` files to portable Markdown.
