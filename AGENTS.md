# Repository guide for agents

## Project purpose

MDU (Markdown Documentation Utility) is a .NET command-line tool distributed as the `mdu` .NET tool package with the `mdu` command. It helps documentation authors lint Markdown, inspect document structure, and render Markdown files to static HTML with breadcrumb navigation generated from the document structure. Source documents remain ordinary Markdown files.

## Architecture

- `Source/Mdu` contains the command-line entry point, command handlers, dependency injection setup, and orchestration services. `Program.cs` defines the CLI with `System.CommandLine`.
- `Source/Markdown.Document` reads Markdown files and exposes document, header, and link models. It uses Markdig for Markdown parsing.
- `Source/Markdown.Linter` contains validation rules and depends on `Source/SuperiorIO` for path and file helpers.
- `Source/Markdown.Renderer` renders documents to HTML with an embedded template, .NET HTML encoding, and Markdig. `Source/StaticCode.Highlighter` provides syntax highlighting for rendered code blocks. `Source/StaticCode.Diagrams` renders a Mermaid subset to static SVG: flowcharts with its own layered (Sugiyama) layout and sequence diagrams, PlantUML component diagrams in `plantuml` code blocks with containers laid out from the inside out, its own radial context diagrams in `context` code blocks, and its own isometric diagrams with phases and animated links in `isometric` code blocks (`Isometric/`; the phase buttons work with CSS alone, and `Res/Js/isometric-diagram.js` adds playback only on pages with such a diagram); the SVG carries only CSS classes, so `main.css` themes it for light and dark mode. `Source/StaticCode.Icons` embeds the icon sets behind the Material for MkDocs–compatible icon shortcodes (`:material-…:`, `:fontawesome-…:`, `:octicons-…:`, `:simple-…:`) and the colored Dashboard Icons (`:dashboard-…:`, sanitized with SVGO by `dashboard-icons.mjs`) as gzip-compressed, reduced Iconify JSON; regenerate them with `Source/StaticCode.Icons/update-icon-sets.sh` (needs npm) instead of editing `Res/*.json.gz`. Custom icon sets are SVG files from the site's `.icons` folder or the `icons:` folder in `mdu.yml` (`CustomIconSets.cs`, sanitized and prefixed per icon); `IconCatalog.UseCustomIcons` makes them process-wide, so tests that set them run in a non-parallel collection. A `hero` code block holds YAML options for an animated banner (`Source/Markdown.Renderer/Hero.cs`); its WebGL beach scene `Res/Js/hero-beach.js` and its styles `Res/Css/hero.css` are inlined only into pages with a hero, and a full-width hero is moved above the page layout through the `%%PAGE_HERO%%` template token. Emoji shortcodes use Markdig's GitHub emoji mapping and render as Unicode. Keyboard keys use the pymdownx Keys syntax of Material for MkDocs (`++ctrl+alt+del++`); the key map with its symbols is in `Source/Markdown.Renderer/MarkdigExtension/KeyboardKeys.cs`.
- `Source/Mdu/Service/MarkdownLinks.cs` finds and rewrites relative Markdown links; `mdu fix`, `mdu page move` (`PageMover`), and `mdu page links` share it.
- `Source/Core.Collections.NodeGraph` supplies the graph and visitor abstractions used to traverse Markdown documents.
- Projects ending in `.Test` contain the corresponding xUnit tests.
- `Docs` contains the user documentation. `.github/workflows` contains CI, Pages deployment, and release workflows.

## Repository goals and constraints

- Keep Markdown files portable and avoid tying user content to a hosting vendor.
- Preserve existing CLI command names, aliases, arguments, and behavior when changing the CLI.
- Keep changes focused on the requested behavior; avoid unrelated refactors and speculative abstractions.
- All projects target `net10.0`. Use standard `dotnet` commands; this repository no longer uses NUKE.
- `Source/Mdu/Mdu.csproj` defines the packaged tool and its `mdu` command name. The package version is supplied at release time from git-cliff; do not add a second hard-coded package version.
- `cliff.toml` defines Conventional Commit changelog groups and version bumps: `feat` bumps minor, `fix` and `perf` bump patch, breaking changes (including `!` or `BREAKING CHANGE` on otherwise ignored types) bump major, and maintenance-only changes do not bump.
- Start releases manually from the `main` branch with the `Release` workflow. It uses git-cliff for the GitHub release tag, notes, and the `dotnet pack` `Version` property, then publishes through NuGet Trusted Publishing. NuGet's trusted publisher is configured for `.github/workflows/release.yml` with no GitHub Environment; `NUGET_USER` is the NuGet profile name stored as a GitHub Actions secret.
- The release workflow validates the Apple signing configuration in `prepare-release`, before tests and platform builds: repository secrets `APPLE_DEVELOPER_ID_APPLICATION_CERTIFICATE_BASE64` (base64-encoded Developer ID Application `.p12`), `APPLE_DEVELOPER_ID_APPLICATION_CERTIFICATE_PASSWORD`, and `APPLE_NOTARIZATION_PRIVATE_KEY` (App Store Connect API key `.p8`); and repository variables `APPLE_NOTARIZATION_KEY_ID` and `APPLE_NOTARIZATION_ISSUER_ID`. The API key must have the Developer role or higher. Create the Developer ID Application certificate in the Apple Developer account and create the API key under App Store Connect **Users and Access → Integrations**.
- Docker Hub publishing requires repository secrets `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` (an access token with read and write permissions) and the public Docker Hub repository `capjan/mdu`. Releases publish the multi-architecture image there.

## Keyboard focus style

Every focusable element in the rendered site shows keyboard focus the same way, so a Tab through the top bar, the sidebar, the header, the page footer, and the content looks uniform. Pointer focus never shows a ring; only `:focus-visible` gets the style.

- Default: `outline: 2px solid var(--link); outline-offset: 3px;` on `:focus-visible`, in the shared focus rule at the top of the site styles in `Source/Markdown.Renderer/Res/Css/main.css`.
- Rounding: the outline follows the element's own `border-radius`. If the element has no radius, add a `border-radius` on `:focus-visible` that matches the element's shape: 6px for sidebar and top-bar links and the site brand, 2px for inline text links (`.markdown-content a`, `.page-meta a`). Pills keep their own radius, such as the search button and the page pagination links.
- Inset ring: where an ancestor clips overflow (any element with `overflow` other than `visible`, such as the top navigation bar or the on-this-page list), use `outline-offset: -2px` so the ring is not cut off on any side.
- Never remove an outline without a visible replacement, and never style `:focus` where `:focus-visible` is meant.
- Check a new or changed control by tabbing to it with the keyboard in a served page, not by reading the CSS alone.

## Working in this repository

- Restore and build with `dotnet restore Mdu.sln` and `dotnet build Mdu.sln`.
- Run the test suite with `dotnet test Mdu.sln` when tests are requested or needed to verify a change.
- Pack the CLI tool for a release with `dotnet pack Source/Mdu/Mdu.csproj --configuration Release -p:Version="$PACKAGE_VERSION"`, where `PACKAGE_VERSION` is the git-cliff version without the leading `v`.
- To update the globally installed `mdu` with a local build for manual testing, pack it with a unique local version and install from a temporary NuGet source:
  ```sh
  PACKAGE_VERSION=3.0.0-local.YYYYMMDD.N
  PACKAGE_DIR=/tmp/mdu-local-package
  mkdir -p "$PACKAGE_DIR"
  dotnet clean Source/Mdu/Mdu.csproj --configuration Release
  dotnet pack Source/Mdu/Mdu.csproj --configuration Release --no-restore -p:Version="$PACKAGE_VERSION" --output "$PACKAGE_DIR"
  dotnet tool update --global mdu --version "$PACKAGE_VERSION" --add-source "$PACKAGE_DIR"
  dotnet tool list --global
  ```
  Increment `N` for repeated local builds on the same date. For a first-time installation, replace `dotnet tool update` with `dotnet tool install`. Restart any running `mdu serve` process to use the updated build.
- Run `dotnet doctor fix` to install this repository's local Git hooks. Use `dotnet doctor diagnose all` to check the hook setup; commit messages must follow Conventional Commits.
- Open `mdu serve` previews at `http://127.0.0.1:<port>/`, not `localhost`. The preview server listens on IPv4 `127.0.0.1` only, while `localhost` can resolve to IPv6 `::1`, where another listener answers with a bare `Not Found (Not Found)`. Use the same host in URLs given to the user and in browser checks.
- Keep CLI output plain-text and dependency-light. `System.CommandLine` is the command parser; do not reintroduce Spectre.Console.
