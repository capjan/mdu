---
tags: [Migration]
navigation.order: 3
---

# fix

`mdu fix` migrates MkDocs Material syntax to portable Markdown, normalizes the MkDocs page structure, and applies the linter's automatic fixes. It changes files in place, so review the changes before you publish.

## Usage

```bash
mdu fix [PATH] [options]
```

See [Paths and file search](README.md#paths-and-file-search) for the default source folder and [common options](README.md#common-options) for `--pattern` and `--hidden`. Then check the result:

```bash
mdu fix ./ProjectDocs
mdu lint ./ProjectDocs
```

If there is nothing to fix, the command reports `No fixable issues found.` and changes nothing.

> [!TIP]
> Commit or stash your work first. `git diff` then shows exactly what `mdu fix` changed.

## Fixing MDU006

When a section folder contains only a `README.md`, `mdu fix` moves that page beside the folder: `docs/Rule3/README.md` becomes `docs/Rule3.md`.

- Links to the old page are updated, including links to the section folder and reference links.
- Relative links inside the moved page are adjusted, so they still reach the same pages and assets.
- Query strings and fragments are kept.
- If the folder still contains assets or other files, it stays in place.

## Migrating MkDocs Material Markdown

The linter reports Material-specific syntax as [migration issues](lint/Rules.md#mkdocs-material-compatibility). `mdu fix` converts what it can:

| MkDocs Material | Becomes |
| --- | --- |
| Admonitions and collapsible details | [GitHub alerts](../Features/Callouts.md); content stays visible, unknown types become `NOTE` |
| Content tabs | Headings |
| Card grids | Markdown link lists with their descriptions |
| `{ .annotate }` markers | Removed; [code annotations](../Features/CodeBlocks.md#annotations) work without them |
| Code fences such as ` ``` { .yaml .annotate } ` | ` ```yaml `, keeping a `title` |
| `index.md` | `README.md` |
| Section without a `README.md` | A new `README.md` named after the folder |
| `.pages` titles and order | `navigation.title` and `navigation.order` front matter; the `.pages` file is removed |
| Blog and tag indexes | Static Markdown link lists |

`MDU012` (`index.md`) and `MDU013` (missing section `README.md`) are warnings, so ordinary Markdown projects do not fail lint because of these naming conventions. Unsupported Material syntax and `.pages` files remain errors.

Good to know:

- `navigation.title` is only added when a `.pages` file sets a title. A normal `MDU002` title mismatch is yours to resolve, or to keep with an explicit `navigation.title`.
- YAML is validated before files are moved or `.pages` files are removed.
- Retired `sidebar.*` front matter is not migrated; rename it to `navigation.*` yourself.
- Mermaid blocks with syntax that MDU cannot render stay unchanged and are reported as errors. `mdu fix` does not rewrite diagrams.
