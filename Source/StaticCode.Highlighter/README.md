# StaticCode.Highlighter

A purpose-built static syntax highlighter for MDU. It tokenizes code blocks at build time and emits plain HTML — no JavaScript, no runtime dependencies, no browser-side parsing.

## Why a static highlighter?

MDU renders the entire documentation to static HTML during `mdu render`. Every page is produced exactly once and then served as a file. An incremental parser such as Tree-sitter is designed for a different problem: re-parsing only the changed portion of a file on every keystroke inside an editor. That capability has no use in a static build pipeline, while the cost (runtime grammar loading, additional dependencies, non-deterministic failure modes) remains.

A lightweight lexer with fixed grammars is the matching abstraction:

- **Deterministic.** The same input always produces the same HTML. There is no browser variance and no WASM initialization.
- **No runtime dependencies.** The output is pure HTML with CSS classes. It works with JavaScript disabled, behind aggressive content filters, and in print/PDF export.
- **Predictable build cost.** A lexer over a typical documentation snippet finishes in microseconds. There is no parser startup or grammar compilation step.
- **Controlled failure.** Malformed or incomplete code simply produces unhighlighted text for the affected region instead of failing the entire page.

This is not a replacement for Tree-sitter or a language server. It is the right tool for short, mostly-correct code examples embedded in documentation.

## Architecture

The library is intentionally small and self-contained:

| Component | Responsibility |
|-----------|----------------|
| `Grammar` / `GrammarStore` | Language definitions (keywords, operators, string rules, comment markers, and optional special modes). |
| `Lexer` | Tokenizes source according to a grammar. Caches one lexer instance per language factory. |
| `HtmlFormatter` | Writes tokens as `<span class="...">` elements inside `<pre><code>`. |
| `TokenType` | Closed set of token kinds used for both analysis and styling. |

Grammars are plain C# objects. Adding or adjusting a language does not require external grammar files or a parser generator.

## Supported languages

The highlighter recognizes the following code-fence names (aliases share a grammar):

- `cs` / `csharp`
- `js` / `javascript` / `jsx`
- `ts` / `typescript`
- `py` / `python`
- `java`
- `kt` / `kotlin`
- `swift`
- `rs` / `rust`
- `go` / `golang`
- `sql`
- `html` / `htm`
- `css`
- `json`
- `yaml` / `yml`
- `bash` / `zsh` / `sh` / `shell`
- `md` / `markdown`

Unknown language names fall back to plain text (no highlighting).

## Special modes

Some languages activate additional analysis beyond keyword/operator matching:

- **YAML** — mapping keys are highlighted (`HighlightKeys`).
- **Shell** — commands, subcommands, options, and arguments are distinguished (`HighlightShellTokens`).
- **Markdown** — headings, emphasis, strong, links, code spans, block markers, and GitHub-style alerts (`HighlightMarkdown`).
- **HTML / CSS** — tags, attributes, and at-rules receive dedicated treatment.

## Output

`HtmlFormatter` produces markup such as:

```html
<pre><code><span class="keyword">if</span> (<span class="string">"ready"</span>) { ... }</code></pre>
```

Token classes map to CSS custom properties defined in the embedded `Highlight.css` (`--syntax-keyword`, `--syntax-string`, `--syntax-comment`, etc.). Themes control the actual colors; the highlighter only emits stable class names.

## Usage (internal)

```csharp
var lexer = Lexer.ForLanguage("csharp");
var tokens = lexer.Analyse("var x = 1;");
var formatter = new HtmlFormatter();
formatter.Write(tokens, writer);
```

`Lexer.ForLanguage` is thread-safe and caches the lexer for each grammar factory.

## Design notes

- Keywords, operators, and string delimiters come from the grammar. Comments and strings are extracted before the main keyword scan so they cannot be mis-tokenized.
- Numbers are recognized by a simple regex when the grammar defines any keywords (avoids highlighting numbers in plain-text or pure-markup modes).
- The lexer does not build a syntax tree and does not attempt error recovery beyond “continue after the current token.” That is sufficient for documentation snippets and keeps the implementation small and predictable.
