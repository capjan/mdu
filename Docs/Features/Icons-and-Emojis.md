# Icons and Emojis

MDU turns shortcodes such as `:rocket:` or `:material-account-circle:` into emojis and icons while it renders your Markdown. Seven open source icon sets and GitHub-compatible emoji shortcodes are built in. They are ready to use out of the box and embedded directly into the rendered pages.

:rocket: :material-account-circle: :dashboard-github:

## Search

Type part of a name, such as `github`, `arrow right`, or `heart`. Click a result to copy its shortcode. The filters limit the results to icons or emojis.

```icon-search
```

Add the same search to your own documentation with an empty `icon-search` code block:

````markdown title="Markdown"
```icon-search
```
````

MDU writes the search script and its index to the `js` folder of the site only when a page contains an icon search.

## Usage

### Using emojis

The best way to add an emoji is usually to type the emoji itself. It needs no conversion, so your Markdown reads the same on GitHub, in your editor, and in every other tool. Every major operating system has an emoji picker:

| Operating system | Shortcut |
|---|---|
| macOS | ++ctrl+cmd+space++ or ++globe+e++ |
| Windows | ++win+period++ or ++win+semicolon++ |
| Linux (GNOME and GTK apps) | ++ctrl+period++ or ++ctrl+semicolon++ |

If you prefer to keep your files plain ASCII, use a shortcode instead. Emojis use the same shortcodes as GitHub, such as `:smile:`, `:+1:`, `:warning:`, or `:de:`:

```markdown title="Markdown"
Ship it :rocket:
```

Ship it :rocket:

MDU writes the Unicode character into the page, so both ways produce the same result: emojis look like the emojis of the reader's operating system, and GitHub shows them too.

### Using icons

Icons use the name of their icon set as a prefix, followed by the icon name. The prefixes are the same as in [Material for MkDocs](https://squidfunk.github.io/mkdocs-material/reference/icons-emojis/), so existing documentation keeps working.

```markdown title="Markdown"
The repository is on :fontawesome-brands-github: GitHub.
```

The repository is on :fontawesome-brands-github: GitHub.

| Icon set | Prefix | Example |
|---|---|---|
| [Material Design Icons](https://pictogrammers.com/library/mdi/) | `material-` | `:material-account-circle:` |
| [Font Awesome Free](https://fontawesome.com/search?ic=free) Brands | `fontawesome-brands-` | `:fontawesome-brands-github:` |
| Font Awesome Free Regular | `fontawesome-regular-` | `:fontawesome-regular-face-laugh-wink:` |
| Font Awesome Free Solid | `fontawesome-solid-` | `:fontawesome-solid-rocket:` |
| [Octicons](https://primer.style/octicons/) | `octicons-` | `:octicons-heart-fill-24:` |
| [Simple Icons](https://simpleicons.org/) | `simple-` | `:simple-dotnet:` |
| [Dashboard Icons](https://dashboardicons.com/) | `dashboard-` | `:dashboard-jellyfin:` |

For example, `:material-account-circle:` shows the `account-circle` icon from Material Design Icons, and `:octicons-heart-fill-24:` shows the 24-pixel version of the filled Octicons heart.

Icons are embedded into the page as inline SVG. They scale with the font size and take the color of the surrounding text, so they fit headings, links, and the light and dark theme without extra work:

> [!TIP]
> Icons in callouts take the color of the text around them.

Outside of MDU, for example on GitHub, icon shortcodes are shown as plain text.

#### Logos in color

Dashboard Icons are logos of apps and self-hosted services. Colored logos keep their own colors. When a logo has a light and a dark variant, MDU adds both and shows the one that fits the current theme, correcting reversed black/white pairs by their colors. Simple all-black or all-white logos without variants take the surrounding text color so they remain visible in either theme. Switch the theme in the header to see the GitHub logo change:

```markdown title="Markdown"
:dashboard-github: GitHub
```

:dashboard-github: GitHub

The variants also have their own shortcodes, for example `:dashboard-github-light:`, if a page should always show the same one. These preserve the upstream files: a suffix can describe the logo color or its intended background, depending on the logo. Use the shortcode without a suffix for automatic theme selection.

MDU includes the Dashboard Icons whose SVG files are at most 20 KB and contain no embedded images, scripts, or links, which are about nine out of ten.

### Where shortcodes work

Shortcodes work anywhere in text, for example in paragraphs, lists, tables, links, and callouts. They are not replaced inside code spans and code blocks, so you can still write about them, as this page does. A shortcode that MDU does not know stays as it is.

### Icons before links

Put an icon directly before a link to lead the reader to it. When the pointer is over the link, the icon moves a little toward it:

```markdown title="Markdown"
:material-arrow-right: [Search](#search)
```

:material-arrow-right: [Search](#search)

Use a separate line for each entry. Any icon works, not only arrows, for example `:octicons-arrow-right-24:` or `:material-chevron-right:`. The link must be the next element after the icon: text or spaces in between are fine, but a link inside emphasis, such as `**[Styling](#styling)**`, does not move the icon. When the reader's system asks for reduced motion, the icon stays in place.

### Styling

Add an attribute list directly after a shortcode, without a space, to give the icon or emoji an ID, classes, or HTML attributes. For example, set a color with the `style` attribute:

```markdown title="Markdown"
:material-check-circle:{ style="color: #16803c" } Complete
```

:material-check-circle:{ style="color: #16803c" } Complete

For Dashboard Icons, an explicit inline `color` style recolors solid, opaque fills and strokes; other brand colors in the logo become the requested color. White details, transparent and `none` paints, gradients, masks, and shapes with filters or partial opacity keep their original treatment. This applies to explicit shortcode icons; automatic dashboard icons retain their bundled colors. A CSS class that sets `color` does not replace an icon's embedded palette, so use the inline `style` attribute to recolor a logo. Hex colors such as `#16803c` are also used in PDF output.

## HTML output

An icon is rendered as a `span` with the class `icon` that contains the SVG; the SVG is hidden from screen readers because icons are usually decorative. An emoji is rendered as a `span` with the class `emoji` and the shortcode as its title:

```html
<span class="icon"><svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" aria-hidden="true" focusable="false">…</svg></span>
<span class="emoji" title=":rocket:">🚀</span>
```

## Licenses

The icon sets are published by their authors under these licenses:

- Material Design Icons: [Apache License 2.0](https://github.com/Templarian/MaterialDesign/blob/master/LICENSE), by Pictogrammers
- Font Awesome Free: [CC BY 4.0](https://fontawesome.com/license/free), by Fonticons, Inc.
- Octicons: [MIT License](https://github.com/primer/octicons/blob/main/LICENSE), by GitHub
- Simple Icons: [CC0 1.0](https://github.com/simple-icons/simple-icons/blob/develop/LICENSE.md), by the Simple Icons contributors
- Dashboard Icons: [Apache License 2.0](https://github.com/homarr-labs/dashboard-icons/blob/main/LICENSE), Copyright (c) 2024 Bjorn Lammers, Meier Lukas, Thomas Camlong and Homarr Labs

MDU converts the icons for inline use: it reduces the Iconify packages to their SVG content and optimizes the Dashboard Icons with [SVGO](https://svgo.dev/). The `mdu` package contains the license texts and these notices in `THIRD-PARTY-NOTICES.md`.

Simple Icons and Dashboard Icons contain logos of products and companies. The logos remain trademarks of their owners, and the icon licenses do not grant any trademark rights. Use a logo to refer to its product, as documentation usually does, and check the brand's guidelines for other uses.
