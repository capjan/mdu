---
navigation.order: 4
---

# Performance methodology

For starting commands and the target/result tables, use [Performance benchmarks](../Benchmarks.md). This optional reference explains how to collect comparable native/browser/memory evidence and assess the architecture requirements.

## Reference and workload scope

The reference environment is an Apple M4 with 16 GiB RAM, macOS arm64, .NET SDK 10.0.403 / runtime 10.0.14. Acceptance uses the native .NET release build; framework-dependent execution has separate startup/build budgets. Both share editing, correctness, and memory requirements.

The frozen acceptance sites have 7, 54, and 503 pages. The large site repeats mixed templates, including a 12-component diagram and an eight-operation API specification; it does not represent every possible 500-page site. An ordinary edit changes one page without changing shared navigation or configuration. Larger content, shared dependencies, PDFs, imports, and slow storage need separately labelled workloads and budgets.

The [acceptance report](https://github.com/capjan/mdu/blob/main/Tools/Performance/results/2026-11-02-acceptance.md) records production `52eaa3a` (or identical production sources at `7ac2f23`), full distributions, outliers, and memory evidence. The [later review check](https://github.com/capjan/mdu/blob/main/Tools/Performance/results/2026-11-02-review2.md) records the subsequent short `c9f286b` series. Keep those sample sizes and revisions separate; neither is an automatic measurement of today's checkout.

## Native and browser measurements

First complete the build from the [quick start](../Benchmarks.md#quick-start). For the reference Mac, Native AOT publication also needs the native compiler provided by the Xcode Command Line Tools. Browser measurement requires an installed Chromium-compatible browser: Google Chrome in `/Applications` is the macOS default. On Linux the default executable is `chromium`; `MDU_BENCHMARK_BROWSER` can point to another compatible executable.

Regenerate the frozen documentation inputs from Git so documentation edits do not change the comparison site. A fresh clone may need the retained PR #190 history to obtain the archived input commit; fetching that history leaves the current checkout in place:

```sh
mkdir -p /tmp/mdu-bench-reference
git fetch origin refs/pull/190/head
git archive 611b84a Docs | tar -xf - -C /tmp/mdu-bench-reference
dotnet run --project Tools/Performance -c Release --no-build -- --mode generate --repo /tmp/mdu-bench-reference --work /tmp/mdu-bench-native
```

Publish the current checkout and measure its native executable. The following RID is for Apple Silicon; use the host's corresponding RID and native compiler on another supported system.

```sh
dotnet publish Source/Mdu/Mdu.csproj -c Release -r osx-arm64 --self-contained true -p:PublishAot=true --output /tmp/mdu-bench-native-cli
dotnet run --project Tools/Performance -c Release --no-build -- --mode browser --work /tmp/mdu-bench-native --cli /tmp/mdu-bench-native-cli/mdu --samples 7 --servers 3 --rounds 100 --edit-kinds content diagram spec
dotnet run --project Tools/Performance -c Release --no-build -- --mode summary --work /tmp/mdu-bench-native
```

This collects 300 observations per edit kind per site across three server processes, including the first edit. Browser readiness means expected DOM text retained across two animation frames, including normal live reload. Browser launch is outside CLI startup timing. Shared configuration changes, Git history, PDFs, and functional browser interactions have separate procedures.

For a comparable framework-dependent run, generate the same frozen inputs into a separate working folder and omit `--cli` from the browser command. Always distinguish current-checkout comparisons from reproducing the historical measured executable.

## Memory and additional checks

The ordinary timing runs do not measure allocation/retention profiles. A separate instrumented native build performs 300 text edits followed by 100 mixed configuration/diagram/API rounds, with idle collection checkpoints:

```sh
dotnet run --project Tools/Performance -c Release --no-build -- --mode memory-native --work /tmp/mdu-bench-native --samples 1 --workloads large --body-edits 300 --mixed-rounds 100
dotnet run --project Tools/Performance -c Release --no-build -- --mode summary --work /tmp/mdu-bench-native
```

The probe exists only in the disposable instrumented source build. Its forced collections and overhead are excluded from ordinary latency runs. Retained managed memory, allocation volume, and process peak RSS are separate quantities.

The repository's [complete tool reference](https://github.com/capjan/mdu/blob/main/Tools/Performance/MeasurementDetails.md) covers `browser-check`, `pdf-check`, stage diagnostics, complexity, output comparison, archiving, and the exact measurement boundaries. Run measurements sequentially without concurrent builds/tests/benchmarks and preserve raw data with the source revision.

## Architectural constraints

| Requirement | Consequence for optimization |
| --- | --- |
| Local work for local edits | Refresh the affected dependency set and reuse valid analysis of unchanged pages. |
| Latest saved content wins | Coalesce events without indefinite delay. Older work cannot replace newer output; invalid intermediate saves must recover. |
| Responsive scheduling | Prioritize requested preview pages and new changes over obsolete background work. |
| Honest readiness | Measure first usable preview separately from complete-site output. Navigation and search stay correct; `render` completes all required output before succeeding. |
| Consistent output | Preserve links, anchors, search, consent, atomic page replacement, and strict PDF-hidden validation. Unfinished downloads must be clearly unavailable. |
| Bounded reuse | Invalidate changed inputs and dependencies, release obsolete state, and set explicit limits on large caches. |

The [processing implementation](Pipeline.md#live-preview) describes the current caches, scheduling, failure recovery, and PDF limitations. If evidence shows that a target cannot be met safely, review the measurements and alternatives before changing the requirement.

## Verification

Record source revision, execution format, runtime settings, input fingerprints, output state, and filesystem-cache conditions. Fresh processes still have warm filesystem caches unless stated otherwise. Keep first-edit observations and outliers; use at least 300 observations across multiple processes before reporting p95. Polling and browser observation overhead remain part of the stated boundaries.

Check direct writes, atomic saves, bursts, changes during rebuilds, additions/deletions, invalid input, shared configuration, external specifications, Git history, imports, and PDF projects separately where needed. A finite retention run can demonstrate stabilization, not unlimited leak freedom.

Timing improvements must preserve behavior: compare exact output where applicable and check search, navigation, anchors, keyboard focus, and consent acceptance/persistence/revocation in a browser. Profile significant costs before adding caches or broadening rebuild changes. Historical reports and raw archives retain their original inputs and provenance.
