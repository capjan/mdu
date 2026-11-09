# Repository Imports

MDU can combine Markdown pages from several Git repositories into one documentation site. Add an import declaration to the front matter of a page where the imported section should appear:

```yaml title="docs/API/README.md"
---
title: API reference
mdu:
  import:
    source: https://github.com/example/api-docs/tree/main/Docs
navigation:
  order: 20
---
```

Copy the repository URL that points to the folder. MDU supports the common browse URL formats used by GitHub and GitHub Enterprise (`/tree/<revision>/<folder>`), GitLab (`/-/tree/<revision>/<folder>`), Bitbucket Cloud (`/src/<revision>/<folder>`), and Gitea or Forgejo (`/src/branch/<branch>/<folder>` or `/src/tag/<tag>/<folder>`). Hosts that use the GitHub-style `/tree/` route also work, including self-hosted instances. For a host with a different browser URL, use its HTTPS clone URL and append `#<revision>/<folder>`, for example `https://git.example.net/team/api.git#feature/docs/Docs`. MDU resolves slash-containing branch and tag names against the repository's refs. GitLab tree URLs may include `?ref_type=heads` or `?ref_type=tags`.

The revision may be omitted. In that case MDU uses the `main` branch. A repository URL without a folder imports the repository root; a folder can be specified without a revision, for example `https://github.com/example/api-docs/tree/Docs`.

When a tree URL's remaining path does not match a branch or tag, MDU treats the whole path as a folder on `main`. This supports revision-less folder URLs, but means a misspelled revision can be interpreted as a folder; if that folder does not exist, the error explains the fallback.

MDU imports the folder's page tree at the declaration's position. The imported folder must contain a `README.md` or `index.md` at its root. The declaration page's front matter controls the root page metadata; keep the declaration page's body empty and put its content in the imported root page instead. Front matter on imported child pages is preserved. Imports declared on local child pages are expanded recursively; imports declared inside the imported repository are rejected.

Edit links on imported pages point to the matching file in the source repository. GitHub-style URLs use their edit route, GitLab URLs use GitLab's edit route, and source-style hosts open the source file page where their edit action is available.

## Merging top navigation

By default, [`navigation.placement: top`](Navigation.md#top-navigation) is evaluated against the combined site's folder structure. A top-level page such as `Testhandbuch.md` becomes `Architecture/Testhandbuch.md` when mounted below `Architecture.md`, so its placement is no longer valid: it does not become a tab, and `mdu lint` reports `MDU017`.

Set `mdu.import.navigation: merge` on an import declaration to include the imported documentation's tabs in the combined site's top navigation:

```yaml title="docs/Architecture.md"
---
title: Architecture
mdu:
  import:
    source: https://github.com/example/architecture/tree/main/docs
    navigation: merge
navigation:
  placement: top
  order: 10
---
```

With this option, top navigation eligibility is evaluated relative to the imported source folder, regardless of how deeply it is mounted. Imported pages still need `navigation.placement: top`; only files directly in the source folder and first-level folder `README.md` or `index.md` pages qualify. Deeper placements remain lint errors.

The declaration's front matter still controls the imported root page, including its title and placement. The root keeps the declaration's position in the combined site, so it can only be a tab when the declaration itself is top-level. For example, the declaration above provides the *Architecture* tab, while imported `Testhandbuch.md` and `Bestandsverzeichnis.md` pages marked `top` provide their own tabs.

Titles, visibility, and ordering use the usual `navigation.*` settings. All tabs share one global order: smaller `navigation.order` values come first, followed by entries without an order sorted by title. A page belongs to its nearest enclosing tab, so selecting an imported tab shows its own sidebar section rather than the enclosing import's section. On compact screens the full tree remains available.

Merging tabs does not move pages or change their URLs, relative links, breadcrumbs, search entries, or PDF structure. Local child pages and other imports do not inherit the option. Omit it to keep the default behavior; `merge` is the only supported value, and other values are errors.

## Abbreviations in imported documentation

If the imported folder has an `mdu.yml` with a [`content.glossary`](Abbreviations.md#shared-glossary), MDU treats that file as the glossary of the import, not as a page. Its definitions apply to the imported pages. The glossary of the importing site still applies there, and on a conflict the imported definition wins:

| Abbreviation | Site glossary | Imported glossary | Meaning on imported pages |
| --- | --- | --- | --- |
| API | Application Programming Interface | — | Application Programming Interface |
| CI | Continuous Integration | Configuration Item | Configuration Item |
| ADR | — | Architecture Decision Record | Architecture Decision Record |

Pages outside the import, including local pages placed below it and other imports, do not see the imported definitions. Definitions on a page still take precedence over both glossaries. An imported folder without a glossary uses the site glossary.

The glossary must be a file inside the imported repository; it may lie outside the imported folder. `mdu render` and `mdu serve` stop when the glossary of an import is missing, outside the repository, has no definitions, or is still set with the old `glossary` name. Other commands then treat the import as having no glossary.

## Requirements

Git must be installed. MDU retrieves repositories with the Git command-line tool, so public repositories work without credentials and private repositories use the authentication already configured for Git. Do not put credentials in the source URL. Rendering an import contacts the HTTPS host named in its front matter, so only render imports from repositories you trust.
