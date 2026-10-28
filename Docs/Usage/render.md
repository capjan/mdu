---
navigation.order: 4
---

# render

`mdu render` converts the Markdown pages into a static HTML site and exits. It writes the pages and linked local assets to the output folder, `./site` by default. If any page contains `[Download the PDF](mdu:pdf)`, MDU also generates a combined `documentation.pdf` there and resolves the link to that file. It renders like any other link. Your Markdown files stay unchanged.

## Usage

```bash
mdu render [PATH [OUTPATH]] [options]
```

The alias `mdu r` runs the same command. See [Paths and file search](README.md#paths-and-file-search) for the default folders and [common options](README.md#common-options) for `--pattern` and `--hidden`.

| Option | Effect |
| --- | --- |
| `--editPageRoot <url>` | GitHub or GitLab edit URL of the documentation folder. Adds an edit icon beside each page's level-one heading, or an **Edit this page** link in the footer when there is none, and a [repository button](#repository-button) to the header. |

### Repository button

The repository button links to the repository root and is named after its path. A GitHub edit URL such as `https://github.com/example/project/edit/main/docs` gives `example/project`. A GitLab edit URL such as `https://gitlab.com/group/subgroup/project/-/edit/main/docs` keeps nested groups and gives `group/subgroup/project`.

The button shows the GitLab logo for GitLab's `/-/edit/` route and for hosts whose name contains `gitlab`, and the GitHub logo for hosts whose name contains `github`, such as GitHub Enterprise. Other hosts with the GitHub-style `/owner/repository/edit/` route get a general repository icon. On a GitLab instance served under a path, such as `https://example.com/gitlab/`, that path is part of the button name.

## Examples

```bash
mdu render
mdu render ./ProjectDocs ./public/docs
mdu render ./docs ./site --pattern "*.markdown" --editPageRoot "https://github.com/example/project/edit/main/docs"
```

## Site configuration

An optional `mdu.yml` in the documentation folder sets the site name, light and dark logos, theme colors, and an optional shared glossary. If no favicon files are specified, MDU uses the light and dark logo files as favicons. MDU copies the referenced files into the output folder. A square logo mark is most legible in a browser tab; MDU uses the logo file as-is. See [Abbreviations](../Features/Abbreviations.md) for local and shared glossary definitions.

An optional `icons:` value is the folder of your own icon sets. Without it, MDU uses the `.icons` folder of the documentation when there is one. See [Your own icons](../Features/Icons-and-Emojis.md#your-own-icons).

An optional `favicon:` value can be a single asset path for both themes, or a mapping with separate `light` and `dark` paths. MDU emits a light favicon link without a `media` condition as a fallback, followed by a dark link with `media="(prefers-color-scheme: dark)"`. The browser selects the dark link when that condition matches, independently of the page theme; the unconditional light link also lets browsers that do not match the dark condition display a favicon. Alternatively, one SVG can switch its internal styles with `prefers-color-scheme`. Both approaches follow the browser's effective preference, which can come from an OS or user-agent setting, though support varies by browser. For separate paths, selection relies on browser support for the `media` attribute on favicon links; Safari behavior can vary by version, so verify the versions you support. Use PNG or ICO if a browser you support does not display SVG favicons.

```yaml title="docs/mdu.yml"
site_name: Documentation
glossary: includes/abbreviations.md
icons: assets/icons
logo:
  light: assets/logo-light.svg
  dark: assets/logo-dark.svg
favicon:
  light: assets/favicon-light.svg
  dark: assets/favicon-dark.svg
colors:
  light:
    background: "#ffffff"
    text: "#1d1d1f"
    primary: "#0066cc"
  dark:
    background: "#1d1d1f"
    text: "#f5f5f7"
    primary: "#2997ff"
```

The background color also applies to the header, navigation, sidebar, and dialogs. Secondary text, lines, and code surfaces are mixed from the text and background colors, and hover backgrounds from the primary color, so they match a custom palette.

## Page front matter

YAML front matter at the top of a page controls its title, metadata, and navigation entry:

```yaml
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
| `navigation.title` | Label in the sidebar, tabs, and breadcrumbs |
| `navigation.order` | Sort order among sibling pages |
| `navigation.visible` | `false` hides the entry; the page stays reachable by its URL |
| `navigation.expanded` | `true` opens the section in the sidebar initially |
| `navigation.placement` | `top` makes a first-level page or section a [top navigation tab](../Features/Navigation.md#top-navigation) |
| `pdf.visible` | `false` leaves the page out of [`mdu pdf`](pdf.md) |

Both the nested form shown above and the compact form `navigation.order: 10` work. [Navigation](../Features/Navigation.md) explains the navigation keys in detail.
