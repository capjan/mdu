---
navigation.order: 20
navigation.placement: top
---

# Usage

## Quick start

To start a documentation project in the current directory, create it first:

```bash
mdu new .
```

Then run the commands from your project folder. MDU finds the `docs` folder on its own.

```bash
mdu tree     # show the page structure
mdu lint     # report broken links and structure problems
mdu serve    # preview the site with live reload
mdu render   # write the static site to ./site
```

A typical workflow checks the pages, previews them while you write, and renders the site when you are ready to publish. Click a step to open its command page.

```mermaid
flowchart LR
    accTitle: Typical MDU workflow
    Write[Write Markdown] --> Lint{mdu lint}
    Lint -->|Errors| Fix[mdu fix or edit]
    Fix --> Lint
    Lint -->|Clean| Serve[mdu serve]
    Serve --> Render[mdu render]
    Render --> Publish[Publish]
    click Lint href "lint/README.md"
    click Fix href "fix.md"
    click Serve href "serve.md"
    click Render href "render.md"
    click Publish href "../Publish/README.md"
```

`mdu serve` opens the preview in your browser and updates it whenever you save a page or a linked image. Press ++ctrl+c++ to stop it. Rendering never changes your Markdown files.

A lint error names the file, line, rule, and problem:

```text
/project/docs/Overview.md:7: MDU004 LocalLinkExists: Local link target './intro.md' does not exist.
Linter detected 1 error.
```

## Commands

| Command | Syntax | Purpose |
| --- | --- | --- |
| [new](new.md) | `mdu new [directory]` | Create a starter documentation project |
| [tree](tree.md) | `mdu tree [path]` | Print the detected page structure. Alias: `print` |
| [lint](./lint/README.md) | `mdu lint [path]` | Check pages and local links against the [rules](./lint/Rules.md). Alias: `l` |
| [fix](fix.md) | `mdu fix [path]` | Migrate MkDocs syntax and apply automatic fixes |
| [render](render.md) | `mdu render [path] [outpath]` | Write a static HTML site. Alias: `r` |
| [serve](serve.md) | `mdu serve [path] [outpath]` | Serve a local preview that updates on changes |
| [pdf](pdf.md) | `mdu pdf [path] [outpath]` | Write one PDF file |
| [publish confluence](../Publish/Confluence.md) | `mdu publish confluence [path]` | Publish an edit-protected Confluence Data Center mirror |
| [page move](page.md#move-pages-and-folders) | `mdu page move <source> <target>` | Move a page or folder and update every link to it. Alias: `page mv` |
| [page links](page.md#find-links-to-a-file) | `mdu page links <file>` | List the links that point to a file |

## Paths and file search

| | Default |
| --- | --- |
| Source folder | `./docs`. Run `mdu new .` to create a starter project, or pass the path explicitly. |
| Output of `render` and `serve` | `./site`, with `documentation.pdf` when a page contains a PDF Link |
| Output of `pdf` | `./documentation.pdf` |
| Pages | Files matching `*.md` |

```bash
mdu render ./ProjectDocs ./public/docs
mdu serve --port 8080
```

## Common options

`tree`, `lint`, `fix`, `render`, `serve`, `pdf`, and `publish confluence` share these options:

| Option | Effect |
| --- | --- |
| `-p, --pattern <pattern>` | Search for files matching the pattern. Default: `*.md`. |
| `--hidden` | Include hidden files. Default: on; `--hidden false` leaves them out. |
| `-?, -h, --help` | Show help for the command. |

Command pages list the additional options. `mdu <command> --help` always shows the current help.
