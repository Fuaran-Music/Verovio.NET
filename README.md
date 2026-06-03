# Verovio.NET

Idiomatic F# / .NET 10 bindings for the [Verovio](https://github.com/rism-digital/verovio)
music engraving engine.

Verovio is an LGPL-3.0 C++ library maintained by the RISM Digital Center; it
renders [MEI](https://music-encoding.org/), MusicXML, Humdrum, PAE, and ABC
to scalable SVG (and PDF, MIDI, etc.). Verovio.NET puts an idiomatic F# face
on the Verovio toolkit so .NET projects can consume it as a regular NuGet
package, without managing native artefacts or hand-rolling P/Invoke
signatures.

## Status

`0.0.1-alpha` — Phase 03 scaffold. Public API + `IVerovioBackend` interface
are stable enough for downstream consumers to wire against. The WASM
backend implementation lands in Phase 04; attempting to render with the
`Verovio.NET.Wasm` package today throws a documented
`NotImplementedException` describing the pending implementation.

## Packages

The repo produces three packages:

| Package                | Role                                                                                  |
| ---------------------- | ------------------------------------------------------------------------------------- |
| `Verovio.NET`          | Public API surface. `Verovio` module, `IVerovioBackend` interface, option records, error unions. No backend implementation. |
| `Verovio.NET.Wasm`     | Backend implementation: Wasmtime.NET hosts the upstream `verovio-toolkit-wasm` blob. The MVP-shipped backend. Implementation lands Phase 04; this package is a stub today. |
| `Verovio.NET.Native`   | Backend implementation: P/Invoke onto a native `libverovio` build. Deferred until profiling justifies the multi-RID build matrix. |

Consumers depend on `Verovio.NET` plus one backend package.

## Quickstart

```fsharp
open Verovio.NET

// Pick a backend (only Wasm is shipped at Phase 04 onwards).
let backend = Verovio.NET.Wasm.WasmBackend.create ()
let toolkit = Verovio.create backend

// Load a small MEI snippet.
let mei = System.IO.File.ReadAllText "c-major.mei"
match Verovio.loadData toolkit { Format = InputFormat.MEI } mei with
| Ok () ->
    // Render page 1 to SVG.
    match Verovio.renderToSvg toolkit RenderOptions.Default 1 with
    | Ok svg -> printfn "%s" svg
    | Error err -> eprintfn "Render failed: %A" err
| Error err ->
    eprintfn "Load failed: %A" err
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

## Layout

```
Verovio.NET/
├── src/
│   ├── Verovio.NET/                # public API: Types, IVerovioBackend, Verovio module
│   ├── Verovio.NET.Wasm/           # Wasmtime-backed implementation (stub at Phase 03)
│   ├── Verovio.NET.Native/         # P/Invoke implementation (deferred)
│   └── Verovio.NET.Tests/          # Expecto smoke suite over the public API
├── samples/
│   └── Verovio.NET.Samples.Console/    # minimal end-to-end smoke
├── Verovio.NET.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── nuget.config
├── global.json
├── .config/dotnet-tools.json
└── run.ps1
```

## Design

* **Backend-agnostic public API.** The `Verovio` module operates on an
  `IVerovioBackend` interface; the Wasm and (future) Native packages each
  provide an implementation. Consumers can substitute a custom backend
  for testing or to swap rendering paths at runtime.
* **F# end-to-end.** No C# bridge layer. The public surface uses closed
  discriminated unions (`InputFormat`, `OutputFormat`), record types with
  smart constructors for option records, and `Result<_, _>` returns where
  failure is in-domain (malformed input, render exception). C# consumers
  can still call the API; the F#-natural shape is the design driver.
* **Value-space-projected option records.** `RenderOptions` and friends
  carry private constructors; smart-ctor `create*` functions reject
  invalid combinations (non-positive page dimensions, scale outside the
  Verovio-documented range, etc.) at construction time. The public
  surface cannot represent an option set that Verovio will reject at
  render time.
* **No `obj`-typed escape hatches.** Format identifiers are closed DUs,
  not strings; render-failure cases are an enumerable `RenderError`
  union, not a stringly-typed message.

See [the upstream Verovio toolkit reference](https://book.verovio.org/toolkit-reference/toolkit-methods.html)
for the underlying method surface this adapter wraps.

## Contributing

Issues and pull requests welcome. Please:

* Keep the public surface backend-agnostic (no Wasm- or Native-specific
  types leaking out of `Verovio.NET`'s public namespace).
* Run `dotnet fantomas .` on changed F# files before committing.
* Add Expecto coverage for new public-API behaviour under
  `src/Verovio.NET.Tests/`.
* CI must pass on Windows and Linux runners.

## License

Verovio.NET is licensed under the [Apache License 2.0](LICENSE).

The upstream Verovio engraver is licensed under the
[LGPL-3.0](https://github.com/rism-digital/verovio/blob/develop/LICENSE.txt);
the Wasm backend hosts the upstream WASM blob through a process boundary
(Wasmtime), and the (future) Native backend will use P/Invoke. The adapter
code in this repo is independent original work and is offered under the
Apache 2.0 terms.
