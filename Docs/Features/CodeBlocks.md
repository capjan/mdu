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
