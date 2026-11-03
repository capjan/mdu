# Formatting

MDU uses Markdig to render CommonMark syntax and its enabled extensions. Markdown remains readable as plain text in the source file. The examples below show the syntax and the rendered result.

## Headings and paragraphs

Use one to six `#` characters for heading levels one through six. The rendered headings receive an automatic ID; headings from level two on also get a permalink icon that appears on hover or focus, and on touch screens for the section you jumped to. Separate paragraphs with a blank line.

~~~markdown title="Markdown"
## A section heading

This is one paragraph. A line break in the source
continues the paragraph.

This is a second paragraph.
~~~
> [!RESULT]
> ### A section heading
>
> This is one paragraph. A line break in the source
> continues the paragraph.
>
> This is a second paragraph.

## Line breaks

A single newline in the Markdown source continues the same paragraph. To render a hard line break, end the line with two spaces or a backslash. The two-space form is preferred for readability: it keeps the break unobtrusive in the Markdown source. Press the space bar twice at the end of the line:

~~~markdown title="Markdown"
This line ends here.  
This line starts below it.
~~~
> [!RESULT]
> This line ends here.  
> This line starts below it.

The backslash is an alternative:

~~~markdown title="Markdown"
This line ends here.\
This line starts below it.
~~~
> [!RESULT]
> This line ends here.\
> This line starts below it.

## Inline formatting

Use asterisks for emphasis and backticks for inline code. Markdig's emphasis extras also support strikethrough, subscript, superscript, and marked text:

~~~markdown title="Markdown"
*italic*, **bold**, ***bold italic***, ~~deleted~~, H~2~O, x^2^, ==marked==, and `inline code`.
~~~
> [!RESULT]
> *italic*, **bold**, ***bold italic***, ~~deleted~~, H~2~O, x^2^, ==marked==, and `inline code`.

## Lists

Use `-`, `*`, or `+` for unordered lists, and a number followed by a period for ordered lists. Indent nested items by two spaces:

~~~markdown title="Markdown"
- First item
- Second item
  - Nested item

1. First step
2. Second step
~~~
> [!RESULT]
> - First item
> - Second item
>   - Nested item
>
> 1. First step
> 2. Second step

Task lists use `[ ]` for an open item and `[x]` for a completed item:

~~~markdown title="Markdown"
- [x] Write the page
- [ ] Review the examples
~~~
> [!RESULT]
> - [x] Write the page
> - [ ] Review the examples

## Links

Create a link with a label and a destination. An optional title appears when the reader hovers over the link. Plain URLs such as `https://github.com/xoofx/markdig` are also recognized automatically.

~~~markdown title="Markdown"
[Markdig](https://github.com/xoofx/markdig "Markdig on GitHub")
~~~
> [!RESULT]
> [Markdig](https://github.com/xoofx/markdig "Markdig on GitHub")

## Block quotes and separators

Start each quoted line with `>`; block quotes can contain other Markdown formatting, including lists:

~~~markdown title="Markdown"
> A block quote can contain *emphasis*.
>
> - It can also contain a list.
~~~
> [!RESULT]
> > A block quote can contain *emphasis*.
> >
> > - It can also contain a list.

Use a line containing three hyphens, asterisks, or underscores to add a horizontal separator:

~~~markdown title="Markdown"
---
~~~
> [!RESULT]
>
> ---

## Collapsible sections

Wrap content in an HTML `<details>` element to show it only on demand; the `<summary>` is the visible title. Leave blank lines around the Markdown inside so it is rendered as Markdown. Add the `open` attribute to expand the section initially:

~~~markdown title="Markdown"
<details>
<summary>Completed projects</summary>

- Project A
- Project B

</details>
~~~
> [!RESULT]
> <details>
> <summary>Completed projects</summary>
>
> - Project A
> - Project B
>
> </details>

## Related features

- [Footnotes](Footnotes.md) - Numbered notes with hover previews
- [Callouts](Callouts.md) - Notes, tips, and warnings
- [Tables](Tables.md) - Pipe tables with alignment
