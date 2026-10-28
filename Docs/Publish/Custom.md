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
