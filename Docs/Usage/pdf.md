---
navigation.order: 45
---

# pdf

`mdu pdf` writes your documentation to a single PDF file. It uses the same Markdown pipeline as `mdu render`, so code highlighting, icons, keyboard keys, math, callouts, and supported diagrams appear in the PDF too. The PDF writer is built in, so export works out of the box and offline.

> [!TIP]
> To offer the PDF as a download on your website, add a [PDF Link](../Features/Pdf-Link.md) to a page. `mdu render` and `mdu serve` then generate the PDF for you.

## Usage

```bash
mdu pdf [PATH [OUTPATH]] [options]
```

| Argument | Meaning | Default |
| --- | --- | --- |
| `PATH` | A documentation folder or one Markdown file | `./docs` |
| `OUTPATH` | The PDF file to write | `./documentation.pdf`, or the Markdown file's name with `.pdf` |

| Option | Effect |
| --- | --- |
| `--page <path>` | Export only this page and [its subpages](#export-a-topic-and-its-subpages) |

See [common options](README.md#common-options) for `--pattern` and `--hidden`.

## Examples

```bash
# ./docs to ./documentation.pdf
mdu pdf

# A folder to a chosen file
mdu pdf manual build/manual.pdf

# A single page
mdu pdf manual/Math.md build/math.pdf

# One topic and its subpages
mdu pdf manual build/api.pdf --page API/README.md
```

## Export a topic and its subpages

Use `--page` with a documentation folder to export one page and all its descendants in the navigation tree:

```bash
mdu pdf manual build/api.pdf --page API/README.md
```

The selected page becomes the start of the PDF, followed by its descendants in navigation order, with a title page and table of contents. Sibling pages and ancestors are not included.

- The page path is relative to `PATH`, not to the current working directory.
- Use `/` as the separator. On Windows, `\` works too.
- Quote paths that contain spaces.
- [Excluded content](#exclude-content) stays out of topic exports too.

> [!NOTE]
> `--page` requires a folder as `PATH`. A single Markdown file without `--page` exports just that file. Missing pages, paths outside the documentation, and selections without any PDF-visible page fail with an error.

<details>
<summary>Selecting pages from repository imports</summary>

Selection uses the assembled navigation tree, including [repository imports](../Features/Repository-Imports.md). Use the import declaration's path or the imported page's mounted path within the site, not a path in MDU's repository cache. For example, an import declared in `API.md` can be selected with `--page API.md`, and its imported child with `--page API/Details.md`.

</details>

## Exclude content

Excluded content stays on the website and is removed only from the PDF. Choose the smallest scope that fits:

| Exclude | Markdown |
| --- | --- |
| [A whole page](#exclude-pages) | `pdf.visible: false` in the front matter |
| [A heading and its section](#exclude-sections) | `{.pdf-hidden}` after the heading text |
| [Any range of content](#exclude-a-range) | `<!-- pdf:hidden -->` before it and `<!-- /pdf:hidden -->` after it |

These rules apply to `mdu pdf` and to every [PDF Link](../Features/Pdf-Link.md) download. In addition:

- Excluded content must be complete HTML: every HTML element opened in it is also closed in it, in the right order. Prose such as `List<String>` counts as an HTML tag; write it as code, for example `` `List<String>` ``. The text of `<script>`, `<style>`, and `<template>` is ignored.
- Footnotes referenced only by excluded content are excluded too. A footnote definition written in excluded content is excluded with it; if visible text refers to it, the export stops with an error. Definitions written after the last paragraph, list, or other block of a hidden section, such as the ones collected at the end of a page, are not part of the section: visible text can still use them. The same applies to link reference definitions such as `[label]: https://example.org`.
- Links to excluded content are not clickable in the PDF.

> [!WARNING]
> Incomplete HTML in excluded content and invalid markers stop the PDF export with an error that names the page. This also makes `mdu render` and the start of `mdu serve` fail when a page links to the PDF, so a CI build catches the mistake. While `mdu serve` is running, it reports the error and keeps updating pages; the affected PDFs are unavailable until the error is fixed, and all other PDFs are still updated.

### Exclude pages

Set `pdf.visible` in a page's front matter to exclude the page from folder exports and their table of contents:

```yaml title="Front matter"
---
pdf.visible: false
---
```

The nested form `pdf: { visible: false }` works as well.

### Exclude sections

Add the `pdf-hidden` class to a heading to exclude the heading and everything up to the next heading of the same or a higher level:

```markdown title="Markdown"
## Release checklist

Steps every reader needs.

## Internal notes {.pdf-hidden}

Excluded from the PDF.

### Contacts

Excluded too, because it belongs to "Internal notes".

## Troubleshooting

Back in the PDF.
```

- The section ends at the next heading of the same or a higher level in the same list item, block quote, or callout, or at the end of that container. Headings inside a nested block quote, list, or callout do not end it.
- A section inside an HTML element such as `<details>` ends before the element's closing tag. Put that tag on its own line after a blank line.
- An HTML element that starts in the section must also end in it, before the next heading of the same or a higher level. To exclude HTML that encloses headings, use a [range](#exclude-a-range).
- An [OpenAPI reference](../Features/OpenAPI.md) inside the section is excluded with it, even though it contains headings of its own.
- Renderers without attribute support, such as GitHub, show `{.pdf-hidden}` as part of the heading. Use a [range](#exclude-a-range) if the page must look clean there.
- If you exclude the page's level-one heading, the PDF uses the `title` from the front matter as the page title. Without one, it uses the file name, or the folder name for a `README.md` or `index.md` page. PDF Link downloads of that page are named the same way.

### Exclude a range

Enclose any content in comment markers on their own lines. Renderers such as GitHub do not display the markers:

```markdown title="Markdown"
<!-- pdf:hidden -->
Content that appears only on the website.
<!-- /pdf:hidden -->
```

- Spaces and letter case do not matter; `<!--pdf:hidden-->` and `<!-- PDF: Hidden -->` work too.
- Start and end markers must be at the same nesting level, for example both at the top level or both inside the same list item.
- A marker counts only on its own line. A misspelled marker such as `<!-- pdf-hidden -->` or `<!-- pdf:hide -->`, a marker inside a paragraph or other HTML, and a start marker directly after HTML stop the export with an error instead of being ignored. Markers inside code, `<script>`, and `<style>` are only text.
- An end marker may directly follow HTML, so a range can enclose an embed without blank lines:

  ```markdown title="Markdown"
  <!-- pdf:hidden -->
  <iframe src="https://example.org/demo"></iframe>
  <!-- /pdf:hidden -->
  ```

## PDF output

### Layout

- Pages are A4 and follow the order of the documentation navigation.
- A folder export starts with a title page and a linked table of contents; a single file has neither.
- Content pages repeat the document title in a small header. The footer shows the current page and the total page count.
- Links between included pages, links on diagram nodes, links in tables, and external links stay clickable.
- Light fills distinguish code and table headers while keeping pages readable on a monochrome printer.
- Embedded HTML is parsed using browser-style HTML rules, including recovery of unclosed or misnested elements. In code examples, escape `<` as `&lt;` in raw HTML or use Markdown code blocks.

### Page breaks

- Wrapped headings, consecutive headings, and callout titles stay with the start of the following block.
- Short paragraphs stay together. Longer paragraphs avoid a single line at the bottom or top of a page.
- Short code blocks stay together when possible; long code blocks continue on the next page.
- New sections use the remaining page space when their heading and opening content fit. At the top of a page, extra section dividers and heading spacing are omitted.

### Tables

- Tables retain inline emphasis, code, icons, and keyboard shortcuts.
- Tables repeat their leading header rows on continuation pages when the headers leave room for body content.
- Ordinary rows stay together; very tall rows are split into readable portions within the page margins.

### Static equivalents

To keep PDF generation fast and self-contained, MDU renders interactive and external elements as static equivalents:

| Element | In the PDF |
| --- | --- |
| Images | Their alt text; remote image URLs stay clickable |
| Embedded videos | Their link |
| Emoji | Their shortcodes, because the built-in PDF fonts have no emoji glyphs |
| Icon search | Not shown; it exists only in HTML |
| Scripts, styles, and templates | Not shown, as in a browser; inside `<textarea>`, their text is kept |
| `<noscript>` content | Shown, because the PDF does not run scripts |
| Hero banner | Its title, text, and button labels as plain text |
