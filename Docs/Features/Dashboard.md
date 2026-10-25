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
| `- [Name](URL)` | A tile that opens the link |
| Text after the link | Subtitle of the tile. A leading `—`, `–`, `-`, or `:` is removed. Without it, the tile shows the host name. |
| `---` | Starts a new row of groups |
| A list item without a link | A note between the tiles |

Groups with subtitles use large tiles; groups without them use compact rows. Groups in a row share the width and wrap on narrow screens. Links to `.md` files point to the rendered pages, and links to other sites open in a new tab.

## Icons

MDU picks an icon for each tile on its own:

1. An icon or emoji shortcode in front of the link, such as `- :dashboard-bookstack: [Wiki](https://wiki.example.org)`, always wins. An image works as well.
2. Otherwise MDU looks for a [Dashboard Icon](Icons-and-Emojis.md) that matches the tile name or the host name, for example `dashboard-github` for `github.com` or `dashboard-google-gemini` for `gemini.google.com`.
3. Then it tries the monochrome Simple Icons, which cover many developer services.
4. A tile without a matching icon shows the initials of its name on a colored background.

Icons are embedded while rendering, so the dashboard loads no favicons from other sites.

## Filter and search

The filter field has the focus when the page opens, and ++slash++ returns to it. While you type:

- Tiles that do not match the name, subtitle, address, or group disappear.
- Matching documentation pages from the site search appear below the tiles.
- ++enter++ opens the selected match, which has a blue ring. The best match is selected while you type. Without a match, it searches the web.
- ++option+enter++ on macOS or ++alt+enter++ on Windows and Linux searches the web directly, even when a link matches.
- Hold ++cmd++ or ++ctrl++ as well to open the result in a new tab.
- ++arrow-down++ and ++arrow-up++ move the selection through the tiles and pages while you keep typing; ++enter++ opens the selected one.
- ++esc++ or the × button in the field clears the filter.

## Options

Add a `dashboard` mapping to the front matter:

```yaml title="Front matter"
---
layout: dashboard
dashboard:
  search: duckduckgo
  clock: true
  status: false
  open: new-tab
---
```

| Option | Values | Default | Description |
| --- | --- | --- | --- |
| `search` | `duckduckgo`, `google`, `bing`, `startpage`, `ecosia`, `none`, or a URL with `{query}` | `duckduckgo` | Web search for queries without a matching link. `none` turns it off. |
| `clock` | `true`, `false` | `true` | Shows the time and date next to the filter field in the reader's language. |
| `status` | `true`, `false` | `false` | Adds a dot to each tile that turns green when the browser reaches the site and red when it does not. |
| `open` | `new-tab`, `same-tab` | `new-tab` | Where links to other sites open. |

## Behavior

- The dashboard spans the whole width. The sidebar starts collapsed and opens with the navigation button, and the page has no table of contents or breadcrumbs.
- The status check runs in the reader's browser. Sites that block such requests and `http` sites on an `https` dashboard show no status.
- A dashboard can be any page of a site, or the only page of its own folder that you render with `mdu render` and use as the browser start page.
