---
navigation.order: 2
---

# tags

`mdu tags` lists the [tags](../Features/Tags.md) of the pages: each tag with the number of pages that carry it and the areas of the navigation they belong to. The most used tags come first.

## Usage

```bash
mdu tags [PATH] [options]
```

See [Paths and file search](README.md#paths-and-file-search) for the default source folder and [common options](README.md#common-options) for `--pattern` and `--hidden`.

## Example

```text
$ mdu tags docs
Security    11  Operations, Development, Reference
Kubernetes   6  Operations, Development
Audit        4  Reference, Operations
Incident     0  only in Tags.md
```

With a [vocabulary](../Features/Tags.md#define-the-vocabulary), its tags that no page carries appear with `0`. If pages use tags wrongly, a line at the end points to `mdu lint`, which names the pages. The command only reads; it changes no files.
