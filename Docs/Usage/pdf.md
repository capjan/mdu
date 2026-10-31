---
navigation.order: 45
---

# pdf

`mdu pdf` combines the Markdown pages of a folder into one PDF file. It uses the same Markdown pipeline as `mdu render`, so code highlighting, icons, keyboard keys, math, callouts, and supported diagrams appear in the PDF too. MDU includes a built-in PDF writer, so PDF export works out of the box and offline.

For automatic PDF generation when a page contains a PDF Link, and for placing links to it inside rendered pages, see the [PDF Link feature](../Features/Pdf-Link.md).

## Usage

```bash
mdu pdf [PATH [OUTPATH]] [options]
```

| Argument | Meaning | Default |
| --- | --- | --- |
| `PATH` | A documentation folder or one Markdown file | `./docs` |
| `OUTPATH` | The PDF file to write | `./documentation.pdf`, or the Markdown file's name with `.pdf` |

See [common options](README.md#common-options) for `--pattern` and `--hidden`.

```bash
mdu pdf
mdu pdf ./ProjectDocs ./build/manual.pdf
mdu pdf ./ProjectDocs/Features/Math.md ./build/math.pdf
```

## Export a topic and its subpages

Use `--page` with a documentation folder to export one page and all its descendants in the navigation tree:

```bash
mdu pdf ./ProjectDocs ./build/api.pdf --page API/README.md
```

The page path is relative to `PATH`, not to the current working directory. Quote paths containing spaces. Use `/` as the separator; on Windows the CLI also accepts native `\` separators. The selected page becomes the PDF's root, followed by its descendants in navigation order, with a title page and table of contents. Sibling pages and ancestors are not included. `pdf.visible` still applies to each page.

Selection uses the assembled navigation tree, including [repository imports](../Features/Repository-Imports.md). Use the import declaration's path or the imported page's mounted path within the site, not a path in MDU's repository cache. For example, an import declared in `API.md` can be selected with `--page API.md`; its imported child can be selected with `--page API/Details.md`.

The option requires a folder as `PATH`; passing a single Markdown file without `--page` still exports only that file. Missing pages, paths outside the documentation, and selections with no PDF-visible pages fail with an error.

## Layout

- Pages are A4 and follow the order of the documentation navigation.
- A folder export starts with a title page and a linked table of contents; a single file has neither.
- Wrapped headings, consecutive headings, and callout titles stay with the start of the following block when a page break is needed.
- Short paragraphs stay together. Longer paragraphs avoid a single line at the bottom or top of a page.
- Short code blocks stay together when possible; long code blocks continue on the next page.
- Content pages repeat the document title in a small header. The footer shows the current page and the total page count.
- Links between included pages, links on diagram nodes, links in tables, and external links stay clickable.
- Tables retain inline emphasis, code, icons, and keyboard shortcuts. Light fills distinguish code and table headers while keeping pages readable on a monochrome printer.
- Tables repeat their leading header rows on continuation pages when the headers leave room for body content. Ordinary rows stay together; very tall rows are split into readable portions within the page margins.
- New sections use the remaining page space when their heading and opening content fit. At the top of a page, extra section dividers and heading spacing are omitted.

## Exclude pages

To leave a page out of a folder export and its table of contents, set `pdf.visible` in its front matter:

```yaml
---
pdf.visible: false
---
```

The nested form `pdf: { visible: false }` works as well.

## How elements appear in PDF

To keep PDF generation fast and self-contained, MDU renders interactive and external elements as static equivalents:

- Images appear as their alt text; remote image URLs stay clickable.
- Embedded videos appear as their link.
- Emoji appear as their shortcodes, because the built-in PDF fonts have no emoji glyphs.
- The interactive icon search and the animated hero exist only in HTML; the hero's title, text, and button labels appear as plain text.
