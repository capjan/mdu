# Images

MDU renders images with standard Markdown syntax. Local image files are copied into the site with their relative paths, and images that can be shown larger open in a viewer when clicked unless you [turn this off](#turn-off-the-viewer).

## Syntax

Add alternative text in square brackets and the image URL in parentheses. An optional title appears when a visitor hovers over the image:

~~~markdown title="Markdown"
![Hand checking off items on a to-do list](https://images.unsplash.com/photo-1768055105012-4c53791b2877)
~~~

![Hand checking off items on a to-do list](https://images.unsplash.com/photo-1768055105012-4c53791b2877)
> Photo by [Jakub Żerdzicki](https://unsplash.com/@jakubzerdzicki) on [unsplash](https://unsplash.com/photos/hand-checking-off-items-on-a-to-do-list-fXlL5I0IvK0)

The alternative text describes the image to people who use assistive technology, so write it for every image.

## Local images

Keep local image files inside the documentation folder and link to them with a relative path from the Markdown file. For example, a page can refer to an image in its own `images` subfolder as `images/page.png`.

`mdu render` and `mdu serve` copy linked local images and keep their relative paths. Supported extensions are `.bmp`, `.png`, `.gif`, `.jpg`, `.jpeg`, `.webp`, `.avif`, and `.svg`. `mdu lint` reports a missing image as `MDU004`. External image URLs are not copied; they load from their original location.

## In the rendered site

- Images scale down to the page width and keep their proportions, so wide images fit small screens.
- Click an image with a zoom cursor to open it in a full-window viewer that shows the whole image. If such an image has alt text, it is also a tab stop, so ++enter++ opens it. Close it with its close button, by clicking beside the image, or with ++esc++.
- An image larger than the window can be zoomed and panned. Zoom by scrolling vertically with the mouse wheel or trackpad, with a pinch, the toolbar, or ++plus++ and ++minus++; click the image to zoom in on that spot and click again to fit it back. Drag, scroll sideways, or use the arrow keys to pan. ++0++ fits the image to the window and ++1++ shows its actual size.
- A pixel image already shown at its full size does not open the viewer and has no zoom cursor, because the viewer could not show it larger. Add `{.image-zoom-on}` to open it anyway. SVG images grow up to three times their size and can always be zoomed, because they stay sharp.
- An image that links to another page keeps its link; an image that links to its own file opens the larger view.
- Images that should not open the viewer, such as logos or layout graphics, can [turn it off](#turn-off-the-viewer).

## Turn off the viewer

The viewer is on by default. Choose the smallest scope that fits:

| Scope | Markdown |
| --- | --- |
| A whole page | `images.zoom: false` in the front matter |
| One image | `{.image-zoom-off}` after the image |
| One image on a page without the viewer | `{.image-zoom-on}` after the image |

Set `images.zoom` in a page's front matter to turn off the viewer for every image on the page:

```yaml title="Front matter"
---
images.zoom: false
---
```

The nested form `images: { zoom: false }` works as well. `mdu lint` reports any other value than `true` or `false` as [`MDU024`](../Usage/lint/Rules.md#mdu024-invalidimagezoom), and also `images: false` without `zoom`. Other `images` values, such as a list of pictures, are left alone.

Add a class to an image to decide for that image alone. The class wins over the page setting:

```markdown title="Markdown"
![Company logo](images/logo.svg){.image-zoom-off}

![Architecture overview](images/overview.png){.image-zoom-on}
```

- An element around an image can carry the class too, such as `<figure class="image-zoom-off">`. The closest class to the image counts.
- Renderers without attribute support, such as GitHub, show `{.image-zoom-off}` as text. Use an HTML `<img>` with the class if the page must stay readable there.
