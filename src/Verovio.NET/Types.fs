namespace Verovio.NET

// ============================================================================
//  Verovio.NET public types — closed DUs + value-space-projected option
//  records. Mirrors the upstream Verovio toolkit's surface (see
//  https://book.verovio.org/toolkit-reference/toolkit-methods.html) without
//  string-typed format identifiers leaking into the public API.
//
//  Shape contract:
//    * `InputFormat` / `OutputFormat` are CLOSED DUs. Adding a format is an
//      additive DU case + a backend dispatch entry; never an `obj`-typed
//      escape hatch.
//    * Option records (`LoadOptions`, `RenderOptions`, `PdfOptions`) carry
//      smart-constructor entry points exposed as static members on the
//      type itself (so C# call sites read `LoadOptions.Default` /
//      `LoadOptions.Create(InputFormat.MEI)`, not `LoadOptionsModule.…`).
//    * Failures are an enumerable `LoadError` / `RenderError` union, not a
//      stringly-typed message. New failure modes are additive DU cases.
// ============================================================================

/// Score-input formats Verovio understands. Mirrors the upstream Toolkit's
/// `InputFrom` enumeration.
type InputFormat =
    /// Music Encoding Initiative — Verovio's canonical native format.
    | MEI
    /// MusicXML — interchange format used by most notation editors.
    | MusicXML
    /// Humdrum **kern — scholarly format used in computational
    /// musicology.
    | Humdrum
    /// Plaine and Easie Code — compact ASCII encoding of monophonic music.
    | PAE
    /// ABC notation — text-based folk-tradition encoding.
    | ABC

/// Score-output formats Verovio can produce.
type OutputFormat =
    /// Scalable Vector Graphics — the canonical render target.
    | Svg
    /// Portable Document Format — multi-page paginated output.
    | Pdf
    /// MEI — round-trip the loaded score back out as MEI.
    | Mei
    /// MIDI bytes — Standard MIDI File for playback / DAW import.
    | Midi
    /// PAE — round-trip back to Plaine and Easie Code (monophonic only).
    | Pae
    /// Humdrum — round-trip back to **kern.
    | Humdrum

/// Page orientation passed to render-time layout. Verovio defaults to
/// `Portrait`.
type PageOrientation =
    | Portrait
    | Landscape

/// In-domain failure modes for `Toolkit.LoadData`. New modes are additive
/// DU cases.
type LoadError =
    /// Input format DU case mapped to a Verovio input format the backend
    /// reports as unsupported.
    | UnsupportedInputFormat of InputFormat
    /// Verovio's parser rejected the input. Message is the parser's
    /// diagnostic string for debugging.
    | ParseFailed of message: string
    /// The native library could not be loaded (missing libverovio.dll on
    /// the current RID, or a load-time error from the OS loader).
    | NativeLibraryUnavailable of message: string
    /// Backend signalled a failure that doesn't map cleanly to the cases
    /// above. Message is the backend's diagnostic for debugging.
    | BackendError of message: string

/// In-domain failure modes for the various `Toolkit.RenderTo*` methods.
type RenderError =
    /// Caller passed a page index outside `[1, pageCount]` (or before any
    /// document was loaded).
    | PageOutOfRange of requestedPage: int * pageCount: int
    /// Output format DU case mapped to a Verovio output Verovio reports
    /// as unsupported.
    | UnsupportedOutputFormat of OutputFormat
    /// Caller invoked a render before successfully calling `LoadData`.
    | NoDocumentLoaded
    /// Verovio raised an exception during the render. Message is the
    /// exception's diagnostic for debugging.
    | RenderFailed of message: string
    /// The native library could not be loaded.
    | NativeLibraryUnavailable of message: string
    /// Backend signalled a failure that doesn't map cleanly to the cases
    /// above. Message is the backend's diagnostic for debugging.
    | BackendError of message: string

/// Options passed to `Toolkit.LoadData`. Constructed via `LoadOptions.Create`
/// or `LoadOptions.Default`.
[<Struct>]
type LoadOptions =
    private
        { Format_: InputFormat }

    member this.Format = this.Format_

    /// Construct from an input-format choice.
    static member Create(format: InputFormat) : LoadOptions = { Format_ = format }

    /// Default — MEI input.
    static member Default: LoadOptions = LoadOptions.Create(InputFormat.MEI)

/// Render-time options shared by SVG + PDF outputs. Constructed via
/// `RenderOptions.Create` (smart ctor returning `Result<_, string>`) or
/// `RenderOptions.Default`.
///
/// Verovio's toolkit accepts a wider option surface; this record carries
/// the subset that ScaleMastery-class consumers need at MVP. Future
/// additions are additive record fields with `Default` fallbacks; no
/// breaking change to the public signature is required.
[<Struct>]
type RenderOptions =
    private
        { PageWidth_: int
          PageHeight_: int
          PageMarginTop_: int
          PageMarginBottom_: int
          PageMarginLeft_: int
          PageMarginRight_: int
          Scale_: int
          Orientation_: PageOrientation
          AdjustPageHeight_: bool }

    member this.PageWidth = this.PageWidth_
    member this.PageHeight = this.PageHeight_
    member this.PageMarginTop = this.PageMarginTop_
    member this.PageMarginBottom = this.PageMarginBottom_
    member this.PageMarginLeft = this.PageMarginLeft_
    member this.PageMarginRight = this.PageMarginRight_
    member this.Scale = this.Scale_
    member this.Orientation = this.Orientation_
    member this.AdjustPageHeight = this.AdjustPageHeight_

    /// Verovio's documented scale range, per the toolkit option reference.
    static member val MinScale = 1
    static member val MaxScale = 1000

    /// Verovio's default page geometry (in 0.1mm units — the toolkit's
    /// native unit).
    static member val DefaultPageWidth = 2100
    static member val DefaultPageHeight = 2970
    static member val DefaultPageMargin = 50
    static member val DefaultScale = 100

    /// Default — A4 portrait, 100% scale, 5mm margins all round, height
    /// auto-adjusted to the rendered content.
    static member Default: RenderOptions =
        { PageWidth_ = RenderOptions.DefaultPageWidth
          PageHeight_ = RenderOptions.DefaultPageHeight
          PageMarginTop_ = RenderOptions.DefaultPageMargin
          PageMarginBottom_ = RenderOptions.DefaultPageMargin
          PageMarginLeft_ = RenderOptions.DefaultPageMargin
          PageMarginRight_ = RenderOptions.DefaultPageMargin
          Scale_ = RenderOptions.DefaultScale
          Orientation_ = Portrait
          AdjustPageHeight_ = true }

    /// Smart constructor — validates every field against the Verovio
    /// option reference. Returns an `Error` describing the first invalid
    /// field encountered, rather than silently clamping (which is what
    /// Verovio itself does and is a common source of "why isn't my page
    /// the size I asked for?" surprise).
    static member Create
        (
            pageWidth: int,
            pageHeight: int,
            pageMarginTop: int,
            pageMarginBottom: int,
            pageMarginLeft: int,
            pageMarginRight: int,
            scale: int,
            orientation: PageOrientation,
            adjustPageHeight: bool
        ) : Result<RenderOptions, string> =
        if pageWidth <= 0 then
            Error(sprintf "pageWidth must be positive (got %d)" pageWidth)
        elif pageHeight <= 0 then
            Error(sprintf "pageHeight must be positive (got %d)" pageHeight)
        elif pageMarginTop < 0 then
            Error(sprintf "pageMarginTop must be non-negative (got %d)" pageMarginTop)
        elif pageMarginBottom < 0 then
            Error(sprintf "pageMarginBottom must be non-negative (got %d)" pageMarginBottom)
        elif pageMarginLeft < 0 then
            Error(sprintf "pageMarginLeft must be non-negative (got %d)" pageMarginLeft)
        elif pageMarginRight < 0 then
            Error(sprintf "pageMarginRight must be non-negative (got %d)" pageMarginRight)
        elif scale < RenderOptions.MinScale || scale > RenderOptions.MaxScale then
            Error(sprintf "scale must be in [%d, %d] (got %d)" RenderOptions.MinScale RenderOptions.MaxScale scale)
        else
            Ok
                { PageWidth_ = pageWidth
                  PageHeight_ = pageHeight
                  PageMarginTop_ = pageMarginTop
                  PageMarginBottom_ = pageMarginBottom
                  PageMarginLeft_ = pageMarginLeft
                  PageMarginRight_ = pageMarginRight
                  Scale_ = scale
                  Orientation_ = orientation
                  AdjustPageHeight_ = adjustPageHeight }

    /// Smart constructor — same as `Create` but throws on invalid input.
    /// Ergonomic for call sites where the values are compile-time
    /// constants or already validated elsewhere; production code should
    /// prefer `Create` and pattern-match the Result.
    static member CreateOrThrow
        (
            pageWidth: int,
            pageHeight: int,
            pageMarginTop: int,
            pageMarginBottom: int,
            pageMarginLeft: int,
            pageMarginRight: int,
            scale: int,
            orientation: PageOrientation,
            adjustPageHeight: bool
        ) : RenderOptions =
        match
            RenderOptions.Create(
                pageWidth,
                pageHeight,
                pageMarginTop,
                pageMarginBottom,
                pageMarginLeft,
                pageMarginRight,
                scale,
                orientation,
                adjustPageHeight
            )
        with
        | Ok options -> options
        | Error msg -> invalidArg "RenderOptions" msg

    /// Builder pattern — derive a new RenderOptions from an existing one
    /// by overriding the scale. Returns `Error` if `scale` is outside
    /// `[MinScale, MaxScale]`.
    member this.WithScale(scale: int) : Result<RenderOptions, string> =
        RenderOptions.Create(
            this.PageWidth_,
            this.PageHeight_,
            this.PageMarginTop_,
            this.PageMarginBottom_,
            this.PageMarginLeft_,
            this.PageMarginRight_,
            scale,
            this.Orientation_,
            this.AdjustPageHeight_
        )

    /// Builder pattern — derive a new RenderOptions from an existing one
    /// by overriding the page size. Returns `Error` if `width` or
    /// `height` is non-positive.
    member this.WithPageSize(width: int, height: int) : Result<RenderOptions, string> =
        RenderOptions.Create(
            width,
            height,
            this.PageMarginTop_,
            this.PageMarginBottom_,
            this.PageMarginLeft_,
            this.PageMarginRight_,
            this.Scale_,
            this.Orientation_,
            this.AdjustPageHeight_
        )

    /// Builder pattern — derive a new RenderOptions from an existing one
    /// by overriding the orientation. Cannot fail.
    member this.WithOrientation(orientation: PageOrientation) : RenderOptions =
        // Field-by-field copy rather than `{ this with ... }` — F# rejects
        // copy-and-update on `byref<struct>` (FS3232).
        { PageWidth_ = this.PageWidth_
          PageHeight_ = this.PageHeight_
          PageMarginTop_ = this.PageMarginTop_
          PageMarginBottom_ = this.PageMarginBottom_
          PageMarginLeft_ = this.PageMarginLeft_
          PageMarginRight_ = this.PageMarginRight_
          Scale_ = this.Scale_
          Orientation_ = orientation
          AdjustPageHeight_ = this.AdjustPageHeight_ }

/// PDF-specific render options. PDF output is multi-page; SVG output is
/// per-page. Constructed via `PdfOptions.Create` or `PdfOptions.Default`.
[<Struct>]
type PdfOptions =
    private
        { Base_: RenderOptions
          EmbedFonts_: bool }

    member this.Base = this.Base_
    member this.EmbedFonts = this.EmbedFonts_

    /// Construct from base render options and an embed-fonts flag.
    static member Create(baseOptions: RenderOptions, embedFonts: bool) : PdfOptions =
        { Base_ = baseOptions
          EmbedFonts_ = embedFonts }

    /// Default — A4 portrait, embedded fonts (so PDFs render identically
    /// on machines without the Verovio glyph fonts installed).
    static member Default: PdfOptions = PdfOptions.Create(RenderOptions.Default, true)

/// A loaded document's structural summary. Returned by
/// `Toolkit.GetDocumentInfo` once a document is loaded.
[<Struct>]
type DocumentInfo =
    { PageCount: int
      // Future fields are additive — score-level metadata (title,
      // composer, etc.) lands here when the public-API surface grows.
      ScoreTimeInMs: int }

/// MIDI realisation of a single Verovio element. Returned by
/// `Toolkit.GetMidiValuesForElement`.
[<Struct>]
type MidiValues =
    { Pitch: byte
      Duration: int
      Time: int }
