---
tags: [Navigation, Search]
---

# Dashboard

## Overview

A dashboard turns a page into a start page for the services and sites you use every day. Groups of link tiles show colored logos, and a filter field finds a link, a documentation page, or searches the web. The page is still an ordinary Markdown link list, so it reads well on GitHub and in any editor, and the rendered result is static HTML that needs no server.

[Open the example dashboard](Dashboard-Example.md)

## Syntax

Set `layout: dashboard` in the front matter. Every second-level heading starts a group, and every list item with a link becomes a tile:

```markdown title="Markdown"
---
layout: dashboard
---

# Start

## Workspace

- [Team Wiki](https://wiki.example.org) — Shared knowledge
- [Handbook](Handbook.md) — How we work

## Developer

- [GitHub](https://github.com)
- [GitLab](https://gitlab.example.org)
- [NuGet](https://www.nuget.org)

---

## Entertainment

- [YouTube](https://www.youtube.com)
- [Spotify](https://open.spotify.com)
```

| Markdown | Result |
| --- | --- |
| `# Heading` and text before the first group | Page title and introduction above the filter field |
| `## Heading` | A group of tiles |
| `### Heading` or a lower level inside a group | A subheading that divides the group; a paragraph below it is a short description |
| `- [Name](URL)` | A tile that opens the link |
| Text after the link | Subtitle of the tile. A leading `—`, `–`, `-`, or `:` is removed. Without it, the tile shows the host name. In a group of compact rows, a host that is only the name and a top-level domain, such as `github.com` for GitHub, is left out; a host with a subdomain other than `www.` always stays. |
| `---` | Starts a new row of groups |
| A list item without a link | A note between the tiles |

Groups with subtitles use large tiles; groups without them use compact rows. A compact row always shows the whole name first and the host only in the space that is left. Groups in a row share the width and wrap on narrow screens. Links to `.md` files point to the rendered pages, and all links open in the same tab unless you set `open: new-tab`. A [`tags` code block](Tags.md#list-tagged-pages-on-any-page) in a group fills it with the pages that carry a tag.

## Icons

MDU picks an icon for each tile on its own:

1. An icon or emoji shortcode in front of the link, such as `- :dashboard-bookstack: [Wiki](https://wiki.example.org)`, always wins. An image works as well.
2. Otherwise an exact match in MDU's small table of known hosts wins, for example `mail.google.com` → Gmail, `code.visualstudio.com` → Visual Studio Code, `nuget.org` → NuGet, `bitbucket.org` → Bitbucket, `codeberg.org` → Codeberg, or `gist.github.com` → GitHub. Host matching ignores case, a leading `www.`, and a trailing dot; unrelated subdomains do not inherit the mapping.
3. Next it tries the complete tile name, including camel-case, hyphenated, and compact spellings, such as `Home Assistant` → `home-assistant`.
4. Finally it tries individual words longer than three letters only when they also match a complete host label before the final dot, or the entire host when it has no dots. For example, `Christians GitLab` at `gitlab.example.org` can use GitLab, `Home Grafana` at `http://grafana:3000` can use Grafana, but `Apple Storage` at `storage.example.org` does not pick the Apple logo.
5. Each candidate tries the colored [Dashboard Icons](Icons-and-Emojis.md) first, then monochrome Simple Icons, before moving to the next candidate. Without a match, the tile shows its initials on a colored background.

The ambiguous icon names `code`, `files`, `music`, and `vault` are excluded from both full-name and individual-word matching. Even `Password Vault` at `vault.example.org` or `Files` at `files.example.org` shows initials; use an explicit shortcode such as `:dashboard-vault:` to select that product logo. A known host still takes precedence, so `Files` at `drive.google.com` uses Google Drive. DevDocs and Artificial Analysis, for example, currently have no logo in the bundled sets and show initials. Use an explicit shortcode, emoji, or image to choose an icon yourself, especially for products sharing one host:

```markdown title="Explicit icons"
- :simple-rider: [JetBrains Rider](https://www.jetbrains.com/rider/)
- :material-book-open-page-variant: [DevDocs](https://devdocs.io)
- :material-chart-line: [Artificial Analysis](https://artificialanalysis.ai)
```

Icons are embedded while rendering, so the dashboard loads no favicons from other sites.

## Filter and search

The filter field has the focus when the page opens, and ++slash++ returns to it. While you type:

- Tiles that do not match the name, subtitle, address, group, or subheading disappear. Subheadings and groups without a matching tile disappear with them.
- Matching documentation pages from the site search appear below the tiles, including the pages of matching [tags](Tags.md).
- ++enter++ opens the best match, which the hint below the field names. Without a match, it searches the web.
- ++option+enter++ on macOS or ++alt+enter++ on Windows and Linux searches the web directly, even when a link matches.
- Hold ++cmd++ or ++ctrl++ as well to open the result in a new tab.
- ++arrow-down++ or ++tab++ moves the focus to the best match.
- ++esc++ or the × button in the field clears the filter.

## Keyboard

The arrow keys move the focus to the nearest control in their direction across the whole page: from the header to the filter, through the columns of tiles and the matching pages, and on to the links below. In the filter field, ++arrow-left++ and ++arrow-right++ move the cursor. ++tab++ moves through the tiles in reading order. On a tile or a matching page, ++home++ and ++end++ jump to the first and last result, and typing continues in the filter. ++esc++ releases the focus anywhere on the page; in the filter, it clears the text first. Moving the mouse takes over from the keyboard, and the next key press hands control back; the first arrow key continues from the tile under the pointer.

## Options

Add a `dashboard` mapping to the front matter:

```yaml title="Front matter"
---
layout: dashboard
dashboard:
  search: duckduckgo
  clock: true
  status: false
  open: same-tab
  limit: 6
  collapsed: [AI, Social]
  frequent: 6
---
```

| Option | Values | Default | Description |
| --- | --- | --- | --- |
| `search` | `duckduckgo`, `google`, `bing`, `startpage`, `ecosia`, `none`, or a URL with `{query}` | `duckduckgo` | Web search for queries without a matching link. `none` turns it off. |
| `clock` | `true`, `false` | `true` | Shows the time and date next to the filter field in the reader's language. |
| `status` | `true`, `false` | `false` | Adds a dot to each tile that turns green when the browser reaches the site and red when it does not. |
| `open` | `same-tab`, `new-tab` | `same-tab` | Where links to other sites open. Links within the site always open in the same tab. |
| `limit` | A number | none | Each group shows its first tiles up to this number and a **Show more** button for the rest. |
| `collapsed` | `true`, a heading, or a list of headings | none | Groups that start folded. Their names match the group headings, ignoring case; `true` folds every group. |
| `frequent` | A number, `true`, `false` | `false` | Adds a row of the links this reader opens most above the groups. A number sets how many it shows; `true` shows 6. |

## Fold groups

A dashboard can hold everything you need without showing it all at once. Select a group label to fold the group to its label; a folded label shows the number of its tiles. With `limit`, a group shows only its first tiles, and **Show more** reveals the rest; when only one tile would be left over, the group shows it instead. The filter always searches every tile, including those in folded groups and past the limit, and while it has text, every group shows its matches. Selecting a group label while filtering clears the filter and opens that group.

What you fold or open stays in your browser for this page, so `collapsed` and `limit` set only the start. A good start page keeps the links you use daily in open groups at the top, and the rest in folded groups below. Without JavaScript, every group shows all of its tiles.

## Frequently used

With `frequent`, the dashboard learns which links you use. Every link you open from a tile or with Enter in the filter counts in your browser, and nothing is sent anywhere. A link you opened at least twice appears in a row above the groups, the most used first; recent uses count more than old ones, so the row follows your habits. The row changes on your next visit, never while you are on the page. The filter searches only the original tiles, so the row steps aside while it has text. **Clear** forgets every counted use for this page. Without JavaScript or browser storage, the row does not appear.

## Behavior

- The dashboard spans the whole width. The sidebar starts collapsed and opens with the navigation button, and the page has no table of contents, breadcrumbs, or links to the previous and next page.
- The status check runs in the reader's browser. Sites that block such requests and `http` sites on an `https` dashboard show no status.
- A dashboard can be any page of a site, or the only page of its own folder that you render with `mdu render` and use as the browser start page.
