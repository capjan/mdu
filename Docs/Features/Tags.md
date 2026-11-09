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

## Migrating from Material for MkDocs

The `tags` front matter of Material for MkDocs works unchanged. To check the tags against a list, move the names of `tags_allowed` into a vocabulary page as described above.

`mdu fix` replaces the `<!-- material/tags -->` marker of a root `Tags.md` with a static list of links. The tag pages make that list unnecessary: before you use the page as the vocabulary, replace each list below a tag heading with a description of the tag.
