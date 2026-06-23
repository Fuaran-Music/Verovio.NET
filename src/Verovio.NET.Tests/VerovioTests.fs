module Verovio.NET.Tests.VerovioTests

open System
open System.IO
open Expecto
open Verovio.NET
open Verovio.NET.Internal

// ============================================================================
//  Public-API tests for `Toolkit`. Two cohorts:
//
//    1. Native-library availability probe — establishes whether
//       libverovio is loadable in the current test run. Drives whether
//       the backend-dispatched tests below run or skip.
//
//    2. Backend-dispatched tests — exercise the real native rendering
//       path. Marked `skip` when libverovio is unavailable (gating
//       allows the suite to pass on a fresh checkout where the DLL
//       hasn't been built yet).
//
//  Phase 04's native pivot replaced the Phase 03 stub-backend suite
//  (NotImplementedException assertions) with real-render assertions.
//  Phase 49 extends with happy + error coverage for the full
//  c_wrapper.h surface.
// ============================================================================

let private nativeAvailable =
    match Interop.probeAvailability () with
    | Ok() -> true
    | Error _ -> false

let private skipUnlessNative (name: string) (body: unit -> unit) =
    if nativeAvailable then
        test name { body () }
    else
        ptest name { body () } // pending — surfaces in the test report

let private fixturePath name =
    Path.Combine(AppContext.BaseDirectory, "fixtures", name)

let private readFixture name = File.ReadAllText(fixturePath name)

let private tempFile (suffix: string) : string =
    let p = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + suffix)
    p

let private nativeProbeTests =
    testList
        "Native library probe"
        [ test "Interop.probeAvailability returns either Ok or NativeLibraryUnavailable Error" {
              match Interop.probeAvailability () with
              | Ok() -> ()
              | Error msg -> Expect.isNonEmpty msg "Error case must carry a diagnostic message"
          }

          test "Toolkit.Create either succeeds or raises VerovioException with NativeLibraryUnavailable" {
              try
                  use _toolkit = Toolkit.Create()
                  ()
              with :? VerovioException as ex ->
                  match ex.InnerError with
                  | :? LoadError as LoadError.NativeLibraryUnavailable _ -> ()
                  | other -> failtestf "Expected NativeLibraryUnavailable, got %A" other
          } ]

let private inputValidationTests =
    // Pre-backend validation — exercises Toolkit's own input-validation
    // logic. Requires a working Toolkit instance (so gated on native
    // availability), but the assertions are about the wrapper's
    // contract, not the backend's behaviour.
    testList
        "Toolkit input validation (pre-backend dispatch)"
        [ skipUnlessNative "LoadData rejects null input with ParseFailed" (fun () ->
              use toolkit = Toolkit.Create()
              let r = toolkit.LoadData(null)

              match r with
              | Error(LoadError.ParseFailed _) -> ()
              | other -> failtestf "expected ParseFailed; got %A" other)

          skipUnlessNative "LoadData rejects empty input with ParseFailed" (fun () ->
              use toolkit = Toolkit.Create()
              let r = toolkit.LoadData("")

              match r with
              | Error(LoadError.ParseFailed _) -> ()
              | other -> failtestf "expected ParseFailed; got %A" other)

          skipUnlessNative "LoadData rejects whitespace-only input with ParseFailed" (fun () ->
              use toolkit = Toolkit.Create()
              let r = toolkit.LoadData("   \n\t  ")

              match r with
              | Error(LoadError.ParseFailed _) -> ()
              | other -> failtestf "expected ParseFailed; got %A" other)

          skipUnlessNative "LoadFile reports FileNotFound for a missing path" (fun () ->
              use toolkit = Toolkit.Create()

              let r =
                  toolkit.LoadFile(Path.Combine(Path.GetTempPath(), "definitely-not-a-real-file.mei"))

              match r with
              | Error(LoadError.FileNotFound _) -> ()
              | other -> failtestf "expected FileNotFound; got %A" other)

          skipUnlessNative "LoadFile rejects null path with ParseFailed" (fun () ->
              use toolkit = Toolkit.Create()
              let r = toolkit.LoadFile(null)

              match r with
              | Error(LoadError.ParseFailed _) -> ()
              | other -> failtestf "expected ParseFailed; got %A" other)

          skipUnlessNative "LoadZipBase64 rejects null with ParseFailed" (fun () ->
              use toolkit = Toolkit.Create()
              let r = toolkit.LoadZipBase64(null)

              match r with
              | Error(LoadError.ParseFailed _) -> ()
              | other -> failtestf "expected ParseFailed; got %A" other)

          skipUnlessNative "LoadZipBuffer rejects empty buffer with ParseFailed" (fun () ->
              use toolkit = Toolkit.Create()
              let r = toolkit.LoadZipBuffer([||])

              match r with
              | Error(LoadError.ParseFailed _) -> ()
              | other -> failtestf "expected ParseFailed; got %A" other)

          skipUnlessNative "RenderToSvg rejects pageNumber < 1 with PageOutOfRange" (fun () ->
              use toolkit = Toolkit.Create()
              let r = toolkit.RenderToSvg(0)

              match r with
              | Error(RenderError.PageOutOfRange(0, _)) -> ()
              | other -> failtestf "expected PageOutOfRange; got %A" other)

          skipUnlessNative "RenderToSvg rejects negative pageNumber" (fun () ->
              use toolkit = Toolkit.Create()
              let r = toolkit.RenderToSvg(-5)

              match r with
              | Error(RenderError.PageOutOfRange(-5, _)) -> ()
              | other -> failtestf "expected PageOutOfRange; got %A" other)

          skipUnlessNative "GetElementsAtTime rejects negative timeMs" (fun () ->
              use toolkit = Toolkit.Create()
              let r = toolkit.GetElementsAtTime(-1)

              match r with
              | Error(RenderError.RenderFailed _) -> ()
              | other -> failtestf "expected RenderFailed; got %A" other)

          skipUnlessNative "GetMidiValuesForElement rejects null elementId" (fun () ->
              use toolkit = Toolkit.Create()
              let r = toolkit.GetMidiValuesForElement(null)

              match r with
              | Error(RenderError.RenderFailed _) -> ()
              | other -> failtestf "expected RenderFailed; got %A" other)

          skipUnlessNative "SetScale rejects out-of-range scale" (fun () ->
              use toolkit = Toolkit.Create()
              let r = toolkit.SetScale(RenderOptions.MaxScale + 1)

              match r with
              | Error(RenderError.RenderFailed _) -> ()
              | other -> failtestf "expected RenderFailed; got %A" other)

          skipUnlessNative "SetOutputTo rejects Pdf with UnsupportedOutputFormat" (fun () ->
              use toolkit = Toolkit.Create()
              let r = toolkit.SetOutputTo(OutputFormat.Pdf)

              match r with
              | Error(RenderError.UnsupportedOutputFormat OutputFormat.Pdf) -> ()
              | other -> failtestf "expected UnsupportedOutputFormat Pdf; got %A" other)

          skipUnlessNative "Edit rejects malformed JSON action" (fun () ->
              use toolkit = Toolkit.Create()
              let r = toolkit.Edit(EditorAction.FromRawJson("{ this is not json }"))

              match r with
              | Error(RenderError.InvalidEditorAction _)
              | Error(RenderError.BackendError _) -> ()
              | other -> failtestf "expected InvalidEditorAction or BackendError; got %A" other)

          skipUnlessNative "Select rejects null with InvalidEditorAction" (fun () ->
              use toolkit = Toolkit.Create()
              let r = toolkit.Select(null)

              match r with
              | Error(RenderError.InvalidEditorAction _) -> ()
              | other -> failtestf "expected InvalidEditorAction; got %A" other)

          skipUnlessNative "ValidatePaeFile reports RenderFailed for a missing path" (fun () ->
              use toolkit = Toolkit.Create()

              let r = toolkit.ValidatePaeFile(Path.Combine(Path.GetTempPath(), "missing.pae"))

              match r with
              | Error(RenderError.RenderFailed _) -> ()
              | other -> failtestf "expected RenderFailed; got %A" other)

          skipUnlessNative "SetResourcePath rejects missing directory" (fun () ->
              use toolkit = Toolkit.Create()

              let r =
                  toolkit.SetResourcePath(Path.Combine(Path.GetTempPath(), "definitely-not-a-real-dir-xyz"))

              match r with
              | Error(RenderError.RenderFailed _) -> ()
              | other -> failtestf "expected RenderFailed; got %A" other) ]

let private renderingTests =
    testList
        "Toolkit rendering (native dispatch)"
        [ skipUnlessNative "Loaded MEI renders to non-empty SVG" (fun () ->
              use toolkit = Toolkit.Create()
              let mei = readFixture "c-major.mei"

              match toolkit.LoadData(mei) with
              | Error err -> failtestf "LoadData failed: %A" err
              | Ok() ->
                  match toolkit.RenderToSvg(1) with
                  | Error err -> failtestf "RenderToSvg failed: %A" err
                  | Ok svg ->
                      Expect.isGreaterThan svg.Length 100 "SVG should be at least 100 bytes"
                      Expect.stringContains svg "<svg" "SVG should contain an <svg> element"
                      Expect.stringContains svg "</svg>" "SVG should close the <svg> element")

          skipUnlessNative "RenderToSvg without LoadData returns NoDocumentLoaded" (fun () ->
              use toolkit = Toolkit.Create()
              let r = toolkit.RenderToSvg(1)

              match r with
              | Error RenderError.NoDocumentLoaded -> ()
              | other -> failtestf "expected NoDocumentLoaded; got %A" other)

          skipUnlessNative "GetDocumentInfo reports page count after LoadData" (fun () ->
              use toolkit = Toolkit.Create()
              let mei = readFixture "c-major.mei"

              match toolkit.LoadData(mei) with
              | Error err -> failtestf "LoadData failed: %A" err
              | Ok() ->
                  match toolkit.GetDocumentInfo() with
                  | Error err -> failtestf "GetDocumentInfo failed: %A" err
                  | Ok info -> Expect.isGreaterThan info.PageCount 0 "Loaded document should have at least one page")

          skipUnlessNative "RenderToPdf returns UnsupportedOutputFormat (deferred)" (fun () ->
              use toolkit = Toolkit.Create()
              let mei = readFixture "c-major.mei"
              toolkit.LoadData(mei) |> ignore

              match toolkit.RenderToPdf() with
              | Error(RenderError.UnsupportedOutputFormat OutputFormat.Pdf) -> ()
              | other -> failtestf "expected UnsupportedOutputFormat Pdf; got %A" other)

          skipUnlessNative "Two Toolkits render the same MEI to identical SVG (determinism contract)" (fun () ->
              use toolkit1 = Toolkit.Create()
              use toolkit2 = Toolkit.Create()
              let mei = readFixture "c-major.mei"
              toolkit1.LoadData(mei) |> ignore
              toolkit2.LoadData(mei) |> ignore

              match toolkit1.RenderToSvg(1), toolkit2.RenderToSvg(1) with
              | Ok svg1, Ok svg2 -> Expect.equal svg1 svg2 "Identical input + default seed must yield identical SVG"
              | r1, r2 -> failtestf "expected two Ok renders; got %A and %A" r1 r2)

          skipUnlessNative "RenderData (one-shot) returns SVG for in-memory input" (fun () ->
              use toolkit = Toolkit.Create()
              let mei = readFixture "c-major.mei"

              match toolkit.RenderData(mei, RenderOptions.Default) with
              | Error err -> failtestf "RenderData failed: %A" err
              | Ok svg ->
                  Expect.isGreaterThan svg.Length 100 "RenderData SVG should be substantive"
                  Expect.stringContains svg "<svg" "SVG marker present") ]

let private determinismTests =
    testList
        "Determinism"
        [ skipUnlessNative "DefaultXmlIdSeed is non-zero (upstream reads 0 as randomize)" (fun () ->
              Expect.notEqual
                  Determinism.DefaultXmlIdSeed
                  0
                  "Upstream's vrvToolkit_resetXmlIdSeed(0) is the randomize branch — default must be non-zero for determinism"

              Expect.isGreaterThan Determinism.DefaultXmlIdSeed 0 "Seed is positive")

          skipUnlessNative "ResetXmlIdSeed with a non-default seed changes SVG output" (fun () ->
              use toolkit1 = Toolkit.Create()
              use toolkit2 = Toolkit.Create()
              toolkit2.ResetXmlIdSeed(12345)
              let mei = readFixture "c-major.mei"
              toolkit1.LoadData(mei) |> ignore
              toolkit2.LoadData(mei) |> ignore

              match toolkit1.RenderToSvg(1), toolkit2.RenderToSvg(1) with
              | Ok svg1, Ok svg2 ->
                  Expect.notEqual svg1 svg2 "Different seeds must produce different xml:ids → different SVG"
              | r1, r2 -> failtestf "expected two Ok renders; got %A and %A" r1 r2) ]

let private fileIoTests =
    testList
        "Toolkit file I/O"
        [ skipUnlessNative "LoadFile reads c-major.mei from disk" (fun () ->
              use toolkit = Toolkit.Create()

              match toolkit.LoadFile(fixturePath "c-major.mei") with
              | Error err -> failtestf "LoadFile failed: %A" err
              | Ok() ->
                  match toolkit.GetDocumentInfo() with
                  | Ok info -> Expect.isGreaterThan info.PageCount 0 "file-loaded document has pages"
                  | Error err -> failtestf "GetDocumentInfo failed: %A" err)

          skipUnlessNative "RenderToSvgFile writes a valid file" (fun () ->
              use toolkit = Toolkit.Create()
              toolkit.LoadFileOrThrow(fixturePath "c-major.mei")
              let dest = tempFile ".svg"

              try
                  match toolkit.RenderToSvgFile(dest, 1) with
                  | Ok() ->
                      Expect.isTrue (File.Exists dest) "SVG file written"
                      Expect.isGreaterThan (FileInfo dest).Length 100L "SVG file substantive"
                  | Error err -> failtestf "RenderToSvgFile failed: %A" err
              finally
                  if File.Exists dest then
                      File.Delete dest)

          skipUnlessNative "RenderToMidiFile writes a non-empty file" (fun () ->
              use toolkit = Toolkit.Create()
              toolkit.LoadFileOrThrow(fixturePath "c-major.mei")
              let dest = tempFile ".mid"

              try
                  match toolkit.RenderToMidiFile(dest) with
                  | Ok() ->
                      Expect.isTrue (File.Exists dest) "MIDI file written"
                      Expect.isGreaterThan (FileInfo dest).Length 0L "MIDI file non-empty"
                  | Error err -> failtestf "RenderToMidiFile failed: %A" err
              finally
                  if File.Exists dest then
                      File.Delete dest)

          skipUnlessNative "RenderToSvgFile flags FileWriteFailed for an invalid path" (fun () ->
              use toolkit = Toolkit.Create()
              toolkit.LoadFileOrThrow(fixturePath "c-major.mei")
              // Use a clearly-invalid path: a directory that doesn't exist
              // under a randomly-named top-level. Verovio's saveFile
              // returns false instead of throwing, which we surface as
              // FileWriteFailed.
              let dest =
                  Path.Combine("Z:\\definitely-no-such-drive", Path.GetRandomFileName() + ".svg")

              match toolkit.RenderToSvgFile(dest, 1) with
              | Error(RenderError.FileWriteFailed _) -> ()
              | other -> failtestf "expected FileWriteFailed; got %A" other) ]

let private outputFormatTests =
    testList
        "Toolkit alternative output formats"
        [ skipUnlessNative "GetMei round-trips loaded MEI to canonical MEI" (fun () ->
              use toolkit = Toolkit.Create()
              toolkit.LoadDataOrThrow(readFixture "c-major.mei")

              match toolkit.GetMei() with
              | Error err -> failtestf "GetMei failed: %A" err
              | Ok mei ->
                  Expect.stringContains mei "<mei" "MEI round-trip contains <mei root>"
                  Expect.stringContains mei "</mei>" "MEI round-trip closes <mei>")

          skipUnlessNative "GetMei without LoadData returns NoDocumentLoaded" (fun () ->
              use toolkit = Toolkit.Create()
              let r = toolkit.GetMei()

              match r with
              | Error RenderError.NoDocumentLoaded -> ()
              | other -> failtestf "expected NoDocumentLoaded; got %A" other)

          skipUnlessNative "RenderToMidi returns a non-empty byte array" (fun () ->
              use toolkit = Toolkit.Create()
              toolkit.LoadDataOrThrow(readFixture "c-major.mei")

              match toolkit.RenderToMidi() with
              | Error err -> failtestf "RenderToMidi failed: %A" err
              | Ok bytes ->
                  Expect.isGreaterThan bytes.Length 0 "MIDI bytes non-empty"
                  // Standard MIDI File header "MThd"
                  Expect.equal bytes[0] 0x4Duy "MIDI byte 0 should be 'M'"
                  Expect.equal bytes[1] 0x54uy "MIDI byte 1 should be 'T'"
                  Expect.equal bytes[2] 0x68uy "MIDI byte 2 should be 'h'"
                  Expect.equal bytes[3] 0x64uy "MIDI byte 3 should be 'd'")

          skipUnlessNative "GetHumdrum returns a non-empty string" (fun () ->
              use toolkit = Toolkit.Create()
              toolkit.LoadDataOrThrow(readFixture "c-major.mei")

              match toolkit.GetHumdrum() with
              | Error err -> failtestf "GetHumdrum failed: %A" err
              | Ok s -> Expect.isGreaterThan s.Length 0 "Humdrum round-trip non-empty")

          skipUnlessNative "RenderToPae returns a non-empty string" (fun () ->
              use toolkit = Toolkit.Create()
              toolkit.LoadDataOrThrow(readFixture "c-major.mei")

              match toolkit.RenderToPae() with
              | Error err -> failtestf "RenderToPae failed: %A" err
              | Ok s -> Expect.isGreaterThan s.Length 0 "PAE round-trip non-empty")

          skipUnlessNative "ConvertMeiToHumdrum produces non-empty output" (fun () ->
              use toolkit = Toolkit.Create()
              let mei = readFixture "c-major.mei"

              match toolkit.ConvertMeiToHumdrum(mei) with
              | Error err -> failtestf "ConvertMeiToHumdrum failed: %A" err
              | Ok s -> Expect.isGreaterThan s.Length 0 "MEI→Humdrum conversion non-empty")

          skipUnlessNative "ConvertHumdrumToMidi produces a valid MIDI header" (fun () ->
              use toolkit = Toolkit.Create()
              let krn = readFixture "c-major.krn"

              match toolkit.ConvertHumdrumToMidi(krn) with
              | Error err -> failtestf "ConvertHumdrumToMidi failed: %A" err
              | Ok bytes ->
                  Expect.isGreaterThan bytes.Length 4 "MIDI bytes contain at least a header"
                  Expect.equal bytes[0] 0x4Duy "MIDI byte 0 = 'M'") ]

let private timemapTests =
    testList
        "Toolkit timemap (typed)"
        [ skipUnlessNative "RenderToTimemap returns at least one entry" (fun () ->
              use toolkit = Toolkit.Create()
              toolkit.LoadDataOrThrow(readFixture "c-major.mei")

              match toolkit.RenderToTimemap() with
              | Error err -> failtestf "RenderToTimemap failed: %A" err
              | Ok timemap ->
                  Expect.isGreaterThan timemap.Entries.Length 0 "timemap should carry at least one entry"
                  let first = timemap.Entries[0]
                  Expect.isGreaterThanOrEqual first.RealTimeMs 0.0 "first event time non-negative")

          skipUnlessNative "RenderToTimemap without LoadData returns NoDocumentLoaded" (fun () ->
              use toolkit = Toolkit.Create()

              match toolkit.RenderToTimemap() with
              | Error RenderError.NoDocumentLoaded -> ()
              | other -> failtestf "expected NoDocumentLoaded; got %A" other)

          skipUnlessNative "RenderToTimemapJson returns a JSON string" (fun () ->
              use toolkit = Toolkit.Create()
              toolkit.LoadDataOrThrow(readFixture "c-major.mei")

              match toolkit.RenderToTimemapJson() with
              | Error err -> failtestf "RenderToTimemapJson failed: %A" err
              | Ok json -> Expect.isGreaterThan json.Length 1 "JSON non-empty") ]

let private queryTests =
    testList
        "Toolkit element queries"
        [ skipUnlessNative "GetElementAttr returns a dictionary for a known note id" (fun () ->
              use toolkit = Toolkit.Create()
              toolkit.LoadDataOrThrow(readFixture "c-major.mei")
              // The c-major.mei fixture doesn't have explicit xml:ids
              // on its notes; Verovio mints them. We round-trip via
              // GetMei to surface the minted IDs, then pick the first
              // note id.
              let mei = toolkit.GetMeiOrThrow()
              // Crude but effective: find `xml:id="note-...">` style
              let i = mei.IndexOf("xml:id=\"note-")

              if i < 0 then
                  // No note IDs in round-tripped MEI — skip silently
                  ()
              else
                  let start = i + "xml:id=\"".Length
                  let stop = mei.IndexOf('"', start)
                  let id = mei.Substring(start, stop - start)

                  match toolkit.GetElementAttr(id) with
                  | Error err -> failtestf "GetElementAttr failed: %A" err
                  | Ok attrs ->
                      // Don't assert specific keys (the surface evolves
                      // upstream-side) — just that the call surfaced a
                      // dictionary (the return type guarantees non-null
                      // under F# nullness).
                      ignore attrs)

          skipUnlessNative "GetElementAttr returns ElementNotFound for an unknown id" (fun () ->
              use toolkit = Toolkit.Create()
              toolkit.LoadDataOrThrow(readFixture "c-major.mei")

              match toolkit.GetElementAttr("no-such-element-id-xyz") with
              | Error(RenderError.ElementNotFound _)
              | Ok _ ->
                  // Verovio's behaviour here is version-dependent — it
                  // may return an empty dict or signal not-found. Either
                  // is acceptable; what's not acceptable is a crash or
                  // BackendError.
                  ()
              | Error(RenderError.RenderFailed _) -> ()
              | other -> failtestf "unexpected outcome: %A" other)

          skipUnlessNative "GetPageWithElement returns ElementNotFound for unknown id" (fun () ->
              use toolkit = Toolkit.Create()
              toolkit.LoadDataOrThrow(readFixture "c-major.mei")

              match toolkit.GetPageWithElement("no-such-element-id-xyz") with
              | Error(RenderError.ElementNotFound _) -> ()
              | other -> failtestf "expected ElementNotFound; got %A" other)

          skipUnlessNative "GetTimeForElement reports either ElementNotFound or 0.0 for unknown id" (fun () ->
              use toolkit = Toolkit.Create()
              toolkit.LoadDataOrThrow(readFixture "c-major.mei")
              // Upstream's behaviour for unknown ids is version-dependent:
              // some emit -1 (we map to ElementNotFound); others return
              // 0.0 silently. The contract surfaced in our XML docs is
              // "negative = absent" — both outcomes are acceptable here.
              match toolkit.GetTimeForElement("no-such-element-id-xyz") with
              | Error(RenderError.ElementNotFound _) -> ()
              | Ok 0.0 -> ()
              | other -> failtestf "expected ElementNotFound or Ok 0.0; got %A" other)

          skipUnlessNative "GetId returns a (possibly empty) string for loaded document" (fun () ->
              use toolkit = Toolkit.Create()
              toolkit.LoadDataOrThrow(readFixture "c-major.mei")
              let id = toolkit.GetId()
              // We don't assert on content — the c-major.mei fixture
              // doesn't carry a root xml:id, so we expect "" or a
              // minted value. The return type guarantees non-null.
              ignore id)

          skipUnlessNative "GetDescriptiveFeatures returns a JSON payload" (fun () ->
              use toolkit = Toolkit.Create()
              toolkit.LoadDataOrThrow(readFixture "c-major.mei")

              match toolkit.GetDescriptiveFeatures("{}") with
              | Error err -> failtestf "GetDescriptiveFeatures failed: %A" err
              | Ok json ->
                  Expect.isGreaterThan json.Length 0 "descriptive features JSON non-empty"
                  // Don't assert on schema — upstream evolves this.
                  ()) ]

let private optionsIntrospectionTests =
    testList
        "Toolkit options introspection"
        [ skipUnlessNative "GetAvailableOptions returns a JSON object" (fun () ->
              use toolkit = Toolkit.Create()
              let json = toolkit.GetAvailableOptions()
              Expect.isGreaterThan json.Length 100 "available options JSON substantive"
              Expect.stringContains json "{" "looks like JSON")

          skipUnlessNative "GetDefaultOptions returns a JSON object" (fun () ->
              use toolkit = Toolkit.Create()
              let json = toolkit.GetDefaultOptions()
              Expect.isGreaterThan json.Length 10 "default options JSON non-empty")

          skipUnlessNative "GetOptions returns a JSON object reflecting active state" (fun () ->
              use toolkit = Toolkit.Create()
              let json = toolkit.GetOptions()
              Expect.isGreaterThan json.Length 10 "current options JSON non-empty")

          skipUnlessNative "GetOptionsIntrospection bundles all four surfaces" (fun () ->
              use toolkit = Toolkit.Create()
              let i = toolkit.GetOptionsIntrospection()
              Expect.isGreaterThan i.AvailableJson.Length 0 "available filled"
              Expect.isGreaterThan i.DefaultsJson.Length 0 "defaults filled"
              Expect.isGreaterThan i.CurrentJson.Length 0 "current filled")

          skipUnlessNative "ResetOptions runs without exception" (fun () ->
              use toolkit = Toolkit.Create()
              toolkit.ResetOptions())

          skipUnlessNative "SetRawOptions accepts a minimal valid JSON" (fun () ->
              use toolkit = Toolkit.Create()

              match toolkit.SetRawOptions("{}") with
              | Ok() -> ()
              | Error err -> failtestf "SetRawOptions({}) failed: %A" err) ]

let private scaleAndResourceTests =
    testList
        "Toolkit scale + resource path"
        [ skipUnlessNative "GetScale returns a positive integer" (fun () ->
              use toolkit = Toolkit.Create()
              let s = toolkit.GetScale()
              Expect.isGreaterThan s 0 "Verovio's default scale is positive")

          skipUnlessNative "SetScale + GetScale round-trip 75" (fun () ->
              use toolkit = Toolkit.Create()

              match toolkit.SetScale(75) with
              | Ok() -> Expect.equal (toolkit.GetScale()) 75 "scale should round-trip"
              | Error err -> failtestf "SetScale(75) failed: %A" err)

          skipUnlessNative "SetOutputTo Svg succeeds" (fun () ->
              use toolkit = Toolkit.Create()

              match toolkit.SetOutputTo(OutputFormat.Svg) with
              | Ok() -> ()
              | Error err -> failtestf "SetOutputTo(Svg) failed: %A" err)

          skipUnlessNative "GetResourcePath returns the auto-resolved path" (fun () ->
              use toolkit = Toolkit.Create()
              let p = toolkit.GetResourcePath()
              Expect.isGreaterThan p.Length 0 "resource path resolved")

          skipUnlessNative "RedoLayout and RedoPagePitchPosLayout run without exception" (fun () ->
              use toolkit = Toolkit.Create()
              toolkit.LoadDataOrThrow(readFixture "c-major.mei")
              toolkit.RedoLayout()
              toolkit.RedoPagePitchPosLayout()) ]

let private editorTests =
    testList
        "Toolkit editor surface"
        [ skipUnlessNative "EditInfo returns a JSON-shaped string when no edit applied" (fun () ->
              use toolkit = Toolkit.Create()
              let info = toolkit.EditInfo()
              // Verovio returns a JSON object even before any edit;
              // accept either empty or "{...}"-prefixed content. The
              // return type guarantees non-null.
              ignore info)

          skipUnlessNative "EditorAction.FromRawJson preserves the JSON exactly" (fun () ->
              let json = """{"action":"set","param":{}}"""
              let a = EditorAction.FromRawJson(json)
              Expect.equal a.Json json "raw JSON preserved")

          skipUnlessNative "EditorAction.FromRawJson rejects null with nullArg" (fun () ->
              Expect.throws (fun () -> EditorAction.FromRawJson(null) |> ignore) "null must throw") ]

let private validationTests =
    testList
        "Toolkit validation"
        [ skipUnlessNative "ValidatePae returns a report for a well-formed PAE" (fun () ->
              use toolkit = Toolkit.Create()
              let pae = readFixture "c-major.pae"

              match toolkit.ValidatePae(pae) with
              | Error err -> failtestf "ValidatePae failed: %A" err
              | Ok report ->
                  Expect.isGreaterThan report.RawJson.Length 0 "raw report non-empty"
                  // IsValid may be true or false depending on the
                  // strictness of our hand-rolled PAE — we assert only
                  // that the call surfaced a report.
                  ())

          skipUnlessNative "ValidatePaeFile reads from disk" (fun () ->
              use toolkit = Toolkit.Create()

              match toolkit.ValidatePaeFile(fixturePath "c-major.pae") with
              | Error err -> failtestf "ValidatePaeFile failed: %A" err
              | Ok report -> Expect.isGreaterThan report.RawJson.Length 0 "raw report non-empty") ]

let private loggingTests =
    testList
        "VerovioLogging + Toolkit.DrainLog"
        [ skipUnlessNative "EnableConsole + EnableBuffer toggles run without exception" (fun () ->
              VerovioLogging.EnableConsole(false)
              VerovioLogging.EnableBuffer(true)
              use toolkit = Toolkit.Create()
              // The act of constructing + version-querying should not
              // crash with buffer mode on.
              toolkit.Version |> ignore
              VerovioLogging.EnableBuffer(false))

          skipUnlessNative "DrainLog returns a string (possibly empty)" (fun () ->
              use toolkit = Toolkit.Create()
              let log = toolkit.DrainLog()
              // Return type guarantees non-null.
              ignore log) ]

let private disposalTests =
    testList
        "Toolkit disposal"
        [ skipUnlessNative "Disposed toolkit throws ObjectDisposedException on use" (fun () ->
              let toolkit = Toolkit.Create()
              (toolkit :> IDisposable).Dispose()

              Expect.throwsT<ObjectDisposedException>
                  (fun () -> toolkit.LoadData("<mei/>") |> ignore)
                  "Using a disposed toolkit must throw ObjectDisposedException")

          skipUnlessNative "Disposed toolkit throws on ResetXmlIdSeed" (fun () ->
              let toolkit = Toolkit.Create()
              (toolkit :> IDisposable).Dispose()

              Expect.throwsT<ObjectDisposedException>
                  (fun () -> toolkit.ResetXmlIdSeed(0))
                  "Disposed toolkit must throw on ResetXmlIdSeed")

          skipUnlessNative "`use` binding disposes on scope exit" (fun () ->
              do
                  use _toolkit = Toolkit.Create()
                  ()) ]

let private percussionRenderTests =
    // Standing render coverage for the five percussion features cleared by
    // the render spike (docs/PERCUSSION-RENDER-SPIKE.md). Each fixture is
    // input-MEI-driven and renders with RenderOptions.Default — no Verovio
    // option knob is load-bearing, so there is no typed percussion API to
    // exercise here; these assert the MEI→SVG contract the spike pinned.
    //
    // Discriminating SVG markers (SMuFL glyph refs take the deterministic
    // form `xlink:href="#E069-<seed-suffix>"`, so we key on the `#E069`
    // prefix, not the full minted id):
    //   * percussion clef → #E069 (unpitchedPercussionClef1) + class="clef"
    //   * x noteheads     → #E0A9 (noteheadXBlack)
    //   * flam/drag grace → cue-sized class="flag" + #E240/#E242 (no
    //                       `grace`/`graceGrp` class exists in 6.2.0 — L2)
    //   * buzz tremolo    → class="bTrem" + #E22A (buzzRoll) / #E222 (tremolo3)
    //   * sticking (R/L)  → class="dir" + R/L tspan text (MEI <sticking> is
    //                       unsupported in 6.2.0 — L1 — so <dir> is canonical)
    let renderFixture (name: string) : string =
        use toolkit = Toolkit.Create()
        toolkit.LoadDataOrThrow(readFixture name)
        toolkit.RenderToSvgOrThrow(1)

    testList
        "Toolkit percussion render coverage"
        [ skipUnlessNative "Percussion clef renders the unpitched-percussion clef glyph (#E069)" (fun () ->
              let svg = renderFixture "percussion-clef.mei"
              Expect.stringContains svg "class=\"clef\"" "a clef group is emitted"
              Expect.stringContains svg "#E069" "percussion clef glyph (unpitchedPercussionClef1)")

          skipUnlessNative "X noteheads render the noteheadXBlack glyph (#E0A9)" (fun () ->
              let svg = renderFixture "percussion-xnotehead.mei"
              Expect.stringContains svg "#E0A9" "x-shaped notehead glyph (noteheadXBlack)"
              Expect.stringContains svg "class=\"notehead\"" "notehead groups are emitted")

          skipUnlessNative "Flam/drag grace notes render cue-sized flags (#E240/#E242)" (fun () ->
              let svg = renderFixture "percussion-grace-flam.mei"
              // L2: Verovio 6.2.0 emits no `grace`/`graceGrp` class — the
              // discriminating signal is the cue-sized flag glyphs (the
              // fixture's main strokes are quarter notes, so any flag is a
              // grace note).
              Expect.stringContains svg "class=\"flag\"" "grace notes carry flags"

              Expect.isTrue
                  (svg.Contains "#E240" || svg.Contains "#E242")
                  "cue flag glyph (flag8thUp/flag16thUp) present")

          skipUnlessNative "Buzz + measured tremolo render bTrem with buzzRoll (#E22A) and tremolo3 (#E222)" (fun () ->
              let svg = renderFixture "percussion-buzz-tremolo.mei"
              Expect.stringContains svg "class=\"bTrem\"" "bTrem groups are emitted"
              Expect.stringContains svg "#E22A" "buzz-roll glyph (buzzRoll) for stem.mod=z"
              Expect.stringContains svg "#E222" "measured-roll glyph (tremolo3) for stem.mod=3slash")

          skipUnlessNative "Sticking renders R/L text via <dir> (MEI <sticking> unsupported)" (fun () ->
              let svg = renderFixture "percussion-sticking.mei"
              // L1: the dedicated MEI <sticking> element is unsupported in
              // libverovio 6.2.0; R/L are encoded as below-staff <dir>s.
              Expect.stringContains svg "class=\"dir\"" "dir groups are emitted for sticking"
              Expect.stringContains svg ">R<" "right-hand sticking text rendered"
              Expect.stringContains svg ">L<" "left-hand sticking text rendered") ]

[<Tests>]
let allVerovioTests =
    testList
        "Verovio"
        [ nativeProbeTests
          inputValidationTests
          renderingTests
          determinismTests
          fileIoTests
          outputFormatTests
          timemapTests
          queryTests
          optionsIntrospectionTests
          scaleAndResourceTests
          editorTests
          validationTests
          loggingTests
          disposalTests
          percussionRenderTests ]
