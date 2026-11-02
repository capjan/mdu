# Render and serve performance investigation (#188)

This directory contains a Python 3 standard-library harness, disposable C# diagnostic instrumentation, and the [measured baseline](results/2026-11-02.md). It does not change the shipped CLI or introduce a benchmark framework or production dependency.

## Reproduce

Requirements: macOS or Linux, Python 3.9+, the repository's .NET 10 SDK and restored packages, Git, and an available IPv4 loopback port. The recorded run used macOS arm64. Windows is not supported by the `wait4` resource collection in this harness.

From the repository root, use a dedicated disposable work directory outside the repository. Generation replaces its `corpora` directory; diagnostic builds replace only `diagnostic-source` or `memory-source`.

```sh
dotnet build Mdu.sln --configuration Release
python3 Tools/Performance/baseline.py --work /tmp/mdu-perf-188 --mode generate
python3 Tools/Performance/baseline.py --work /tmp/mdu-perf-188 --mode baseline
python3 Tools/Performance/baseline.py --work /tmp/mdu-perf-188 --mode memory --samples 3
python3 Tools/Performance/baseline.py --work /tmp/mdu-perf-188 --mode diagnostic --samples 3
python3 Tools/Performance/baseline.py --work /tmp/mdu-perf-188 --mode complexity
python3 Tools/Performance/baseline.py --work /tmp/mdu-perf-188 --mode startup
python3 Tools/Performance/baseline.py --work /tmp/mdu-perf-188 --mode compare
python3 Tools/Performance/summarize.py /tmp/mdu-perf-188
```

Run these sequentially without other builds/tests/benchmarks. The default baseline has seven fresh render processes and three serve processes per workload, with three edits of each of four kinds per server (nine observations per kind). `--samples`, `--servers`, `--rounds`, and `--workloads small docs large` can shorten exploratory runs; record changed arguments. `--repo` selects an explicit source checkout when running the harness from elsewhere.

`generate` records page counts, input bytes, and content hashes in `corpora.json`. Every measurement JSON records the source commit, invocation, .NET environment, Python version and runtime settings. The source commit identifies the production code under investigation; also retain this directory's harness revision for exact reproduction. Logs and individual profile snapshots are under `logs/`. Output always goes to `output/<workload>` in the work directory. Each render and initial serve starts with that directory absent; rebuilds overwrite the same directory in place.

To retain the raw observations and profiles in one small artifact:

```sh
python3 Tools/Performance/summarize.py /tmp/mdu-perf-188 \
  --archive Tools/Performance/results/2026-11-02.json.gz
```

The gzip file contains JSON objects keyed by source filename and a `profiles` object containing JSONL snapshots as arrays. Use Python's `gzip`/`json` modules to inspect it; no previous chat's temporary files are required.

## Inputs

All fixtures come from this checkout or deterministic strings in `baseline.py`:

| Name | Input |
| --- | --- |
| `small` | Four real `Docs/Architecture` pages plus two exercised feature pages. |
| `docs` | All 51 `Docs` Markdown pages plus the same two feature pages. All `mdu:pdf` strings become `https://example.invalid/pdf-disabled`. PDF generation is outside #188; detection still runs. |
| `large` | 503 pages: a root, ten section roots, 490 template pages, and two feature pages. The templates repeat prose, 12-node PlantUML diagrams, eight-operation OpenAPI references, 80-line C# blocks, and icon/table content. There are 100 repeated diagram pages, 100 API pages, 100 code pages, 90 icon/table pages and 100 prose pages. |
| `diagram-6/18/36` | One PlantUML page: three containers, a chain, and skip links across containers. Respectively 6/18/36 components and 6/22/46 links. |

The two feature pages add an eight-operation local OpenAPI file, a 12-node PlantUML diagram, highlighted C#, icon shortcodes, links, an explicit heading anchor and a YouTube consent placeholder. They make the same correctness and dependency assertions possible on every workload. The larger corpus is **synthetic repetition with distinct URLs**, not evidence about 503 diverse real-world documents. Corpora live outside Git: repository import/network and full Git history traversal costs are not exercised. Architecture links to documentation outside the small extracted subset are not a whole-site link audit.

## Measurement boundaries

Normal runs execute `dotnet <Release mdu.dll> render <source> <output>` or `serve ... --port <ephemeral> --no-browser`, with inherited/default runtime settings. No `dotnet run`, compilation, packaging or instrumentation is inside their timing. This is framework-dependent JIT execution, not a Native AOT/publish comparison.

One unreported fresh process per workload warms file/image caches before samples. Every measured render is another fresh process, with cold process-local JIT/static initialization but **no claim of cold filesystem caches**. Filesystem caches are not purged. All work uses fixed source/output paths; output removal, source enumeration by the observer, and validation are outside render timing. Render wall time starts before process creation and ends after process exit and log-reader completion. `os.wait4` supplies CPU use and peak process RSS; Darwin reports bytes, Linux KiB, both converted to MiB. The log pipe is drained to a file on a background observer thread.

Serve startup ends after the first successful HTTP response containing the expected content. Edits begin immediately before a direct on-disk write. The observer polls the selected page over `127.0.0.1` every 10 ms until it contains a unique marker. This is HTTP availability, not browser display/reload latency; request duration and polling resolution are included. Completion is a separately timestamped live-reload SSE event, after HTML, specification pages, search, reconciliation and any required preflight work. The 75 ms watcher debounce belongs to measured latency. Early availability can precede site completion substantially; polling can also observe content slightly after completion.

Each successful SSE event is checked for its intended page paths or `reloadAll`, followed by another HTTP marker check. HTML modification times establish the rewrite scope, including redirect aliases and generated specification pages. Content/OpenAPI changes must reach the search index. Each independent save has a 100 ms quiet interval outside timing. Saves use `write_text`, not an editor's atomic-save strategy. The four mutations are a body paragraph, `site_name` in `mdu.yml`, diagram edge labels, and an operation summary in the referenced `api.yaml`. Restoring originals happens only after stopping the server. Source and output trees remain separate.

Reported spreads are observed minimum–maximum, with medians and sample counts. These small samples do not support p95/p99 or significance claims about small differences. There is no CPU affinity, filesystem-cache purge or controlled power/thermal state. Rerun noisy comparisons on the same machine; do not interpret a single extreme as a regression.

## Diagnostic profiles and memory

`instrument.py` copies production sources, omitting build products and test projects, into the disposable directory. Guarded replacements insert scopes at actual method boundaries, with nested call paths. Moving boundaries fail assertions instead of silently misprofiling. A separate `Probe` project uses the same Markdig version as the profiled source. Its reference and all inserted code exist only in the generated tree. Diagnostic package auditing is disabled at build time (`-p:NuGetAudit=false`); dependency versions and runtime settings are preserved.

`diagnostic` records inclusive/exclusive elapsed time, call counts and inclusive allocation estimates for discovery/metadata, reads, Markdown parsing, asset analysis/copying, OpenAPI expansion/loading/parsing, PDF-link preflight, media discovery, navigation, diagrams/layout, highlighting, HTML composition/output and search generation/serialization. Inclusive parents contain their children; never sum inclusive rows. Exclusive allocations are deliberately not calculated across mixed process/thread counters. The residual in `page` includes stream flushing, atomic moves, redirects and specification orchestration. `html.write` covers `WriteAsync`, not all filesystem work. Stage timings include JIT and naturally occurring GC.

Allocation snapshots use `GC.GetTotalAllocatedBytes(true)`. Scopes crossing async work or enclosing whole commands/pages use process totals; synchronous leaf scopes use `GC.GetAllocatedBytesForCurrentThread` to avoid querying all threads thousands of times. Markdig's public and lower-level parser APIs retain their original call paths through wrappers. Leaf attribution is approximate where library internals use worker threads; process totals remain the authoritative allocation volume. Serve process totals include the observer's HTTP requests, timer and snapshot writing. Stage scopes and snapshot serialization add overhead: use normal results for latency, and report the measured diagnostic overhead separately.

`memory` builds a lighter copy with just command/edit scopes and startup/exit snapshots. It records three fresh render processes and one long-lived server per workload. That server handles 60 body edits and 15 further edits cycling configuration, diagram and API changes. At startup, after every ten changes and at the end, the observer requests two full blocking collections with finalizer draining **while the server is idle, outside all timed changes**. Post-collection `GC.GetTotalMemory(false)` estimates retained managed bytes. `GC.GetGCMemoryInfo` adds heap/fragmentation information and `Environment.WorkingSet` supplies an idle RSS observation. These forced-collection runs assess stabilization; they are not natural-GC latency baselines or proofs of absence of leaks. No forced collection occurs in normal or detailed-profile runs.

`compare` freshly renders every corpus with normal and detailed diagnostic binaries into the same fixed output path, then checks SHA-256 equality for **every output file**. It also checks anchors, navigation links, SVG content, highlighting, OpenAPI, search entries and consent markup. `validate` runs after every measured render/server series; it asserts no eager iframe and no accidental PDF generation. The report records additional test-suite and browser checks.

`startup` measures `--help` in seven fresh processes after one discarded run. It is a command-startup reference, not a decomposition of render's JIT and cold feature initialization. `complexity` measures full processes on the fixed three-container topology; page count is constant while graph size changes. It is not a bound on arbitrary graphs or all diagram types.
