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
//  Phase 04's native pivot replaces the Phase 03 stub-backend suite
//  (NotImplementedException assertions) with real-render assertions.
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

          skipUnlessNative "GetMidiValuesForElement rejects empty elementId" (fun () ->
              use toolkit = Toolkit.Create()
              let r = toolkit.GetMidiValuesForElement("")

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

          skipUnlessNative "RenderToPdf returns UnsupportedOutputFormat (deferred at Phase 04)" (fun () ->
              use toolkit = Toolkit.Create()
              let mei = readFixture "c-major.mei"
              toolkit.LoadData(mei) |> ignore

              match toolkit.RenderToPdf() with
              | Error(RenderError.UnsupportedOutputFormat OutputFormat.Pdf) -> ()
              | other -> failtestf "expected UnsupportedOutputFormat Pdf; got %A" other)

          skipUnlessNative "Two Toolkits render the same MEI to identical SVG" (fun () ->
              use toolkit1 = Toolkit.Create()
              use toolkit2 = Toolkit.Create()
              let mei = readFixture "c-major.mei"
              toolkit1.LoadData(mei) |> ignore
              toolkit2.LoadData(mei) |> ignore

              match toolkit1.RenderToSvg(1), toolkit2.RenderToSvg(1) with
              | Ok svg1, Ok svg2 -> Expect.equal svg1 svg2 "Identical input must yield identical SVG"
              | r1, r2 -> failtestf "expected two Ok renders; got %A and %A" r1 r2) ]

let private disposalTests =
    testList
        "Toolkit disposal"
        [ skipUnlessNative "Disposed toolkit throws ObjectDisposedException on use" (fun () ->
              let toolkit = Toolkit.Create()
              (toolkit :> IDisposable).Dispose()

              Expect.throwsT<ObjectDisposedException>
                  (fun () -> toolkit.LoadData("<mei/>") |> ignore)
                  "Using a disposed toolkit must throw ObjectDisposedException")

          skipUnlessNative "`use` binding disposes on scope exit" (fun () ->
              do
                  use _toolkit = Toolkit.Create()
                  ()) ]

[<Tests>]
let allVerovioTests =
    testList "Verovio" [ nativeProbeTests; inputValidationTests; renderingTests; disposalTests ]
