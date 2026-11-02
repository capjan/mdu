# Optimization checkpoint — 2026-11-02

The approved follow-up to [#188](https://github.com/capjan/mdu/issues/188) includes implementation, native .NET tooling, and the [architecture quality requirements](../../../Docs/Architecture/README.md#quality-requirements). **Implementation and acceptance are not complete.** The normal framework-dependent reference still misses startup/full-render targets. Native release execution produces substantially different results, so it must not silently replace that reference.

## Revisions, environment and inputs

- Started from fetched `origin/main` **`2ca86130be88294de36b1a838aad382305b9a3ee`**, including #187 and #189.
- Native-tool control: `611b84a`, before production optimization. Its results are included as `origin-control.json` in the JIT archive.
- Timed production and harness: **`d955cf8034f7d01b596c17e4517c4d5719e469bc`**, clean working tree. `02e79d2` subsequently improves summaries and selected-binary output comparison; production sources are identical.
- Apple M4, 16 GiB, macOS 27.0.1 arm64; .NET SDK 10.0.403, runtime 10.0.14; Release, default tiering. Browser checks: disposable headless Chrome 156.0.8078.74.
- Frozen inputs from `611b84a`: **7 / 54 / 503 pages**, including feature fixtures. The additional technology document explains the difference from the historical 6 / 53 / 503-page baseline. Control and optimized runs use exactly the same frozen inputs.
- Input SHA-256: small `24dc7e80d72ff5207a2e6ec2e708e6cf265a624eec1a12a410fa59b7f56d713a`; docs `77bef01a3ce3038358ab241c2e05c660f6a90fc8e6cb06ceba8baac4a852cc69`; large `ff1a499b59d5ff313993fedf5cb98509fee1f7b8007d65f01d1011cee3c9b2a8`.

[Framework-dependent results, control, profiles, memory, browser checks and comparisons](2026-11-02-optimization-jit.json.gz). [Native executable results](2026-11-02-optimization-native.json.gz). The original [historical report](2026-11-02.md) and archive are preserved unchanged.

Each normal series uses one discarded render, seven measured fresh render processes, and three server processes with three rounds per server: nine observations per edit kind. Output is removed before each render/server; edits overwrite in place. Filesystem caches are warm, process-local state is fresh at startup. No concurrent builds or other benchmark series and no forced collections occur in normal timing. The .NET resource helper and browser launch are outside CLI timing.

## Implemented and verified

- Benchmark generation, timing, process resources, instrumentation, browser control, reports and archives now use .NET. The icon regeneration workflow and its tests also use .NET; all previously shipped shortcode names survive complete regeneration.
- Stable per-page Markdown snapshots, retained media membership/PDF-link analysis, shared navigation preparation and streamed HTML/template/search output remove repeated site-wide work from ordinary edits.
- Specification edits update their consumers, companion specification pages and search, preserving unrelated HTML. Consent-provider membership changes still update shared dialogs.
- A 20 ms change batch replaces the previous 75 ms delay. Atomic saves, bursts and recovery from invalid intermediate content have integration coverage.
- First preview readiness includes complete navigation/search and the entry page. Pending HTML is completed serially between change batches; requested pages and aliases are materialized before serving. New generations replace obsolete pending work, and changed pages leave the old queue.
- OpenAPI retention is limited to 128 entries and 8,388,608 UTF-16 source characters. Parsed graphs add memory beyond that source-text allowance. Highlighter entries are bounded by the finite grammar factories, including one shared fallback for unknown names.
- Fixed regexes in HTML, media, OpenAPI and the principal diagram parsers use .NET source generation. Existing upstream component-diagram limits remain intact.

## Normal framework-dependent execution

Medians, with observed minimum–maximum. All times are milliseconds. These are not p95 results.

| Workload | Complete render, n=7 | First usable HTTP, n=3 | Complete initial HTML, n=3 | Render peak RSS MiB, n=7 |
| --- | ---: | ---: | ---: | ---: |
| small | 474.0 (446.4–495.0) | 494.4 (458.3–499.4) | 538.9 (502.3–544.1) | 118.3 (118.2–118.5) |
| docs | 1038.8 (1022.9–1064.0) | 660.1 (651.0–681.9) | 1098.9 (1090.1–1099.0) | 238.3 (238.1–238.4) |
| large | 3798.9 (3558.3–3879.6) | 1032.9 (989.6–1046.7) | 3623.1 (3532.1–3685.8) | 151.2 (150.4–153.1) |

| Workload | Text HTTP, n=9 | Diagram HTTP, n=9 | API HTTP, n=9 | Shared configuration HTTP, n=9 |
| --- | ---: | ---: | ---: | ---: |
| small | 42.3 (39.8–51.6) | 43.4 (40.9–46.5) | 56.5 (49.3–72.0) | 76.9 (66.8–95.0) |
| docs | 39.4 (28.7–44.3) | 42.5 (40.9–49.3) | 62.4 (52.6–74.0) | 181.5 (170.8–211.0) |
| large | 47.4 (39.6–62.7) | 40.7 (39.5–45.8) | 49.1 (46.1–54.2) | 223.1 (197.0–549.2) |

For the large site, the fresh native-tool control measured 4918.6 ms complete render, 4260.8 ms initial HTTP, and 351.5 ms text HTTP (n=3/2/6 respectively). The new initial response is usable earlier; full initial completion is reported separately rather than counted as a faster complete build. Shared configuration still affects the whole site: its median complete update is 1255.6 ms (1098.5–2176.1), despite the earlier HTTP response.

The ordinary text HTTP median target of 50 ms is met in this sample. The 2 s complete-render target and 250 ms small-site startup target are missed; large-site first readiness is close to, but slightly above, 1 s. This establishes a target miss, not mathematical impossibility of further optimization.

## Existing native release execution, separately measured

Native AOT was already the release executable format. This experiment does not change the NuGet package or release workflow. The executable was published from the same production sources with:

```sh
dotnet publish Source/Mdu/Mdu.csproj -c Release -r osx-arm64 --self-contained true -p:PublishAot=true -warnaserror:IL2026,IL2104,IL3050,IL3053 --output /tmp/mdu-native
```

The archive records the executable's SHA-256. Native medians and spread:

| Workload | Complete render, n=7 | First usable HTTP, n=3 | Complete initial HTML, n=3 | Render peak RSS MiB, n=7 |
| --- | ---: | ---: | ---: | ---: |
| small | 104.1 (95.5–109.4) | 124.0 (118.8–143.4) | 135.7 (130.4–144.2) | 69.0 (69.0–70.0) |
| docs | 287.9 (263.1–296.7) | 191.0 (188.9–191.2) | 338.1 (322.1–381.0) | 169.1 (169.0–169.3) |
| large | 1351.8 (1306.4–1409.3) | 326.3 (322.4–367.3) | 1434.5 (1416.4–1449.2) | 95.5 (94.9–96.0) |

Native text HTTP medians are 42.4 / 43.1 / 47.0 ms (n=9 each). The observed native startup/full-render medians meet the numerical goals for these corpora. They do not establish acceptance of the agreed framework-dependent reference or tail latency.

## Browser and correctness

Headless .NET/CDP checks exercise loading once, persisted consent, revocation, exact search sections, navigation/anchors, and keyboard Tab with visible focus. Both small and large workloads passed. Images are stored in the disposable run's `logs/` directory; boolean results and browser version are retained in the archive.

Three browser edits per kind in one server per workload gave these frame-readiness observations:

| Workload | Text ms | Diagram ms | API ms | Shared configuration ms |
| --- | ---: | ---: | ---: | ---: |
| small | 67.4 (67.0–85.0) | 81.8 (67.5–82.3) | 99.2 (83.1–166.8) | 116.6 (114.0–116.6) |
| large | 82.7 (81.5–83.1) | 83.4 (81.9–84.4) | 82.2 (82.0–82.9) | 251.3 (249.8–433.0) |

The boundary is expected DOM text retained across two animation frames, including normal live reload; it is not physical display-panel presentation. This small sample cannot establish the 150/300 ms p95 targets. At least 300 observations across multiple processes remain required.

Every produced file is byte-identical for the exercised small/docs/large outputs: normal optimized output versus the pre-optimization diagnostic build, normal versus current diagnostic, and native versus current diagnostic. Comparisons cover 17 / 110 / 1099 files, including HTML, aliases, search, CSS and scripts. Integration tests separately cover pending pages/aliases, changes during completion, API source reload events, shared media membership and strict PDF behavior.

Release solution tests: **1,483 passed, 6 platform-conditional skips, 0 failed**. The final alias/default-entry correction also passed all 23 focused visitor/serve tests. Native publication passed the configured trimming/AOT warning checks.

## Allocation and retention observations

A separate lightweight, instrumented large-site run applies 60 body edits followed by 15 mixed edits. It includes idle full-collection checkpoints; these collections are absent from normal timing.

- Body edit scope: **4.2 MiB median, 4.2–6.5 MiB observed, n=60**, including the first edit. This is below the 10 MiB text-edit target for this workload. The historical instrumented warm edit was about 1,120 MiB; instrumentation boundaries and source revisions are explicitly different.
- Diagram scopes: 7.0 MiB median, n=5; API scopes: 15.4 MiB, n=5. The 10 MiB requirement applies to ordinary text edits, not every feature edit.
- Configuration scopes: 272.3 MiB median, n=5. Background page work occurs outside that scope; this is **not total configuration-rebuild allocation**. Full render allocation remains about 3,003 MiB in a separate detailed profile, n=1.
- Post-GC managed memory: 51.95 MiB initially; 53.20 at edit 10; 53.48 at edit 30; 53.48 at edit 60; 53.79 at edit 75. Body-edit retention stabilizes; the final short mixed sequence adds about 0.31 MiB. Longer mixed/change-of-input runs remain necessary before claiming sustained bounded retention.

Resource peaks, cumulative allocation and retained managed memory describe different quantities. Profiles include overhead and observation requests. Streamed template lookup/write is now one `html.write` stage; first-ready and complete-site snapshots are separate. Do not sum inclusive stage allocations or interpret early rebuild scopes as the whole background generation.

## Decision and unfinished acceptance

Pause at this checkpoint to agree how to handle the measured execution-format difference. Keep the documented goals unchanged until that decision. A candidate is to offer native implementations through platform-specific .NET tool packages while retaining a portable fallback; this would require packaging, signing, platform-coverage and release validation. Another path is further framework-dependent startup work, including a separately measured ReadyToRun experiment. Neither packaging change has been implemented or published here.

Still open:

- Decide whether startup/full-render acceptance remains mandatory for the portable/development execution too, or is evaluated against shipped native implementations with separately stated portable budgets.
- Run the 300-observation browser/HTTP percentile series across multiple processes after that decision.
- Extend retention checks for longer mixed sequences, Git repositories/imports, input churn, and PDF workloads. Existing functional tests are not a performance budget for these cases.
- Finish scheduling for expensive PDF workloads. Required PDFs are currently generated synchronously before relevant readiness/completion; no unfinished download is silently advertised as current. They can still block the next change batch.
- Validate the eventual delivery approach across supported platforms; these measurements cover one reference Mac.

## Reproduce the frozen inputs and checkpoint

Use a checkout of `02e79d2` (production sources equal `d955cf8`). The archived results have no dependency on the original temporary input directory. Restore/build and regenerate the frozen documentation from Git:

```sh
dotnet build Mdu.sln -c Release
repo="$PWD"
perf="$repo/Tools/Performance/bin/Release/net10.0/Performance.dll"
mkdir -p /tmp/mdu-reference-docs
git archive 611b84a Docs | tar -xf - -C /tmp/mdu-reference-docs
dotnet "$perf" --mode generate --repo /tmp/mdu-reference-docs --work /tmp/mdu-checkpoint-jit
dotnet "$perf" --mode baseline --repo "$repo" --work /tmp/mdu-checkpoint-jit --samples 7 --servers 3 --rounds 3
dotnet "$perf" --mode memory --repo "$repo" --work /tmp/mdu-checkpoint-jit --samples 1 --workloads large
dotnet "$perf" --mode browser-check --repo "$repo" --work /tmp/mdu-checkpoint-jit --samples 0 --servers 1 --rounds 3 --workloads small large
dotnet "$perf" --mode diagnostic --repo "$repo" --work /tmp/mdu-checkpoint-jit --samples 1 --rounds 1 --workloads large
dotnet "$perf" --mode compare --repo "$repo" --work /tmp/mdu-checkpoint-jit
dotnet "$perf" --mode generate --repo /tmp/mdu-reference-docs --work /tmp/mdu-checkpoint-native
dotnet "$perf" --mode baseline --repo "$repo" --work /tmp/mdu-checkpoint-native --cli /tmp/mdu-native/mdu --samples 7 --servers 3 --rounds 3
```

Publish the native executable with the preceding command before its series. The `tar` extraction is an allowed OS utility; all workload generation, measurement and analysis use .NET. Check the manifest hashes above. Use fresh output and sequential runs. Full definitions, limitations and archive inspection are in the [tooling README](../README.md).
