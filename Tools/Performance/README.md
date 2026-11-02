# Native .NET performance measurements

All active benchmark generation, process measurement, diagnostics, browser control, reporting and archiving use C#/.NET 10. There is no Python, Node.js or npm dependency. Git and the operating system provide repository metadata and process resource APIs. The [historical baseline](results/2026-11-02.md) and its raw archive retain the provenance of the original investigation; their recorded Python version describes that past run, not a current requirement.

## Reproduce

Run from the repository root, sequentially without concurrent builds/tests/benchmarks:

```sh
dotnet build Mdu.sln -c Release
dotnet run --project Tools/Performance -c Release --no-build -- --mode generate --work /tmp/mdu-performance
dotnet run --project Tools/Performance -c Release --no-build -- --mode baseline --work /tmp/mdu-performance
dotnet run --project Tools/Performance -c Release --no-build -- --mode diagnostic --samples 3 --work /tmp/mdu-performance
dotnet run --project Tools/Performance -c Release --no-build -- --mode memory --samples 3 --work /tmp/mdu-performance
dotnet run --project Tools/Performance -c Release --no-build -- --mode complexity --work /tmp/mdu-performance
dotnet run --project Tools/Performance -c Release --no-build -- --mode startup --work /tmp/mdu-performance
dotnet run --project Tools/Performance -c Release --no-build -- --mode compare --work /tmp/mdu-performance
dotnet run --project Tools/Performance -c Release --no-build -- --mode summary --work /tmp/mdu-performance
dotnet run --project Tools/Performance -c Release --no-build -- --mode archive --work /tmp/mdu-performance --archive /tmp/mdu-performance.json.gz
```

Use a dedicated disposable work directory outside the repository. Generation replaces `corpora`; diagnostic builds replace only `diagnostic-source` or `memory-source`. Output uses fixed `output/<workload>` paths. Each render/initial serve removes previous output; edits overwrite in place. Results record source commit, environment, invocation and corpus manifest. Keep the harness revision with the production revision.

Defaults: seven fresh render processes, three server processes, three rounds per server of body/configuration/diagram/API edits (nine observations per kind). Change `--samples`, `--servers`, `--rounds`, `--workloads small docs large` or `--repo` for exploration and retain the invocation. Exact resource collection currently supports macOS and Linux.

## Browser observations

```sh
dotnet run --project Tools/Performance -c Release --no-build -- --mode browser --samples 0 --servers 3 --rounds 100 --work /tmp/mdu-performance
```

The runner launches its own disposable Chromium profile and controls CDP through .NET `ClientWebSocket`; no Node driver or JavaScript automation framework is used. The macOS default is Google Chrome in `/Applications`; otherwise it is `chromium`. Set `MDU_BENCHMARK_BROWSER` to another installed compatible executable if needed. Browser startup is excluded from CLI startup timing.

API tag groups are consistently expanded after page load so the changed operation summary is visible. Browser readiness requires the expected text to survive two animation frames. This is a DOM/frame observation, not physical display-panel presentation. It includes normal preview reload and is separate from HTTP availability and the reload event. Initial pages are checked for local scripts and absence of eager video iframes. These observations do not substitute for functional search/navigation/consent tests.

The example collects 300 observations per edit kind across three servers. p95 is printed only for at least 300 observations; smaller samples report median, observed min/max and count. Record hardware, browser version and foreground/background behavior for comparisons.

Run `--mode browser-check --samples 0 --servers 1 --rounds 0 --workloads small` for functional checks of exact search sections, navigation and anchors, keyboard focus, video consent, persisted consent, and revocation. It saves a browser screenshot under `logs/`. This functional check is independent of percentile acceptance.

## Inputs and validation

`small` copies the checkout's `Docs/Architecture`; `docs` copies all `Docs`. Both add two feature pages exercising links, a stable heading anchor, an eight-operation API file, a 12-component diagram, highlighted C#, icons and a consent-gated video link. In `docs`, `mdu:pdf` becomes `https://example.invalid/pdf-disabled`, excluding PDF generation while keeping preflight cost. Counts depend on source revision; the historical investigation used 6/53 pages including fixtures.

`large` has 503 pages: root, ten sections, 490 repeated template pages and two fixtures. Templates include 100 diagram pages, 100 API references, 100 80-line C# pages, 90 icon/table pages and 100 prose pages. This is synthetic repetition with distinct URLs. `diagram-6/18/36` varies the fixed three-container chain/skip-link graph at one page. Their historical input hashes are checked in `Tools/Development.Test`.

`corpora.json` records counts, bytes and SHA-256 fingerprints. Corpora are outside Git: history traversal and repository imports need additional workloads. The small extracted subset retains links outside that subset; explicit fixture links are checked rather than claiming a complete link audit.

Each render/server series validates anchors, navigation, SVG, highlighting, API paths, one exact search section, consent markup and no accidental PDF. Unique edit markers must reach HTTP; body/API markers must reach search before completion. SSE must include the affected page or `reloadAll`. HTML modification times record rewrite scope without assuming full rebuilds remain necessary after optimization. `compare` requires SHA-256 equality of every output file between normal and detailed diagnostic builds.

## Time and resource boundaries

Normal runs execute the Release `mdu.dll` in fresh processes. Compilation is outside timing. One discarded process per workload warms file/image caches; samples retain fresh process-local JIT/static state. Filesystem caches are not purged. Inherited/default settings are retained; this is framework-dependent JIT execution, not AOT.

A fresh .NET helper starts exactly one CLI process and reads OS `getrusage(RUSAGE_CHILDREN)` after it exits. This supplies lifetime peak RSS and CPU use without the unsupported macOS `Process.PeakWorkingSet64`. Darwin bytes and Linux KiB become MiB. Helper CLR startup/memory/shutdown are excluded; its monotonic CLI-start timestamp is forwarded to the observer. CLI wall time includes process creation through exit. Logs drain concurrently. Resource use of descendants waited by the CLI is included by the OS.

Serve startup ends at the first expected HTTP response; complete initial output is checked separately. Edits start just before the on-disk write. HTTP is polled every 10 ms; completion is a separately timestamped SSE event. Watcher debounce is included. Browser readiness is another interval. Saves are direct writes with a 100 ms quiet interval outside timing; atomic saves/bursts need explicit acceptance scenarios. Originals are restored after stopping the server.

## Diagnostics and memory

`Instrumentation` makes an isolated source copy and inserts guarded method scopes, failing when boundaries move. Its Probe project uses the source's Markdig version. References and instrumentation never enter the shipped CLI. Package auditing is disabled only for the disposable diagnostic build.

Profiles record nested paths, counts, inclusive/exclusive times and inclusive allocation estimates. Parents include children; do not sum inclusive rows. Async/command/page scopes use process counters and synchronous leaves use thread counters. Exclusive allocations are not subtracted across different domains. Process totals are authoritative; leaf attribution is approximate for threaded libraries. Report profile overhead separately from normal timings.

`memory` uses only command/edit scopes. Each server processes 60 body edits and 15 configuration/diagram/API edits. Startup, every tenth edit and the end have idle checkpoints with two full collections and finalizer draining. Post-GC `GC.GetTotalMemory(false)` estimates retention; heap/fragmentation, collection counts, cumulative allocations and working set are retained too. Forced collections never occur in normal runs. Serving allocations include observation requests and snapshots. These sequences assess stabilization, not multi-hour leak freedom.

Results are JSON/JSONL under `logs/`. `archive` writes gzip-compressed JSON containing results and profiles, compatible with the historical archive container. Do not overwrite historical source reports or data.

Inspect either a historical or new archive with the same .NET tool:

```sh
dotnet run --project Tools/Performance -c Release --no-build -- --mode inspect --archive /tmp/mdu-performance.json.gz
```
