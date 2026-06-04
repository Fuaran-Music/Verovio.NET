module Verovio.NET.Samples.Console.Program

open System
open System.IO
open System.Xml
open Verovio.NET

// ============================================================================
//  Sample console — end-to-end smoke for the public Verovio.NET API.
//
//  Loads the c-major.mei fixture, asserts the SVG renders without
//  exception, and validates the output is well-formed XML. Optional
//  `--write-svg` flag writes the rendered SVG to disk under output/
//  for visual verification.
//
//  Failure modes are distinguished:
//    * libverovio.dll missing — Verovio.NET raises VerovioException with
//      inner LoadError.NativeLibraryUnavailable. The sample reports this
//      cleanly so the operator knows to install the vendored DLL.
//    * Load failure — Verovio rejected the MEI input. Should not happen
//      on the bundled fixture; if it does, the fixture is malformed.
//    * Render failure — Verovio raised an internal error. Should not
//      happen on a 2-measure scale; if it does, libverovio is broken.
//
//  Exit code is 0 on success, non-zero on any failure other than
//  NativeLibraryUnavailable (which exits 2 to signal "ship vendored DLL
//  first").
// ============================================================================

let private fixturePath () =
    Path.Combine(AppContext.BaseDirectory, "fixtures", "c-major.mei")

let private parseArgs (argv: string[]) =
    {| WriteSvg = Array.contains "--write-svg" argv |}

let private validateXml (svg: string) : Result<unit, string> =
    try
        let settings = XmlReaderSettings()
        settings.DtdProcessing <- DtdProcessing.Ignore
        use sr = new StringReader(svg)
        use reader = XmlReader.Create(sr, settings)

        while reader.Read() do
            ()

        Ok()
    with ex ->
        Error(sprintf "XML parse failed: %s" ex.Message)

let private runSmoke (args: {| WriteSvg: bool |}) : int =
    printfn "Verovio.NET — Phase 04 sample console (native backend)"
    printfn "======================================================"
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

        try
            use toolkit = Toolkit.Create()
            printfn "Toolkit constructed. Verovio version: %s" toolkit.Version
            printfn ""

            printfn "Calling toolkit.LoadData ..."

            match toolkit.LoadData(mei) with
            | Error err ->
                eprintfn "  loadData returned Error %A" err
                1
            | Ok() ->
                printfn "  loadData returned Ok ()."
                printfn ""

                printfn "Calling toolkit.RenderToSvg(1) ..."

                match toolkit.RenderToSvg(1) with
                | Error err ->
                    eprintfn "  renderToSvg returned Error %A" err
                    1
                | Ok svg ->
                    printfn "  renderToSvg returned %d bytes of SVG" svg.Length

                    match validateXml svg with
                    | Error msg ->
                        eprintfn "  SVG is not well-formed XML: %s" msg
                        1
                    | Ok() ->
                        printfn "  SVG parses as well-formed XML ✓"

                        if args.WriteSvg then
                            let outDir = Path.Combine(AppContext.BaseDirectory, "output")
                            Directory.CreateDirectory outDir |> ignore
                            let outPath = Path.Combine(outDir, "c-major.svg")
                            File.WriteAllText(outPath, svg)
                            printfn "  Wrote SVG to %s" outPath

                        printfn ""
                        printfn "✓ Sample console smoke passed."
                        0
        with :? VerovioException as ex ->
            match ex.InnerError with
            | :? LoadError as loadErr ->
                match loadErr with
                | LoadError.NativeLibraryUnavailable msg ->
                    eprintfn "libverovio.dll is not loadable on this RID:"
                    eprintfn "  %s" msg
                    eprintfn ""
                    eprintfn "Vendored DLLs ship at runtimes/<rid>/native/libverovio.dll."
                    eprintfn "Build via the upstream verovio CMake — see"
                    eprintfn "src/Verovio.NET/runtimes/win-x64/native/PROVENANCE.md."
                    2
                | other ->
                    eprintfn "Toolkit.Create raised VerovioException with inner LoadError %A" other
                    1
            | other ->
                eprintfn "VerovioException raised with unexpected inner error: %A" other
                1

[<EntryPoint>]
let main argv = runSmoke (parseArgs argv)
