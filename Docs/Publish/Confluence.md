---
navigation.order: 4
---

# Confluence

Publish a one-way, edit-protected copy of your Markdown documentation to Confluence Data Center:

```bash
mdu publish confluence Docs --parent-page-id 123456 --dry-run
mdu publish confluence Docs --parent-page-id 123456
```

MDU creates its own documentation tree below the existing parent page. It never replaces that parent, the space homepage, or unrelated pages. Markdown remains the source of truth. No Confluence IDs are written into your source files.

> [!IMPORTANT]
> This integration targets the Confluence Data Center REST API, with contracts checked against the published **10.2.19** reference and automated simulated-server tests. It has not yet been validated against a live 10.2.19 installation. Complete the [acceptance checks](#before-production-use) in a test space before production use. Confluence Cloud is not supported.

## Connection

Provide three environment variables:

| Variable | Required | Meaning |
| --- | --- | --- |
| `MDU_CONFLUENCE_URL` | Yes | HTTPS base URL, including any context path, for example `https://wiki.example.org/confluence` |
| `MDU_CONFLUENCE_SPACE` | Yes | The only space MDU may write to |
| `MDU_CONFLUENCE_PAT` | Yes | Personal access token of a dedicated publishing account |
| `MDU_CONFLUENCE_PARENT_PAGE_ID` | No | Existing page below which MDU creates its own root |

Store the PAT as a masked CI secret, not in Markdown, a committed file, shell history, or a command argument. No username is needed: MDU sends the PAT as a Bearer token. Install your organization's certificate authority on the runner if needed; certificate verification cannot be disabled.

Without a parent page ID, MDU creates a top-level managed root in the specified space. For a **shared space**, always specify a parent page ID. The parent must belong to the configured space. Its existing view restrictions continue to apply to the exported descendants.

## Command reference

```text
mdu publish confluence [path] [options]
```

| Option | Default | Meaning |
| --- | --- | --- |
| `--url <url>` | `MDU_CONFLUENCE_URL` | Override the Confluence base URL |
| `--space <key>` | `MDU_CONFLUENCE_SPACE` | Override the target space |
| `--parent-page-id <id>` | `MDU_CONFLUENCE_PARENT_PAGE_ID` | Set the existing parent page |
| `--export-id <id>` | `documentation` | Stable identity for this documentation project |
| `--dry-run` | Off | Prepare content, validate the target, and print planned changes using GET requests only |
| `-p, --pattern <pattern>` | `*.md` | Select Markdown files |
| `--hidden <bool>` | `true` | Include hidden files and folders |

The source path uses the same default as `mdu render`: the project's `docs` folder. Connection errors, conflicts, unsafe targets, and unsupported local links produce a nonzero exit code.

Use a different `--export-id` for each project published below the same parent. Keep URL, space, parent, and export ID stable between deployments. Changing them selects another export; MDU does not move or remove the previous one. CLI options and CI variables are trusted deployment configuration: protect changes to them with repository reviews and protected environments.

## Write boundary

A PAT inherits its user's permissions. **It is not a space-scoped credential.** A space key in a command cannot reduce the permissions of a stolen PAT.

Before creating the token, ask the Confluence administrator to:

1. Create a dedicated, non-administrative publishing account.
2. Grant it **View**, **Add page**, **Add attachment**, and the required **Restrictions** permissions in the target space.
3. Ensure it has no write access in other spaces, including through groups, default permissions, or anonymous access.
4. Do not grant page deletion, space administration, or global administration for this integration.
5. Test the effective permissions with the actual publishing account.

MDU adds application-level protection:

- The space is fixed for the duration of a run; Markdown and front matter cannot override it.
- Before writes, MDU reads and verifies the actual page space, ancestry, and export ownership.
- Updates are confined to its managed root and descendants, and additionally to the configured parent subtree.
- Pages are identified by export metadata and logical source paths, never adopted just because their titles match.
- A registry on the managed root detects registered pages moved outside the subtree or into another space. MDU stops instead of moving them back.
- Attachments, page properties, and restrictions use the same guarded client.
- MDU does not change space permissions or create spaces. It never deletes pages or attachments.
- Redirects are rejected. Pagination requests are constructed locally; server-provided next-page URLs are not followed.

Confluence titles carry a deterministic MDU suffix so equally named pages in different source folders can coexist. This suffix is intentional and should not be removed manually.

These client-side checks are not a server-enforced subtree permission model. The account still has space-level capabilities, and concurrent administrative moves cannot be made atomic with a REST request. Server-side account restrictions remain mandatory. If the managed root is moved out of the configured space, a later run may no longer discover it there; it will never follow it to write into that other space.

## Readonly means edit protection

Every exported page is restricted for editing to the publishing account. Edit restrictions are set on **each page**, because Confluence does not inherit them from a parent.

For a shared space this is **not a blanket prohibition on all interaction**:

- Comments remain allowed when the reader has the space's **Add comments** permission.
- Uploading attachments, adding child pages, and other actions remain governed by their own Confluence permissions.
- Space and system administrators can remove restrictions.

MDU does not silently change those space-wide permissions. To provide a fully non-interactive mirror, use a dedicated space whose readers have only View permission, or arrange the necessary server-side controls with your administrator.

Before transferring documentation content, MDU creates a neutral placeholder, applies publisher-only read and edit restrictions, and verifies them. If the server ignores or rejects those restrictions, no documentation body or attachments are transferred to that new page. A neutral placeholder can briefly be visible if the server ignores restrictions supplied during creation.

New pages remain readable only by the publisher until their content and edit protection have been checked. The initial root is opened last. Existing pages retain their edit protection throughout updates. A failure can leave private pages behind; a later successful run can finish publishing them.

## Synchronization and recovery

- The page tree comes from the same Markdown graph as HTML rendering, including repository imports.
- Content properties store the export identity, logical source path, and format version; the managed root holds a per-page registry.
- Identical bodies and titles are not rewritten. Identical attachments are recognized by their content-addressed names and checksum comments.
- A title conflict with an unrelated page, unexpected parent, missing registered page, weakened edit restriction, or HTTP version conflict stops publication.
- MDU never guesses renames. A moved or renamed source path becomes a new managed page; the old page is retained and reported as stale.
- Removed pages and old attachment versions are not deleted automatically.
- A whole publication is not transactional. Existing readers can see a mixture of old and new pages during deployment.
- Partial runs normally resume without duplicates. If initial creation succeeds but writing the ownership property fails, MDU deliberately refuses to adopt the unmarked placeholder on retry. An administrator must inspect and remove that specific placeholder before retrying.
- Restoring a deleted or moved managed page is an administrative recovery action, not an automatic import into another location.

Run only **one publisher per target at a time**. Use CI concurrency/resource groups; the command does not provide a distributed lock across pipelines or repositories.

## Content compatibility

The mirror uses Confluence's storage format, not a copy of the complete HTML website.

| Source content | Confluence representation |
| --- | --- |
| Headings, paragraphs, lists, tables, emphasis | Static page content |
| Code | Native code macro |
| Callouts | Panel macro |
| Markdown links and heading links | Page links and explicit anchors |
| Local images and downloads | Content-addressed page attachments |
| External images and links | External URLs; MDU does not fetch them with the PAT |
| Supported Mermaid, PlantUML, and context diagrams | Self-contained SVG attachments |
| Math and icons | SVG attachments; Unicode emojis remain text |
| Keyboard shortcuts and task lists | Static keys and checkbox text |
| Shared glossary | Abbreviation titles, with local definitions taking precedence |
| OpenAPI blocks | Expanded static reference; referenced specification files are attached |
| `mdu:pdf` links | Generated documentation PDF attachment |
| Hero, icon search, unsupported diagrams, isometric animation | Source code with an explicit diagnostic |
| Dashboard layout | Ordinary Markdown content, with an explicit diagnostic |
| Raw HTML in Markdown | Escaped text with an explicit diagnostic |
| Website navigation, theme, search, scripts | Not transferred; Confluence provides its own interface |

Inline OpenAPI specifications have no separate original file attachment. Generated interactive OpenAPI controls become static content; collapsible details are expanded. SVG display and link behavior depend on the target installation and must be checked in the pilot.

Local links must resolve inside the export tree. Local link query strings, protocol-relative URLs, unsafe URL schemes, missing files, attachments outside their source root, and symlink attachments are rejected before publishing. Imported assets may stay inside their imported repository root.

## Pipeline examples

Install the approved MDU build using your existing [.NET tool](../Setup/DotNet-Tool-Installation.md), [portable](../Setup/Portable-Installation.md), or [Docker](../Setup/Docker-Installation.md) deployment method. The runner must reach the internal Confluence URL.

Publish only from a trusted deployment branch, after validation and the normal site deployment have succeeded. Never expose the PAT to untrusted pull request or fork code. Keep the publication in the same deployment sequence when you want a site deployment failure to prevent Confluence publication.

### GitHub Actions

Add a step to your existing deployment job, after MDU installation and site deployment. Use a runner with internal network access. Set a protected environment, define URL/space/parent as environment variables, and store the PAT as an environment secret.

```yaml
concurrency:
  group: confluence-documentation
  cancel-in-progress: false

jobs:
  deploy:
    if: github.ref == 'refs/heads/main'
    runs-on: self-hosted
    environment: documentation
    steps:
      # Existing checkout, MDU installation, validation and site deployment steps go here.
      - name: Publish Confluence mirror
        env:
          MDU_CONFLUENCE_URL: ${{ vars.MDU_CONFLUENCE_URL }}
          MDU_CONFLUENCE_SPACE: ${{ vars.MDU_CONFLUENCE_SPACE }}
          MDU_CONFLUENCE_PARENT_PAGE_ID: ${{ vars.MDU_CONFLUENCE_PARENT_PAGE_ID }}
          MDU_CONFLUENCE_PAT: ${{ secrets.MDU_CONFLUENCE_PAT }}
        run: mdu publish confluence Docs
```

Keep the concurrency group identical for every workflow that publishes this target. GitHub concurrency does not coordinate across repositories.

### GitLab CI

Use a protected runner with network access and MDU installed. Configure the four connection variables in CI/CD settings; mark the PAT **masked and protected**.

```yaml
publish-confluence:
  stage: deploy
  resource_group: confluence-documentation
  rules:
    - if: '$CI_COMMIT_BRANCH == $CI_DEFAULT_BRANCH'
  script:
    - mdu lint Docs
    # Put this command after the existing successful site deployment.
    - mdu publish confluence Docs
```

When site deployment is a separate job in the same stage, declare the corresponding job dependency with `needs`. A resource group serializes jobs only within its GitLab project.

## Before production use

Use a disposable subtree in a test space on your actual Confluence version:

1. Run `--dry-run`; confirm the exact URL, space, parent and export ID.
2. Publish representative pages, images, a diagram, a formula, an OpenAPI reference, a PDF link and cross-page anchors.
3. As a normal reader, confirm that pages are visible and editing is denied. Check comments separately; they are not disabled by MDU.
4. Repeat the export unchanged; confirm that page and attachment versions do not change.
5. Interrupt a run and retry; confirm private-page recovery without duplicates.
6. Supply a parent from another space, introduce a foreign title collision, and move a registered managed page outside the subtree. Each case must fail without modifying unrelated content.
7. Independently confirm that the publishing account cannot write in any other space.
8. Check notification behavior, SVG rendering, and proxy/certificate handling.

No live Confluence checks are performed by the repository's normal test suite.

## Why this is part of MDU

`Markdown.Confluence` is a separate library delivered through the existing `mdu` executable. It reuses the page graph and rendering helpers without introducing a second installation, Python/Sphinx configuration, a server, or a plugin framework. HTML rendering remains a separate command and never publishes to Confluence implicitly.

See Atlassian's documentation for [personal access tokens](https://confluence.atlassian.com/enterprise/using-personal-access-tokens-1026032365.html), [space permissions](https://confluence.atlassian.com/doc/space-permissions-overview-139521.html), [page restrictions](https://confluence.atlassian.com/doc/page-restrictions-139414.html), [storage format](https://confluence.atlassian.com/doc/confluence-storage-format-790796544.html), and the [10.2.19 REST API](https://developer.atlassian.com/server/confluence/rest/v10219/).
