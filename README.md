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

`0.1.0-alpha` — Phase 04 structural pivot.

The repo now ships a **single NuGet package** (`Verovio.NET`) carrying the
public API + P/Invoke implementation. The original Phase 03 three-package
design (Verovio.NET + Verovio.NET.Wasm + Verovio.NET.Native) was collapsed
in Phase 04 once the upstream WASM distribution shape was found
incompatible with direct Wasmtime.NET hosting (it's an Emscripten build
with the WASM base64-embedded inside a Node-only JS bundle). The native
P/Invoke path consumes upstream's [`tools/c_wrapper.h`](https://github.com/rism-digital/verovio/blob/develop/tools/c_wrapper.h)
shim directly — the same surface upstream's Go bindings use.

**What ships at 0.1.0-alpha:**

- Public `Toolkit` class with C#-friendly member methods (LoadData,
  RenderToSvg, GetMei, RenderToMidi, GetElementsAtTime,
  GetMidiValuesForElement, GetDocumentInfo, Version) — both
  `Result`-returning and `*OrThrow` variants.
- Closed-DU type surface (`InputFormat`, `OutputFormat`, `LoadError`,
  `RenderError`, smart-ctor `RenderOptions` / `LoadOptions` /
  `PdfOptions`).
- DllImport surface against the upstream c_wrapper.h, ready to bind once
  `libverovio.dll` is built and vendored.
- Sample console + Expecto test suite — both compile and run; the
  native-dispatched tests skip gracefully when `libverovio.dll` is
  missing.

**What's deferred to a follow-up phase (also Phase 04 close-out):**

- `libverovio.dll` build for win-x64 (see [Building libverovio.dll](#building-libveroviodll)).
- The DLL itself committed under `src/Verovio.NET/runtimes/win-x64/native/`.
- Snapshot tests against a golden SVG corpus (generated from the built DLL).
- PDF rendering — upstream's `c_wrapper.h` doesn't expose
  `vrvToolkit_renderToPDF`; we either extend the wrapper or post-process
  multi-page SVG.
- Multi-RID coverage: linux-x64, osx-arm64, linux-arm64. The CMake build
  + CI job to produce these are tracked for the v0.x scope.

## Package

| Package         | Role                                                                                                |
| --------------- | --------------------------------------------------------------------------------------------------- |
| `Verovio.NET`   | The library. Public API + P/Invoke implementation. Ships with `libverovio.dll` vendored for win-x64. |

Consumers add a single package reference. The native DLL is resolved
automatically via .NET's `runtimes/<rid>/native` convention.

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

## Building libverovio.dll

The vendored DLL ships in the NuGet — most consumers never need to build
it themselves. If you do (security audit, custom upstream version,
contribution to this repo), see
[`scripts/build-libverovio.ps1`](scripts/build-libverovio.ps1) and the
build provenance documented in
[`src/Verovio.NET/runtimes/win-x64/native/PROVENANCE.md`](src/Verovio.NET/runtimes/win-x64/native/PROVENANCE.md).

Build requirements:
- CMake 3.15+
- Visual Studio 2022 Build Tools with the "Desktop development with C++"
  workload (MSVC v143 + Windows 10/11 SDK)
- `git` on PATH for the upstream clone

Build command:
```powershell
pwsh ./scripts/build-libverovio.ps1
```

The script clones the pinned upstream tag, runs CMake + MSBuild, and
copies the resulting DLL into `src/Verovio.NET/runtimes/win-x64/native/`.

## Vendored upstream

| Field           | Value                                          |
| --------------- | ---------------------------------------------- |
| Upstream        | https://github.com/rism-digital/verovio        |
| Version pin     | `version-6.2.0`                                |
| License         | LGPL-3.0-or-later                              |
| Vendor location | `src/Verovio.NET/runtimes/win-x64/native/`     |

Bump procedure: edit the version tag in `scripts/build-libverovio.ps1`,
re-run the vendor script, run the snapshot tests, update the
`PROVENANCE.md` row.

## Layout

```
Verovio.NET/
├── src/
│   ├── Verovio.NET/
│   │   ├── Types.fs                     # public closed DUs + option records
│   │   ├── Internal/Interop.fs          # DllImport bindings
│   │   ├── Toolkit.fs                   # public Toolkit class
│   │   └── runtimes/win-x64/native/
│   │       ├── libverovio.dll           # vendored binary (built via scripts/)
│   │       └── PROVENANCE.md
│   └── Verovio.NET.Tests/               # Expecto suite over the public API
├── samples/
│   └── Verovio.NET.Samples.Console/     # minimal end-to-end smoke
├── scripts/
│   └── build-libverovio.ps1             # vendor-DLL build (operator-run)
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
* CI must pass on Windows runners (Linux + macOS runners land when those
  RIDs ship).

## License

Verovio.NET is licensed under the [Apache License 2.0](LICENSE).

The upstream Verovio engraver is licensed under the
[LGPL-3.0](https://github.com/rism-digital/verovio/blob/develop/LICENSE.txt);
the vendored `libverovio.dll` is dynamically linked through a
P/Invoke boundary — the standard LGPL dynamic-linking boundary, same
posture as SQLite.NET, libgit2sharp, etc. Adapter code in this repo is
independent original work and is offered under the Apache 2.0 terms.
