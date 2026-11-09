---
navigation.order: 1
---

# tree

`mdu tree` prints the titles of the detected Markdown pages as an indented tree. A folder's `README.md` is the parent page; the other pages in the folder and in its subfolders appear beneath it.

## Usage

```bash
mdu tree [PATH] [options]
```

The aliases `mdu print` and `mdu print-tree` run the same command. See [Paths and file search](README.md#paths-and-file-search) for the default source folder and [common options](README.md#common-options) for `--pattern` and `--hidden`.

## Example

```text
$ mdu tree ./ProjectDocs/Usage
Usage
  fix
  new
  check-site
  render
  Configuration
  tree
  pdf
  page
  serve
  lint
    Rule 5
    Rule 2
    Rule 3
    Lint Rules Reference
```

Each line is a page title, taken from the `title` front matter or the level-one heading. Pages appear in the order they are found on disk; the rendered navigation sorts them with [`navigation.order`](../Features/Navigation.md#ordering-entries).

Use another pattern when your pages have a different extension:

```bash
mdu tree ./ProjectDocs --pattern "*.markdown"
```
