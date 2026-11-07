# Shared navigation and runtime assets — issue #207

## Scope and method

Measured on 7 November 2026, macOS arm64. Baseline: `4d3ec773ea7c8c5e73d3d3d5d6b12a06374c8565`; comparison: that revision plus the shared-assets changes in this working tree. The pre-existing Dockerfile edit was not used or changed. The installed SDK selected by `dotnet` is `11.0.100-preview.7.26381.103`; the projects still target `net10.0`, and portable execution uses .NET 10.0.14. This is a current-checkout comparison, not a reproduction of the November 2 acceptance environment.

The large generated site has 503 Markdown pages, repeated diagrams and API references, and additional specification and alias HTML files. Input fingerprint: `05cd41aa08615e45ec554ef363219bd24891cfee6aa0cca100c9998749b15ce4`. Before and after use the same inputs. Sizes are raw file bytes, not compressed transfer sizes. Measurements ran sequentially, without concurrent builds/tests/benchmarks. Fresh processes retained warm filesystem caches.

## Output size

| Complete render | Before | After |
| --- | ---: | ---: |
| All generated files, bytes | 208,244,161 | 13,877,594 |
| HTML files, including aliases/specifications/sitemap | 1,096 | 1,097 |

The reduction is **194,366,567 bytes (93.3%)**. The added HTML file is the complete static sitemap. The original issue's 366-page corpus was not available; these figures describe the reproducible 503-page fixture, not that original site.

The main savings come from writing the full navigation once and sharing common and feature-specific JavaScript/CSS. Navigation indentation was removed from generated layout only. User Markdown, code whitespace, SVG, images, and PDF fragments are not minified. Alias pages are retained. No new dependencies or non-.NET tooling were introduced.

## Portable comparison

Three render processes, one serve process, three observations per edit kind. These short edit samples are a comparison, not a p95 acceptance series.

| Measurement | Before | After |
| --- | ---: | ---: |
| Complete render, median | 3,649.6 ms | 2,855.7 ms |
| First usable HTTP preview, one observation | 1,017.2 ms | 1,031.0 ms |
| Complete-site startup, one observation | 3,449.7 ms | 3,114.9 ms |
| Text edit available over HTTP, median | 48.5 ms | 43.6 ms |
| Diagram edit available over HTTP, median | 50.7 ms | 47.4 ms |
| API edit available over HTTP, median | 60.1 ms | 59.4 ms |
| Render process peak RSS, median | 149.9 MiB | 146.8 MiB |

## Native acceptance series

Native AOT build of the comparison (`osx-arm64`), Chrome 156.0.8078.80, same 503-page input fingerprint. Three render processes; three serve processes with 100 rounds each of content, diagram and API edits, i.e. 300 verified observations per edit kind. Every edit was checked over HTTP, in the search index where applicable, and in the browser. An earlier run of this series lost the third serve process's result because the measuring tool treated the event stream ending at shutdown as a failure; the tool now accepts that ending, and the series was repeated in full. Archive: `2026-11-07-shared-assets-native.json.gz`.

| Measurement | Median (range) | p95 | Target |
| --- | ---: | ---: | ---: |
| Complete render | 1,122.8 ms (1,008.6–1,133.3) | | |
| Render process peak RSS | 95.4 MiB | | |
| First usable HTTP preview | 399.6 ms (357.8–403.3) | | |
| Complete-site startup | 1,159.1 ms | | |
| Text edit over HTTP | 41.8 ms | 44.9 ms | ≤ 100 ms |
| Text edit in the browser | 67.9 ms | 84.9 ms | ≤ 150 ms |
| Diagram edit over HTTP | 40.7 ms | 46.0 ms | |
| Diagram edit in the browser | 67.8 ms | 85.2 ms | |
| API edit over HTTP | 42.4 ms | 49.5 ms | |
| API edit in the browser | 84.5 ms | 101.8 ms | |

The functional browser check (`2026-11-07-shared-assets-browser-check.json.gz`) passed: consent and revocation, search, theme, TOC, code copy, shared navigation state, keyboard focus in the sidebar and on the footer sitemap link, and, with JavaScript disabled, the static navigation fallback and a complete, fully visible sitemap. The PDF check (`2026-11-07-shared-assets-pdf.json.gz`) passed for pending, failed and recovered downloads; the recovered PDF contains only the latest generation and was ready 490 ms after the fixing edit.

## Repeat after review fixes

The review fixes moved generated files into the reserved `_mdu/` folder, added content versions to every generated script and stylesheet, assigned top-section membership per item, and changed when `serve` replaces the navigation. Both series were repeated on the final state of the pull request with the same input fingerprint, sequentially and without concurrent work. Archives: `2026-11-07-shared-assets-final.json.gz` (portable) and `2026-11-07-shared-assets-final-native.json.gz` (native).

| Measurement | Before review fixes | Final |
| --- | ---: | ---: |
| All generated files, bytes | 13,877,594 | 14,031,135 |
| Reduction against the baseline (208,244,161 bytes) | 93.3% | 93.3% |
| HTML files | 1,097 | 1,097 |
| Portable complete render, median | 2,855.7 ms | 2,969.0 ms |
| Native complete render, median | 1,122.8 ms | 1,088.8 ms |
| Native first usable HTTP preview, median | 399.6 ms | 409.2 ms |
| Text edit over HTTP / in the browser, p95 | 44.9 / 84.9 ms | 44.8 / 84.1 ms |
| Diagram edit over HTTP / in the browser, p95 | 46.0 / 85.2 ms | 45.8 / 87.0 ms |
| API edit over HTTP / in the browser, p95 | 49.5 / 101.8 ms | 49.5 / 100.8 ms |

The output grows by 153,541 bytes (about 300 bytes per page) for the version queries and the per-item top-section owners. All 900 native edits were verified. The latency differences are within the run-to-run variation of these series; every p95 stays well inside the targets of ≤ 100 ms over HTTP and ≤ 150 ms in the browser.

## Implementation and correctness

- `SiteAssets` publishes runtime files once per generation, using atomic file replacement. Optional assets are emitted and referenced only when needed; Hero effects remain selective.
- `SiteNavigation` generates one shared navigation script and a fully expanded static sitemap. The browser inserts C#-generated markup, resolves relative URLs and applies current/ancestor/top-section state before the existing controls initialize. It does not fetch fragments or require a server endpoint.
- Page state uses URLs instead of tree indices, avoiding accidental selection of a different item during structural preview rebuilds.
- Ordinary content edits leave shared runtime/navigation files untouched. Title/structure changes refresh the shared tree and sitemap.
- Breadcrumbs, pagination and a footer sitemap link remain static. The footer link is accessible even when a narrow-screen sidebar cannot open without JavaScript.
- PDF rendering remains on its existing Markdown/fragment path, independent of website runtime files.

Automated coverage includes a raw size budget of 2 MB for a 102-page fixture, local asset/link resolution, complete script-free sitemap, preserved code whitespace, selective Hero assets, theme refresh, unchanged assets during ordinary serve edits, navigation title refresh, and a real-browser comparison of shared versus static navigation with nested/escaped URLs, current state and expanded ancestors. The normal PDF suite covers the unchanged export path.

## Reproduction

Generate inputs before editing documentation, or reuse the archived input fingerprint. The portable comparison uses:

```sh
dotnet build Mdu.sln -c Release
dotnet run --project Tools/Performance -c Release --no-build -- --mode generate --work /tmp/mdu-shared-assets
dotnet run --project Tools/Performance -c Release --no-build -- --mode baseline --work /tmp/mdu-shared-assets --workloads large --samples 3 --servers 1 --rounds 3 --edit-kinds content diagram spec
dotnet run --project Tools/Performance -c Release --no-build -- --mode summary --work /tmp/mdu-shared-assets
```

The native build uses `dotnet publish Source/Mdu/Mdu.csproj -c Release -r osx-arm64 --self-contained true -p:PublishAot=true --output /tmp/mdu-shared-assets-cli`. Use its `mdu` executable with `--cli`, and use `--mode browser --samples 3 --servers 3 --rounds 100 --edit-kinds content diagram spec --workloads large` for 300 observations per edit kind. Functional checks use `--mode browser-check --workloads small --samples 0 --servers 1 --rounds 0`; PDF preview/recovery uses `--mode pdf-check`.
