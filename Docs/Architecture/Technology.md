---
navigation.order: 4
---

# Technology decision

## Development and build framework

**Only .NET is permitted as the development and build framework.** MDU's own generators, benchmarks, diagnostic tools, icon preparation, tests, and build orchestration must run as .NET programs. They must not require Python, Node.js, npm, a JavaScript build framework, or another language runtime.

Git/GitHub, operating-system utilities and APIs, browsers, signing tools, and packaging infrastructure remain permitted supporting tools. Downloading source data from a package registry does not require running that registry's language runtime. Browser acceptance measurements must be driven by .NET; the browser itself remains an external executable.

This decision keeps development reproducible with the same framework used by the product. Existing historical measurement artifacts retain their original provenance; active reproduction commands must use the current .NET tools.

## Published website

**The published website consists of static HTML, CSS, and local JavaScript.** Dynamic behavior runs in the browser using scripts shipped with the site, either inline or as local files. Local SVG, images, fonts, JSON search data, and generated downloads are ordinary static assets. The site needs no application server, remote JavaScript library, CDN runtime, or external rendering service.

An explicit exception permits the existing YouTube/Vimeo players after the reader consents to that provider. Those embedded third-party players may execute their own scripts. MDU's own scripts remain local, and the initial page must not load a player before consent.

The local `mdu serve` development server may provide preview-only endpoints, reload notifications, and readiness information. These capabilities must not become dependencies of the published static site.

## Preview readiness

`serve` may publish the current page before other affected pages or expensive PDF downloads finish. Navigation and search must stay correct; pending or outdated downloads must be clearly identified. An older build must never replace a newer result. First usable preview, completion of all affected HTML/search output, and download readiness are separate milestones. `render` retains its complete-output success contract.

## Acceptance scope

Native .NET release execution is the accepted reference for startup and complete-render goals. Portable framework-dependent execution retains its own documented startup/build budget; both formats retain the editing, correctness and memory requirements. Results always identify the format. Platform-specific native .NET tool packages are being evaluated as a delivery option; this does not authorize silently dropping portable platform coverage or bypassing signing/notarization.

The [quality requirements](README.md#quality-requirements) are initially evaluated on the documented reference machine and workloads. Additional platforms must preserve correctness and record their own performance results; identical absolute timings are not assumed. If measurement shows that an agreed goal cannot be met safely, implementation pauses for review of the evidence and alternatives rather than silently relaxing the requirement.
