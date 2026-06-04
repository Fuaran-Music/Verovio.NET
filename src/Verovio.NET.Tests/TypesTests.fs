module Verovio.NET.Tests.TypesTests

open Expecto
open Verovio.NET

// ============================================================================
//  Public-type smoke suite. Covers:
//    1. RenderOptions smart-ctor value-space projection — invalid
//       combinations are rejected at construction time.
//    2. RenderOptions.Default round-trips through `WithScale` /
//       `WithPageSize` cleanly.
//    3. LoadOptions / PdfOptions constructor coverage.
//    4. Closed-DU exhaustiveness — pattern-matching every
//       InputFormat / OutputFormat / PageOrientation case stays
//       exhaustive (compile-time, but a runtime assertion protects
//       against future additions that would silently widen the public
//       surface).
//
//  Phase 04 — static-member API surface (LoadOptions.Create etc.); the
//  Phase 03 module-function form was retired for C#-friendliness.
// ============================================================================

let private renderOptionsTests =
    testList
        "RenderOptions"
        [ test "Default is well-formed (matches Verovio's documented A4 100% defaults)" {
              let d = RenderOptions.Default
              Expect.equal d.PageWidth RenderOptions.DefaultPageWidth "PageWidth should match the documented default"
              Expect.equal d.PageHeight RenderOptions.DefaultPageHeight "PageHeight should match the documented default"
              Expect.equal d.Scale RenderOptions.DefaultScale "Scale should match the documented default"
              Expect.equal d.Orientation Portrait "Default orientation is Portrait"
              Expect.isTrue d.AdjustPageHeight "Default adjusts page height to content"
          }

          test "Create rejects non-positive pageWidth" {
              let r = RenderOptions.Create(0, 2970, 50, 50, 50, 50, 100, Portrait, true)
              Expect.isError r "pageWidth = 0 must fail"
          }

          test "Create rejects negative pageWidth" {
              let r = RenderOptions.Create(-1, 2970, 50, 50, 50, 50, 100, Portrait, true)
              Expect.isError r "pageWidth = -1 must fail"
          }

          test "Create rejects non-positive pageHeight" {
              let r = RenderOptions.Create(2100, 0, 50, 50, 50, 50, 100, Portrait, true)
              Expect.isError r "pageHeight = 0 must fail"
          }

          test "Create rejects negative margin" {
              let r = RenderOptions.Create(2100, 2970, -1, 50, 50, 50, 100, Portrait, true)
              Expect.isError r "pageMarginTop = -1 must fail"
          }

          test "Create accepts zero margin (touching the page edge is legal)" {
              let r = RenderOptions.Create(2100, 2970, 0, 0, 0, 0, 100, Portrait, true)
              Expect.isOk r "zero-margin renders are legitimate (full-bleed)"
          }

          test "Create rejects scale below MinScale" {
              let r =
                  RenderOptions.Create(2100, 2970, 50, 50, 50, 50, RenderOptions.MinScale - 1, Portrait, true)

              Expect.isError r "scale below the documented minimum must fail"
          }

          test "Create rejects scale above MaxScale" {
              let r =
                  RenderOptions.Create(2100, 2970, 50, 50, 50, 50, RenderOptions.MaxScale + 1, Portrait, true)

              Expect.isError r "scale above the documented maximum must fail"
          }

          test "Create accepts boundary scales (MinScale, MaxScale)" {
              let lo =
                  RenderOptions.Create(2100, 2970, 50, 50, 50, 50, RenderOptions.MinScale, Portrait, true)

              let hi =
                  RenderOptions.Create(2100, 2970, 50, 50, 50, 50, RenderOptions.MaxScale, Portrait, true)

              Expect.isOk lo "MinScale is inclusive"
              Expect.isOk hi "MaxScale is inclusive"
          }

          test "CreateOrThrow returns the same record as Create on valid input" {
              let viaResult =
                  match RenderOptions.Create(1500, 2100, 30, 30, 30, 30, 75, Landscape, false) with
                  | Ok r -> r
                  | Error msg -> failtest msg

              let viaThrow =
                  RenderOptions.CreateOrThrow(1500, 2100, 30, 30, 30, 30, 75, Landscape, false)

              Expect.equal viaThrow viaResult "CreateOrThrow and Create produce identical records on valid input"
          }

          test "CreateOrThrow raises invalidArg on invalid input" {
              Expect.throws
                  (fun () ->
                      RenderOptions.CreateOrThrow(0, 2970, 50, 50, 50, 50, 100, Portrait, true)
                      |> ignore)
                  "CreateOrThrow must throw on invalid input"
          }

          test "WithScale preserves other fields" {
              let updated =
                  match RenderOptions.Default.WithScale(50) with
                  | Ok r -> r
                  | Error msg -> failtest msg

              Expect.equal updated.Scale 50 "Scale should update"
              Expect.equal updated.PageWidth RenderOptions.Default.PageWidth "PageWidth preserved"
              Expect.equal updated.PageHeight RenderOptions.Default.PageHeight "PageHeight preserved"
              Expect.equal updated.Orientation RenderOptions.Default.Orientation "Orientation preserved"
          }

          test "WithScale rejects out-of-range scale" {
              let r = RenderOptions.Default.WithScale(RenderOptions.MaxScale + 1)
              Expect.isError r "WithScale must validate just like Create"
          }

          test "WithPageSize updates both dimensions" {
              let updated =
                  match RenderOptions.Default.WithPageSize(1500, 2100) with
                  | Ok r -> r
                  | Error msg -> failtest msg

              Expect.equal updated.PageWidth 1500 "PageWidth updated"
              Expect.equal updated.PageHeight 2100 "PageHeight updated"
          }

          test "WithOrientation preserves all other fields (cannot invalidate)" {
              let updated = RenderOptions.Default.WithOrientation(Landscape)
              Expect.equal updated.Orientation Landscape "Orientation updated"
              Expect.equal updated.PageWidth RenderOptions.Default.PageWidth "PageWidth preserved"
          } ]

let private loadOptionsTests =
    testList
        "LoadOptions"
        [ test "Create captures Format" {
              let opts = LoadOptions.Create(InputFormat.MusicXML)
              Expect.equal opts.Format InputFormat.MusicXML "Format captured"
          }

          test "Default is MEI" { Expect.equal LoadOptions.Default.Format InputFormat.MEI "Default load format is MEI" } ]

let private pdfOptionsTests =
    testList
        "PdfOptions"
        [ test "Create captures base + embedFonts" {
              let pdf = PdfOptions.Create(RenderOptions.Default, false)
              Expect.equal pdf.Base RenderOptions.Default "Base options captured"
              Expect.isFalse pdf.EmbedFonts "EmbedFonts captured"
          }

          test "Default embeds fonts" { Expect.isTrue PdfOptions.Default.EmbedFonts "Default PDF embeds fonts" } ]

let private exhaustivenessTests =
    // Compile-time exhaustiveness checks dressed up as runtime tests —
    // they fail only if a new DU case is added without updating the match.
    let inputFormatName (f: InputFormat) =
        match f with
        | InputFormat.MEI -> "MEI"
        | InputFormat.MusicXML -> "MusicXML"
        | InputFormat.Humdrum -> "Humdrum"
        | InputFormat.PAE -> "PAE"
        | InputFormat.ABC -> "ABC"

    let outputFormatName (f: OutputFormat) =
        match f with
        | OutputFormat.Svg -> "Svg"
        | OutputFormat.Pdf -> "Pdf"
        | OutputFormat.Mei -> "Mei"
        | OutputFormat.Midi -> "Midi"
        | OutputFormat.Pae -> "Pae"
        | OutputFormat.Humdrum -> "Humdrum"

    let orientationName (o: PageOrientation) =
        match o with
        | Portrait -> "Portrait"
        | Landscape -> "Landscape"

    testList
        "Closed-DU exhaustiveness"
        [ test "Every InputFormat case has a name" {
              let names =
                  [ InputFormat.MEI
                    InputFormat.MusicXML
                    InputFormat.Humdrum
                    InputFormat.PAE
                    InputFormat.ABC ]
                  |> List.map inputFormatName

              Expect.equal (List.length names) 5 "Five InputFormat cases at Phase 04"
              Expect.allEqual (names |> List.map (fun n -> n.Length > 0)) true "Every case yields a non-empty name"
          }

          test "Every OutputFormat case has a name" {
              let names =
                  [ OutputFormat.Svg
                    OutputFormat.Pdf
                    OutputFormat.Mei
                    OutputFormat.Midi
                    OutputFormat.Pae
                    OutputFormat.Humdrum ]
                  |> List.map outputFormatName

              Expect.equal (List.length names) 6 "Six OutputFormat cases at Phase 04"
          }

          test "Every PageOrientation case has a name" {
              Expect.equal (orientationName Portrait) "Portrait" "Portrait case"
              Expect.equal (orientationName Landscape) "Landscape" "Landscape case"
          } ]

[<Tests>]
let allTypesTests =
    testList "Types" [ renderOptionsTests; loadOptionsTests; pdfOptionsTests; exhaustivenessTests ]
