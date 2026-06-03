module Verovio.NET.Samples.Console.Program

open System
open System.IO
open Verovio.NET
open Verovio.NET.Wasm

// ============================================================================
//  Sample console — smoke-tests the public API shape.
//
//  At Phase 03 the Wasm backend is a stub that throws
//  NotImplementedException on every operation. The sample exercises the
//  end-to-end shape (instantiate backend -> wrap in Toolkit -> attempt
//  to load fixture -> attempt to render -> catch the documented
//  NotImplementedException), proves the public API surface compiles and
//  links cleanly, then exits 0.
//
//  Phase 04 swaps the stub for a real Wasmtime-hosted backend without
//  changing this file; the sample at Phase 04 asserts non-empty SVG
//  output instead of catching the exception.
// ============================================================================

let private fixturePath () =
    Path.Combine(AppContext.BaseDirectory, "fixtures", "c-major.mei")

let private runSmoke () =
    printfn "Verovio.NET — Phase 03 sample console smoke"
    printfn "==========================================="
    printfn ""

    let fixture = fixturePath ()
    printfn "Loading fixture: %s" fixture

    if not (File.Exists fixture) then
        eprintfn "  FIXTURE MISSING — expected %s" fixture
        eprintfn "  (Build output should copy samples/Verovio.NET.Samples.Console/fixtures/* — check the fsproj.)"
        1
    else
        let mei = File.ReadAllText fixture
        printfn "  Loaded %d bytes of MEI" mei.Length
        printfn ""

        let backend = WasmBackend.create ()
        printfn "Backend: %s" backend.Name
        use toolkit = Verovio.create backend
        printfn ""

        printfn "Calling Verovio.loadData ..."

        try
            let loadResult = Verovio.loadData toolkit LoadOptions.Default mei

            match loadResult with
            | Ok() -> printfn "  loadData returned Ok ()."
            | Error err ->
                printfn
                    "  loadData returned Error %A — expected at Phase 04 once the backend rejects malformed input."
                    err

            printfn ""
            printfn "Calling Verovio.renderToSvg ..."

            match Verovio.renderToSvg toolkit RenderOptions.Default 1 with
            | Ok svg ->
                printfn "  renderToSvg returned %d bytes of SVG — Phase 04 has shipped! (Was this expected?)" svg.Length
            | Error err ->
                printfn
                    "  renderToSvg returned Error %A — expected at Phase 04 once the backend rejects renders without loaded docs."
                    err
        with :? NotImplementedException as ex ->
            printfn "  Caught documented NotImplementedException — correct behaviour at Phase 03."
            printfn ""
            printfn "  Backend message:"

            for line in ex.Message.Split([| ". " |], StringSplitOptions.RemoveEmptyEntries) do
                printfn "    %s." line

        printfn ""
        printfn "Phase 04 (roadmap/phases/04-verovio-net-wasm-backend.md) wires this end-to-end."
        printfn ""
        printfn "✓ Public API shape smoke-tests cleanly."
        0

[<EntryPoint>]
let main _argv = runSmoke ()
