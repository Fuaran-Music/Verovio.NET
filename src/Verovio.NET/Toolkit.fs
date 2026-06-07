namespace Verovio.NET

open System
open System.IO
open System.Runtime.InteropServices
open System.Text.Json
open Verovio.NET.Internal

// ============================================================================
//  Toolkit — the public Verovio.NET API. A C#-friendly class with member
//  methods, NOT a module of curried F# functions; matches the workspace
//  C#-friendly-API mandate (per CLAUDE.md "C#-friendly public API
//  mandate" section).
//
//  Every fallible method has two shapes:
//    * `Result<_, _>`-returning, preferred from F# and from
//      explicit-error-handling C# call sites.
//    * `*OrThrow` — throwing variant raising `VerovioException` with
//      the wrapped error, ergonomic for C# happy paths.
//
//  The Toolkit owns a native handle; dispose via `use` (F#) or `using`
//  (C#) to release. Concurrent use of a single Toolkit is unsafe — pool
//  instances per consumer.
//
//  PDF rendering is not exposed by upstream Verovio's c_wrapper.h —
//  RenderToPdf returns `Error UnsupportedOutputFormat`. A follow-up
//  phase will choose between a C-wrapper extension, an SVG→PDF
//  post-process via SkiaSharp/PdfSharp, or a CLI invocation of the
//  upstream `verovio` binary.
//
//  Phase 49 (Verovio.NET phase 05) — full c_wrapper.h surface (61/61).
//  Determinism: every `Toolkit.Create` applies
//  `Determinism.DefaultXmlIdSeed` so two toolkits render byte-identical
//  SVG for the same input. Consumers wanting non-default determinism
//  call `ResetXmlIdSeed` with their own seed.
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
    // Sticky xml:id seed. Initialised to Determinism.DefaultXmlIdSeed
    // at construction; overridden by `ResetXmlIdSeed(seed)`. Re-applied
    // before every load and every render so the process-global RNG state
    // is reset to this value at each determinism boundary.
    let mutable xmlIdSeed = Determinism.DefaultXmlIdSeed

    let checkDisposed () =
        if disposed then
            raise (ObjectDisposedException("Toolkit"))

    let reseedFromMember () =
        Interop.vrvToolkit_resetXmlIdSeed (handle, xmlIdSeed)

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

        let candidate =
            IO.Path.Combine(baseDir, "runtimes", "win-x64", "native", "verovio-data")

        if IO.Directory.Exists candidate then
            Some candidate
        else
            None

    /// Apply the determinism contract: seed the xml:id RNG to a known
    /// value. Upstream's `Object::SeedID` writes a C++ static, so the
    /// seed is process-global rather than per-toolkit; we re-apply it
    /// at every load entry point so two toolkits loading the same
    /// input from any thread / call order produce byte-identical SVG.
    /// See `Determinism.DefaultXmlIdSeed`.
    static member private SeedDeterministicXmlIds(handle: nativeint) : unit =
        Interop.vrvToolkit_resetXmlIdSeed (handle, Determinism.DefaultXmlIdSeed)

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
    /// Applies `Determinism.DefaultXmlIdSeed` immediately after
    /// construction (per the determinism contract).
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

            Toolkit.SeedDeterministicXmlIds handle
            new Toolkit(handle)

    /// Construct a toolkit with an explicit resource path. Use when the
    /// hosting process can't auto-resolve Verovio's font/resource
    /// directory (e.g. unusual deployment topologies).
    ///
    /// Applies `Determinism.DefaultXmlIdSeed` immediately after
    /// construction.
    ///
    /// Raises `VerovioException` if the library is unloadable.
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

            Toolkit.SeedDeterministicXmlIds handle
            new Toolkit(handle)

    /// Upstream Verovio version string of the linked libverovio. Returns
    /// the empty string if the toolkit returns a null pointer (defensive
    /// — should not happen in practice).
    member _.Version: string =
        checkDisposed ()

        match Interop.ptrToStringOrNull (Interop.vrvToolkit_getVersion handle) with
        | null -> ""
        | v -> v

    // ── Determinism ─────────────────────────────────────────────────────

    /// Reseed the xml:id RNG. The same seed against the same source
    /// produces byte-identical SVG output. `Toolkit.Create` applies
    /// `Determinism.DefaultXmlIdSeed` automatically; call this only
    /// when you want a different seed (e.g. one derived from a
    /// document hash for content-addressable engraving).
    ///
    /// The seed is **sticky** — subsequent `LoadData` / `LoadFile` /
    /// `LoadZip*` / `RenderData` / `RenderToSvg` calls re-apply this
    /// value at their determinism boundaries (since upstream's RNG
    /// is process-global, we have to re-seed at every load/render
    /// boundary to keep two toolkits in lock-step). To go back to
    /// the default, call `ResetXmlIdSeed(Determinism.DefaultXmlIdSeed)`.
    member _.ResetXmlIdSeed(seed: int) : unit =
        checkDisposed ()
        xmlIdSeed <- seed
        Interop.vrvToolkit_resetXmlIdSeed (handle, seed)

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
                    // Per-load seed reset is the determinism contract —
                    // see `SeedDeterministicXmlIds` doc. Honours
                    // ResetXmlIdSeed if the user has overridden the
                    // default seed.
                    reseedFromMember ()

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

    /// Load a score document directly from a file path. Pre-flight
    /// existence check surfaces `FileNotFound` distinctly from
    /// `ParseFailed` (the upstream wrapper conflates the two).
    member this.LoadFile(path: string | null) : Result<unit, LoadError> =
        this.LoadFile(path, LoadOptions.Default)

    /// Load a score document from a file path with explicit load
    /// options. The file's content format is set from
    /// `options.Format`.
    member _.LoadFile(path: string | null, options: LoadOptions) : Result<unit, LoadError> =
        checkDisposed ()

        match path with
        | null -> Error(LoadError.ParseFailed "path cannot be null")
        | p when String.IsNullOrWhiteSpace p -> Error(LoadError.ParseFailed "path cannot be empty or whitespace")
        | p when not (File.Exists p) -> Error(LoadError.FileNotFound p)
        | p ->
            let inputFrom = Toolkit.inputFormatToString options.Format

            if not (Interop.vrvToolkit_setInputFrom (handle, inputFrom)) then
                Error(LoadError.UnsupportedInputFormat options.Format)
            else
                try
                    reseedFromMember ()

                    if Interop.vrvToolkit_loadFile (handle, p) then
                        Ok()
                    else
                        Error(LoadError.ParseFailed "Verovio rejected the input file (see backend log for details)")
                with ex ->
                    Error(LoadError.BackendError ex.Message)

    /// `LoadFile` variant raising `VerovioException` on failure.
    member this.LoadFileOrThrow(path: string | null) : unit =
        this.LoadFileOrThrow(path, LoadOptions.Default)

    /// `LoadFile` variant raising `VerovioException` on failure.
    member this.LoadFileOrThrow(path: string | null, options: LoadOptions) : unit =
        match this.LoadFile(path, options) with
        | Ok() -> ()
        | Error err -> raise (VerovioException(sprintf "LoadFile failed: %A" err, err))

    /// Load a zip-packed MEI archive from a base64-encoded string.
    /// Useful for in-memory delivery of multi-resource MEI bundles.
    member _.LoadZipBase64(input: string | null) : Result<unit, LoadError> =
        checkDisposed ()

        match input with
        | null -> Error(LoadError.ParseFailed "input cannot be null")
        | s when String.IsNullOrWhiteSpace s -> Error(LoadError.ParseFailed "input cannot be empty or whitespace")
        | s ->
            try
                reseedFromMember ()

                if Interop.vrvToolkit_loadZipDataBase64 (handle, s) then
                    Ok()
                else
                    Error(LoadError.ParseFailed "Verovio rejected the zip archive (see backend log for details)")
            with ex ->
                Error(LoadError.BackendError ex.Message)

    /// `LoadZipBase64` variant raising `VerovioException` on failure.
    member this.LoadZipBase64OrThrow(input: string | null) : unit =
        match this.LoadZipBase64(input) with
        | Ok() -> ()
        | Error err -> raise (VerovioException(sprintf "LoadZipBase64 failed: %A" err, err))

    /// Load a zip-packed MEI archive from raw bytes. Pins the buffer
    /// for the duration of the call.
    member _.LoadZipBuffer(bytes: byte[] | null) : Result<unit, LoadError> =
        checkDisposed ()

        match bytes with
        | null -> Error(LoadError.ParseFailed "bytes cannot be null")
        | b when b.Length = 0 -> Error(LoadError.ParseFailed "bytes cannot be empty")
        | b ->
            let handleAlloc = GCHandle.Alloc(b, GCHandleType.Pinned)

            try
                try
                    let ptr = handleAlloc.AddrOfPinnedObject()

                    reseedFromMember ()

                    if Interop.vrvToolkit_loadZipDataBuffer (handle, ptr, b.Length) then
                        Ok()
                    else
                        Error(LoadError.ParseFailed "Verovio rejected the zip buffer (see backend log for details)")
                with ex ->
                    Error(LoadError.BackendError ex.Message)
            finally
                handleAlloc.Free()

    /// `LoadZipBuffer` variant raising `VerovioException` on failure.
    member this.LoadZipBufferOrThrow(bytes: byte[] | null) : unit =
        match this.LoadZipBuffer(bytes) with
        | Ok() -> ()
        | Error err -> raise (VerovioException(sprintf "LoadZipBuffer failed: %A" err, err))

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
                        //
                        // redoLayout + renderToSVG both mint xml:ids for
                        // layout-time elements (`g id="system…"` etc.)
                        // from the process-global RNG. Reseed at every
                        // render boundary so two toolkits rendering the
                        // same input from any thread / call order
                        // produce byte-identical SVG (the determinism
                        // contract documented on `Determinism`).
                        reseedFromMember ()
                        Interop.vrvToolkit_redoLayout (handle, "{}")

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

    /// One-shot: load `data` + render page 1 to SVG. Convenience for
    /// callers that don't iterate pages. Equivalent to
    /// `LoadData(data); RenderToSvg(1)` but Verovio short-circuits
    /// the intermediate document tree exposure.
    member _.RenderData(data: string | null, options: RenderOptions) : Result<string, RenderError> =
        checkDisposed ()

        match data with
        | null -> Error(RenderError.RenderFailed "data cannot be null")
        | s when String.IsNullOrWhiteSpace s -> Error(RenderError.RenderFailed "data cannot be empty")
        | s ->
            try
                let optionsJson = Toolkit.renderOptionsToJson options
                reseedFromMember ()

                match Interop.ptrToStringOrNull (Interop.vrvToolkit_renderData (handle, s, optionsJson)) with
                | null -> Error(RenderError.RenderFailed "vrvToolkit_renderData returned null")
                | svg -> Ok svg
            with ex ->
                Error(RenderError.BackendError ex.Message)

    /// `RenderData` variant raising `VerovioException` on failure.
    member this.RenderDataOrThrow(data: string | null, options: RenderOptions) : string =
        match this.RenderData(data, options) with
        | Ok svg -> svg
        | Error err -> raise (VerovioException(sprintf "RenderData failed: %A" err, err))

    // ── PDF render ──────────────────────────────────────────────────────

    /// PDF rendering is deferred. Upstream Verovio's `c_wrapper.h` does
    /// not expose PDF output; the post-process / extension path lands
    /// in a follow-up phase.
    member this.RenderToPdf() : Result<byte[], RenderError> = this.RenderToPdf(PdfOptions.Default)

    /// PDF rendering is deferred. Always returns
    /// `Error (UnsupportedOutputFormat Pdf)`.
    member _.RenderToPdf(_options: PdfOptions) : Result<byte[], RenderError> =
        checkDisposed ()
        Error(RenderError.UnsupportedOutputFormat OutputFormat.Pdf)

    // ── MEI / Humdrum / PAE round-trip ──────────────────────────────────

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

    /// Round-trip the loaded document back out as Humdrum **kern.
    member _.GetHumdrum() : Result<string, RenderError> =
        checkDisposed ()

        if Interop.vrvToolkit_getPageCount handle = 0 then
            Error RenderError.NoDocumentLoaded
        else
            try
                match Interop.ptrToStringOrNull (Interop.vrvToolkit_getHumdrum handle) with
                | null -> Error(RenderError.RenderFailed "vrvToolkit_getHumdrum returned null")
                | s -> Ok s
            with ex ->
                Error(RenderError.BackendError ex.Message)

    /// `GetHumdrum` variant raising `VerovioException` on failure.
    member this.GetHumdrumOrThrow() : string =
        match this.GetHumdrum() with
        | Ok s -> s
        | Error err -> raise (VerovioException(sprintf "GetHumdrum failed: %A" err, err))

    /// Render the loaded document back out as Plaine and Easie code
    /// (monophonic only).
    member _.RenderToPae() : Result<string, RenderError> =
        checkDisposed ()

        if Interop.vrvToolkit_getPageCount handle = 0 then
            Error RenderError.NoDocumentLoaded
        else
            try
                match Interop.ptrToStringOrNull (Interop.vrvToolkit_renderToPAE handle) with
                | null -> Error(RenderError.RenderFailed "vrvToolkit_renderToPAE returned null")
                | s -> Ok s
            with ex ->
                Error(RenderError.BackendError ex.Message)

    /// `RenderToPae` variant raising `VerovioException` on failure.
    member this.RenderToPaeOrThrow() : string =
        match this.RenderToPae() with
        | Ok s -> s
        | Error err -> raise (VerovioException(sprintf "RenderToPae failed: %A" err, err))

    /// Render the document's `<expansion>` map as JSON.
    member _.RenderToExpansionMap() : Result<string, RenderError> =
        checkDisposed ()

        if Interop.vrvToolkit_getPageCount handle = 0 then
            Error RenderError.NoDocumentLoaded
        else
            try
                match Interop.ptrToStringOrNull (Interop.vrvToolkit_renderToExpansionMap handle) with
                | null -> Error(RenderError.RenderFailed "vrvToolkit_renderToExpansionMap returned null")
                | s -> Ok s
            with ex ->
                Error(RenderError.BackendError ex.Message)

    /// `RenderToExpansionMap` variant raising `VerovioException` on
    /// failure.
    member this.RenderToExpansionMapOrThrow() : string =
        match this.RenderToExpansionMap() with
        | Ok s -> s
        | Error err -> raise (VerovioException(sprintf "RenderToExpansionMap failed: %A" err, err))

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

    // ── Timemap (typed) ─────────────────────────────────────────────────

    /// Render Verovio's per-event timing table. Each entry pairs a
    /// real-time/score-time pair with the elements beginning and
    /// ending at that instant; suitable input to a playback-sync UI
    /// that highlights notation in step with audio.
    member _.RenderToTimemap() : Result<Timemap, RenderError> =
        checkDisposed ()

        if Interop.vrvToolkit_getPageCount handle = 0 then
            Error RenderError.NoDocumentLoaded
        else
            try
                match Interop.ptrToStringOrNull (Interop.vrvToolkit_renderToTimemap (handle, "{}")) with
                | null -> Error(RenderError.RenderFailed "vrvToolkit_renderToTimemap returned null")
                | json -> Toolkit.parseTimemap json
            with
            | :? JsonException as ex ->
                Error(RenderError.RenderFailed(sprintf "renderToTimemap JSON parse failed: %s" ex.Message))
            | ex -> Error(RenderError.BackendError ex.Message)

    /// `RenderToTimemap` variant raising `VerovioException` on failure.
    member this.RenderToTimemapOrThrow() : Timemap =
        match this.RenderToTimemap() with
        | Ok t -> t
        | Error err -> raise (VerovioException(sprintf "RenderToTimemap failed: %A" err, err))

    /// Render Verovio's per-event timing table as raw JSON. Useful
    /// when the upstream timemap schema evolves past the typed
    /// `Timemap` model and the caller wants the upstream-current
    /// shape.
    member _.RenderToTimemapJson() : Result<string, RenderError> =
        checkDisposed ()

        if Interop.vrvToolkit_getPageCount handle = 0 then
            Error RenderError.NoDocumentLoaded
        else
            try
                match Interop.ptrToStringOrNull (Interop.vrvToolkit_renderToTimemap (handle, "{}")) with
                | null -> Error(RenderError.RenderFailed "vrvToolkit_renderToTimemap returned null")
                | s -> Ok s
            with ex ->
                Error(RenderError.BackendError ex.Message)

    // ── Format converters ──────────────────────────────────────────────

    /// Normalise a Humdrum document by round-tripping through
    /// Verovio's parser + emitter. Does not disturb any currently-
    /// loaded document.
    member _.ConvertHumdrumToHumdrum(humdrum: string | null) : Result<string, RenderError> =
        checkDisposed ()

        match humdrum with
        | null -> Error(RenderError.RenderFailed "humdrum cannot be null")
        | s when String.IsNullOrWhiteSpace s -> Error(RenderError.RenderFailed "humdrum cannot be empty")
        | s ->
            try
                match Interop.ptrToStringOrNull (Interop.vrvToolkit_convertHumdrumToHumdrum (handle, s)) with
                | null -> Error(RenderError.RenderFailed "vrvToolkit_convertHumdrumToHumdrum returned null")
                | out -> Ok out
            with ex ->
                Error(RenderError.BackendError ex.Message)

    /// `ConvertHumdrumToHumdrum` variant raising `VerovioException` on
    /// failure.
    member this.ConvertHumdrumToHumdrumOrThrow(humdrum: string | null) : string =
        match this.ConvertHumdrumToHumdrum(humdrum) with
        | Ok s -> s
        | Error err -> raise (VerovioException(sprintf "ConvertHumdrumToHumdrum failed: %A" err, err))

    /// Convert a Humdrum document to a Standard MIDI File. Returns
    /// the decoded MIDI bytes.
    member _.ConvertHumdrumToMidi(humdrum: string | null) : Result<byte[], RenderError> =
        checkDisposed ()

        match humdrum with
        | null -> Error(RenderError.RenderFailed "humdrum cannot be null")
        | s when String.IsNullOrWhiteSpace s -> Error(RenderError.RenderFailed "humdrum cannot be empty")
        | s ->
            try
                match Interop.ptrToStringOrNull (Interop.vrvToolkit_convertHumdrumToMIDI (handle, s)) with
                | null -> Error(RenderError.RenderFailed "vrvToolkit_convertHumdrumToMIDI returned null")
                | base64 -> Ok(Convert.FromBase64String base64)
            with
            | :? FormatException as ex ->
                Error(RenderError.RenderFailed(sprintf "MIDI base64 decode failed: %s" ex.Message))
            | ex -> Error(RenderError.BackendError ex.Message)

    /// `ConvertHumdrumToMidi` variant raising `VerovioException` on
    /// failure.
    member this.ConvertHumdrumToMidiOrThrow(humdrum: string | null) : byte[] =
        match this.ConvertHumdrumToMidi(humdrum) with
        | Ok b -> b
        | Error err -> raise (VerovioException(sprintf "ConvertHumdrumToMidi failed: %A" err, err))

    /// Convert an MEI document to Humdrum **kern.
    member _.ConvertMeiToHumdrum(mei: string | null) : Result<string, RenderError> =
        checkDisposed ()

        match mei with
        | null -> Error(RenderError.RenderFailed "mei cannot be null")
        | s when String.IsNullOrWhiteSpace s -> Error(RenderError.RenderFailed "mei cannot be empty")
        | s ->
            try
                match Interop.ptrToStringOrNull (Interop.vrvToolkit_convertMEIToHumdrum (handle, s)) with
                | null -> Error(RenderError.RenderFailed "vrvToolkit_convertMEIToHumdrum returned null")
                | out -> Ok out
            with ex ->
                Error(RenderError.BackendError ex.Message)

    /// `ConvertMeiToHumdrum` variant raising `VerovioException` on
    /// failure.
    member this.ConvertMeiToHumdrumOrThrow(mei: string | null) : string =
        match this.ConvertMeiToHumdrum(mei) with
        | Ok s -> s
        | Error err -> raise (VerovioException(sprintf "ConvertMeiToHumdrum failed: %A" err, err))

    // ── File I/O (rendering / saving) ──────────────────────────────────

    /// Save the toolkit's current document state to disk in the
    /// format set by `SetOutputTo`. `cOptionsJson` is a JSON option
    /// string (empty `"{}"` for defaults).
    member this.SaveFile(path: string | null) : Result<unit, RenderError> = this.SaveFile(path, "{}")

    /// `SaveFile` with an explicit JSON options string.
    member _.SaveFile(path: string | null, cOptionsJson: string | null) : Result<unit, RenderError> =
        checkDisposed ()

        match path with
        | null -> Error(RenderError.FileWriteFailed("", "path cannot be null"))
        | p when String.IsNullOrWhiteSpace p -> Error(RenderError.FileWriteFailed(p, "path cannot be empty"))
        | p ->
            let opts =
                match cOptionsJson with
                | null -> "{}"
                | s -> s

            try
                if Interop.vrvToolkit_saveFile (handle, p, opts) then
                    Ok()
                else
                    Error(RenderError.FileWriteFailed(p, "vrvToolkit_saveFile returned false"))
            with ex ->
                Error(RenderError.FileWriteFailed(p, ex.Message))

    /// `SaveFile` variant raising `VerovioException` on failure.
    member this.SaveFileOrThrow(path: string | null) : unit = this.SaveFileOrThrow(path, "{}")

    /// `SaveFile` variant raising `VerovioException` on failure.
    member this.SaveFileOrThrow(path: string | null, cOptionsJson: string | null) : unit =
        match this.SaveFile(path, cOptionsJson) with
        | Ok() -> ()
        | Error err -> raise (VerovioException(sprintf "SaveFile failed: %A" err, err))

    /// Render page `pageNumber` to an SVG file at `path`.
    member _.RenderToSvgFile(path: string | null, pageNumber: int) : Result<unit, RenderError> =
        checkDisposed ()

        if pageNumber < 1 then
            Error(RenderError.PageOutOfRange(pageNumber, 0))
        else
            match path with
            | null -> Error(RenderError.FileWriteFailed("", "path cannot be null"))
            | p when String.IsNullOrWhiteSpace p -> Error(RenderError.FileWriteFailed(p, "path cannot be empty"))
            | p ->
                let pageCount = Interop.vrvToolkit_getPageCount handle

                if pageCount = 0 then
                    Error RenderError.NoDocumentLoaded
                elif pageNumber > pageCount then
                    Error(RenderError.PageOutOfRange(pageNumber, pageCount))
                else
                    try
                        if Interop.vrvToolkit_renderToSVGFile (handle, p, pageNumber) then
                            Ok()
                        else
                            Error(RenderError.FileWriteFailed(p, "vrvToolkit_renderToSVGFile returned false"))
                    with ex ->
                        Error(RenderError.FileWriteFailed(p, ex.Message))

    /// `RenderToSvgFile` variant raising `VerovioException` on failure.
    member this.RenderToSvgFileOrThrow(path: string | null, pageNumber: int) : unit =
        match this.RenderToSvgFile(path, pageNumber) with
        | Ok() -> ()
        | Error err -> raise (VerovioException(sprintf "RenderToSvgFile failed: %A" err, err))

    /// Render the document to a Standard MIDI File at `path`.
    member _.RenderToMidiFile(path: string | null) : Result<unit, RenderError> =
        checkDisposed ()

        match path with
        | null -> Error(RenderError.FileWriteFailed("", "path cannot be null"))
        | p when String.IsNullOrWhiteSpace p -> Error(RenderError.FileWriteFailed(p, "path cannot be empty"))
        | p ->
            if Interop.vrvToolkit_getPageCount handle = 0 then
                Error RenderError.NoDocumentLoaded
            else
                try
                    if Interop.vrvToolkit_renderToMIDIFile (handle, p) then
                        Ok()
                    else
                        Error(RenderError.FileWriteFailed(p, "vrvToolkit_renderToMIDIFile returned false"))
                with ex ->
                    Error(RenderError.FileWriteFailed(p, ex.Message))

    /// `RenderToMidiFile` variant raising `VerovioException` on failure.
    member this.RenderToMidiFileOrThrow(path: string | null) : unit =
        match this.RenderToMidiFile(path) with
        | Ok() -> ()
        | Error err -> raise (VerovioException(sprintf "RenderToMidiFile failed: %A" err, err))

    /// Render the document to a PAE file at `path`.
    member _.RenderToPaeFile(path: string | null) : Result<unit, RenderError> =
        checkDisposed ()

        match path with
        | null -> Error(RenderError.FileWriteFailed("", "path cannot be null"))
        | p when String.IsNullOrWhiteSpace p -> Error(RenderError.FileWriteFailed(p, "path cannot be empty"))
        | p ->
            if Interop.vrvToolkit_getPageCount handle = 0 then
                Error RenderError.NoDocumentLoaded
            else
                try
                    if Interop.vrvToolkit_renderToPAEFile (handle, p) then
                        Ok()
                    else
                        Error(RenderError.FileWriteFailed(p, "vrvToolkit_renderToPAEFile returned false"))
                with ex ->
                    Error(RenderError.FileWriteFailed(p, ex.Message))

    /// `RenderToPaeFile` variant raising `VerovioException` on failure.
    member this.RenderToPaeFileOrThrow(path: string | null) : unit =
        match this.RenderToPaeFile(path) with
        | Ok() -> ()
        | Error err -> raise (VerovioException(sprintf "RenderToPaeFile failed: %A" err, err))

    /// Render the document's expansion map to a JSON file at `path`.
    member _.RenderToExpansionMapFile(path: string | null) : Result<unit, RenderError> =
        checkDisposed ()

        match path with
        | null -> Error(RenderError.FileWriteFailed("", "path cannot be null"))
        | p when String.IsNullOrWhiteSpace p -> Error(RenderError.FileWriteFailed(p, "path cannot be empty"))
        | p ->
            if Interop.vrvToolkit_getPageCount handle = 0 then
                Error RenderError.NoDocumentLoaded
            else
                try
                    if Interop.vrvToolkit_renderToExpansionMapFile (handle, p) then
                        Ok()
                    else
                        Error(RenderError.FileWriteFailed(p, "vrvToolkit_renderToExpansionMapFile returned false"))
                with ex ->
                    Error(RenderError.FileWriteFailed(p, ex.Message))

    /// `RenderToExpansionMapFile` variant raising `VerovioException` on
    /// failure.
    member this.RenderToExpansionMapFileOrThrow(path: string | null) : unit =
        match this.RenderToExpansionMapFile(path) with
        | Ok() -> ()
        | Error err -> raise (VerovioException(sprintf "RenderToExpansionMapFile failed: %A" err, err))

    /// Render the document's timemap to a JSON file at `path`.
    member _.RenderToTimemapFile(path: string | null) : Result<unit, RenderError> =
        checkDisposed ()

        match path with
        | null -> Error(RenderError.FileWriteFailed("", "path cannot be null"))
        | p when String.IsNullOrWhiteSpace p -> Error(RenderError.FileWriteFailed(p, "path cannot be empty"))
        | p ->
            if Interop.vrvToolkit_getPageCount handle = 0 then
                Error RenderError.NoDocumentLoaded
            else
                try
                    if Interop.vrvToolkit_renderToTimemapFile (handle, p, "{}") then
                        Ok()
                    else
                        Error(RenderError.FileWriteFailed(p, "vrvToolkit_renderToTimemapFile returned false"))
                with ex ->
                    Error(RenderError.FileWriteFailed(p, ex.Message))

    /// `RenderToTimemapFile` variant raising `VerovioException` on
    /// failure.
    member this.RenderToTimemapFileOrThrow(path: string | null) : unit =
        match this.RenderToTimemapFile(path) with
        | Ok() -> ()
        | Error err -> raise (VerovioException(sprintf "RenderToTimemapFile failed: %A" err, err))

    /// Write the loaded document's Humdrum **kern representation to a
    /// file at `path`.
    member _.GetHumdrumFile(path: string | null) : Result<unit, RenderError> =
        checkDisposed ()

        match path with
        | null -> Error(RenderError.FileWriteFailed("", "path cannot be null"))
        | p when String.IsNullOrWhiteSpace p -> Error(RenderError.FileWriteFailed(p, "path cannot be empty"))
        | p ->
            if Interop.vrvToolkit_getPageCount handle = 0 then
                Error RenderError.NoDocumentLoaded
            else
                try
                    if Interop.vrvToolkit_getHumdrumFile (handle, p) then
                        Ok()
                    else
                        Error(RenderError.FileWriteFailed(p, "vrvToolkit_getHumdrumFile returned false"))
                with ex ->
                    Error(RenderError.FileWriteFailed(p, ex.Message))

    /// `GetHumdrumFile` variant raising `VerovioException` on failure.
    member this.GetHumdrumFileOrThrow(path: string | null) : unit =
        match this.GetHumdrumFile(path) with
        | Ok() -> ()
        | Error err -> raise (VerovioException(sprintf "GetHumdrumFile failed: %A" err, err))

    // ── Options introspection ──────────────────────────────────────────

    /// JSON document describing every option Verovio understands
    /// (name, type, default, range, doc). Entry point for callers
    /// building a dynamic options UI or generating documentation
    /// against the linked libverovio version.
    member _.GetAvailableOptions() : string =
        checkDisposed ()

        match Interop.ptrToStringOrNull (Interop.vrvToolkit_getAvailableOptions handle) with
        | null -> ""
        | s -> s

    /// JSON object with the default value of every option.
    member _.GetDefaultOptions() : string =
        checkDisposed ()

        match Interop.ptrToStringOrNull (Interop.vrvToolkit_getDefaultOptions handle) with
        | null -> ""
        | s -> s

    /// JSON object with the currently-active option values.
    member _.GetOptions() : string =
        checkDisposed ()

        match Interop.ptrToStringOrNull (Interop.vrvToolkit_getOptions handle) with
        | null -> ""
        | s -> s

    /// Human-readable `--help`-style option summary string.
    member _.GetOptionUsageString() : string =
        checkDisposed ()

        match Interop.ptrToStringOrNull (Interop.vrvToolkit_getOptionUsageString handle) with
        | null -> ""
        | s -> s

    /// Snapshot of all four introspection surfaces in one call —
    /// useful for building a dynamic options UI without four
    /// round-trips.
    member this.GetOptionsIntrospection() : OptionsIntrospection =
        { AvailableJson = this.GetAvailableOptions()
          DefaultsJson = this.GetDefaultOptions()
          CurrentJson = this.GetOptions()
          UsageString = this.GetOptionUsageString() }

    /// Reset all toolkit options to Verovio's documented defaults.
    member _.ResetOptions() : unit =
        checkDisposed ()
        Interop.vrvToolkit_resetOptions handle

    /// Pass an arbitrary JSON option document directly. Lower-level
    /// than the typed `RenderToSvg(options)` path — primarily for
    /// consumers using `GetOptionsIntrospection` to surface every
    /// option, including ones the typed `RenderOptions` record
    /// doesn't carry.
    member _.SetRawOptions(json: string | null) : Result<unit, RenderError> =
        checkDisposed ()

        match json with
        | null -> Error(RenderError.RenderFailed "json cannot be null")
        | s ->
            try
                if Interop.vrvToolkit_setOptions (handle, s) then
                    Ok()
                else
                    Error(RenderError.RenderFailed "vrvToolkit_setOptions rejected the JSON")
            with ex ->
                Error(RenderError.BackendError ex.Message)

    /// `SetRawOptions` variant raising `VerovioException` on failure.
    member this.SetRawOptionsOrThrow(json: string | null) : unit =
        match this.SetRawOptions(json) with
        | Ok() -> ()
        | Error err -> raise (VerovioException(sprintf "SetRawOptions failed: %A" err, err))

    // ── Layout / scale / resource path ─────────────────────────────────

    /// Re-run document layout against the current options. Required
    /// after an option change that affects layout (breaks /
    /// pageWidth / pageHeight / scale) when a document has already
    /// been loaded.
    member _.RedoLayout() : unit =
        checkDisposed ()
        Interop.vrvToolkit_redoLayout (handle, "{}")

    /// Re-run the pitch / position layout pass for the current page
    /// only. Lighter than a full `RedoLayout`; used after an editor
    /// action that only adjusts pitch / position of existing
    /// elements.
    member _.RedoPagePitchPosLayout() : unit =
        checkDisposed ()
        Interop.vrvToolkit_redoPagePitchPosLayout handle

    /// Current render scale (percent; 100 = 1:1).
    member _.GetScale() : int =
        checkDisposed ()
        Interop.vrvToolkit_getScale handle

    /// Set the render scale by integer percent. Cheaper than the
    /// JSON-options path when only the scale changes. Returns
    /// `Error` if the scale is outside Verovio's accepted range
    /// (`RenderOptions.MinScale` / `MaxScale`).
    member _.SetScale(scale: int) : Result<unit, RenderError> =
        checkDisposed ()

        if scale < RenderOptions.MinScale || scale > RenderOptions.MaxScale then
            Error(
                RenderError.RenderFailed(
                    sprintf "scale must be in [%d, %d] (got %d)" RenderOptions.MinScale RenderOptions.MaxScale scale
                )
            )
        else
            try
                if Interop.vrvToolkit_setScale (handle, scale) then
                    Ok()
                else
                    Error(RenderError.RenderFailed(sprintf "vrvToolkit_setScale(%d) returned false" scale))
            with ex ->
                Error(RenderError.BackendError ex.Message)

    /// `SetScale` variant raising `VerovioException` on failure.
    member this.SetScaleOrThrow(scale: int) : unit =
        match this.SetScale(scale) with
        | Ok() -> ()
        | Error err -> raise (VerovioException(sprintf "SetScale(%d) failed: %A" scale err, err))

    /// Set the toolkit's output format by DU case. Affects round-trip
    /// via `GetMei` / `GetHumdrum` / `SaveFile`.
    member _.SetOutputTo(format: OutputFormat) : Result<unit, RenderError> =
        checkDisposed ()
        let name = Toolkit.outputFormatToString format

        match name with
        | None -> Error(RenderError.UnsupportedOutputFormat format)
        | Some n ->
            try
                if Interop.vrvToolkit_setOutputTo (handle, n) then
                    Ok()
                else
                    Error(RenderError.UnsupportedOutputFormat format)
            with ex ->
                Error(RenderError.BackendError ex.Message)

    /// `SetOutputTo` variant raising `VerovioException` on failure.
    member this.SetOutputToOrThrow(format: OutputFormat) : unit =
        match this.SetOutputTo(format) with
        | Ok() -> ()
        | Error err -> raise (VerovioException(sprintf "SetOutputTo(%A) failed: %A" format err, err))

    /// Current SMuFL / Verovio-data resource directory.
    member _.GetResourcePath() : string =
        checkDisposed ()

        match Interop.ptrToStringOrNull (Interop.vrvToolkit_getResourcePath handle) with
        | null -> ""
        | s -> s

    /// Update the toolkit's resource-lookup directory. Returns
    /// `Error` if the path is null, empty, missing, or rejected by
    /// Verovio.
    member _.SetResourcePath(path: string | null) : Result<unit, RenderError> =
        checkDisposed ()

        match path with
        | null -> Error(RenderError.RenderFailed "path cannot be null")
        | p when String.IsNullOrWhiteSpace p -> Error(RenderError.RenderFailed "path cannot be empty")
        | p when not (Directory.Exists p) ->
            Error(RenderError.RenderFailed(sprintf "resource path does not exist: %s" p))
        | p ->
            try
                if Interop.vrvToolkit_setResourcePath (handle, p) then
                    Ok()
                else
                    Error(RenderError.RenderFailed(sprintf "vrvToolkit_setResourcePath rejected '%s'" p))
            with ex ->
                Error(RenderError.BackendError ex.Message)

    /// `SetResourcePath` variant raising `VerovioException` on failure.
    member this.SetResourcePathOrThrow(path: string | null) : unit =
        match this.SetResourcePath(path) with
        | Ok() -> ()
        | Error err -> raise (VerovioException(sprintf "SetResourcePath failed: %A" err, err))

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
            // getElementsAtTime, which we defer. 0 here is a documented
            // placeholder until a follow-up phase wires the proper
            // score-time query.
            Ok
                { PageCount = pageCount
                  ScoreTimeInMs = 0 }

    /// Document xml:id at the root of the loaded MEI (`<mei xml:id="…">`),
    /// or the empty string if none / nothing loaded.
    member _.GetId() : string =
        checkDisposed ()

        match Interop.ptrToStringOrNull (Interop.vrvToolkit_getID handle) with
        | null -> ""
        | s -> s

    /// Returns the 1-based page index containing the element with the
    /// given xml:id, or `Error ElementNotFound` if the element is not
    /// in the loaded document.
    member _.GetPageWithElement(xmlId: string | null) : Result<int, RenderError> =
        checkDisposed ()

        match xmlId with
        | null -> Error(RenderError.RenderFailed "xmlId cannot be null")
        | s when String.IsNullOrWhiteSpace s -> Error(RenderError.RenderFailed "xmlId cannot be empty")
        | s ->
            if Interop.vrvToolkit_getPageCount handle = 0 then
                Error RenderError.NoDocumentLoaded
            else
                let pageNo = Interop.vrvToolkit_getPageWithElement (handle, s)

                if pageNo <= 0 then
                    Error(RenderError.ElementNotFound s)
                else
                    Ok pageNo

    /// Query elements sounding at a given time offset (ms).
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
                    // or an empty object when nothing is sounding.
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

    /// MEI attribute set of the named element, returned as a
    /// (string -> string) dictionary. Empty dictionary if the element
    /// has no attributes; `Error ElementNotFound` if Verovio cannot
    /// locate the xml:id.
    member _.GetElementAttr
        (elementId: string | null)
        : Result<System.Collections.Generic.IReadOnlyDictionary<string, string>, RenderError> =
        checkDisposed ()

        match elementId with
        | null -> Error(RenderError.RenderFailed "elementId cannot be null")
        | s when String.IsNullOrWhiteSpace s -> Error(RenderError.RenderFailed "elementId cannot be empty")
        | s ->
            if Interop.vrvToolkit_getPageCount handle = 0 then
                Error RenderError.NoDocumentLoaded
            else
                try
                    match Interop.ptrToStringOrNull (Interop.vrvToolkit_getElementAttr (handle, s)) with
                    | null -> Error(RenderError.ElementNotFound s)
                    | json ->
                        use doc = JsonDocument.Parse(json: string)
                        let root = doc.RootElement

                        let dict = System.Collections.Generic.Dictionary<string, string>()

                        if root.ValueKind = JsonValueKind.Object then
                            for prop in root.EnumerateObject() do
                                let v =
                                    match prop.Value.ValueKind with
                                    | JsonValueKind.String ->
                                        match prop.Value.GetString() with
                                        | null -> ""
                                        | s -> s
                                    | _ -> prop.Value.GetRawText()

                                dict[prop.Name] <- v

                        Ok(dict :> System.Collections.Generic.IReadOnlyDictionary<string, string>)
                with
                | :? JsonException as ex ->
                    Error(RenderError.RenderFailed(sprintf "getElementAttr JSON parse failed: %s" ex.Message))
                | ex -> Error(RenderError.BackendError ex.Message)

    /// xml:ids generated by Verovio's `<expansion>` resolution for the
    /// given source element.
    member _.GetExpansionIdsForElement(elementId: string | null) : Result<string[], RenderError> =
        checkDisposed ()

        match elementId with
        | null -> Error(RenderError.RenderFailed "elementId cannot be null")
        | s when String.IsNullOrWhiteSpace s -> Error(RenderError.RenderFailed "elementId cannot be empty")
        | s ->
            if Interop.vrvToolkit_getPageCount handle = 0 then
                Error RenderError.NoDocumentLoaded
            else
                try
                    match Interop.ptrToStringOrNull (Interop.vrvToolkit_getExpansionIdsForElement (handle, s)) with
                    | null -> Ok [||]
                    | json ->
                        use doc = JsonDocument.Parse(json: string)
                        let root = doc.RootElement

                        if root.ValueKind = JsonValueKind.Array then
                            let ids =
                                root.EnumerateArray()
                                |> Seq.choose (fun el ->
                                    if el.ValueKind = JsonValueKind.String then
                                        match el.GetString() with
                                        | null -> None
                                        | s -> Some s
                                    else
                                        None)
                                |> Seq.toArray

                            Ok ids
                        else
                            Ok [||]
                with
                | :? JsonException as ex ->
                    Error(
                        RenderError.RenderFailed(sprintf "getExpansionIdsForElement JSON parse failed: %s" ex.Message)
                    )
                | ex -> Error(RenderError.BackendError ex.Message)

    /// Given an expansion-introduced element id, returns the
    /// originating source-MEI element's xml:id (or `Error
    /// ElementNotFound`).
    member _.GetNotatedIdForElement(elementId: string | null) : Result<string, RenderError> =
        checkDisposed ()

        match elementId with
        | null -> Error(RenderError.RenderFailed "elementId cannot be null")
        | s when String.IsNullOrWhiteSpace s -> Error(RenderError.RenderFailed "elementId cannot be empty")
        | s ->
            if Interop.vrvToolkit_getPageCount handle = 0 then
                Error RenderError.NoDocumentLoaded
            else
                try
                    match Interop.ptrToStringOrNull (Interop.vrvToolkit_getNotatedIdForElement (handle, s)) with
                    | null -> Error(RenderError.ElementNotFound s)
                    | result when String.IsNullOrWhiteSpace result -> Error(RenderError.ElementNotFound s)
                    | result -> Ok result
                with ex ->
                    Error(RenderError.BackendError ex.Message)

    /// Realised onset time (ms) of the named element. Returns
    /// `Error ElementNotFound` if Verovio reports a negative time
    /// (the upstream "not in timed content" sentinel) — the
    /// underlying wrapper's contract is "negative = absent, zero =
    /// time-zero element"; callers that want to disambiguate
    /// "element actually starts at t=0" from "unknown id, defaulted
    /// to 0" should combine this with `GetPageWithElement` first.
    member _.GetTimeForElement(elementId: string | null) : Result<double, RenderError> =
        checkDisposed ()

        match elementId with
        | null -> Error(RenderError.RenderFailed "elementId cannot be null")
        | s when String.IsNullOrWhiteSpace s -> Error(RenderError.RenderFailed "elementId cannot be empty")
        | s ->
            if Interop.vrvToolkit_getPageCount handle = 0 then
                Error RenderError.NoDocumentLoaded
            else
                try
                    let time = Interop.vrvToolkit_getTimeForElement (handle, s)

                    if time < 0.0 then
                        Error(RenderError.ElementNotFound s)
                    else
                        Ok time
                with ex ->
                    Error(RenderError.BackendError ex.Message)

    /// Score-time + real-time onset/offset pairs for the named element.
    /// One entry per sounding instance (expansion can sound an element
    /// more than once).
    member _.GetTimesForElement(elementId: string | null) : Result<ElementTimes, RenderError> =
        checkDisposed ()

        match elementId with
        | null -> Error(RenderError.RenderFailed "elementId cannot be null")
        | s when String.IsNullOrWhiteSpace s -> Error(RenderError.RenderFailed "elementId cannot be empty")
        | s ->
            if Interop.vrvToolkit_getPageCount handle = 0 then
                Error RenderError.NoDocumentLoaded
            else
                try
                    match Interop.ptrToStringOrNull (Interop.vrvToolkit_getTimesForElement (handle, s)) with
                    | null -> Error(RenderError.ElementNotFound s)
                    | json -> Toolkit.parseElementTimes json
                with
                | :? JsonException as ex ->
                    Error(RenderError.RenderFailed(sprintf "getTimesForElement JSON parse failed: %s" ex.Message))
                | ex -> Error(RenderError.BackendError ex.Message)

    /// Verovio's descriptive-features summary for the loaded document,
    /// as raw JSON. The upstream schema is version-tied and richer
    /// than a single-record fit; typing it deliberately is a follow-up
    /// phase. `optionsJson` may be `null` or `"{}"` for defaults.
    member _.GetDescriptiveFeatures(optionsJson: string | null) : Result<string, RenderError> =
        checkDisposed ()

        if Interop.vrvToolkit_getPageCount handle = 0 then
            Error RenderError.NoDocumentLoaded
        else
            let opts =
                match optionsJson with
                | null -> "{}"
                | s -> s

            try
                match Interop.ptrToStringOrNull (Interop.vrvToolkit_getDescriptiveFeatures (handle, opts)) with
                | null -> Error(RenderError.RenderFailed "vrvToolkit_getDescriptiveFeatures returned null")
                | s -> Ok s
            with ex ->
                Error(RenderError.BackendError ex.Message)

    // ── Editor surface (raw passthrough) ───────────────────────────────

    /// Apply an editor action described by a JSON object
    /// (`{action: "drag"|"insert"|"delete"|"set", param: {…}}`).
    /// Returns `Error InvalidEditorAction` if Verovio rejects the
    /// JSON.
    ///
    /// Phase 49 ships raw-string passthrough only; a typed
    /// `EditorAction` DU lands in a follow-up phase. See the
    /// `EditorAction` type doc.
    member _.Edit(action: EditorAction) : Result<unit, RenderError> =
        checkDisposed ()

        try
            if Interop.vrvToolkit_edit (handle, action.Json) then
                Ok()
            else
                Error(RenderError.InvalidEditorAction "vrvToolkit_edit returned false")
        with ex ->
            Error(RenderError.BackendError ex.Message)

    /// `Edit` variant raising `VerovioException` on failure.
    member this.EditOrThrow(action: EditorAction) : unit =
        match this.Edit(action) with
        | Ok() -> ()
        | Error err -> raise (VerovioException(sprintf "Edit failed: %A" err, err))

    /// JSON describing the result of the last `Edit` call
    /// (`{status: "OK"|"FAILURE", message: "…"}` per upstream
    /// convention).
    member _.EditInfo() : string =
        checkDisposed ()

        match Interop.ptrToStringOrNull (Interop.vrvToolkit_editInfo handle) with
        | null -> ""
        | s -> s

    /// Constrain subsequent renders to a JSON-described selection
    /// (measure range / time range / element list). Returns
    /// `Error InvalidEditorAction` if the JSON is malformed.
    member _.Select(selectionJson: string | null) : Result<unit, RenderError> =
        checkDisposed ()

        match selectionJson with
        | null -> Error(RenderError.InvalidEditorAction "selectionJson cannot be null")
        | s when String.IsNullOrWhiteSpace s -> Error(RenderError.InvalidEditorAction "selectionJson cannot be empty")
        | s ->
            try
                if Interop.vrvToolkit_select (handle, s) then
                    Ok()
                else
                    Error(RenderError.InvalidEditorAction "vrvToolkit_select returned false")
            with ex ->
                Error(RenderError.BackendError ex.Message)

    /// `Select` variant raising `VerovioException` on failure.
    member this.SelectOrThrow(selectionJson: string | null) : unit =
        match this.Select(selectionJson) with
        | Ok() -> ()
        | Error err -> raise (VerovioException(sprintf "Select failed: %A" err, err))

    // ── Validation ─────────────────────────────────────────────────────

    /// Validate a Plaine and Easie document. Verovio reports errors +
    /// warnings keyed by line / token in a JSON object; the result
    /// flags `IsValid` true only when the report carries zero errors.
    member _.ValidatePae(data: string | null) : Result<PaeValidationReport, RenderError> =
        checkDisposed ()

        match data with
        | null -> Error(RenderError.RenderFailed "data cannot be null")
        | s when String.IsNullOrWhiteSpace s -> Error(RenderError.RenderFailed "data cannot be empty")
        | s ->
            try
                match Interop.ptrToStringOrNull (Interop.vrvToolkit_validatePAE (handle, s)) with
                | null -> Error(RenderError.RenderFailed "vrvToolkit_validatePAE returned null")
                | json -> Ok(Toolkit.parsePaeReport json)
            with ex ->
                Error(RenderError.BackendError ex.Message)

    /// `ValidatePae` variant raising `VerovioException` on failure.
    member this.ValidatePaeOrThrow(data: string | null) : PaeValidationReport =
        match this.ValidatePae(data) with
        | Ok r -> r
        | Error err -> raise (VerovioException(sprintf "ValidatePae failed: %A" err, err))

    /// Validate a PAE file. Same report shape as `ValidatePae`.
    member _.ValidatePaeFile(path: string | null) : Result<PaeValidationReport, RenderError> =
        checkDisposed ()

        match path with
        | null -> Error(RenderError.RenderFailed "path cannot be null")
        | p when String.IsNullOrWhiteSpace p -> Error(RenderError.RenderFailed "path cannot be empty")
        | p when not (File.Exists p) -> Error(RenderError.RenderFailed(sprintf "PAE file does not exist: %s" p))
        | p ->
            try
                match Interop.ptrToStringOrNull (Interop.vrvToolkit_validatePAEFile (handle, p)) with
                | null -> Error(RenderError.RenderFailed "vrvToolkit_validatePAEFile returned null")
                | json -> Ok(Toolkit.parsePaeReport json)
            with ex ->
                Error(RenderError.BackendError ex.Message)

    /// `ValidatePaeFile` variant raising `VerovioException` on failure.
    member this.ValidatePaeFileOrThrow(path: string | null) : PaeValidationReport =
        match this.ValidatePaeFile(path) with
        | Ok r -> r
        | Error err -> raise (VerovioException(sprintf "ValidatePaeFile failed: %A" err, err))

    // ── Per-toolkit log ────────────────────────────────────────────────

    /// Drain this toolkit's accumulated log buffer (only meaningful
    /// when `VerovioLogging.EnableBuffer(true)` has been set).
    member _.DrainLog() : string =
        checkDisposed ()

        match Interop.ptrToStringOrNull (Interop.vrvToolkit_getLog handle) with
        | null -> ""
        | s -> s

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

    /// PDF is unsupported by upstream's c_wrapper.h surface — returns
    /// `None` so SetOutputTo can flag UnsupportedOutputFormat instead
    /// of silently asking Verovio to switch to a format it can't
    /// render.
    static member private outputFormatToString(fmt: OutputFormat) : string option =
        match fmt with
        | OutputFormat.Svg -> Some "svg"
        | OutputFormat.Mei -> Some "mei"
        | OutputFormat.Midi -> Some "midi"
        | OutputFormat.Pae -> Some "pae"
        | OutputFormat.Humdrum -> Some "humdrum"
        | OutputFormat.Pdf -> None

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

        let footerJson =
            match options.Footer with
            | FooterDisplay.Auto -> ""
            | FooterDisplay.None -> ",\"footer\":\"none\""
            | FooterDisplay.Always -> ",\"footer\":\"always\""
            | FooterDisplay.Encoded -> ",\"footer\":\"encoded\""

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

    /// Parse Verovio's timemap JSON. Upstream emits an array of
    /// per-event objects; each has `tstamp` (or `realTimeMs`),
    /// `qstamp` (or `scoreTimeQuarter`), `on`, `off`, and optional
    /// `tempo`. The exact field naming is version-tied; this parser
    /// tries the v6.x names first and falls back to alternates.
    static member private parseTimemap(json: string) : Result<Timemap, RenderError> =
        use doc = JsonDocument.Parse(json: string)
        let root = doc.RootElement

        if root.ValueKind <> JsonValueKind.Array then
            Error(RenderError.RenderFailed(sprintf "timemap JSON is not an array (kind=%A)" root.ValueKind))
        else
            let readDouble (el: JsonElement) (preferred: string) (fallback: string) : double =
                let mutable prop = Unchecked.defaultof<JsonElement>

                if el.TryGetProperty(preferred, &prop) then
                    prop.GetDouble()
                elif el.TryGetProperty(fallback, &prop) then
                    prop.GetDouble()
                else
                    0.0

            let readStringArray (el: JsonElement) (name: string) : string[] =
                let mutable prop = Unchecked.defaultof<JsonElement>

                if el.TryGetProperty(name, &prop) && prop.ValueKind = JsonValueKind.Array then
                    prop.EnumerateArray()
                    |> Seq.choose (fun e ->
                        if e.ValueKind = JsonValueKind.String then
                            match e.GetString() with
                            | null -> None
                            | s -> Some s
                        else
                            None)
                    |> Seq.toArray
                else
                    [||]

            let readTempo (el: JsonElement) : double option =
                let mutable prop = Unchecked.defaultof<JsonElement>

                if el.TryGetProperty("tempo", &prop) && prop.ValueKind = JsonValueKind.Number then
                    Some(prop.GetDouble())
                else
                    None

            let entries =
                root.EnumerateArray()
                |> Seq.map (fun el ->
                    { RealTimeMs = readDouble el "tstamp" "realTimeMs"
                      ScoreTimeQuarter = readDouble el "qstamp" "scoreTimeQuarter"
                      NotesOn = readStringArray el "on"
                      NotesOff = readStringArray el "off"
                      Tempo = readTempo el })
                |> Seq.toArray

            Ok { Entries = entries }

    /// Parse Verovio's getTimesForElement JSON. Upstream emits a
    /// single object with four parallel arrays
    /// (`scoreTimeOnset` / `scoreTimeOffset` / `realTimeOnsetMilliseconds` /
    /// `realTimeOffsetMilliseconds`); the parser pairs them
    /// element-wise into `ElementTimeInstance` records.
    static member private parseElementTimes(json: string) : Result<ElementTimes, RenderError> =
        use doc = JsonDocument.Parse(json: string)
        let root = doc.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            Error(RenderError.RenderFailed(sprintf "getTimesForElement JSON is not an object (kind=%A)" root.ValueKind))
        else
            let readDoubleArray (name: string) : double[] =
                let mutable prop = Unchecked.defaultof<JsonElement>

                if root.TryGetProperty(name, &prop) && prop.ValueKind = JsonValueKind.Array then
                    prop.EnumerateArray()
                    |> Seq.choose (fun e ->
                        if e.ValueKind = JsonValueKind.Number then
                            Some(e.GetDouble())
                        else
                            None)
                    |> Seq.toArray
                else
                    [||]

            let scoreOn = readDoubleArray "scoreTimeOnset"
            let scoreOff = readDoubleArray "scoreTimeOffset"
            let realOn = readDoubleArray "realTimeOnsetMilliseconds"
            let realOff = readDoubleArray "realTimeOffsetMilliseconds"

            let n =
                [| scoreOn.Length; scoreOff.Length; realOn.Length; realOff.Length |]
                |> Array.max

            // Pad short arrays with the first element (or 0.0 if empty)
            // so we always produce well-formed instances even if upstream
            // is inconsistent in a future version. Defensive — current
            // upstream emits arrays of equal length.
            let safeGet (arr: double[]) (i: int) : double =
                if arr.Length = 0 then 0.0
                elif i < arr.Length then arr[i]
                else arr[arr.Length - 1]

            let instances =
                [| for i in 0 .. n - 1 ->
                       { ScoreTimeOnset = safeGet scoreOn i
                         ScoreTimeOffset = safeGet scoreOff i
                         RealTimeOnsetMs = safeGet realOn i
                         RealTimeOffsetMs = safeGet realOff i } |]

            Ok { Instances = instances }

    /// Parse Verovio's PAE-validation report. Sets `IsValid` true only
    /// when the report carries zero errors (warnings do not flip the
    /// flag).
    static member private parsePaeReport(json: string) : PaeValidationReport =
        try
            use doc = JsonDocument.Parse(json: string)
            let root = doc.RootElement

            // Verovio's validatePAE report typically has a top-level
            // "errors" object keyed by line/token; we flag invalid the
            // moment we see any error entry. Some upstream variants
            // surface a top-level "status" string instead — handle
            // both.
            let mutable errs = Unchecked.defaultof<JsonElement>
            let mutable status = Unchecked.defaultof<JsonElement>

            let hasErrors =
                if root.TryGetProperty("errors", &errs) then
                    match errs.ValueKind with
                    | JsonValueKind.Object -> errs.EnumerateObject() |> Seq.isEmpty |> not
                    | JsonValueKind.Array -> errs.GetArrayLength() > 0
                    | _ -> false
                else
                    false

            let statusBad =
                if
                    root.TryGetProperty("status", &status)
                    && status.ValueKind = JsonValueKind.String
                then
                    match status.GetString() with
                    | null -> false
                    | s -> not (s.Equals("OK", StringComparison.OrdinalIgnoreCase))
                else
                    false

            { IsValid = not (hasErrors || statusBad)
              RawJson = json }
        with _ ->
            // Unparseable report — surface as invalid + raw text.
            { IsValid = false; RawJson = json }
