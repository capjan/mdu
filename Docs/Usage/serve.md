---
navigation.order: 5
---

# serve

`mdu serve` renders the site, starts a local preview server, and opens it in your browser. It generates `documentation.pdf` only if a page contains `[Download the PDF](mdu:pdf)`. While it runs, it watches the documentation folder and the folders containing referenced OpenAPI files: when you save a page, a linked image, or a referenced OpenAPI file, the affected output is refreshed and the browser reloads it. Markdown changes refresh the PDF when a PDF Link is present; adding the first link creates the PDF, and removing the last link removes it. Press ++ctrl+c++ to stop.

The preview becomes available once the first page, navigation and search are ready. Other pages are completed in the background or when requested, followed by PDFs. A status message shows when downloads are being updated or have failed; those downloads return HTTP 503 until successfully regenerated, so an old PDF cannot be mistaken for the current version.

A PDF generation error, including during the initial build, keeps the preview running so you can fix the source and save again. Use [`mdu render`](render.md) or [`mdu pdf`](pdf.md) when you need a command that fails if required PDF output cannot be generated. Errors that prevent the initial HTML preview or server from starting still exit with code 1.

The output uses the same [generated files](render.md#generated-files) as `mdu render`, including the reserved `_mdu` folder.

If a page update fails, requests for that unfinished page return an error instead of an older successful version. Pending pages can be retried by requesting them again; after an interrupted change batch, fixing the source and saving triggers a fresh site rebuild. The server logs the error and continues serving other available pages.

## Usage

```bash
mdu serve [PATH [OUTPATH]] [options]
```

See [Paths and file search](README.md#paths-and-file-search) for the default folders and [common options](README.md#common-options) for `--pattern` and `--hidden`.

| Option | Effect |
| --- | --- |
| `--port <port>` | Port of the preview server. Default: `4173`. |
| `--no-browser` | Start the server without opening a browser. |
| `--editPageRoot <url>` | Add edit links and a repository button, as with [`mdu render`](render.md). |

## Examples

```bash
mdu serve
mdu serve ./ProjectDocs ./site --port 8080
mdu serve --no-browser
```

> [!NOTE]
> The output folder must differ from the documentation folder and must not contain it. Use `mdu render` to create the site you publish; `serve` is meant for local preview.

[How live reload works](../Architecture/Pipeline.md#live-preview) describes what `serve` renders again after a change.
