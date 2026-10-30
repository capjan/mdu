# PDF Link

`mdu render` and `mdu serve` create PDF downloads requested by links in Markdown pages. The link `mdu:pdf` requests the combined `documentation.pdf` in the site output folder. With no PDF Link, no PDF asset is generated. During incremental Markdown updates, `mdu serve` regenerates only exports whose selected content changed or whose PDF is missing. Full site rebuilds regenerate all requested PDFs.

## Syntax

Place a link anywhere in your Markdown and choose its label:

```markdown title="Markdown"
[Download Documentation as PDF](mdu:pdf)
```

MDU resolves the link to the generated PDF, including on pages in nested folders. It renders with the site's normal link styling.

## Rendered example

[Download Documentation as PDF](mdu:pdf)

The PDF follows the documentation navigation order and honors [`pdf.visible`](../Usage/pdf.md#exclude-pages) in page front matter.

## Export one topic

Add `?root=` to select a page and all its descendants in the navigation tree:

```markdown title="Markdown"
[This topic as PDF](mdu:pdf?root=README.md)
[API as PDF](mdu:pdf?root=../API/README.md)
[Guide as PDF](mdu:pdf?root=User%20Guide/README.md)
```

The root path is relative to the Markdown page containing the link. It must identify a Markdown page inside the documentation; encode spaces as `%20`. The PDF includes the root page and its navigation descendants, not its siblings or ancestors. Each export has a title page and table of contents and honors `pdf.visible`.

PDF links in rendered OpenAPI descriptions request downloads too. Their root paths are relative to the Markdown page containing the `openapi` block.

This also works with [repository imports](Repository-Imports.md): paths refer to their location in the assembled site, not the source repository or cache. An import declaration such as `API.md` can be used as the root too.

Several topic PDFs and the full documentation PDF can coexist. Links selecting the same root share one download. Topic PDFs are generated in the reserved `_mdu-pdf/` output folder with stable generated filenames; link to them using `mdu:pdf?root=...`, not their generated filenames. Obsolete generated PDFs are removed when their last requesting link is removed. Invalid or missing roots fail the render rather than producing a broken download.

Topic download links suggest the selected page's title as the filename through the HTML `download` attribute, with characters unsuitable for filenames replaced. The generated URL stays unchanged. Full-document downloads use `documentation.pdf`.

For the equivalent CLI export, see [`mdu pdf --page`](../Usage/pdf.md#export-a-topic-and-its-subpages).

The PDF uses the same Markdown rendering pipeline as [`mdu pdf`](../Usage/pdf.md), including syntax highlighting, math, callouts, and supported diagrams. See [PDF output](../Usage/pdf.md) for the layout and element rendering.
