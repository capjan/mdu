---
navigation.order: 4
---

# render

`mdu render` converts the Markdown pages into a static HTML site and exits. It writes the pages and linked local assets to the output folder, `./site` by default. If any page contains `[Download the PDF](mdu:pdf)`, MDU also generates a combined `documentation.pdf` there and resolves the link to that file. It renders like any other link. Your Markdown files stay unchanged.

## Usage

```bash
mdu render [PATH [OUTPATH]] [options]
```

The alias `mdu r` runs the same command. See [Paths and file search](README.md#paths-and-file-search) for the default folders and [common options](README.md#common-options) for `--pattern` and `--hidden`.

| Option | Effect |
| --- | --- |
| `--editPageRoot <url>` | GitHub or GitLab edit URL of the documentation folder. Adds an edit icon beside each page's level-one heading, or an **Edit this page** link in the footer when there is none, and a [repository button](#repository-button) to the header. |

### Repository button

The repository button links to the repository root and is named after its path. A GitHub edit URL such as `https://github.com/example/project/edit/main/docs` gives `example/project`. A GitLab edit URL such as `https://gitlab.com/group/subgroup/project/-/edit/main/docs` keeps nested groups and gives `group/subgroup/project`.

Web IDE URLs work too. `https://code.example.com/-/ide/project/group/subgroup/project/edit/main/-/docs/` gives a GitLab button linking to `https://code.example.com/group/subgroup/project`, including on self-hosted instances. A `github.dev` URL such as `https://github.dev/example/project/blob/main/docs/` gives a GitHub button linking to `https://github.com/example/project`. The page's edit link keeps the original Web IDE URL in both cases.

The button shows the GitLab logo for GitLab's `/-/edit/` route and for hosts whose name contains `gitlab`, and the GitHub logo for hosts whose name contains `github`, such as GitHub Enterprise. Other hosts with the GitHub-style `/owner/repository/edit/` route get a general repository icon. On a GitLab instance served under a path, such as `https://example.com/gitlab/`, that path is part of the button name and repository URL, including for Web IDE links. For example, `https://example.com/gitlab/-/ide/project/group/project/edit/main/-/docs/` gives `gitlab/group/project` and links to `https://example.com/gitlab/group/project`.

## Examples

```bash
mdu render
mdu render ./ProjectDocs ./public/docs
mdu render ./docs ./site --pattern "*.markdown" --editPageRoot "https://github.com/example/project/edit/main/docs"
```

The site name, logo, colors, and page metadata come from [Configuration](configuration.md).

