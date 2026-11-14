# Code Blocks

Fenced code blocks show code or commands exactly as written. A language identifier after the opening fence turns on syntax highlighting, and every block gets a copy button.

## Syntax

Add a language identifier after the opening backticks:

~~~markdown title="Markdown"
```csharp
var greeting = "Hello, world!";
Console.WriteLine(greeting);
```
~~~

Add `title="..."` after the language to display a title above the code:

~~~markdown title="Markdown"
```js title="index.js"
console.log('Hello World!')
```
~~~

GitHub still displays this as a JavaScript code block, but does not render the title because GitHub Markdown has no code block title syntax. MDU displays the title above the code.

MDU renders the titled code block with a header and syntax highlighting:

```js title="index.js"
console.log('Hello World!')
```

> [!TIP]
> Hover over a code block or focus it with the keyboard to show the copy button. It copies the code without the title.

If you omit the language identifier or use an unsupported one, the code is rendered without syntax highlighting:

~~~
This is plain code.
~~~

## Show the result

Show what a piece of code produces directly below it, so readers see cause and effect at one glance. Write a block quote that starts with `[!RESULT]` on the line after the code block:

~~~markdown title="Markdown"
```bash title="Terminal"
mdu tree
```
> [!RESULT]
> ```text
> Documentation
> ├── Setup
> └── Usage
> ```
~~~

The code and its result become one card:

```bash title="Terminal"
mdu tree
```
> [!RESULT]
> ```text
> Documentation
> ├── Setup
> └── Usage
> ```

A result can contain any Markdown, so it also shows what a source renders to. Text appears as it would on the page:

~~~markdown title="Markdown"
```markdown title="Markdown"
Press ++ctrl+s++ to **save**.
```
> [!RESULT]
> Press ++ctrl+s++ to **save**.
~~~

```markdown title="Markdown"
Press ++ctrl+s++ to **save**.
```
> [!RESULT]
> Press ++ctrl+s++ to **save**.

Your file stays ordinary Markdown: GitHub shows the code block followed by a block quote with the marker as its first line. The rules of [callouts](Callouts.md#rules) apply, so a result has content, its marker stands alone, and it cannot sit inside a list or another block quote. Without a code block above it, a result is a card of its own. A heading in a result looks like a heading but is only an example, so it has no permalink and stays out of **On this Page** and the search sections.

## Annotations

Explain single lines without cluttering the code. Write a marker such as `(1)` in a comment, and a numbered list directly after the code block. Each marker becomes a numbered button that opens the matching entry next to it:

~~~markdown title="Markdown"
```yaml title="mdu.yml"
site:
  name: Acme Docs # (1)!
content:
  tags: Tags.md # (2)!
```

1. Shown in the header and in the browser tab.
2. A page whose `##` headings are the allowed tags. Unknown tags then stop `mdu render`.
~~~

```yaml title="mdu.yml"
site:
  name: Acme Docs # (1)!
content:
  tags: Tags.md # (2)!
```

1. Shown in the header and in the browser tab.
2. A page whose `##` headings are the allowed tags. Unknown tags then stop `mdu render`.

Select a marker, or move to it with ++tab++ and press ++enter++. ++esc++ or a click elsewhere closes the explanation. The copy button copies the code without the markers.

The syntax is the one of Material for MkDocs, so annotated pages need no changes when you migrate. On GitHub and in your editor, the comment and the numbered list stay readable as they are.

### Rules

- A marker is a number in parentheses: `(1)`, `(2)`, and so on. In a comment it counts anywhere.
- Outside comments, a marker counts only at the end of a line and after a space, such as `kubectl apply -f app.yaml (1)`. So `print(1)` stays code. This also annotates `text` blocks, console output, and languages without comments.
- `(1)!` removes more than the marker: the comment characters of an otherwise empty comment, such as `# (1)!`, or the space before a marker outside comments.
- The numbered list starts at `1.` and is the next block after the code block, or after its [result](#show-the-result). Its entries can contain any Markdown, such as code, links, or lists.
- A code block is annotated when at least one marker matches an entry. The whole list then belongs to the code block. A list after code without matching markers stays an ordinary list.
- Several markers on one line and the same marker in several places are fine.

In the [PDF](../Usage/pdf.md), markers read as `(1)` in the code and the entries follow as a numbered list below it. `mdu lint` warns about a marker without an entry (`MDU029`) and an entry without a marker (`MDU030`).

### Turn annotations off

Annotations are on by default. If a numbered list after code is not about the code, turn them off where it happens. The narrowest setting wins:

| Where | Write | Applies to |
| --- | --- | --- |
| Code block | `annotate=false` after the language, such as ` ```python annotate=false ` | This code block |
| Front matter | `content.code.annotate: false` | All code blocks of the page |
| `mdu.yml` | `content.code.annotate: false` | All pages |

`annotate=true` on a code block turns annotations back on where a page or the site has turned them off. Like `title=`, GitHub ignores `annotate=`.

## Supported languages

Use one of these language identifiers:

| Language | Identifiers |
| --- | --- |
| C# | `cs`, `csharp` |
| JavaScript | `js`, `javascript`, `jsx` |
| Swift | `swift` |
| TypeScript | `ts`, `typescript` |
| Python | `py`, `python` |
| Kotlin | `kt`, `kotlin` |
| JSON | `json` |
| SQL | `sql` |
| HTML | `html`, `htm` |
| CSS | `css` |
| Java | `java` |
| Rust | `rs`, `rust` |
| Go | `go`, `golang` |
| YAML | `yaml`, `yml` |
| Shell | `bash`, `zsh`, `sh`, `shell` |
| Markdown | `md`, `markdown` |

Markdown code blocks highlight headings, block markers, links, inline code, and emphasis while keeping the Markdown source visible as code. The highlighter uses lightweight language rules rather than full language parsers, so it colors selected syntax elements. Other languages remain readable as plain code.
