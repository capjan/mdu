# Native-reference acceptance — 2026-11-02

The user resolved the [execution-format checkpoint](2026-11-02-optimization.md): **native .NET release execution is the acceptance reference**, and portable execution has separate startup/build budgets. The editing and memory requirements remain shared. This report records the completed implementation and selected reference-workload checks, not a guarantee for arbitrary input or hardware.

Production source: **`52eaa3a`**, based on fetched `origin/main` `2ca8613`. Native percentile and memory runs used the clean `52eaa3a` checkout. Git/portable runs and output comparisons used `7ac2f23`, whose production source is identical; that commit fixes cancellation of test-browser output drains and preserves per-server receipts. Later reporting changes do not alter measured production code. The intermittent browser-cleanup hang was in the measurement helper after `mdu` had exited; the interrupted Git series was discarded and repeated successfully.

Environment: Apple M4 / 16 GiB, macOS 27.0.1 arm64; .NET SDK 10.0.403, runtime 10.0.14; Release, default runtime settings. Disposable headless Chrome 156.0.8078.74, driven directly by .NET/CDP. No browser engine or extra runtime is used to build MDU output.

Frozen corpora from `611b84a`: 7 / 54 / 503 pages. Fingerprints and generation recipe are unchanged from the checkpoint. Native and portable runs use the same inputs. The large corpus repeats content templates; it is not every possible 500-page site.

## Raw evidence

- [Native timings, allocation/retention profiles, PDF checks and output comparisons](2026-11-02-accepted-native.json.gz)
- [Portable timings](2026-11-02-accepted-portable.json.gz)
- [Git-repository measurements and fixture identity](2026-11-02-accepted-git.json.gz)

Historical baselines and checkpoint archives remain unchanged. Measurements run sequentially, without concurrent builds or benchmark series. Each corpus uses one discarded render, seven fresh measured render processes, and three fresh server/browser processes. Filesystem caches are warm; process-local initialization is fresh. Outputs are removed before each initial render/server.

## Native acceptance results

Medians with observed minimum–maximum. Startup ends at expected HTML over HTTP, with correct navigation/search; complete initial output is a separate boundary. Render and startup units are milliseconds; RSS is MiB.

| Corpus | Complete render, n=7 | First usable HTTP, n=3 | Complete initial HTML, n=3 | Render peak RSS, n=7 |
| --- | ---: | ---: | ---: | ---: |
| small | 99.6 (93.6–101.8) | 146.8 (133.4–153.9) | 157.1 (151.4–165.6) | 68.8 (68.8–69.9) |
| docs | 259.3 (254.3–266.1) | 205.6 (201.5–207.8) | 353.6 (343.7–432.2) | 169.0 (168.9–169.0) |
| large | 1312.8 (1292.9–1347.6) | 403.2 (355.3–416.8) | 1500.3 (1420.9–1528.2) | 95.4 (95.0–95.7) |

The native startup goals (250 ms small, 1 s large) and complete large-render goal (2 s) pass for these observed medians.

Separately labelled component-complexity renders (6 / 18 / 36 components, n=7 fresh processes each) took 49.1 / 54.5 / 60.4 ms medians. These fixed graphs are additional observations, not a universal diagram-complexity budget; existing upstream size limits remain in force.

Each server performs 100 rounds of text, diagram and API edits: **300 observations per kind per corpus across three processes**, 2,700 edits total. This series excludes intervening shared configuration rebuilds; shared changes have separate checkpoint and mixed-retention observations. Every edit checks the unique new output marker and affected reload scope; text/API edits also check search freshness.

| Corpus | Text HTTP median / p95 | Text browser p95 | Diagram browser p95 | API browser p95 |
| --- | ---: | ---: | ---: | ---: |
| small | 45.1 / 48.7 | 99.4 | 99.4 | 101.7 |
| docs | 43.8 / 48.3 | 87.5 | 84.5 | 98.4 |
| large | 43.7 / 48.5 | 89.6 | 95.1 | 102.0 |

All units are ms. Text HTTP median/p95 goals of 50/100 ms, text browser p95 of 150 ms, and diagram/API browser p95 of 300 ms pass. Browser readiness means the expected DOM text survives two animation frames, including the real live reload. It is not physical panel presentation.

First text edits are retained separately, n=3 each: HTTP medians 34.6 / 37.9 / 45.4 ms and browser medians 66.6 / 65.5 / 83.8 ms for small/docs/large. The large first-browser spread is 82.8–89.7 ms. These observations are included in the full distributions, not discarded as warm-up.

Percentiles are not maximum guarantees. One small-site browser observation was 334.5 ms despite new HTTP content at 32.5 ms and reload notification at 29.9 ms; it was edit 52, not the first edit. That locates the delay after server publication, without claiming a traced root cause. The raw observation remains included. Large-site browser maxima were 100.6 ms text, 100.3 ms diagram and 199.6 ms API.

## Git repository case

The large corpus was also initialized as a Git repository with one commit. This is additional end-to-end coverage of Git metadata, not a cold-history benchmark for a repository with thousands of commits.

Three native server/browser processes applied 100 body edits each. Text HTTP median **43.5 ms**, p95 **48.3 ms**; browser p95 **84.7 ms**, observed 49.6–101.0 ms. First usable HTTP median was 455.0 ms (445.3–480.1). The archive records the fixture commit/date.

Per-preview commit-date reuse removes repeated Git subprocess queries from unchanged-history edits. Correctness tests cover uncommitted edits, new commits, packed refs, detached HEAD, linked worktrees and unshallowing. History/path/configuration overrides, graft/replacement history, reftable storage and oversized metadata fall back to the original Git query. Those fallback cases are not claimed to have the same latency.

## Portable execution

The separate portable startup/build budgets are 750 ms small first-ready, 1.5 s large first-ready, and 5 s complete large render. The same 300-observation editing checks were run for each kind and corpus.

| Corpus | Complete render median, n=7 | First usable HTTP median, n=3 | Text HTTP median / p95 | Text / diagram / API browser p95 |
| --- | ---: | ---: | ---: | ---: |
| small | 445.9 | 495.5 | 36.4 / 42.7 | 84.9 / 85.2 / 99.9 |
| docs | 972.5 | 680.9 | 36.7 / 45.7 | 83.8 / 83.1 / 98.8 |
| large | 3683.8 | 1065.7 | 39.4 / 45.4 | 84.9 / 85.7 / 100.2 |

All units ms. The portable budgets and shared ordinary-edit targets pass. Large render spread was 3474.6–3848.5 ms; first-ready spread 1046.4–1102.7 ms. Portable and native warm-edit differences are not evidence that their startup paths are equivalent. Raw data includes isolated maxima beyond the p95 thresholds; for example portable large text browser reached 182.1 ms.

## Native allocation and retention

A separate lightweight **Native AOT instrumented** build processes 300 text edits followed by 100 configuration/diagram/API rounds: **600 edits**, with idle collection checkpoints every tenth edit and at the boundaries. Probe JSON uses .NET source generation. Instrumentation is absent from shipped builds, and forced collections never occur in normal latency runs.

- Text-edit scope: **4.3 MiB median, 4.3–6.9 MiB observed, n=300**, including the first edit. The 10 MiB allocation target passes for this workload.
- Diagram scope: 7.3 MiB median (7.3–9.9), n=100; API scope: 15.6 MiB (15.6–18.6), n=100. The text allocation budget is not applied to every feature edit.
- Complete render allocated about 3,000 MiB in the instrumented process, n=1. This is cumulative allocation, not peak RSS or retained memory. Full-site navigation/output still has material cost.
- Configuration command scopes exclude subsequent background page work; their 274.1 MiB median is **not total configuration-rebuild allocation**.

| Completed edits | Post-GC managed MiB |
| ---: | ---: |
| 0 | 51.64 |
| 10 | 53.04 |
| 100 | 53.35 |
| 200 | 52.49 |
| 300 | 52.81 |
| 400 | 52.96 |
| 500 | 52.85 |
| 600 | 52.96 |

This finite run shows stabilization and no sustained retained-state increase in the mixed phase; it is not proof of multi-hour leak freedom. Frozen/native runtime state contributes to managed memory differently from the GC heap/fragmentation counters, so those values must not be summed. Allocation scopes include observation traffic and have instrumentation overhead.

## PDF responsiveness and readiness

A separate generated 181-page workload uses code and pagination content. Native first usable HTTP was **132.4 ms**. After recovery from invalid input, updated page content survived two browser frames after **83.1 ms** while the new PDF completed after **318.6 ms**. This is one separately labelled observation, not a PDF p95 guarantee.

The .NET/browser check verified visible pending, failed and ready states; HTTP 503 while generated downloads are unavailable; recovery; and a PDF containing the latest marker without the obsolete marker. Pending/ready screenshots were inspected. Integration tests additionally verify that an existing stale PDF is not served during replacement, rapid newer saves discard unfinished work, and strict hidden-content failures recover. Stepped and synchronous PDF rendering produce identical bytes in the regression fixture.

PDF sections run serially between HTML work and newer changes. Large single sections and final serialization remain indivisible; their costs are outside the ordinary no-PDF edit budgets. `mdu render` still completes all required outputs before succeeding.

## Correctness and tooling

- **1,488 Release tests passed, 6 platform skips, zero failures.**
- Normal portable and native outputs were compared against the pre-optimization diagnostic executable on the frozen corpora. Every output file is byte-identical: **17 / 110 / 1,099 files**. Preview-only readiness markup is injected by the development server and does not change published static files.
- Existing browser checks cover search sections, navigation/anchors, keyboard focus, consent acceptance, persistence and revocation. Tests also cover atomic saves, bursts, invalid intermediate input, pending pages/aliases, source dependencies and PDF failure handling.
- All active workload generation, timing, resource collection, instrumentation, browser control, reporting, archiving and icon preparation use .NET. Git/OS/browser/signing infrastructure remains within the approved exceptions. Historical provenance is preserved.
- [Native .NET tool packages were evaluated locally](../NativePackages.md), including native installation/execution and a separately tested portable fallback. This is a completed packaging study, **not a release migration**. Publication, supported-runner installation and signed-package validation remain prerequisites for adopting it; no global installation or external release was changed.

## Reproduction and ongoing regression checks

Use the checkpoint's frozen-input recipe (`git archive 611b84a Docs`, native .NET generation), build production `52eaa3a` or the identical production source at `7ac2f23`, and publish Native AOT for the host RID. Keep native and portable work directories separate. Main invocations:

```sh
dotnet Tools/Performance/bin/Release/net10.0/Performance.dll --mode browser --work /tmp/mdu-native --cli /tmp/mdu-release/mdu --samples 7 --servers 3 --rounds 100 --edit-kinds content diagram spec
dotnet Tools/Performance/bin/Release/net10.0/Performance.dll --mode memory-native --work /tmp/mdu-native --samples 1 --workloads large --body-edits 300 --mixed-rounds 100
dotnet Tools/Performance/bin/Release/net10.0/Performance.dll --mode pdf-check --work /tmp/mdu-native --cli /tmp/mdu-release/mdu
dotnet Tools/Performance/bin/Release/net10.0/Performance.dll --mode browser --work /tmp/mdu-portable --samples 7 --servers 3 --rounds 100 --edit-kinds content diagram spec
```

The Git variant initializes the frozen large corpus as a repository, commits its files, and runs the native browser series with `--workloads large --samples 0 --servers 3 --rounds 100 --edit-kinds content`. Record its commit and metadata; do not confuse it with the non-Git inputs.

Repeat the appropriate reference series for changes to rendering, dependency tracking, preview scheduling or output delivery. Keep normal and instrumented runs separate, preserve first edits and all outliers, compare exact output where applicable, and inspect functional browser behavior before accepting a timing improvement. Package changes require install/update/fallback and signing checks in addition to performance. Absolute timings here cover this reference Mac, not a cross-platform performance guarantee.
