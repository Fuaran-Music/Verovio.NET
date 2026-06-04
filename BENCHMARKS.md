# Verovio.NET — render benchmarks

Native-backend benchmarks for the win-x64 path.

## Status

**Phase 04 close-out pending — measurements deferred until libverovio.dll
ships.** This file is the structural placeholder; real numbers land when
the DLL is built and vendored. The expected ranges are based on
upstream's published timing data for the equivalent JS toolkit and on
scoping conversations during the Phase 04 design.

## Expected baseline (rough, pre-measurement)

| Metric                                 | Expected | Notes                                                                            |
| -------------------------------------- | -------- | -------------------------------------------------------------------------------- |
| `Toolkit.Create()` cold start          | ~50–150 ms | First-call cost: native lib load + Verovio font/resource setup.                |
| Subsequent `Toolkit.Create()`          | < 10 ms  | Native lib stays loaded; only Verovio's per-instance setup runs.                 |
| Small scale render (2 measures) to SVG | ~5–20 ms | Round trip including `LoadData` + `setOptions` + `renderToSVG`.                  |
| Medium piece (10 pages) to SVG         | ~50–150 ms / page | Verovio's layout dominates; per-page cost roughly linear.                |
| Sustained throughput (warmed)          | ~50–200 renders/sec/instance | Single-instance, no pool; multi-instance pool scales linearly. |

## Measurement procedure (for the follow-up phase)

```powershell
# Pre-warm:
dotnet build -c Release src/Verovio.NET.Tests/Verovio.NET.Tests.fsproj

# BenchmarkDotNet-style timing — Phase 04b will add a Verovio.NET.Bench
# project with the canonical workload definitions. Until then:
$mei = Get-Content samples/Verovio.NET.Samples.Console/fixtures/c-major.mei -Raw

# Cold start
Measure-Command {
    Add-Type -Path src/Verovio.NET/bin/Release/net10.0/Verovio.NET.dll
    $tk = [Verovio.NET.Toolkit]::Create()
    $tk.LoadDataOrThrow($mei)
    [void]$tk.RenderToSvgOrThrow(1)
    $tk.Dispose()
}

# Sustained
$tk = [Verovio.NET.Toolkit]::Create()
$tk.LoadDataOrThrow($mei)
Measure-Command {
    1..1000 | ForEach-Object { [void]$tk.RenderToSvgOrThrow(1) }
}
$tk.Dispose()
```

## Context

ScaleMastery's SSR routes expect ~99% cache hit rate after warmup; the
native render speed is bounded only by cache misses and worker-pool
saturation under burst. Sight-reading.ai's per-keystroke-render UX will
likely route to a client-side Fable backend (separate NuGet) rather
than round-tripping to the server for sub-100ms feel. Both consumption
patterns sit well inside the expected envelope above.

If benchmarks come back materially worse than the expected ranges, the
hot-path to profile is most likely Verovio's MEI parser + layout engine
in the underlying C++ — not the marshalling layer (a single P/Invoke
hop with UTF-8 string copy is < 1 µs typically).
