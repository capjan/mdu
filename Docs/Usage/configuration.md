---
tags: [Navigation, PDF]
title: Configuration
description: Everything you can set in mdu.yml and in page front matter.
navigation.order: 50
---

# Configuration

MDU works without any configuration. When you want the site to carry your name and look, add one file, `mdu.yml`, to the documentation folder. Start small and come back to the reference when you need a setting.

## Start with two settings

```yaml title="docs/mdu.yml"
site:
  name: Acme Docs
  logo: assets/logo.svg
```

> [!RESULT]
> Your site is now called **Acme Docs**. The logo appears in the header and, because no favicon is set, in the browser tab. `mdu serve` picks up the change as soon as you save the file.

## The whole file at a glance

The file has three scopes, one for each question you might ask. You will rarely need all of it.

```yaml title="docs/mdu.yml"
site:                 # How does the published site present itself?
  name: Acme Docs
  logo:
    light: assets/logo-light.svg
    dark: assets/logo-dark.svg
  favicon: assets/favicon.svg
  colors:
    light:
      background: "#ffffff"
      text: "#1d1d1f"
      primary: "#0066cc"
    dark:
      background: "#1d1d1f"
      text: "#f5f5f7"
      primary: "#2997ff"
  attribution:
    visible: false

content:              # What do all pages share?
  glossary: includes/abbreviations.md
  icons: assets/icons
  tags: Tags.md

diagrams:             # How large may a diagram get?
  limits:
    elements: 3000
    links: 6000
```

## Four rules

Every setting follows the same rules, so you only learn them once:

1. **Lowercase words in scopes.** A scope groups what belongs together, and a name is a single lowercase word.
2. **Nested or dotted.** `site.name: Acme Docs` is the same setting as `name:` nested below `site:`. Use one form per setting: two different values are an error.
3. **One value or one per theme.** `logo` and `favicon` take one path for both themes, or `light` and `dark`.
4. **Paths start at the documentation folder.** That is the folder that contains `mdu.yml`.

`mdu render`, `mdu serve`, `mdu pdf`, and `mdu fix` stop with an error if the file uses a renamed setting, and `mdu lint` and `mdu tree` warn about it. `mdu render` and `mdu serve` also stop on an unknown setting, a value of the wrong form, or a setting with two different values, so a misspelled name cannot go unnoticed. MDU does not check color values or whether a logo file exists.

## Reference

### `site`

| Setting | Default | Effect |
| --- | --- | --- |
| `site.name` | `Documentation` | Name in the header and the browser title |
| `site.logo` | None | Logo in the header |
| `site.favicon` | The logo | Icon in the browser tab |
| `site.colors.light`<br>`site.colors.dark` | MDU theme | The `background`, `text`, and `primary` color of each theme |
| `site.attribution.visible` | `true` | `false` removes the small **Made with MDU** credit at the end of each HTML page; `mdu pdf` ignores this setting |

The background color also applies to the header, navigation, sidebar, and dialogs. Secondary text, lines, and code surfaces are mixed from the text and background colors, and hover backgrounds from the primary color, so they match your palette.

MDU copies the referenced files into the output folder and uses them as they are. A square logo mark is most legible in a browser tab.

<details>
<summary>How browsers choose between a light and a dark favicon</summary>

MDU emits a light favicon link without a `media` condition as a fallback, followed by a dark link with `media="(prefers-color-scheme: dark)"`. The browser selects the dark link when that condition matches, independently of the page theme. Alternatively, one SVG can switch its internal styles with `prefers-color-scheme`.

Both approaches follow the browser's effective preference, which can come from an OS or user-agent setting. Selection relies on browser support for the `media` attribute on favicon links; Safari behavior can vary by version, so verify the versions you support. Use PNG or ICO if a browser you support does not display SVG favicons.

</details>

### `content`

| Setting | Default | Effect |
| --- | --- | --- |
| `content.glossary` | None | Markdown file with [abbreviations](../Features/Abbreviations.md#shared-glossary) for all pages |
| `content.icons` | `.icons`, if it exists | Folder with [your own icon sets](../Features/Icons-and-Emojis.md#your-own-icons) |
| `content.tags` | None | Markdown page that defines the allowed [tags](../Features/Tags.md#define-the-vocabulary); unknown tags then stop `mdu render` and `mdu pdf` |
| `content.code.annotate` | `true` | `false` turns off [code annotations](../Features/CodeBlocks.md#annotations) on all pages |

### `diagrams`

| Setting | Default | Effect |
| --- | --- | --- |
| `diagrams.limits.elements` | `1000` | Elements up to which a [component diagram](../Features/Diagrams.md#size-limits) is drawn; `0` for no limit |
| `diagrams.limits.links` | `2000` | Links up to which a component diagram is drawn; `0` for no limit |

## Settings for one page

`mdu.yml` applies to the whole site. YAML front matter at the top of a page applies to that page and uses the same naming. Its `navigation`, `pdf`, `images`, `page`, and `content` keys also work in the dotted form, such as `navigation.order: 10`:

```yaml title="docs/Setup/README.md"
---
title: Installing MDU
description: Install the MDU .NET tool globally or per repository.
icon: material-download
navigation:
  title: Setup
  order: 10
---
```

| Key | Effect |
| --- | --- |
| `title` | Browser title and fallback navigation title |
| `description` | Page meta description |
| `icon` | Icon shortcode shown beside the level-one heading, such as `material-download` |
| `tags` | [Tags](../Features/Tags.md) of the page, such as `[Security, Kubernetes]` |
| `navigation.title` | Label in the sidebar, tabs, and breadcrumbs |
| `navigation.order` | Sort order among sibling pages |
| `navigation.visible` | `false` hides the entry; the page stays reachable by its URL |
| `navigation.expanded` | `true` opens the section in the sidebar initially |
| `navigation.placement` | `top` makes a first-level page or section a [top navigation tab](../Features/Navigation.md#top-navigation) |
| `pdf.visible` | `false` leaves the page out of [`mdu pdf`](pdf.md) |
| `images.zoom` | `false` turns off the [image viewer](../Features/Images.md#turn-off-the-viewer) on the page |
| `page.breadcrumbs` | `false` hides the page’s breadcrumb trail |
| `page.pagination` | `false` hides the page’s previous and next links |
| `page.toc` | `false` hides the table of contents and gives its space to the content |
| `content.code.annotate` | `false` turns off [code annotations](../Features/CodeBlocks.md#turn-annotations-off) on the page, `true` turns them on against the site setting |

[Navigation](../Features/Navigation.md) explains the navigation keys in detail.
