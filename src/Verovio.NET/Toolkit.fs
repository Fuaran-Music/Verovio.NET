namespace Verovio.NET

open System
open System.Text.Json
open Verovio.NET.Internal

// ============================================================================
//  Toolkit — the public Verovio.NET API. A C#-friendly class with member
//  methods, NOT a module of curried F# functions; matches the workspace
//  C#-friendly-API mandate (per CLAUDE.md "C#-friendly public API
//  mandate" section).
//
//  Every fallible method has two shapes:
//    * `LoadData / RenderToSvg / GetMei / RenderToMidi` — `Result`-returning,
//      preferred from F# and from explicit-error-handling C# call sites.
//    * `LoadDataOrThrow / RenderToSvgOrThrow / GetMeiOrThrow /
//      RenderToMidiOrThrow` — throwing variant raising
//      `VerovioException` with the wrapped error, ergonomic for C# happy
//      paths.
//
//  The Toolkit owns a native handle; dispose via `use` (F#) or `using`
//  (C#) to release. Concurrent use of a single Toolkit is unsafe — pool
//  instances per consumer.
//
//  PDF rendering is not exposed by upstream Verovio's c_wrapper.h —
//  Phase 04 ships `RenderToPdf` returning `Error UnsupportedOutputFormat`.
//  Follow-up phase will choose between a C-wrapper extension, an
//  SVG→PDF post-process via SkiaSharp/PdfSharp, or a CLI invocation of
//  the upstream `verovio` binary.
// ============================================================================

/// Raised by the throwing variants of Toolkit methods. Wraps the closed-DU
/// error case in an exception so C# happy-path callers can catch a single
/// type. F# callers prefer the `Result`-returning variants and
/// `match`-handle the DU directly.
type VerovioException(message: string, innerError: obj) =
    inherit Exception(message)

    /// The closed-DU error case (`LoadError` or `RenderError`) that
    /// triggered this exception. Boxed because the same exception type
    /// covers both error DUs; cast at the call site.
    member _.InnerError = innerError

/// Public Verovio toolkit handle. Construct via `Toolkit.Create()` (or
/// `Toolkit.Create(resourcePath)` to pin Verovio's font/resource lookup
/// path). Dispose via `use` (F#) or `using` (C#) to release the native
/// handle.
type Toolkit private (handle: nativeint) =
    let mutable disposed = false

    let checkDisposed () =
        if disposed then
            raise (ObjectDisposedException("Toolkit"))

    /// Auto-resolve the vendored `verovio-data/` resource directory.
    /// The NuGet package ships the upstream `data/` tree alongside
    /// `libverovio.dll` at `runtimes/<rid>/native/verovio-data/`; .NET
    /// copies that payload next to the consuming assembly via the
    /// CopyToOutputDirectory rule in Verovio.NET.fsproj. We find it
    /// relative to AppContext.BaseDirectory so the path resolves both
    /// in dev (consumer's `bin/Debug/net10.0/runtimes/...`) and in
    /// `dotnet publish` output (same layout under the publish dir).
    /// Returns `None` when the directory doesn't exist — the
    /// parameterless Create() then falls back to vrvToolkit_constructor
    /// (no path), which uses whatever default Verovio was built with.
    static member private TryResolveVendoredResourcePath() : string option =
        let baseDir = AppContext.BaseDirectory
        let candidate = IO.Path.Combine(baseDir, "runtimes", "win-x64", "native", "verovio-data")

        if IO.Directory.Exists candidate then
            Some candidate
        else
            None

    /// Construct a toolkit with default resource-path resolution.
    /// Auto-resolves the vendored Verovio data/ folder (Bravura,
    /// Leland, Leipzig, Petaluma, Gootville, text) from the package's
    /// `runtimes/<rid>/native/verovio-data/` payload — required for
    /// LoadData to succeed without the consumer thinking about font
    /// paths. Falls back to the parameterless Verovio ctor when the
    /// vendored payload is missing (e.g. a custom build that strips
    /// the runtimes/ tree); consumers who hit that path should call
    /// `Create(resourcePath)` with their own data/ location.
    ///
    /// Raises `VerovioException` (with inner
    /// `LoadError.NativeLibraryUnavailable`) if `libverovio` is not
    /// loadable on the current RID.
    static member Create() : Toolkit =
        match Interop.probeAvailability () with
        | Error msg -> raise (VerovioException(msg, LoadError.NativeLibraryUnavailable msg))
        | Ok() ->
            let handle =
                match Toolkit.TryResolveVendoredResourcePath() with
                | Some path -> Interop.vrvToolkit_constructorResourcePath path
                | None -> Interop.vrvToolkit_constructor ()

            if handle = nativeint 0 then
                raise (
                    VerovioException(
                        "vrvToolkit_constructor returned a null handle",
                        LoadError.BackendError "null handle"
                    )
                )

            new Toolkit(handle)

    /// Construct a toolkit with an explicit resource path. Use when the
    /// hosting process can't auto-resolve Verovio's font/resource
    /// directory (e.g. unusual deployment topologies). Raises
    /// `VerovioException` if the library is unloadable.
    static member Create(resourcePath: string | null) : Toolkit =
        let path =
            match resourcePath with
            | null ->
                nullArg "resourcePath"
                "" // unreachable; nullArg always throws.
            | s -> s

        match Interop.probeAvailability () with
        | Error msg -> raise (VerovioException(msg, LoadError.NativeLibraryUnavailable msg))
        | Ok() ->
            let handle = Interop.vrvToolkit_constructorResourcePath (path)

            if handle = nativeint 0 then
                raise (
                    VerovioException(
                        sprintf "vrvToolkit_constructorResourcePath returned a null handle (resourcePath=%s)" path,
                        LoadError.BackendError "null handle"
                    )
                )

            new Toolkit(handle)

    /// Upstream Verovio version string of the linked libverovio. Returns
    /// the empty string if the toolkit returns a null pointer (defensive
    /// — should not happen in practice).
    member _.Version: string =
        checkDisposed ()

        match Interop.ptrToStringOrNull (Interop.vrvToolkit_getVersion handle) with
        | null -> ""
        | v -> v

    // ── Document loading ────────────────────────────────────────────────

    /// Load a score document from an in-memory string with default
    /// options (MEI input).
    member this.LoadData(input: string | null) : Result<unit, LoadError> =
        this.LoadData(input, LoadOptions.Default)

    /// Load a score document from an in-memory string with explicit
    /// load options. `input` accepts `null` for defence against C#
    /// callers.
    member _.LoadData(input: string | null, options: LoadOptions) : Result<unit, LoadError> =
        checkDisposed ()

        match input with
        | null -> Error(LoadError.ParseFailed "input cannot be null")
        | s when String.IsNullOrWhiteSpace s -> Error(LoadError.ParseFailed "input cannot be empty or whitespace")
        | s ->
            let inputFrom = Toolkit.inputFormatToString options.Format

            if not (Interop.vrvToolkit_setInputFrom (handle, inputFrom)) then
                Error(LoadError.UnsupportedInputFormat options.Format)
            else
                try
                    if Interop.vrvToolkit_loadData (handle, s) then
                        Ok()
                    else
                        Error(LoadError.ParseFailed "Verovio rejected the input (see backend log for details)")
                with ex ->
                    Error(LoadError.BackendError ex.Message)

    /// `LoadData` variant raising `VerovioException` on failure.
    member this.LoadDataOrThrow(input: string | null) : unit =
        this.LoadDataOrThrow(input, LoadOptions.Default)

    /// `LoadData` variant raising `VerovioException` on failure.
    member this.LoadDataOrThrow(input: string | null, options: LoadOptions) : unit =
        match this.LoadData(input, options) with
        | Ok() -> ()
        | Error err -> raise (VerovioException(sprintf "LoadData failed: %A" err, err))

    // ── SVG render ──────────────────────────────────────────────────────

    /// Render a single page to SVG with default options. Page indices
    /// are 1-based.
    member this.RenderToSvg(pageNumber: int) : Result<string, RenderError> =
        this.RenderToSvg(pageNumber, RenderOptions.Default)

    /// Render a single page to SVG. Page indices are 1-based per
    /// Verovio's convention.
    member _.RenderToSvg(pageNumber: int, options: RenderOptions) : Result<string, RenderError> =
        checkDisposed ()

        if pageNumber < 1 then
            Error(RenderError.PageOutOfRange(pageNumber, 0))
        else
            let pageCount = Interop.vrvToolkit_getPageCount handle

            if pageCount = 0 then
                Error RenderError.NoDocumentLoaded
            elif pageNumber > pageCount then
                Error(RenderError.PageOutOfRange(pageNumber, pageCount))
            else
                try
                    let optionsJson = Toolkit.renderOptionsToJson options

                    if not (Interop.vrvToolkit_setOptions (handle, optionsJson)) then
                        Error(RenderError.RenderFailed "vrvToolkit_setOptions rejected the option string")
                    else
                        // Layout-affecting options (breaks, pageWidth, scale)
                        // need a redoLayout after setOptions — Verovio
                        // commits the initial layout at loadData time and
                        // setOptions alone doesn't re-flow. redoLayout
                        // re-runs the layout pass against the new options.
                        // Cost is one extra layout per render; cheap
                        // relative to the SVG emit that follows.
                        Interop.vrvToolkit_redoLayout (handle, "{}") |> ignore

                        match Interop.ptrToStringOrNull (Interop.vrvToolkit_renderToSVG (handle, pageNumber, true)) with
                        | null -> Error(RenderError.RenderFailed "vrvToolkit_renderToSVG returned null")
                        | svg -> Ok svg
                with ex ->
                    Error(RenderError.BackendError ex.Message)

    /// `RenderToSvg` variant raising `VerovioException` on failure.
    member this.RenderToSvgOrThrow(pageNumber: int) : string =
        this.RenderToSvgOrThrow(pageNumber, RenderOptions.Default)

    /// `RenderToSvg` variant raising `VerovioException` on failure.
    member this.RenderToSvgOrThrow(pageNumber: int, options: RenderOptions) : string =
        match this.RenderToSvg(pageNumber, options) with
        | Ok svg -> svg
        | Error err -> raise (VerovioException(sprintf "RenderToSvg(%d) failed: %A" pageNumber err, err))

    // ── PDF render ──────────────────────────────────────────────────────

    /// PDF rendering is deferred. See the Phase 04 phase doc "PDF
    /// rendering deferred" note — upstream Verovio's `c_wrapper.h` does
    /// not expose PDF output; the post-process / extension path lands in
    /// a follow-up phase.
    member this.RenderToPdf() : Result<byte[], RenderError> = this.RenderToPdf(PdfOptions.Default)

    /// PDF rendering is deferred. Always returns
    /// `Error (UnsupportedOutputFormat Pdf)` at Phase 04.
    member _.RenderToPdf(_options: PdfOptions) : Result<byte[], RenderError> =
        checkDisposed ()
        Error(RenderError.UnsupportedOutputFormat OutputFormat.Pdf)

    // ── MEI round-trip ──────────────────────────────────────────────────

    /// Round-trip the loaded document back out as MEI. Verovio may
    /// normalise and add layout information during the round-trip; the
    /// returned MEI is canonical for the toolkit's interpretation.
    member _.GetMei() : Result<string, RenderError> =
        checkDisposed ()

        if Interop.vrvToolkit_getPageCount handle = 0 then
            Error RenderError.NoDocumentLoaded
        else
            try
                match Interop.ptrToStringOrNull (Interop.vrvToolkit_getMEI (handle, "{}")) with
                | null -> Error(RenderError.RenderFailed "vrvToolkit_getMEI returned null")
                | mei -> Ok mei
            with ex ->
                Error(RenderError.BackendError ex.Message)

    /// `GetMei` variant raising `VerovioException` on failure.
    member this.GetMeiOrThrow() : string =
        match this.GetMei() with
        | Ok mei -> mei
        | Error err -> raise (VerovioException(sprintf "GetMei failed: %A" err, err))

    // ── MIDI render ─────────────────────────────────────────────────────

    /// Render the loaded document to a Standard MIDI File. Verovio
    /// returns base64-encoded bytes; this method decodes them.
    member _.RenderToMidi() : Result<byte[], RenderError> =
        checkDisposed ()

        if Interop.vrvToolkit_getPageCount handle = 0 then
            Error RenderError.NoDocumentLoaded
        else
            try
                match Interop.ptrToStringOrNull (Interop.vrvToolkit_renderToMIDI handle) with
                | null -> Error(RenderError.RenderFailed "vrvToolkit_renderToMIDI returned null")
                | base64 -> Ok(Convert.FromBase64String base64)
            with
            | :? FormatException as ex ->
                Error(RenderError.RenderFailed(sprintf "MIDI base64 decode failed: %s" ex.Message))
            | ex -> Error(RenderError.BackendError ex.Message)

    /// `RenderToMidi` variant raising `VerovioException` on failure.
    member this.RenderToMidiOrThrow() : byte[] =
        match this.RenderToMidi() with
        | Ok bytes -> bytes
        | Error err -> raise (VerovioException(sprintf "RenderToMidi failed: %A" err, err))

    // ── Queries ─────────────────────────────────────────────────────────

    /// Structural summary of the currently-loaded document.
    member _.GetDocumentInfo() : Result<DocumentInfo, RenderError> =
        checkDisposed ()
        let pageCount = Interop.vrvToolkit_getPageCount handle

        if pageCount = 0 then
            Error RenderError.NoDocumentLoaded
        else
            // ScoreTimeInMs is derived from the last sounding element;
            // computing it through the c_wrapper requires walking
            // getElementsAtTime, which we defer for the Phase 04 MVP.
            // 0 here is a documented placeholder until a follow-up phase
            // wires the proper score-time query.
            Ok
                { PageCount = pageCount
                  ScoreTimeInMs = 0 }

    /// Query MEI element xml:ids sounding at a given time offset (ms).
    member _.GetElementsAtTime(timeMs: int) : Result<string[], RenderError> =
        checkDisposed ()

        if timeMs < 0 then
            Error(RenderError.RenderFailed(sprintf "timeMs must be non-negative (got %d)" timeMs))
        elif Interop.vrvToolkit_getPageCount handle = 0 then
            Error RenderError.NoDocumentLoaded
        else
            try
                match Interop.ptrToStringOrNull (Interop.vrvToolkit_getElementsAtTime (handle, timeMs)) with
                | null -> Ok [||]
                | json ->
                    // Upstream returns {"notes": [...], "chords": [...]}
                    // or an empty object when nothing is sounding. We
                    // flatten note + chord ids into a single array.
                    use doc = JsonDocument.Parse(json: string)
                    let root = doc.RootElement
                    let collect kind = Toolkit.collectIds root kind
                    Ok(Array.append (collect "notes") (collect "chords"))
            with
            | :? JsonException as ex ->
                Error(RenderError.RenderFailed(sprintf "getElementsAtTime JSON parse failed: %s" ex.Message))
            | ex -> Error(RenderError.BackendError ex.Message)

    /// Look up MIDI values (pitch, duration in ms, start time in ms) for
    /// an MEI element by `xml:id`.
    member _.GetMidiValuesForElement(elementId: string | null) : Result<MidiValues, RenderError> =
        checkDisposed ()

        match elementId with
        | null -> Error(RenderError.RenderFailed "elementId cannot be null")
        | s when String.IsNullOrWhiteSpace s -> Error(RenderError.RenderFailed "elementId cannot be empty")
        | s ->
            if Interop.vrvToolkit_getPageCount handle = 0 then
                Error RenderError.NoDocumentLoaded
            else
                try
                    match Interop.ptrToStringOrNull (Interop.vrvToolkit_getMIDIValuesForElement (handle, s)) with
                    | null -> Error(RenderError.RenderFailed "vrvToolkit_getMIDIValuesForElement returned null")
                    | json ->
                        use doc = JsonDocument.Parse(json: string)
                        let root = doc.RootElement

                        let getInt (name: string) =
                            let mutable prop = Unchecked.defaultof<JsonElement>

                            if root.TryGetProperty(name, &prop) then
                                prop.GetInt32()
                            else
                                0

                        Ok
                            { Pitch = byte (getInt "pitch")
                              Duration = getInt "duration"
                              Time = getInt "time" }
                with
                | :? JsonException as ex ->
                    Error(RenderError.RenderFailed(sprintf "getMIDIValuesForElement JSON parse failed: %s" ex.Message))
                | ex -> Error(RenderError.BackendError ex.Message)

    // ── Disposal ────────────────────────────────────────────────────────

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                Interop.vrvToolkit_destructor handle

    // ── Internal helpers ────────────────────────────────────────────────

    static member private inputFormatToString(fmt: InputFormat) : string =
        match fmt with
        | InputFormat.MEI -> "mei"
        | InputFormat.MusicXML -> "musicxml"
        | InputFormat.Humdrum -> "humdrum"
        | InputFormat.PAE -> "pae"
        | InputFormat.ABC -> "abc"

    static member private renderOptionsToJson(options: RenderOptions) : string =
        // Verovio's option JSON keys mirror the toolkit option reference.
        // We build the JSON by hand here to avoid a dependency on a JSON
        // serialiser at the option-marshalling path; the option surface
        // is small and the keys are stable across upstream versions.
        //
        // `orientation` was dropped — Verovio v6.x doesn't accept it
        // ("Unsupported option 'orientation'" stderr warning). Page
        // orientation in Verovio is implicit in `pageWidth` /
        // `pageHeight`; setting the wider dimension as `pageWidth`
        // produces landscape. We honour the `Landscape` enum on the
        // F# side by swapping the page dimensions before serialising,
        // keeping the public C#/F# API stable.
        let pageWidth, pageHeight =
            match options.Orientation with
            | Portrait -> options.PageWidth, options.PageHeight
            | Landscape -> options.PageHeight, options.PageWidth

        // `footer` — only emit the JSON key when the consumer asked
        // for a non-default value, to keep the option JSON minimal
        // and let Verovio's own default behaviour stand otherwise.
        let footerJson =
            match options.Footer with
            | FooterDisplay.Auto -> ""
            | FooterDisplay.None -> ",\"footer\":\"none\""
            | FooterDisplay.Always -> ",\"footer\":\"always\""
            | FooterDisplay.Encoded -> ",\"footer\":\"encoded\""

        // `breaks` — same minimality rule as `footer`. Verovio's
        // toolkit accepts: "auto" | "none" | "smart" | "line" |
        // "encoded".
        let breaksJson =
            match options.Breaks with
            | BreaksMode.Auto -> ""
            | BreaksMode.None -> ",\"breaks\":\"none\""
            | BreaksMode.Smart -> ",\"breaks\":\"smart\""
            | BreaksMode.Line -> ",\"breaks\":\"line\""
            | BreaksMode.Encoded -> ",\"breaks\":\"encoded\""

        sprintf
            """{"pageWidth":%d,"pageHeight":%d,"pageMarginTop":%d,"pageMarginBottom":%d,"pageMarginLeft":%d,"pageMarginRight":%d,"scale":%d,"adjustPageHeight":%b%s%s}"""
            pageWidth
            pageHeight
            options.PageMarginTop
            options.PageMarginBottom
            options.PageMarginLeft
            options.PageMarginRight
            options.Scale
            options.AdjustPageHeight
            footerJson
            breaksJson

    static member private collectIds (root: JsonElement) (kind: string) : string[] =
        let mutable arr = Unchecked.defaultof<JsonElement>

        if not (root.TryGetProperty(kind, &arr)) then
            [||]
        elif arr.ValueKind <> JsonValueKind.Array then
            [||]
        else
            arr.EnumerateArray()
            |> Seq.choose (fun el ->
                match el.ValueKind with
                | JsonValueKind.String ->
                    match el.GetString() with
                    | null -> None
                    | s -> Some s
                | JsonValueKind.Object ->
                    let mutable idProp = Unchecked.defaultof<JsonElement>

                    if el.TryGetProperty("id", &idProp) && idProp.ValueKind = JsonValueKind.String then
                        match idProp.GetString() with
                        | null -> None
                        | s -> Some s
                    else
                        None
                | _ -> None)
            |> Seq.toArray
