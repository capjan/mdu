# Optimization implementation

Approved scope: native .NET development tools, local static website assets, responsive preview, and measured acceptance against the architecture quality requirements. Git, OS APIs/utilities and browsers are permitted. Consent-gated external video players remain supported.

- [x] Replace Python benchmark generation, process measurements, diagnostics, reporting and archiving with .NET tools.
- [ ] Add .NET-driven browser measurements, correctness checks, and percentile workloads.
- [x] Replace the Node/npm icon preparation workflow and its tests with .NET tools.
- [ ] Remove site-wide media/PDF rediscovery from ordinary edits, preserving validation.
- [ ] Reuse navigation preparation and reduce avoidable HTML allocations.
- [ ] Restrict rebuilds to affected dependencies.
- [ ] Coalesce changes, prioritize active previews, and prevent obsolete work from winning.
- [ ] Make initial preview usable early, with correct navigation/search and clear download readiness.
- [ ] Verify bounded memory and all latency/allocation targets; document measurements and remaining limits.

Preserve the historical baseline and its provenance. Each implementation step requires focused correctness checks and measured end-to-end comparisons. Pause for the user's decision if a target proves incompatible with correctness or the approved architecture.

## Tool migration verification

Implementation base: fetched `origin/main` at `2ca86130be88294de36b1a838aad382305b9a3ee`, including #187 and #189. The prior investigation and quality documentation were replayed on this base.

- Native render/serve/diagnostic/memory/compare/archive/inspection runs passed on the small corpus. Diagnostic output remained byte-identical. Native OS resource collection reports lifetime peak RSS without including the helper CLR.
- The .NET-driven browser completed two rounds of all four edit types. Larger percentile runs and functional browser acceptance are still pending.
- Full pinned-source icon regeneration completed. Catalog verification retained all previously shipped shortcodes (including 3,486 Dashboard Icons); the .NET converter additionally accepted 13 icons. Bundled resources were deliberately preserved during tooling migration.
- Release solution tests: 1,465 passed, 6 platform-conditional skips, 0 failed. New tests cover frozen synthetic input hashes, SVG theme behavior, reference/style isolation, legacy SVG namespaces and hidden editor images.
