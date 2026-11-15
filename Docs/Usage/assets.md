---
navigation.order: 8
description: Upload images to the MDU Asset Server of the site and get their Markdown snippets, and let mdu lint check image links on it.
---

# assets

The [MDU Asset Server](https://github.com/capjan/mdu-assets) keeps the images of a documentation site behind stable links. `mdu assets upload` sends images to it and prints a ready snippet for each, and [`mdu lint`](lint/README.md) reports image links on it that point to nothing ([MDU032](lint/Rules.md#mdu032-brokenassetlink)).

## Connect the site

Name the server in [`mdu.yml`](configuration.md#assets):

```yaml title="docs/mdu.yml"
assets:
  server: https://assets.example.com   # web interface and API
  public: https://img.example.com      # public image links, if on another host
  project: docs                        # default project for uploads
```

Uploads need a personal API token. Create one on the **Account** page of the asset server and put it into the environment, never into `mdu.yml`:

```bash
export MDU_ASSETS_TOKEN=mdua_…
```

MDU sends the token only over https, or to a server on the same machine.

## Upload images

```bash
mdu assets upload screenshot.png
```

> [!RESULT]
> `![Screenshot](https://img.example.com/i/k3F9xQa2Bc/screenshot.webp)`

Each file gets one line on standard output, so the result can be piped, for example into the clipboard with `| pbcopy`. Messages go to standard error. When the project already holds the same image, MDU prints the existing link instead of uploading it twice.

| Option | Effect |
| --- | --- |
| `--project <name>` | Project to upload into, instead of `assets.project` |
| `--tag <tag>` | Tag for the uploaded images; repeat for several |
| `--alt <text>` | Alt text; without it the server makes one from the file name |
| `--name <file name>` | File name for an image from standard input, or for a single file |
| `--format <format>` | `markdown` (default), `url`, or `html` |
| `--root <folder>` | Documentation folder with `mdu.yml`; defaults to `./docs` |

`-` reads one image from standard input, for example a screenshot from the clipboard on macOS:

```bash
pngpaste - | mdu assets upload - --name settings.png --tag Setup
```

The exit code is `1` when an image could not be uploaded; the other images are still uploaded.

## Check image links

When `assets.server` is set, `mdu lint` asks the server about every link and image on its public host and reports those whose image is unknown, deleted, or in the trash. No token is needed. If the server cannot be reached, lint warns and goes on; `--offline` skips the check.

[Back to Usage](README.md)
