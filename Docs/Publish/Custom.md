---
navigation.order: 3
---

# Custom

[Install MDU](../Setup/README.md), then render your documentation:

```bash
mdu render
```

By default, MDU reads the only `docs` folder and writes to `site`. To use different folders, pass the input and output paths after `render`, for example `mdu render ./content ./public/docs`. The generated site consists of static HTML, CSS, and JavaScript that you can host on any web server or CDN.

If the sources are on GitHub or GitLab, pass the edit URL of your docs folder with `--editPageRoot`. Every page then gets an edit link, and the header shows a [repository button](../Usage/render.md#repository-button):

```bash
mdu render --editPageRoot "https://github.com/example/project/edit/main/docs"
mdu render --editPageRoot "https://gitlab.com/example/project/-/edit/main/docs"
```

## Publishing independently generated sites

An API reference or interactive application can remain a complete static site built by its own generator. Give each build a separate output directory, then assemble the finished outputs into separate directories of the publication. For example:

```text
public/
├── docs/                  # complete MDU output
│   ├── index.html
│   └── Guide.html
└── reference/
    └── python/            # complete reference-generator output
        ├── index.html
        └── ...
```

Build the MDU part in its own directory:

```bash
mdu lint ./docs
mdu render ./docs ./build/mdu
```

After both generators finish successfully, copy the complete MDU output to `public/docs/` and the complete reference output to `public/reference/python/`. Keep the assembled publication separate from the generators' working directories. Copying only the reference's entry HTML can omit styles, scripts or other pages; MDU's [linked-asset copying](../Usage/render.md#local-assets-and-raw-html) is not a replacement for the reference generator's complete output.

For a publication hosted at `https://docs.example.org/product/`, an absolute link in the Markdown can point to the reference:

```markdown
[Python reference](https://docs.example.org/product/reference/python/index.html)
```

Configure the reference generator's return link to `https://docs.example.org/product/docs/index.html`. Check return links from nested reference pages too. Include the hosting subpath (`/product/` here) in both destinations. Absolute HTTPS links remain unchanged in MDU; `mdu lint` does not fetch or verify them. They point to the deployed site even in local preview and do not provide offline navigation between local copies.

Relative links allow local navigation when the assembled layout matches, but a local target must exist when `mdu lint` checks it. A target supplied only after rendering still produces `MDU004`; MDU currently has no exception setting for later-generated sites. Do not treat an absolute link as proof that its destination exists, or disable ordinary link checks to hide a missing target.

Before publishing, check the assembled output for missing local files and anchors, including the reference site's own pages and dependencies. Then serve the assembled directory under the intended hosting subpath and verify documentation → reference → nested page → documentation, together with any scripts and styles those pages load. MDU's source linter does not replace this final publication check; it [does not validate fragments in HTML files](../Usage/lint/Rules.md#mdu022-localfragmentexists).
