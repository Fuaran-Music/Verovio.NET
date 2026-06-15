# Verovio.NET

F# / .NET 10 bindings for the [Verovio](https://github.com/rism-digital/verovio)
music engraving engine.

Verovio is an LGPL-3.0 C++ library maintained by the RISM Digital Center; it
renders [MEI](https://music-encoding.org/), MusicXML, Humdrum, PAE, and ABC
to scalable SVG and MIDI (and PDF via the upstream C++ Toolkit, though PDF
output is not exposed by upstream's C wrapper — see [Status](#status)).
Verovio.NET puts an idiomatic F# face on the Verovio toolkit so .NET
projects can consume it as a regular NuGet package without managing native
artefacts or hand-rolling P/Invoke signatures. The public API is designed
to be ergonomic from both F# and C#.

## Status

`0.2.2-alpha` — **complete `c_wrapper.h` coverage** (Phase 49) + **multi-RID
native vendoring** (Phase 56).

The repo ships a **single NuGet package** (`Verovio.NET`) carrying the
public API + P/Invoke implementation, vendoring the native engine for
**win-x64**, **win-arm64**, and **linux-x64** (see
[Supported runtimes](#supported-runtimes)). The native path consumes
upstream's
[`tools/c_wrapper.h`](https://github.com/rism-digital/verovio/blob/version-6.2.0/tools/c_wrapper.h)
shim directly — the same surface upstream's Go bindings use.

Every one of the 61 upstream functions is bound and surfaced through the
public `Toolkit` class (plus the `VerovioLogging` static for the two
process-global logging toggles). Fallible methods expose both
`Result<_, _>`-returning and `*OrThrow` variants.

### Capability coverage

| Domain | Surface |
|---|---|
| Lifecycle | `Toolkit.Create()` / `Create(resourcePath)` / `IDisposable` |
| Document loading | `LoadData` / `LoadFile` / `LoadZipBase64` / `LoadZipBuffer` |
| In-memory rendering | `RenderToSvg` / `RenderToMidi` / `RenderData` (one-shot) / `GetMei` / `GetHumdrum` / `RenderToPae` / `RenderToExpansionMap` |
| Typed Timemap | `RenderToTimemap` → `Timemap` (per-event `RealTimeMs` + `ScoreTimeQuarter` + `NotesOn`/`Off` + optional `Tempo`) + `RenderToTimemapJson` raw |
| Format conversion | `ConvertHumdrumToHumdrum` / `ConvertHumdrumToMidi` / `ConvertMeiToHumdrum` |
| File I/O | `SaveFile` / `RenderToSvgFile` / `RenderToMidiFile` / `RenderToPaeFile` / `RenderToExpansionMapFile` / `RenderToTimemapFile` / `GetHumdrumFile` |
| Options introspection | `GetAvailableOptions` / `GetDefaultOptions` / `GetOptions` / `GetOptionUsageString` / `GetOptionsIntrospection` (bundle) / `SetRawOptions` / `ResetOptions` |
| Layout / scale / resource path | `RedoLayout` / `RedoPagePitchPosLayout` / `GetScale` / `SetScale` / `SetOutputTo` / `GetResourcePath` / `SetResourcePath` |
| Element queries | `GetDocumentInfo` / `GetId` / `GetPageWithElement` / `GetElementsAtTime` / `GetMidiValuesForElement` / `GetElementAttr` / `GetExpansionIdsForElement` / `GetNotatedIdForElement` / `GetTimeForElement` / `GetTimesForElement` (typed) / `GetDescriptiveFeatures` |
| Editor surface (raw passthrough) | `Edit(EditorAction)` / `EditInfo` / `Select` |
| Validation | `ValidatePae` / `ValidatePaeFile` → `PaeValidationReport` |
| Determinism | `Determinism.DefaultXmlIdSeed` / `Toolkit.ResetXmlIdSeed(seed)` (sticky) |
| Logging | `VerovioLogging.EnableConsole` / `.EnableBuffer` (global) / `Toolkit.DrainLog` (per-toolkit) |

### Determinism contract

> With the same `(libverovio version, MEI input, RenderOptions, xml:id seed)` tuple, every Toolkit on every machine produces byte-identical SVG.

Upstream's xml:id RNG is C++-static (process-global rather than
per-toolkit). Verovio.NET surfaces a stable contract on top of it by
re-seeding at every load and render boundary from a sticky
per-Toolkit field initialised to `Determinism.DefaultXmlIdSeed = 1`
(the smallest non-zero value; `0` is upstream's "randomize-from-clock"
sentinel). Override the sticky seed via `ResetXmlIdSeed(seed)` — useful
for content-addressable engraving where seed = hash(document) makes
identical outputs trivially attributable.

### Supported runtimes

| RID | Native binary | How it's built |
|---|---|---|
| `win-x64` | `libverovio.dll` | `scripts/build-libverovio.ps1` (MSVC, `-A x64`) |
| `win-arm64` | `libverovio.dll` | `scripts/build-libverovio.ps1 -Rid win-arm64` (MSVC ARM64 toolset, `-A ARM64`; cross-builds from an x64 host) |
| `linux-x64` | `libverovio.so` | `scripts/build-libverovio-linux.ps1` (containerised CMake) |

The `.dll`/`.so` is a NuGet **native runtime asset** resolved per RID at
load time. Verovio's SMuFL font/glyph payload (`verovio-data`) ships once
and is delivered to consumer output via `buildTransitive/Verovio.NET.targets`
(not as a native asset — see the note in that file for the NETSDK1152
rationale); the runtime resolves it cross-RID, so the single copy serves
every platform.

`osx-arm64`, `osx-x64`, and `linux-arm64` are **deferred until demanded** —
the vendoring path is RID-parametric, so adding one is a build + commit, not
a code change.

### What's deferred

- **macOS + linux-arm64 RIDs**: `osx-arm64`, `osx-x64`, `linux-arm64`.
  Gated on the first consumer that needs them.
- **PDF rendering**: upstream's `c_wrapper.h` doesn't expose
  `vrvToolkit_renderToPDF`. `RenderToPdf` returns
  `Error UnsupportedOutputFormat` pending a wrapper-extension or
  SVG-post-process decision.
- **Typed `EditorAction` DU**: editor actions ship as raw JSON via
  `EditorAction.FromRawJson` for now. The typed constructor lands when
  a Builder consumer drives editor actions through Verovio (instead of
  through F# Score trees).
- **Typed `DescriptiveFeatures` model**: ships as raw JSON. The typed
  model lands when a curriculum / pedagogy consumer drives the
  requirements.

## Package

| Package         | Role                                                                                                |
| --------------- | --------------------------------------------------------------------------------------------------- |
| `Verovio.NET`   | The library. Public API + P/Invoke implementation. Ships the native engine vendored for win-x64, win-arm64, and linux-x64. |

Consumers add a single package reference. The native engine is resolved
automatically via .NET's `runtimes/<rid>/native` convention; the font/glyph
data is delivered to output via the package's `buildTransitive` targets.

## Quickstart

### F#

```fsharp
open Verovio.NET

use toolkit = Toolkit.Create()

let mei = System.IO.File.ReadAllText "c-major.mei"

match toolkit.LoadData(mei) with
| Error err -> eprintfn "Load failed: %A" err
| Ok () ->
    match toolkit.RenderToSvg(1) with
    | Ok svg -> printfn "%s" svg
    | Error err -> eprintfn "Render failed: %A" err
```

### C#

```csharp
using Verovio.NET;

using var toolkit = Toolkit.Create();

var mei = File.ReadAllText("c-major.mei");

// Throwing variants are ergonomic for C# happy paths:
toolkit.LoadDataOrThrow(mei);
var svg = toolkit.RenderToSvgOrThrow(1);
Console.WriteLine(svg);

// Or the Result-returning shape if you want explicit error handling.
// You can pattern-match the Result via the FSharpResult<,> shim.
```

## Building

```powershell
# Verify (default — fantomas + build + tests)
pwsh ./run.ps1

# Fast iteration
pwsh ./run.ps1 -SkipFormat -SkipBuild

# Pack into ..\local-nuget-feed\
pwsh ./run.ps1 -Pack
```

Requirements: .NET SDK `10.0.203` (pinned in [`global.json`](global.json)).
PowerShell 7+ for `run.ps1`.

## Building the native engine

The vendored binaries ship in the NuGet — most consumers never need to
build them. If you do (security audit, custom upstream version,
contribution to this repo), the vendor scripts clone the pinned upstream
tag, run CMake, and copy the result into
`src/Verovio.NET/runtimes/<rid>/native/`. Build provenance for each RID is
documented in that RID's `PROVENANCE.md`.

### Windows (win-x64 / win-arm64)

```powershell
pwsh ./scripts/build-libverovio.ps1                  # win-x64 (default)
pwsh ./scripts/build-libverovio.ps1 -Rid win-arm64   # win-arm64
```

Requirements:
- CMake 3.15+
- Visual Studio 2022 Build Tools with the "Desktop development with C++"
  workload (MSVC v143 + Windows 11 SDK). For `-Rid win-arm64`, also the
  **C++ ARM64 build tools** component
  (`Microsoft.VisualStudio.Component.VC.Tools.ARM64`).
- `git` on PATH for the upstream clone

Because CMake's Visual Studio generator selects the toolset via `-A`, either
Windows RID can be produced from either host architecture — one Windows
machine with both toolsets emits both DLLs (win-arm64 is a cross-build from
an x64 host, and vice-versa).

### Linux (linux-x64)

```powershell
pwsh ./scripts/build-libverovio-linux.ps1
```

Requirements: Docker with BuildKit. The script builds
[`scripts/Dockerfile.libverovio-linux`](scripts/Dockerfile.libverovio-linux)
(Debian + CMake + g++), extracts `libverovio.so` via a BuildKit
`--output`, and copies it into
`src/Verovio.NET/runtimes/linux-x64/native/`. Without Docker, run the
equivalent CMake steps in the Dockerfile inside WSL or any Linux box and
copy the `.so` into that directory.

## Vendored upstream

| Field           | Value                                          |
| --------------- | ---------------------------------------------- |
| Upstream        | https://github.com/rism-digital/verovio        |
| Version pin     | `version-6.2.0`                                |
| License         | LGPL-3.0-or-later                              |
| Vendor location | `src/Verovio.NET/runtimes/<rid>/native/` (win-x64, win-arm64, linux-x64) |

Bump procedure: edit the version tag in `scripts/build-libverovio.ps1`
(and `scripts/Dockerfile.libverovio-linux`'s `VEROVIO_TAG`), re-run the
vendor script for each RID, run the snapshot tests, update each RID's
`PROVENANCE.md` row.

## Layout

```
Verovio.NET/
├── src/
│   ├── Verovio.NET/
│   │   ├── Types.fs                     # public closed DUs + option records + typed returns
│   │   ├── Internal/Interop.fs          # DllImport bindings (61/61 c_wrapper functions)
│   │   ├── Logging.fs                   # VerovioLogging static (process-global toggles)
│   │   ├── Toolkit.fs                   # public Toolkit class
│   │   ├── build/Verovio.NET.targets    # buildTransitive: delivers verovio-data to consumers
│   │   └── runtimes/<rid>/native/       # win-x64 + win-arm64 (libverovio.dll), linux-x64 (libverovio.so)
│   │       ├── libverovio.{dll,so}      # vendored binary (built via scripts/)
│   │       ├── PROVENANCE.md
│   │       └── verovio-data/            # SMuFL fonts (win-x64 copy; shipped to consumers via buildTransitive)
│   └── Verovio.NET.Tests/               # Expecto suite over the public API
├── samples/
│   └── Verovio.NET.Samples.Console/     # multi-domain end-to-end smoke
├── scripts/
│   ├── build-libverovio.ps1             # win-x64 / win-arm64 vendor build (operator-run)
│   ├── build-libverovio-linux.ps1       # linux-x64 vendor build (containerised)
│   └── Dockerfile.libverovio-linux      # reproducible linux-x64 .so build
├── Verovio.NET.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── nuget.config
├── global.json
├── .config/dotnet-tools.json
├── BENCHMARKS.md
└── run.ps1
```

## Design

* **F# implementation, C#-friendly public surface.** `Toolkit` is a class
  with member methods (`toolkit.LoadData(mei)`) rather than a module of
  curried F# functions. Fallible operations expose both `Result`-returning
  and `*OrThrow` variants so C# happy-paths read naturally without giving
  up F#'s closed-DU error vocabulary for callers that want it.
* **Closed DUs throughout.** Format identifiers are closed unions
  (`InputFormat`, `OutputFormat`), not strings; failure modes are
  enumerable `LoadError` / `RenderError` unions, not stringly-typed
  messages. New cases are additive.
* **Value-space-projected option records.** `RenderOptions` and friends
  carry private constructors; smart-ctor `Create` methods reject invalid
  combinations (non-positive page dimensions, scale outside the
  Verovio-documented range, etc.) at construction time. The public
  surface cannot represent an option set that Verovio will reject at
  render time.
* **No `obj`-typed escape hatches.** Even backend failure translation
  passes through closed-DU cases — no string-typed catch-alls in the
  public API.

See [the upstream Verovio toolkit reference](https://book.verovio.org/toolkit-reference/toolkit-methods.html)
for the underlying method surface this adapter wraps.

## Contributing

Issues and pull requests welcome. Please:

* Run `dotnet fantomas .` on changed F# files before committing.
* Add Expecto coverage for new public-API behaviour under
  `src/Verovio.NET.Tests/`.
* If you touch the P/Invoke surface in `Internal/Interop.fs`, validate
  against the upstream `tools/c_wrapper.h` signatures — they're the
  authoritative ABI.
* CI must pass on Windows + Linux runners (win-x64 + linux-x64 are
  vendored; macOS runners land when those RIDs ship).

## License

Verovio.NET is licensed under the [Apache License 2.0](LICENSE).

The upstream Verovio engraver is licensed under the
[LGPL-3.0](https://github.com/rism-digital/verovio/blob/develop/LICENSE.txt);
the vendored `libverovio.dll` is dynamically linked through a
P/Invoke boundary — the standard LGPL dynamic-linking boundary, same
posture as SQLite.NET, libgit2sharp, etc. Adapter code in this repo is
independent original work and is offered under the Apache 2.0 terms.
