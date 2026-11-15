---
navigation.order: 2
---

# lint

`mdu lint` checks the Markdown pages under a folder and reports each problem with its file, line, rule code, and message. It exits with code `1` when it finds errors and `0` otherwise; warnings do not change the exit code.

## Usage

```bash
mdu lint [PATH] [options]
```

The alias `mdu l` runs the same command. See [Paths and file search](../README.md#paths-and-file-search) for the default source folder and [common options](../README.md#common-options) for `--pattern` and `--hidden`.

| Option | Effect |
| --- | --- |
| `--pretty` | Group results by file and use paths relative to the lint root. |
| `--color` | Colorize errors and warnings. |
| `--format <format>` | `text` (default), `json` for scripts and tools, or `github` for annotations in GitHub Actions. `--pretty` and `--color` apply to `text` only. |
| `--offline` | Skip checks that need the network, such as image links on the [asset server](../assets.md#check-image-links) ([MDU032](Rules.md#mdu032-brokenassetlink)). |

## Rules

Each diagnostic has a stable code and a descriptive name, for example `MDU004` `LocalLinkExists`:

```text
/project/docs/Overview.md:7: MDU004 LocalLinkExists: Local link target './intro.md' does not exist.
```

- [Lint Rules Reference](Rules.md) - All codes, severities, checks, and fixes
- Guides for [Rule 2](Rule2.md), [Rule 3](Rule3.md), and [Rule 5](Rule5.md)

The linter also lists orphaned pages: pages hidden with `navigation.visible: false` that no visible page links to. This list is informational and does not affect the exit code.

> [!TIP]
> [`mdu fix`](../fix.md) migrates MkDocs Material syntax and fixes `MDU006` automatically, including the links to moved pages. Run `mdu lint` again afterward.

## Output for tools and CI

`--format json` prints one JSON document and nothing else, so scripts can read it directly. `file` is relative to the lint root and `path` is the full path. `rule` is `null` for rules whose messages carry only a code.

```json
{
  "root": "/project/docs",
  "errors": 1,
  "warnings": 0,
  "diagnostics": [
    {
      "file": "Overview.md",
      "path": "/project/docs/Overview.md",
      "line": 7,
      "severity": "error",
      "code": "MDU004",
      "rule": "LocalLinkExists",
      "message": "Local link target './intro.md' does not exist."
    }
  ],
  "orphanedPages": []
}
```

`--format github` writes [workflow commands](https://docs.github.com/actions/using-workflows/workflow-commands-for-github-actions) that GitHub Actions shows as annotations on the affected lines of a pull request. Paths are relative to the working directory, so run the command from the repository root:

```yaml title=".github/workflows/docs.yml"
- name: Lint documentation
  run: mdu lint --format github
```

The exit code is the same for every format.

## Examples

```bash
mdu lint                                   # the default docs folder
mdu lint ./ProjectDocs                     # another folder
mdu lint ./ProjectDocs --pattern "*.markdown"
mdu lint --pretty --color                  # readable output in a terminal
```
