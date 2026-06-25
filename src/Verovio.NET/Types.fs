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

/// In-domain failure modes for `Toolkit.LoadData` / `Toolkit.LoadFile` /
/// the zip-loading variants. New modes are additive DU cases.
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
    /// `LoadFile` / `ValidatePaeFile` could not find the requested path.
    /// Pre-flight existence check at the wrapper layer; surfaced
    /// separately so callers don't have to disambiguate "missing file"
    /// from "parse failed" themselves.
    | FileNotFound of path: string
    /// Backend signalled a failure that doesn't map cleanly to the cases
    /// above. Message is the backend's diagnostic for debugging.
    | BackendError of message: string

/// In-domain failure modes for the various `Toolkit.RenderTo*` methods,
/// query methods, and editor calls.
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
    /// `*File` write target could not be created or written to. Path is
    /// the requested filename; message is the OS/Verovio diagnostic.
    | FileWriteFailed of path: string * message: string
    /// `Toolkit.Edit` / `Toolkit.Select` was passed a JSON action that
    /// Verovio rejected as malformed or unsupported.
    | InvalidEditorAction of message: string
    /// `*ForElement` query referenced an xml:id that Verovio could not
    /// find in the loaded document.
    | ElementNotFound of xmlId: string
    /// Backend signalled a failure that doesn't map cleanly to the cases
    /// above. Message is the backend's diagnostic for debugging.
    | BackendError of message: string

/// Verovio's `footer` rendering knob. Controls whether the
/// auto-generated "Engraved by Verovio" footer mark (with the MEI
/// logo) renders at the bottom of each page.
///   - `Auto`: Verovio's default — renders the autogenerated footer
///     when no source-MEI footer is present.
///   - `None`: never render any footer. Quiet output; useful when the
///     hosting page provides its own attribution and the Verovio
///     mark would compete visually.
///   - `Always`: always render the autogenerated footer (override
///     source-MEI footer if any).
///   - `Encoded`: only render the source-MEI footer (skip the
///     autogenerated one).
///
/// `[<RequireQualifiedAccess>]` so the `None` case doesn't shadow
/// `Option.None` at call sites that mix both types.
[<RequireQualifiedAccess>]
type FooterDisplay =
    | Auto
    | None
    | Always
    | Encoded

/// Verovio's `breaks` rendering knob. Controls how the engraver decides
/// where to wrap from one staff system to the next.
///   - `Auto`: Verovio's default — wraps when content would otherwise
///     overflow the page width.
///   - `None`: never wrap; everything on a single line. Overflows the
///     page width if content is long.
///   - `Smart`: heuristic that tries to balance line lengths instead of
///     packing each line full. Useful for scale displays where a "6
///     bars + 2" auto wrap looks ragged.
///   - `Line`: respect source-MEI `<lb/>` line-break elements only.
///   - `Encoded`: respect source-MEI `<sb/>` system-break elements
///     only. Use this when the caller has explicitly placed breaks at
///     the desired bar positions.
///
/// `[<RequireQualifiedAccess>]` so the `None` case doesn't shadow
/// `Option.None` at call sites that mix both types.
[<RequireQualifiedAccess>]
type BreaksMode =
    | Auto
    | None
    | Smart
    | Line
    | Encoded

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
/// the subset that typical consumers need at MVP. Future
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
          AdjustPageHeight_: bool
          Footer_: FooterDisplay
          Breaks_: BreaksMode }

    member this.PageWidth = this.PageWidth_
    member this.PageHeight = this.PageHeight_
    member this.PageMarginTop = this.PageMarginTop_
    member this.PageMarginBottom = this.PageMarginBottom_
    member this.PageMarginLeft = this.PageMarginLeft_
    member this.PageMarginRight = this.PageMarginRight_
    member this.Scale = this.Scale_
    member this.Orientation = this.Orientation_
    member this.AdjustPageHeight = this.AdjustPageHeight_
    member this.Footer = this.Footer_
    member this.Breaks = this.Breaks_

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
    /// auto-adjusted to the rendered content. Footer left at Verovio's
    /// `Auto` default — consumers that want to suppress the
    /// "Engraved by Verovio" mark chain `.WithFooter(FooterDisplay.None)`.
    static member Default: RenderOptions =
        { PageWidth_ = RenderOptions.DefaultPageWidth
          PageHeight_ = RenderOptions.DefaultPageHeight
          PageMarginTop_ = RenderOptions.DefaultPageMargin
          PageMarginBottom_ = RenderOptions.DefaultPageMargin
          PageMarginLeft_ = RenderOptions.DefaultPageMargin
          PageMarginRight_ = RenderOptions.DefaultPageMargin
          Scale_ = RenderOptions.DefaultScale
          Orientation_ = Portrait
          AdjustPageHeight_ = true
          Footer_ = FooterDisplay.Auto
          Breaks_ = BreaksMode.Auto }

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
                  AdjustPageHeight_ = adjustPageHeight
                  // Footer + Breaks are not part of the positional Create
                  // signature — both default to Verovio's Auto. Override
                  // via `.WithFooter(...)` / `.WithBreaks(...)` builders
                  // below.
                  Footer_ = FooterDisplay.Auto
                  Breaks_ = BreaksMode.Auto }

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
    /// `[MinScale, MaxScale]`. Carries the current `Footer_` through so
    /// chains like `.WithFooter(None).WithScale(80)` preserve the
    /// footer setting (otherwise Create would reseed it to Auto).
    member this.WithScale(scale: int) : Result<RenderOptions, string> =
        // Stash byref `this`'s footer + breaks into locals — closures
        // can't capture struct `this` (FS0406).
        let footer = this.Footer_
        let breaks = this.Breaks_

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
        |> Result.map (fun opts -> opts.WithFooter(footer).WithBreaks(breaks))

    /// Builder pattern — derive a new RenderOptions from an existing one
    /// by overriding the page size. Returns `Error` if `width` or
    /// `height` is non-positive. Footer + breaks carried through per
    /// `WithScale`.
    member this.WithPageSize(width: int, height: int) : Result<RenderOptions, string> =
        let footer = this.Footer_
        let breaks = this.Breaks_

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
        |> Result.map (fun opts -> opts.WithFooter(footer).WithBreaks(breaks))

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
          AdjustPageHeight_ = this.AdjustPageHeight_
          Footer_ = this.Footer_
          Breaks_ = this.Breaks_ }

    /// Builder pattern — derive a new RenderOptions from an existing one
    /// by overriding the footer display mode. Cannot fail. Use
    /// `FooterDisplay.None` to suppress the "Engraved by Verovio" mark
    /// (the hosting page typically carries its own attribution).
    member this.WithFooter(footer: FooterDisplay) : RenderOptions =
        { PageWidth_ = this.PageWidth_
          PageHeight_ = this.PageHeight_
          PageMarginTop_ = this.PageMarginTop_
          PageMarginBottom_ = this.PageMarginBottom_
          PageMarginLeft_ = this.PageMarginLeft_
          PageMarginRight_ = this.PageMarginRight_
          Scale_ = this.Scale_
          Orientation_ = this.Orientation_
          AdjustPageHeight_ = this.AdjustPageHeight_
          Footer_ = footer
          Breaks_ = this.Breaks_ }

    /// Builder pattern — derive a new RenderOptions from an existing one
    /// by overriding the breaks (line-wrap) mode. Cannot fail. Use
    /// `BreaksMode.Smart` to balance line lengths in a layout that
    /// `Auto` would render ragged (e.g. an 8-bar scale as 6+2 vs.
    /// 4+4); use `BreaksMode.Encoded` when the source MEI carries
    /// explicit `<sb/>` system-break markers.
    member this.WithBreaks(breaks: BreaksMode) : RenderOptions =
        { PageWidth_ = this.PageWidth_
          PageHeight_ = this.PageHeight_
          PageMarginTop_ = this.PageMarginTop_
          PageMarginBottom_ = this.PageMarginBottom_
          PageMarginLeft_ = this.PageMarginLeft_
          PageMarginRight_ = this.PageMarginRight_
          Scale_ = this.Scale_
          Orientation_ = this.Orientation_
          AdjustPageHeight_ = this.AdjustPageHeight_
          Footer_ = this.Footer_
          Breaks_ = breaks }

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

/// One event in a Verovio timemap — the engraver's per-event timing
/// table. Generated by `Toolkit.RenderToTimemap`. Each entry marks an
/// instant where elements start sounding (`NotesOn`) and / or stop
/// sounding (`NotesOff`).
///
/// Distinguishing `RealTimeMs` (millisecond wall clock derived from the
/// score's tempo timeline) and `ScoreTimeQuarter` (beats in
/// quarter-note units, tempo-independent) lets a consumer drive
/// playback against either timeline — the former for an audio playhead,
/// the latter for a notation cursor that should advance independent of
/// tempo changes.
[<Struct>]
type TimemapEntry =
    {
        /// Wall-clock time of this event, in milliseconds since document
        /// start.
        RealTimeMs: double
        /// Score-time of this event in quarter-note units (beats).
        /// Independent of tempo changes.
        ScoreTimeQuarter: double
        /// xml:ids of elements that begin sounding at this event.
        NotesOn: string[]
        /// xml:ids of elements that stop sounding at this event.
        NotesOff: string[]
        /// Tempo in BPM at this event, if Verovio's emitted a tempo
        /// change here. `None` means the tempo has not changed since
        /// the previous entry.
        Tempo: double option
    }

/// Verovio's per-event timing table. Returned by
/// `Toolkit.RenderToTimemap`. Suitable input to a playback-sync UI
/// that highlights notation in step with audio playback.
[<Struct>]
type Timemap =
    {
        /// Events in document order. May be empty if no document is
        /// loaded or the loaded document has no timed content.
        Entries: TimemapEntry[]
    }

/// A single sounding instance of an element. Verovio reports an array
/// of these because expansion / repeats can sound an element more than
/// once.
[<Struct>]
type ElementTimeInstance =
    {
        /// Score-time onset in quarter-note units.
        ScoreTimeOnset: double
        /// Score-time offset in quarter-note units.
        ScoreTimeOffset: double
        /// Wall-clock onset in milliseconds.
        RealTimeOnsetMs: double
        /// Wall-clock offset in milliseconds.
        RealTimeOffsetMs: double
    }

/// Timing information for an element, returned by
/// `Toolkit.GetTimesForElement`. May contain multiple sounding
/// instances when expansion / repeats sound the element more than
/// once.
[<Struct>]
type ElementTimes =
    {
        /// One entry per sounding instance.
        Instances: ElementTimeInstance[]
    }

/// Result of `Toolkit.ValidatePae` / `Toolkit.ValidatePaeFile`. Raw
/// JSON for v0 — the upstream schema is keyed by line / token with
/// nested error and warning lists; typing it deliberately is a
/// follow-up phase. The `IsValid` flag flips false the moment the
/// JSON report contains any `errors` key with content.
[<Struct>]
type PaeValidationReport =
    {
        /// True if Verovio reported zero errors. Warnings do not flip
        /// this flag.
        IsValid: bool
        /// Raw JSON report. Suitable for surface to a UI; the typed
        /// model is deferred.
        RawJson: string
    }

/// Snapshot of Verovio's `--help`-style option introspection,
/// returned by `Toolkit.GetOptionsIntrospection`. Provides the
/// available-options JSON + default-values JSON + currently-active
/// JSON + the human-readable usage string in one round trip.
[<Struct>]
type OptionsIntrospection =
    {
        /// JSON describing every option Verovio understands
        /// (name, type, default, range, doc).
        AvailableJson: string
        /// JSON object with the default value of every option.
        DefaultsJson: string
        /// JSON object with the currently-active option values.
        CurrentJson: string
        /// Human-readable usage / help text.
        UsageString: string
    }

/// JSON-described editor action passed to `Toolkit.Edit`. Phase 49
/// ships raw-string passthrough only; a typed `EditorAction` DU
/// (`Drag of Point * Point | Insert of …` etc.) lands in a follow-up
/// phase once the upstream action-schema surface is enumerated
/// against a Pro-tier editor consumer (per `voice-leading.pro` Open
/// Decision #11 / `harmonic-mechanisms.com` Open Decision #21
/// editor-direction). Keeping the seam typed today (rather than
/// taking a bare `string`) lets the follow-up phase add the typed
/// constructor without breaking the call sites that exist by then.
[<Struct>]
type EditorAction =
    private
        { Json_: string }

    member this.Json = this.Json_

    /// Construct from a raw JSON action string. The string is not
    /// validated up front; Verovio rejects malformed actions at
    /// `Edit` time with `RenderError.InvalidEditorAction`.
    static member FromRawJson(json: string | null) : EditorAction =
        match json with
        | null -> nullArg "json"
        | s -> { Json_ = s }

/// Static surface for the xml:id-seed determinism contract.
///
/// **Determinism contract** — with the same `(libverovio version, MEI
/// input, RenderOptions, xml:id seed)` tuple, every Toolkit on every
/// machine produces byte-identical SVG. Without seeding, every
/// `vrvToolkit_constructor` call mints fresh xml:ids from a process-
/// global RNG and equality assertions fail across instances.
///
/// `Toolkit.Create` applies `DefaultXmlIdSeed` immediately after
/// constructing the native handle so the default behaviour is
/// deterministic. Consumers that want a different seed (e.g. one
/// derived from a document hash for content-addressable engraving)
/// call `Toolkit.ResetXmlIdSeed` with their own value.
[<AbstractClass; Sealed>]
type Determinism =
    /// Default xml:id seed applied by `Toolkit.Create`. Two
    /// independently-constructed toolkits render byte-identical SVG
    /// for the same input MEI at this seed.
    ///
    /// **Why not 0?** Upstream's `Object::SeedID(0)` is the
    /// `randomize-from-clock` branch (the C++ wrapper's
    /// `vrvToolkit_resetXmlIdSeed` reads zero as "no fixed seed,
    /// please reseed from entropy"). A non-zero value pins the RNG
    /// to a reproducible state — `1` is the smallest valid choice
    /// and the easiest to recognise in a diff.
    static member val DefaultXmlIdSeed: int = 1
