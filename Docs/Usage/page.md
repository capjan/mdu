---
navigation.order: 6
---

# page

`mdu page` works on the documentation folder as a whole: `page move` moves or renames a page or folder and updates every link to it, and `page links` shows which pages link to a file.

Both commands work in the [default documentation folder](README.md#paths-and-file-search); choose another one with `--root`. Links in pages outside that folder are not updated.

## Move pages and folders

```bash
mdu page move <SOURCE> <TARGET> [--dry-run] [--root <PATH>]
```

The alias `mdu page mv` runs the same command. `SOURCE` is a Markdown page or a folder. `TARGET` is the new path, or an existing folder to move into; a trailing `/` also moves into a folder and creates it. When a page target has no `.md` extension, MDU adds the extension of the source.

MDU then updates every relative link in the documentation folder:

- Links to the moved page, including links with an anchor or a query such as `Setup.md#install`.
- Links inside the moved page to other pages, images, and files, so they still work from the new place.
- Reference definitions such as `[setup]: ./Setup.md`.
- For a moved folder, links to every file inside it, including images.

A link that still points to the right file is left as it is: pages of a moved folder that link to each other keep their links, and a link to a folder such as `Guide/` stays a folder link. Links in code, links to web addresses, and absolute paths never change. A rename that only changes the case of a name, such as `Setup.md` to `setup.md`, updates the links too, because web servers usually tell case apart.

Paths in raw HTML, such as `<iframe src="assets/demo.html">`, are not updated. Run `mdu lint` after a move: it reports such paths when they no longer resolve (`MDU004`).

```text
$ mdu page move docs/Setup.md docs/Start/
Moved page: Setup.md -> Start/Setup.md
Updated 5 links in 3 pages:
  Guide/README.md (1)
  README.md (2)
  Start/Setup.md (2)
Run 'mdu lint' to check the new page structure.
```

Add `--dry-run` to see the same report without changing any file.

MDU refuses a move when:

- the target already exists, lies outside the documentation folder, or is inside the moved folder itself;
- the source or target path goes through a symbolic link;
- a symbolic link inside a moved folder would point to a different file afterward, for example a relative `../Shared` in a folder that moves one level deeper, or an absolute link to a file inside the moved folder.

A folder moves together with its symbolic links, and renaming a folder in place keeps their relative targets working. Pages reached through symbolic links are never rewritten.

> [!TIP]
> A new place can change the navigation: a folder needs a `README.md` page, and the [title of a section page](lint/Rule2.md) should match its folder name. Run `mdu lint` after a move.

Git recognizes moved pages as renames, so `git log --follow` still shows a page's history. Commit the moved files together with the pages whose links changed.

## Find links to a file

```bash
mdu page links <FILE> [--root <PATH>]
```

`page links` lists every link that points to a page, image, or other file, with the page and line of the link. A folder stands for its `README.md`. Use it before you delete or rename a file, or to find the pages that use an image.

```text
$ mdu page links docs/Setup.md
Guide/README.md:3: [Setup](../Setup.md)
README.md:3: [Setup](Setup.md#install)
README.md:5: [Reference][setup]
README.md:7: [setup]: ./Setup.md
4 links in 2 pages point to Setup.md.
```

## Options

| Option | Commands | Effect |
| --- | --- | --- |
| `--root <PATH>` | `move`, `links` | Documentation folder whose pages are updated or searched. Default: `./docs`. |
| `--dry-run` | `move` | Show what would change without changing any file. |
| `--hidden` | `move`, `links` | Include hidden files and folders. Default: on. |
