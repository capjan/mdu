# Navigation

MDU builds the navigation from the folder structure. A folder's `README.md` represents the folder, and the other pages appear beneath it. You control the result with front matter:

| Key | Effect |
| --- | --- |
| `navigation.title` | Label in the sidebar, tabs, and breadcrumbs |
| `navigation.order` | Sort order among siblings |
| `navigation.visible` | `false` hides the entry |
| `navigation.expanded` | `true` opens a sidebar section initially |
| `navigation.placement` | `top` turns a first-level entry into a tab |

Breadcrumbs above each page show its path from the documentation root. Pages whose trail would only name the page itself, such as the landing page, show none.

## Top navigation

Set `navigation.placement: top` on a page at the documentation root, or on the `README.md` or `index.md` of a first-level folder, to make it a tab:

```text
docs/
├── README.md
├── Development/
│   ├── README.md  # navigation.placement: top
│   └── Setup.md
└── Operations.md  # navigation.placement: top
```

```yaml title="docs/Development/README.md"
---
navigation:
  placement: top
  title: Development
  order: 10
---
```

How tabs behave:

- **Wide screens** show the tabs below the site header. The sidebar lists pages in the active tab and pages that are not assigned to a tab; it is hidden when there are no such pages.
- **Up to 1220 px** wide, the tabs fold into the full sidebar tree at their original place, and the selected section opens there. On narrow screens the sidebar becomes a drawer.
- **Breadcrumbs** always keep the full path from the documentation root; selecting a tab does not change it.
- The root `README.md` stays the landing page. Mark it `top` as well if it should also appear as a tab, such as *Dashboard*.

A top-level Markdown file can be a tab too; its sidebar section is simply empty.

## Ordering entries

Smaller `navigation.order` values come first among siblings. Entries without an order follow and sort alphabetically by their navigation title; the same applies to tabs.

```yaml
---
navigation:
  order: 10
---
```

The compact form `navigation.order: 10` works too. If both forms are present, the nested `navigation` value wins. `navigation.title` changes only the navigation label, not the page's `title` or heading.

## Expanding a section

Set `navigation.expanded: true` on a page with child pages to open its sidebar section by default:

```yaml
---
navigation:
  expanded: true
---
```

The section leading to the current page always opens, and sections the reader opens or closes stay that way while they browse. A selected top navigation tab is always expanded, so `navigation.expanded` on it is a lint error.

## Hiding an entry

Set `navigation.visible: false` to omit an entry from the sidebar and tabs. The page stays reachable by its URL, and the child pages of a hidden folder page move up one level:

```yaml
---
navigation:
  visible: false
---
```

> [!NOTE]
> `mdu lint` rejects the retired `sidebar.*` front matter. Rename it to `navigation.*`; `mdu fix` does not migrate it.

## Page footer

Each page ends with links to the previous and next page in sidebar order, so readers can work through the documentation from start to finish. Hidden pages have no such links and are skipped by their neighbors.

The footer also shows when the page last changed, taken from the last git commit that touched its Markdown file. Uncommitted changes do not count, and files that were never committed show no date. With [`--editPageRoot`](../Usage/render.md), an edit icon appears beside the level-one heading, or an **Edit this page** link in the footer when there is no heading.

> [!IMPORTANT]
> The date needs the full git history. In a shallow clone, which CI services create by default, MDU leaves the date out instead of showing a wrong one.

Fetch the full history in GitHub Actions with `fetch-depth: 0`:

```yaml
- uses: actions/checkout@v7
  with:
    fetch-depth: 0
```

In GitLab CI, set the `GIT_DEPTH` variable to `0` for the job that renders the site.
