# mdu check-site

Run `mdu check-site <directory>` after rendering and adding any generated content, before publishing the site.

```bash
mdu render
mdu check-site ./site
```

MDU checks local HTML links, asset references in `href`, `src`, and `poster`, and static anchors in HTML files. The first HTML `<base href>` controls link resolution. Root-relative URLs on the site are resolved against the supplied directory. A directory URL resolves to its `index.html`. Missing targets, paths above the site root, and references through symbolic links produce an error and a nonzero exit code. The check runs offline.

If a page generates anchors in JavaScript, pass its site-relative HTML file path as `--dynamic-fragments`. Repeat the option for additional pages. MDU still checks that those pages exist and checks their local file references.

```bash
mdu check-site ./site --dynamic-fragments reference/rest/index.html
```
