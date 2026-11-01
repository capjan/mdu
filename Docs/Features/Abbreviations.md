# Abbreviations

Markdig's abbreviation syntax adds a tooltip to matching terms. MDU uses the same preview placement and styling as footnotes.

## Page-local definitions

Define abbreviations anywhere in a page, then use their labels in the text:

*[HTML]: Hyper Text Markup Language
*[W3C]: World Wide Web Consortium

~~~markdown title="Markdown with abbreviations"
The HTML specification is maintained by the W3C.

*[HTML]: Hyper Text Markup Language
*[W3C]: World Wide Web Consortium
~~~

<div class="result-block">
<p>The <abbr title="Hyper Text Markup Language">HTML</abbr> specification is maintained by the <abbr title="World Wide Web Consortium">W3C</abbr>.</p>
</div>

Definitions follow Markdig's matching rules. Code remains unchanged. A definition on a page takes precedence over the shared glossary for that page only.

## Shared glossary

Set `glossary` in `mdu.yml` to a Markdown file path relative to the documentation root:

```yaml title="docs/mdu.yml"
glossary: includes/abbreviations.md
```

Use the same definition syntax in that file:

```markdown title="docs/includes/abbreviations.md"
*[API]: Application Programming Interface
*[HTML]: Hypertext Markup Language
```

The glossary applies across the site. Its source file is excluded from the generated pages, navigation, search index, lint page graph, and combined PDF. If no `mdu.yml` glossary is configured, local definitions continue to work as usual.

An [imported repository](Repository-Imports.md#abbreviations-in-imported-documentation) can bring its own glossary. It is excluded from the site in the same way and adds to or overrides the site glossary on the imported pages only.

In the HTML site, hover over or focus an abbreviation to open its preview. On touch screens, tap the abbreviation. The PDF shows the term as ordinary text without an interactive preview.

## Migrating from Material for MkDocs

If `mkdocs.yml` has one path under `pymdownx.snippets.auto_append`, run `mdu fix`. MDU adds that path to `mdu.yml` and leaves the glossary file in place. The migration is safe to run again. If `auto_append` contains multiple paths, add the intended glossary path to `mdu.yml` yourself.
