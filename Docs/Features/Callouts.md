# Callouts

Callouts, also called admonitions or alerts, highlight information that readers should not miss. MDU uses the syntax of __GitHub alerts__, so your files look right on GitHub and in the rendered site.

## Syntax

Start a block quote with the alert type on its own line, then write the content on the following lines:

```markdown title="Markdown"
> [!NOTE]
> Useful information that users should know, even when skimming content.
```

MDU supports all five GitHub alert types:

```markdown title="Markdown"
> [!NOTE]
> Useful `information` that users should know, even when skimming content.

> [!TIP]
> `Helpful advice` for doing things better or more easily.

> [!IMPORTANT]
> `Key information` users need to know to achieve their goal.

> [!WARNING]
> `Urgent info` that needs immediate user attention to avoid problems.

> [!CAUTION]
> Advises about `risks` or `negative` outcomes of certain actions.
```

## Examples

This is the Markdown above, rendered. Switch the theme in the header to see the light and dark colors.

> [!NOTE]
> Useful `information` that users should know, even when skimming content.

> [!TIP]
> `Helpful advice` for doing things better or more easily.

> [!IMPORTANT]
> `Key information` users need to know to achieve their goal.

> [!WARNING]
> `Urgent info` that needs immediate user attention to avoid problems.

> [!CAUTION]
> Advises about `risks` or `negative` outcomes of certain actions.

A callout can contain any Markdown, such as several paragraphs, lists, links, and code:

> [!TIP]
> Preview your documentation while you write:
>
> ```shell
> mdu serve
> ```
>
> The [Usage](../Usage/README.md) section describes all commands.

## Rules

MDU follows the same rules as GitHub:

- The marker must be the first line of a block quote and stand alone on that line. Text after the marker, such as `> [!NOTE] Text`, renders a regular block quote.
- The type is not case-sensitive: `[!NOTE]`, `[!Note]`, and `[!note]` are equivalent.
- Only `NOTE`, `TIP`, `IMPORTANT`, `WARNING`, and `CAUTION` are alert types. Any other type renders a regular block quote.
- The marker must be followed by content. A block quote that contains only the marker remains a regular block quote.
- Callouts cannot be nested inside lists or other block quotes.

## Styling

MDU uses [Markdig's alert blocks](https://xoofx.github.io/markdig/docs/extensions/alert-blocks/) and renders the same HTML structure as GitHub: a `div` with the classes `markdown-alert` and `markdown-alert-<type>`, for example `markdown-alert-tip`, followed by a `markdown-alert-title` paragraph. The title is shown without an icon, and the colors adapt to the light and dark themes.
