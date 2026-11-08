# Hero Banner

<!-- pdf:hidden -->

```hero
title: Make it your own
text: Try an effect, change the colors, and build your own hero banner right on this page.
effect: connections
width: content
height: 420
playground: true
actions:
  - text: See the options
    link: "#options"
```

## Try it

This feature page is the playground. It starts in **Content width** so navigation between feature pages keeps the same layout. Choose an effect, **Full width** or **Content width**, the site theme, and speed directly in the compact controls. The width switch moves the same live banner between the header and its original place in the article. Expand **Text & layout** for the title, description, height, alignment, and quality. **Scene & colors** contains the accent color or, for **Beach**, lighting, waves, and water, shallows, and sand colors. **Use default colors** restores the theme-aware palette. All changes preview immediately.

The action bar stays accessible while you edit. **View preview** takes you to the actual banner; **Back to controls** returns to the last focused setting. **Hero YAML** is collapsed initially but updates live. **Copy YAML** exports your settings whether or not that section is open.

**Own shader (advanced)** previews an original animated hexagonal tunnel with depth and glowing edges. Expand its optional editor to experiment with the code. Changes preview automatically after a 300 ms typing pause, without an Apply button. If the code does not compile, the last valid animation remains visible with an explicit error; copying is disabled until the shader is valid.

The site-theme selector changes the whole page without saving that preference. Other controls affect only the first banner. Pause is preview-only; `speed: 0` exports a still banner. The browser's reduced-motion preference always wins. Reset returns this playground to **Content width**. Action links remain YAML options.

<!-- /pdf:hidden -->

## Overview

A hero banner opens a landing page with an animated background behind your title and calls to action. Choose a beach, a moving network, flowing contours, or animated cells. All effects use procedural WebGL, adapt to the light and dark theme, and run entirely from inline code without external images, fonts, or scripts.

## Syntax

Add a fenced code block with the language `hero` and describe the banner with YAML. Place it right below the page heading:

````markdown title="Markdown"
# Documentation

```hero
title: Plain Markdown, beautifully published
text: Lint, inspect, and render your Markdown files into a fast static documentation site.
actions:
  - text: Get started
    link: Setup/README.md
  - text: Browse features
    link: ./Features/README.md
```
````

The source stays an ordinary code block, so the file remains readable on GitHub and in any other Markdown viewer.

## Product page example

Combine a full-width hero with ordinary Markdown sections. The first hero action is the primary link. Each `page` switch defaults to `true` and works in nested or dotted form, such as `page.toc: false`.

````markdown title="README.md"
---
page:
  breadcrumbs: false
  pagination: false
  toc: false
---

# Typing Studio

```hero
title: Build typing confidence
text: Short lessons make it easy to practice every day.
effect: connections
width: full
actions:
  - text: Get Typing Studio
    link: Download.md
```

## Practice at your pace

Follow short lessons and see your progress.

^^^
![Typing Studio workspace](images/workspace-light.png#only-light){.image-frame}
![Typing Studio workspace](images/workspace-dark.png#only-dark){.image-frame}
^^^ Choose the practice settings that suit you.
````

## Effects

The beach remains the default. To choose another background, add just one line:

````markdown
```hero
title: My documentation
effect: connections
```
````

| Effect | Appearance |
|---|---|
| `beach` | The original coastal scene with waves, sky, and day, sunset, or night lighting. |
| `connections` | A gently moving field of points and fine connections. |
| `flow` | Flowing contour lines across a quiet background. |
| `voronoi` | Moving geometric cells with subtle shading. |
| `shader` | Your own inline GLSL fragment shader. |

The abstract effects use the site's background and text colors, with an optional `colors.accent`. Text, buttons, and the transparent page header follow the same theme. Beach banners retain white text and their existing lighting options.

### More than one banner

These banners have independent effects and settings. Changes to the first banner do not change them, except for the site theme.

```hero
title: Follow the flow
text: Slow contours with just a hint of color.
effect: flow
width: content
height: 320
speed: 0.6
colors:
  accent: "#6688aa"
```

```hero
title: Room for ideas
text: Moving cells on a quiet, theme-aware background.
effect: voronoi
width: content
height: 320
speed: 0.7
```

```hero
title: A familiar shore
text: The original beach, with the same options as before.
width: content
height: 360
```

## Options

| Option | Values | Default | Description |
|---|---|---|---|
| `title` | Text | | Large heading of the banner. |
| `text` | Text | | Short introduction below the title. |
| `effect` | `beach`, `connections`, `flow`, `voronoi`, `shader` | `beach` | Animated background. Existing banners need no changes. |
| `actions` | List | | Buttons with `text`, `link`, and an optional `style` of `primary` or `secondary`. The first button is primary unless you set a style. Links to `.md` files point to the rendered pages, and links to other sites open in a new tab. |
| `time` | `auto`, `day`, `sunset`, `night` | `auto` | Beach lighting. `auto` shows the day in the light theme and a moonlit night in the dark theme, and fades between them when the reader switches the theme. |
| `width` | `full`, `content` | `full` | `full` spans the whole window below the header, `content` places a rounded banner in the page text. |
| `align` | `left`, `center` | `left` | Position of the title, text, and buttons. |
| `height` | 240 to 1200 | `520` | Height in pixels. The banner never takes more than 85 % of the window height. |
| `waves` | 0 to 2.5 | `1` | Beach wave height, from a calm sea at `0` to heavy surf. |
| `speed` | 0 to 4 | `1` | Animation speed. `0` freezes the scene. |
| `quality` | `auto`, `low`, `high` | `auto` | Render resolution. `auto` lowers the resolution on devices that cannot keep up. |
| `colors` | `water`, `shallow`, `sand`, `accent` | | Beach colors replace deep water, shallow water, and sand. `accent` tints abstract effects and is available to custom shaders. Accepts hex colors such as `"#0a3d62"`. Quote them, because `#` starts a YAML comment. |
| `shader` | YAML multiline text | | GLSL code for `effect: shader`, up to 32768 characters. |
| `playground` | `true`, `false` | `false` | Add live controls, a shader editor, and copyable YAML below this banner. Ordinary banners have no editor. |

Example with the beach options:

````markdown title="Markdown"
```hero
title: Release 3.0
text: Faster rendering, new diagrams, and a fresh look.
time: sunset
width: full
align: center
height: 600
waves: 1.4
speed: 0.8
quality: auto
actions:
  - text: Read the changelog
    link: Changelog.md
  - text: Source code
    link: https://github.com/capjan/mdu
    style: secondary
colors:
  water: "#0a3d62"
  shallow: "#1fa2a0"
  sand: "#e8d2a6"
```
````

## Behavior

- Effects initialize when their banner becomes visible. The animation only runs while the banner is visible and the browser tab is active.
- Readers who prefer reduced motion see a still image of the scene.
- Without WebGL, the beach shows a static gradient and abstract effects show the site background. A status message explains why the effect could not start.
- A page shows at most one full-width banner; further `hero` blocks on the same page use the content width.
- Multiple banners have independent effects, colors, timing, and playground controls.
- Only Hero pages include the runtime; beach code, abstract-effect code, and the editor are included only when needed. A playground includes all effects so you can switch between them.
- The PDF export shows the title, text, and button labels as plain text, without shader code or editor controls.

## Your own shader

Write a single WebGL 1 / GLSL ES 1.00 fragment shader in the same block. MDU supplies the canvas, uniforms, animation loop, resizing, and theme updates:

Custom shaders can draw much more than a simple line: fractals, tunnels, procedural landscapes, and raymarched 3D shapes are possible with this single-pass interface. The tunnel in the playground is an original example with a fixed loop of 16 projected rings, rather than a heavy scene or a copied animation. More complex shaders cost more GPU time; there is no performance guarantee for arbitrary user code.

````markdown
```hero
title: My own effect
effect: shader
shader: |
  void mainImage(out vec4 color, in vec2 fragCoord) {
      vec2 uv = fragCoord / iResolution.xy;
      float wave = 0.5 + 0.12 * sin(uv.x * 10.0 + iTime * 0.4);
      float line = 1.0 - smoothstep(0.003, 0.012, abs(uv.y - wave));
      color = vec4(mix(iBackground, iAccent, line * 0.5), 1.0);
  }
```
````

Do not declare these uniforms again; they are already available:

| Uniform | Type | Meaning |
|---|---|---|
| `iResolution` | `vec3` | Canvas width and height in render pixels; `z` is `1`. |
| `iTime` | `float` | Animation time in seconds, scaled by `speed`. Starts at a nonzero time for an interesting still frame; pauses offscreen. |
| `iMouse` | `vec4` | Hover position in render pixels, measured from the lower left. Zero outside the banner or without mouse movement; `zw` are reserved and always zero. |
| `iDark` | `float` | `1.0` in dark mode, `0.0` in light mode. |
| `iBackground` | `vec3` | Site background color, RGB values from 0 to 1. |
| `iForeground` | `vec3` | Site text color, RGB values from 0 to 1. |
| `iAccent` | `vec3` | `colors.accent`, or the site's link color. |

This is a small, single-pass interface, not full Shadertoy support. Textures, channels, audio, external shader files, custom JavaScript, and multipass rendering are not supported. Use `mainImage`, not `main`, and omit `#version`. Compiler errors appear below the banner or in its playground; its title and links remain usable.

> [!WARNING]
> Only run shaders you trust. GLSL is not JavaScript and cannot access the DOM, but an expensive or unbounded shader can stall the GPU or browser. The editor is not a sandbox. Keep loops small and bounded, avoid rapid flashing, and use the provided theme colors so your text remains readable.

## Playground

Add `playground: true` to any banner to show live controls for its width, text, dimensions, alignment, effect, speed, quality, and colors, plus Pause/Resume and Reset buttons. Text/layout, scene/colors, and YAML are collapsed initially to keep the controls compact. **View preview** and **Back to controls** make it easy to check changes while editing longer settings. Only one banner may use full width at a time; switching another to full width reports the conflict rather than moving the existing banner. Selecting **Own shader (advanced)** offers the optional editor and tunnel example. Shader edits preview after a 300 ms typing pause; compilation failures are shown explicitly without replacing the last valid preview. **Copy YAML** exports the current valid settings as a fenced block, without the playground itself.

The theme selector previews the whole page's theme without saving it. Pause is preview-only; set `speed: 0` to export a still banner. The browser's reduced-motion preference always wins. Beach colors and lighting are adjustable directly; the accent picker applies to abstract effects and custom shaders. Width and actions remain configurable through YAML.

Our abstract effects are original implementations of general generative-art techniques, inspired by the quiet geometric motion in [Bleuje's work](https://bleuje.com/animationsite/2024_1/), not copies of his animations or source code.
