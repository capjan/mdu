---
navigation.order: 4
---

# Performance benchmarks

Use this page to run a benchmark and compare results with MDU's targets. The commands test the code in your checkout; the recorded measurements below describe identified earlier reference runs.

## Quick start

You need the **.NET 10 SDK**, a Git checkout of MDU, and **macOS or Linux** for the current resource-measurement tool. Run these commands from the repository root, one after another. This quick run needs no browser and uses the **framework-dependent Release build**, the same execution format as the .NET tool.

Choose a disposable working folder outside the checkout: generation replaces its benchmark inputs and outputs.

**1. Build MDU and the benchmark tool.**

```sh
dotnet build Mdu.sln -c Release
```

**2. Generate the example sites.**

```sh
dotnet run --project Tools/Performance -c Release --no-build -- --mode generate --work /tmp/mdu-bench-quick
```

**3. Measure the 503-page site.** This collects three complete renders, one server start, and three changes each to text, a diagram, and an API reference.

```sh
dotnet run --project Tools/Performance -c Release --no-build -- --mode baseline --work /tmp/mdu-bench-quick --workloads large --samples 3 --servers 1 --rounds 3 --edit-kinds content diagram spec
```

**4. Show the results.**

```sh
dotnet run --project Tools/Performance -c Release --no-build -- --mode summary --work /tmp/mdu-bench-quick
```

Look for `render ms` (complete build), `startup ms` (first usable HTTP page), and `content HTTP ms` (saved text available over HTTP). Each line gives the median, minimum–maximum, and sample count. Raw observations are in `/tmp/mdu-bench-quick/baseline.json`; `logs/` contains process output.

This short run checks rendering and HTTP updates. Browser display timing, p95, and memory need the [optional measurement procedures](Details/Performance.md). p95 means 95% of observations are at or below that value; the tool only prints it with at least 300 observations.

## Targets and recorded results

Reference: **2 November 2026**, Apple M4, 16 GiB RAM, macOS arm64, .NET 10. The small site has 7 pages; the large site has 503 and repeats mixed content templates. These are recorded observations, not guarantees or an automatic measurement of the current checkout.

### Native release

The native release is a self-contained Native AOT executable built like the [portable binaries](../Setup/Portable-Installation.md). The Docker image is also Native AOT, but it is built for musl with size optimization and invariant globalization, so its timings can differ.

The full acceptance series used seven render processes, three server processes, and 300 observations per edit kind and site. The allocation/retention check was a separate instrumented run.

| Measurement | Target | Measured |
| --- | --- | --- |
| First usable preview, small site, median | ≤ 250 ms | 146.8 ms |
| First usable preview, large site, median | ≤ 1,000 ms | 403.2 ms |
| Complete render, large site, median | ≤ 2,000 ms | 1,312.8 ms |
| Text change available over HTTP, large site, median / p95 | ≤ 50 / 100 ms | 43.7 / 48.5 ms |
| Text change displayed in browser, large site, p95 | ≤ 150 ms | 89.6 ms |
| Diagram change displayed in browser, large site, p95 | ≤ 300 ms | 95.1 ms |
| API change displayed in browser, large site, p95 | ≤ 300 ms | 102.0 ms |
| Allocations per text edit, large site | ≤ 10 MiB | 4.3 MiB median; 6.9 MiB maximum observed |
| Retained managed memory over 600 edits, large site | No sustained growth after warm-up | 52.81 MiB at edit 300; 52.96 MiB at edit 600 |

All these targets were met in that reference run. Browser timing observes the expected page text across two animation frames, not physical display-panel presentation. The finite memory run showed stabilization; it is not proof of unlimited leak freedom.

### Framework-dependent Release build

Compare the quick-start timings with these separate startup/build budgets. Editing targets are shared with the native build.

| Measurement | Target | Measured |
| --- | --- | --- |
| First usable preview, small site, median | ≤ 750 ms | 495.5 ms |
| First usable preview, large site, median | ≤ 1,500 ms | 1,065.7 ms |
| Complete render, large site, median | ≤ 5,000 ms | 3,683.8 ms |
| Text change available over HTTP, large site, median / p95 | ≤ 50 / 100 ms | 39.4 / 45.4 ms |
| Text change displayed in browser, large site, p95 | ≤ 150 ms | 84.9 ms |

The [full acceptance report and raw data](https://github.com/capjan/mdu/blob/main/Tools/Performance/results/2026-11-02-acceptance.md) identify the exact source revisions, inputs, distributions, and framework-dependent edit results. A [later short review check](https://github.com/capjan/mdu/blob/main/Tools/Performance/results/2026-11-02-review2.md) measured a 1.43 s native large-site render and 379 ms first preview; it did not repeat the p95 series.

## Further measurements

Ordinary edit targets exclude shared configuration changes, PDF generation, and much larger diagrams or specifications. Those need their own workloads. Repeat comparisons on the same machine, execution format, and inputs, with other builds and benchmarks stopped.

For native builds, browser measurements, memory, frozen inputs, and additional commands, continue to [measurement procedures](Details/Performance.md). Implementation and profiling internals are optional; they are not needed for the quick start.
