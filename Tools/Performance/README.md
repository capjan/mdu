# Performance benchmarks

Start with the [benchmark guide and target/result tables](../../Docs/Architecture/Benchmarks.md). All workload generation, timing, resource collection, and reporting use .NET 10.

## Quick start

From the repository root on macOS or Linux, with the .NET 10 SDK:

```sh
dotnet build Mdu.sln -c Release
dotnet run --project Tools/Performance -c Release --no-build -- --mode generate --work /tmp/mdu-bench-quick
dotnet run --project Tools/Performance -c Release --no-build -- --mode baseline --work /tmp/mdu-bench-quick --workloads large --samples 3 --servers 1 --rounds 3 --edit-kinds content diagram spec
dotnet run --project Tools/Performance -c Release --no-build -- --mode summary --work /tmp/mdu-bench-quick
```

The working folder is disposable; generation replaces its inputs and outputs. This uses the portable Release build and measures complete rendering, first usable preview, and HTTP updates. It does not collect browser timing, p95, or memory profiles. Results are in `baseline.json` and `logs/` under the working folder.

## Optional references

- [Native, browser, and memory procedures](../../Docs/Architecture/Details/Performance.md)
- [Complete tool options and measurement internals](MeasurementDetails.md)
- [Full reference acceptance and raw evidence](results/2026-11-02-acceptance.md)
- [Latest measured review follow-up and remaining limits](results/2026-11-02-review2.md)
- [Native .NET tool package study](NativePackages.md)

Historical reports and archives remain unchanged. Current-checkout measurements are new observations; they do not automatically replace the recorded reference results.
