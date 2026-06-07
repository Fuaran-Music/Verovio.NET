module Verovio.NET.Samples.Console.Program

open System
open System.IO
open System.Xml
open Verovio.NET

// ============================================================================
//  Sample console — end-to-end smoke for the public Verovio.NET API.
//
//  Loads the c-major.mei fixture and exercises one method from each
//  binding domain so OSS consumers have a concrete "what does this
//  look like in F#?" reference: SVG render → MEI round-trip → MIDI →
//  Humdrum → timemap → options introspection → element query → edit
//  scaffold. Each section prints a one-line summary; the program
//  exits 0 on success.
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

let private head (s: string) (n: int) : string =
    if s.Length <= n then
        s.Replace("\n", " ").Replace("\r", "")
    else
        s.Substring(0, n).Replace("\n", " ").Replace("\r", "") + "…"

let private runSmoke (args: {| WriteSvg: bool |}) : int =
    printfn "Verovio.NET — Phase 49 sample console (full c_wrapper coverage)"
    printfn "==============================================================="
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
            printfn "  Default xml:id seed: %d" Determinism.DefaultXmlIdSeed
            printfn ""

            printfn "── 1. SVG render ─────────────────────────────────────────────"
            toolkit.LoadDataOrThrow(mei)
            let svg = toolkit.RenderToSvgOrThrow(1)
            printfn "  RenderToSvg(1) → %d bytes" svg.Length

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

                printfn "── 2. MEI round-trip ─────────────────────────────────────────"
                let meiOut = toolkit.GetMeiOrThrow()
                printfn "  GetMei → %d bytes, preview: %s" meiOut.Length (head meiOut 80)
                printfn ""

                printfn "── 3. MIDI render ────────────────────────────────────────────"
                let midi = toolkit.RenderToMidiOrThrow()

                printfn
                    "  RenderToMidi → %d bytes (MThd header: 0x%02X%02X%02X%02X)"
                    midi.Length
                    midi[0]
                    midi[1]
                    midi[2]
                    midi[3]

                printfn ""

                printfn "── 4. Humdrum round-trip ────────────────────────────────────"
                let krn = toolkit.GetHumdrumOrThrow()
                printfn "  GetHumdrum → %d bytes, preview: %s" krn.Length (head krn 80)
                printfn ""

                printfn "── 5. Timemap (typed) ───────────────────────────────────────"
                let timemap = toolkit.RenderToTimemapOrThrow()
                printfn "  RenderToTimemap → %d entries" timemap.Entries.Length

                if timemap.Entries.Length > 0 then
                    let first = timemap.Entries[0]

                    printfn
                        "  First event: realTimeMs=%.1f, quarter=%.2f, notesOn=%d, notesOff=%d"
                        first.RealTimeMs
                        first.ScoreTimeQuarter
                        first.NotesOn.Length
                        first.NotesOff.Length

                printfn ""

                printfn "── 6. Options introspection ─────────────────────────────────"
                let avail = toolkit.GetAvailableOptions()
                let defaults = toolkit.GetDefaultOptions()
                let usage = toolkit.GetOptionUsageString()
                printfn "  GetAvailableOptions → %d bytes" avail.Length
                printfn "  GetDefaultOptions   → %d bytes" defaults.Length
                printfn "  GetOptionUsageString → %d bytes" usage.Length
                printfn ""

                printfn "── 7. Element queries ───────────────────────────────────────"

                match toolkit.GetPageWithElement("no-such-id") with
                | Error(RenderError.ElementNotFound _) ->
                    printfn "  GetPageWithElement(\"no-such-id\") → ElementNotFound (expected)"
                | other -> printfn "  GetPageWithElement(\"no-such-id\") → %A" other

                let descriptive =
                    match toolkit.GetDescriptiveFeatures("{}") with
                    | Ok j -> j
                    | Error e -> sprintf "(error: %A)" e

                printfn "  GetDescriptiveFeatures → %d bytes, preview: %s" descriptive.Length (head descriptive 80)
                printfn ""

                printfn "── 8. Editor surface (raw passthrough) ──────────────────────"
                let info = toolkit.EditInfo()
                printfn "  EditInfo (no prior edit): %s" (head info 80)
                // Demonstrate the error path without crashing the smoke
                let bogus = EditorAction.FromRawJson("""{"action":"unknown","param":{}}""")

                match toolkit.Edit(bogus) with
                | Error(RenderError.InvalidEditorAction _)
                | Error(RenderError.BackendError _) -> printfn "  Edit(unknown action) → rejected as expected"
                | Ok() -> printfn "  Edit(unknown action) → Ok (Verovio accepted it)"
                | Error e -> printfn "  Edit(unknown action) → unexpected error %A" e

                printfn ""

                printfn "── 9. Logging (process-global toggles) ──────────────────────"
                VerovioLogging.EnableBuffer(true)
                let log = toolkit.DrainLog()
                printfn "  DrainLog returned %d bytes (buffer mode enabled)" log.Length
                VerovioLogging.EnableBuffer(false)
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
                    eprintfn "Toolkit raised VerovioException with inner LoadError %A" other
                    1
            | other ->
                eprintfn "VerovioException raised with unexpected inner error: %A" other
                1

[<EntryPoint>]
let main argv = runSmoke (parseArgs argv)
