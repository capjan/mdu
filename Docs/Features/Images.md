# Images

MDU renders images with standard Markdown syntax. Local image files are copied into the site with their relative paths, and every image opens in a larger view when clicked.

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
- Click an image to open a larger view. Close it with its close button, by clicking the backdrop, or with ++esc++.
- An image that links to another page keeps its link; an image that links to its own file opens the larger view.
