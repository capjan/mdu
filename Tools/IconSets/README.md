# Native icon preparation

Run from the repository root:

```sh
dotnet run --project Tools/IconSets -c Release -- update
dotnet test Tools/Development.Test -c Release
```

This .NET tool reads pinned Iconify package versions as data from the registry, using `HttpClient`, `GZipStream` and `TarReader`. It never executes npm, Node.js or package scripts. Git retrieves the pinned Dashboard Icons SVG folder and license. The converter reuses MDU's XML SVG sanitizer, prefixes identifiers and CSS, preserves colored paints and dark-theme variants, and writes compressed reduced JSON to `Source/StaticCode.Icons/Res`.

Change versions and the Dashboard commit in `Program.cs` when updating. To validate a different destination without changing bundled resources, pass a repository-shaped output root after `update`. To convert an existing Dashboard checkout only:

```sh
dotnet run --project Tools/IconSets -c Release -- dashboard /path/to/checkout /tmp/dashboard-icons.json.gz revision
```

The original shipped Dashboard resources were optimized with SVGO before the .NET migration. Regeneration may produce different SVG/XML formatting and compression while preserving the icon contract. Unit tests cover theme selection, monochrome adaptation, references, scoped styles, and rejection/removal of unsafe embedded content.

To check that regenerated catalogs retain every previously shipped shortcode:

```sh
dotnet run --project Tools/IconSets -c Release -- verify Source/StaticCode.Icons/Res /path/to/generated/Res
```
