---
navigation:
  visible: false
---

# Lint Rules Reference

`mdu lint` reports diagnostics with a stable rule code and name. Errors make the command exit with code `1`; warnings do not.

## Rule summary

| Code | Name | Severity | Check | More |
| --- | --- | --- | --- | --- |
| `MDU001` | `HeadingRequired` | Error | Every Markdown page has a level-one heading that defines its title. | — |
| `MDU002` | `DirectoryTitleMatch` | Error | A section directory name matches the title of its `README.md`. | [Rule 2](Rule2.md) |
| `MDU003` | `LinkCasing` | Error | The casing in a local link target matches the file-system entry. | [Rule 3](Rule3.md) |
| `MDU004` | `LocalLinkExists` | Error | A local link or image target exists. | — |
| `MDU005` | `DuplicatePageRepresentation` | Error | A page is not represented by both a Markdown file and a same-named directory `README.md`. | [Rule 5](Rule5.md) |
| `MDU006` | `ReadmeWithoutSubpages` | Warning | A section `README.md` is its directory's only Markdown page. | [Rule 5](Rule5.md) |
| `MDU007` | `MaterialAdmonition` | Error | Material admonitions must be converted to GitHub Alerts. | [fix](../fix.md#migrating-mkdocs-material-markdown) |
| `MDU008` | `MaterialTabs` | Error | Material tabs must be converted to headings. | [fix](../fix.md#migrating-mkdocs-material-markdown) |
| `MDU009` | `MaterialCards` | Error | Material card grids must be converted to Markdown link lists. | [fix](../fix.md#migrating-mkdocs-material-markdown) |
| `MDU010` | `UnsupportedMermaid` | Error | A Mermaid diagram uses syntax MDU cannot render. | — |
| `MDU011` | `CodeAnnotations` | Error | Material code-annotation markers are unsupported. | [fix](../fix.md#migrating-mkdocs-material-markdown) |
| `MDU012` | `MkDocsIndexPage` | Warning | Rename the MkDocs `index.md` page to `README.md`. | [fix](../fix.md#migrating-mkdocs-material-markdown) |
| `MDU013` | `MissingSectionReadme` | Warning | A section with child pages needs a `README.md` page. | [fix](../fix.md#migrating-mkdocs-material-markdown) |
| `MDU014` | `MkDocsPagesFile` | Error | Migrate MkDocs `.pages` settings to Markdown front matter. | [fix](../fix.md#migrating-mkdocs-material-markdown) |
| `MDU015` | `LegacySidebarMetadata` | Error | Replace retired `sidebar.*` front matter with `navigation.*`. | — |
| `MDU016` | `InvalidNavigationPlacement` | Error | `navigation` must be a mapping and `placement` must be `sidebar` or `top`. | — |
| `MDU017` | `NavigationPlacementLevel` | Error | `placement: top` is only valid on first-level pages or section indexes. | — |
| `MDU018` | `ExpandedTopNavigationRoot` | Error | A top navigation root is always expanded; remove its ineffective `navigation.expanded`. | — |
| `MDU019` | `DuplicateNavigationTitle` | Error | Visible sibling pages have unique navigation titles. | — |
| `MDU020` | `UnusedFootnote` | Warning | Every footnote definition is reachable from a reference in page text. | — |
| `MDU021` | `DuplicateFootnote` | Warning | A footnote label is defined only once per page. | — |

## MDU001: HeadingRequired

Every Markdown page needs a level-one heading (`# Page title`) after any YAML front matter. Add a heading that describes the page when this diagnostic appears.

## MDU002: DirectoryTitleMatch

For a section index named `README.md`, the directory name must match the heading title. The comparison ignores case, punctuation, spaces, hyphens, and underscores. If the title contains a colon, only the text before the first colon is compared. Set `navigation.title` when a section intentionally uses a different navigation label. `mdu fix` migrates this field only when it is explicitly present in a `.pages` file.

For example, `# Keyboard Keys` matches the directory `Keyboard-Keys`. See [Rule 2](Rule2.md) for examples and guidance.

## MDU003: LinkCasing

The path in a local link must use the same casing as its target on disk. This catches links that may work on a case-insensitive file system but fail on a case-sensitive one. Query strings and fragments do not affect the path check. This also applies to local destinations in supported Mermaid, context, and PlantUML node links, and to `src`, `href`, and `poster` attributes in raw HTML. Correct the target path to match the file or directory name. See [Rule 3](Rule3.md) for an example.

## MDU004: LocalLinkExists

Relative local links, image targets, destinations in supported Mermaid, context, and PlantUML node links, and `src`, `href`, and `poster` attributes in raw HTML (outside HTML comments) must point to an existing file or to a directory containing a `README.md`. Query strings and fragments are ignored when resolving the path. External URLs and fragment-only links are not checked by this rule.

Fix the destination or add the missing target. A casing mismatch for an existing target is reported as `MDU003` instead.

## MDU005: DuplicatePageRepresentation

A page cannot have both a Markdown file and a same-named directory containing `README.md`. For example, `Setup.md` and `Setup/README.md` represent the same page; keep one representation. See [Rule 5](Rule5.md) for valid and invalid layouts.

## MDU006: ReadmeWithoutSubpages

This warning appears when a section directory's `README.md` is its only Markdown page and there is no same-named Markdown file beside the directory. The section can be represented by a single file instead. Run `mdu fix` to move the `README.md` beside the directory and update affected links; see [fixing MDU006](../fix.md#fixing-mdu006).

## MkDocs Material compatibility

Rules `MDU007`–`MDU014` identify MkDocs-specific syntax and structure that needs migration. Run [`mdu fix`](../fix.md#migrating-mkdocs-material-markdown) to convert the supported constructs. An `MDU010` error means a Mermaid diagram uses [syntax that MDU cannot render](../../Features/Diagrams.md#unsupported-syntax); simplify the diagram, or it stays a code block in the rendered page.

## Navigation front matter

`MDU015` rejects the retired `sidebar.*` fields. Rename them to `navigation.*` directly; `mdu fix` does not convert these fields. `MDU016` reports malformed navigation metadata or a `placement` value other than `sidebar` or `top`. `MDU017` requires `placement: top` to be set on a first-level Markdown page or the `README.md`/`index.md` of a first-level folder. `MDU018` rejects `navigation.expanded` on a top navigation root because that root is always open.

## MDU019: DuplicateNavigationTitle

Visible sibling pages must have unique navigation names, ignoring letter case. The name comes from `navigation.title` when set, or otherwise from the page title. Set a distinct `navigation.title` on one of the pages when two entries would otherwise look the same. Hidden pages are excluded, and their visible children are compared under the next visible parent.

## MDU020: UnusedFootnote

This warning points to each footnote definition that is not reachable from a reference in page text, directly or through another used footnote. A reference inside an unused footnote does not make its target used. Remove unused definitions or add a reference; warnings do not make `mdu lint` fail.

## MDU021: DuplicateFootnote

This warning points to every later definition that repeats a footnote label. Markdig uses only the first definition, so remove the duplicate or give it a unique label.

## Informational output

The linter also lists pages marked `navigation.visible: false` that no visible page links to. These orphaned pages are informational; they do not have a rule code and do not affect the exit code.

[Back to lint](README.md)
