---
description: Connect pages across sections with tags, tag pages, and a checked vocabulary.
tags: [Navigation, Search, PDF, Migration]
---

# Tags

The navigation sorts pages by folder. Many topics cut across that order: security is part of operations, development, and the reference. Tags connect such pages, so readers find everything about a topic in one place. Each page lists its tags below the title, each tag has a page of its own, and the search finds tags by name.

Tags are plain front matter, so the pages stay ordinary Markdown. Material for MkDocs, Docusaurus, and Hugo read the same `tags` key.

## Tag a page

List the tags in the front matter:

```yaml title="docs/Operations/Certificates.md"
---
description: Renew TLS certificates without downtime.
tags:
  - Security
  - Kubernetes
---
```

`tags: [Security, Kubernetes]` works too, and a single tag can be written as `tags: Security`. Letter case and repeated spaces do not make a different tag, but `mdu lint` warns about spellings that differ from the name.

Use tags for what lies across the navigation. A tag that only the pages of one folder carry repeats the navigation and helps nobody.

## Define the vocabulary

Without further setup, any tag is allowed, and the spelling most pages use names the tag. For a site that several people write, keep the tags in one Markdown page and set `content.tags` in [`mdu.yml`](../Usage/configuration.md#content):

```yaml title="docs/mdu.yml"
content:
  tags: Tags.md
```

Every second-level heading of that page defines a tag. The text below the heading describes it, and a single line starting with `Also:` lists other spellings that mean the same tag, separated by commas:

```markdown title="docs/Tags.md"
# Tags

Topics that span several areas of the documentation.

## Security

Certificates, secrets, and access rights across all environments.

## Kubernetes

Clusters, ingress, and Helm charts.

Also: k8s
```

With a vocabulary, every tag of a page must be one of its names or aliases:

- `mdu lint` reports an unknown tag as [`MDU026`](../Usage/lint/Rules.md#mdu026-unknowntag) and suggests the closest name.
- `mdu render` and `mdu pdf` stop with the same error, so a misspelled tag cannot reach the published site.
- `mdu serve` reports the error and keeps the preview running, so you can fix it while you write.

A page that uses an alias, such as `k8s`, is listed under the tag's name.

The vocabulary page remains an ordinary page of the site, with its place in the navigation. When MDU renders it, every tag section ends with a link to the tag page, such as **4 pages**, so the page doubles as the overview of all tags. On GitHub, it reads like a glossary. A tag that no page carries has no link.

This documentation uses its own [vocabulary](../Tags.md) as an example.

## What readers see

- **Tags below the title.** Each tag links to its tag page. Dashboards keep their tags for the tag pages but do not show them.
- **A page per tag** at `tags/<name>.html`, such as `tags/security.html`. It shows the tag's description, the number of pages, and the tags often used together with it. The pages are grouped by the first navigation level below the home page, in navigation order, each with its `description`, or its place in the navigation if it has none.
- **The overview** at `tags/index.html`. It forwards to the vocabulary page, or lists all tags with their counts when the site has no vocabulary.
- **The search** lists a tag first when the query starts its name or one of its aliases, such as `k8s` for Kubernetes. Page results show their tags. The [dashboard](Dashboard.md#filter-and-search) filter finds tags in the same way.
- **The PDF** shows a line such as *Tags: Security · Kubernetes* below each page title. Tag pages are not part of the PDF.

> [!NOTE]
> The tag pages need the `tags` folder of the output. If the documentation has a folder or a page of that name, such as `tags.md`, and a page uses tags, `mdu render` stops with an error. Rename the folder or page in that case. Relative links in a tag description keep working on the tag page.

- **The PDF properties** list the tags of the exported pages as keywords, so file managers and document search can find the PDF by them.

## List tagged pages on any page

A `tags` code block lists the pages that carry a tag, such as on a hub page that collects a topic. The list is written when the site is rendered, so it never goes out of date:

~~~markdown title="Markdown"
```tags
tags: [PDF]
exclude: [Migration]
limit: 6
```
~~~

This block lists the pages of this documentation that carry the tag **PDF** but not **Migration**:

```tags
tags: [PDF]
exclude: [Migration]
limit: 6
```

| Option | Default | Effect |
| --- | --- | --- |
| `tags` | Required | One tag or a list. A page must carry all of them. |
| `exclude` | None | One tag or a list. Pages with any of them are left out. |
| `limit` | `6` on pages, all on dashboards and in the PDF | The number of pages shown; `0` shows all. On a page, a link leads to the tag page when pages are left out. |

The pages appear in navigation order as cards with their area and `description`. The page with the block is never listed itself. The block also works inside a callout or a list item. With a vocabulary, the tags of the block must be defined there, like the tags of a page.

- **On a [dashboard](Dashboard.md)**, the pages become tiles of the group the block is in. The page `icon` and `description` become the tile's icon and subtitle.
- **In the PDF**, the block is a plain list. Pages of the same export link to their place in the document; the others are marked *(not in this PDF)*.
- **On GitHub**, the block stays a readable code block with its options.

## Check the tags

`mdu tags` lists every tag with the number of pages that carry it and the areas they belong to, most used first. Unused tags of the vocabulary appear with `0`:

```text
$ mdu tags docs
Security    11  Operations, Development, Reference
Kubernetes   6  Operations, Development
Incident     0  only in Tags.md
```

`mdu lint` warns about tags that only one page carries and about vocabulary tags that no page carries ([`MDU028`](../Usage/lint/Rules.md#mdu028-unusedtag)). A tag page with a single entry leads nowhere.

## Migrating from Material for MkDocs

The `tags` front matter of Material for MkDocs works unchanged. `mdu fix` turns a `Tags.md` or `tags.md` at the documentation root with the `<!-- material/tags -->` marker into the vocabulary: each marker becomes one heading per tag that it lists, and `content.tags` is added to `mdu.yml`. A marker lists the tags of its `include` option, or all tags of the pages, without those of its `exclude` option; a tag that several markers list gets its heading at the first one. The page keeps its other text. Its own second-level headings become third-level ones, because a second-level heading defines a tag. Markers in code blocks stay as they are. Without tagged pages, `mdu fix` only removes the markers. Add a description below each heading afterwards. To check the tags against a list as with `tags_allowed`, keep only the allowed names in the vocabulary.
