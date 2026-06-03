namespace Verovio.NET

open System

// ============================================================================
//  Verovio — idiomatic F# wrappers above `IVerovioBackend`. This is the
//  stable public API; consumers should call through this module and
//  never touch backend implementations directly.
//
//  The wrappers are thin — most of the work (option marshalling, toolkit
//  invocation, failure translation) lives in the backend. The module's
//  job is to:
//
//    * Hold the backend handle in an opaque `Toolkit` wrapper so the
//      public surface doesn't leak the `IVerovioBackend` interface to
//      consumers who shouldn't be substituting it.
//    * Surface `Result`-returning entry points for all fallible
//      operations, with closed-DU error types (no string-typed messages
//      escape this layer).
//    * Implement `IDisposable` cleanly so consumers can use `use` to
//      bound the toolkit's lifetime.
// ============================================================================

/// Opaque handle to a Verovio toolkit instance bound to a backend.
/// Construct with `Verovio.create`; dispose with `use` or
/// `Verovio.dispose`.
type Toolkit private (backend: IVerovioBackend) =
    member internal _.Backend = backend

    interface IDisposable with
        member _.Dispose() = backend.Dispose()

    static member internal Wrap(backend: IVerovioBackend) = new Toolkit(backend)

/// Public API for the Verovio toolkit. All entry points dispatch to the
/// bound `IVerovioBackend`. Fallible operations return `Result<_, _>`
/// with closed-DU error types; no exceptions are raised by this module's
/// own logic (backend implementations may, but those translate into
/// `Result.Error` cases at the boundary).
[<RequireQualifiedAccess>]
module Verovio =

    /// Wrap a backend in a `Toolkit`. The toolkit owns the backend's
    /// lifetime; dispose the toolkit (or rely on `use`) to release.
    let create (backend: IVerovioBackend) : Toolkit = Toolkit.Wrap(backend)

    /// The backend's display name (e.g. "Wasm", "Native"). For
    /// diagnostics and logging.
    let backendName (toolkit: Toolkit) : string = toolkit.Backend.Name

    /// Load a score document into the toolkit. Subsequent renders /
    /// queries operate on this document. The `input` parameter accepts
    /// `null` for defence against C# callers; F# callers should pass a
    /// non-null string.
    let loadData (toolkit: Toolkit) (options: LoadOptions) (input: string | null) : Result<unit, LoadError> =
        match input with
        | null -> Error(LoadError.ParseFailed "input cannot be null")
        | s when String.IsNullOrWhiteSpace s -> Error(LoadError.ParseFailed "input cannot be empty or whitespace")
        | s -> toolkit.Backend.LoadData(s, options)

    /// Structural summary of the currently-loaded document.
    let getDocumentInfo (toolkit: Toolkit) : Result<DocumentInfo, RenderError> = toolkit.Backend.GetDocumentInfo()

    /// Render a single page to SVG. Page indices are 1-based.
    let renderToSvg (toolkit: Toolkit) (options: RenderOptions) (pageNumber: int) : Result<string, RenderError> =
        if pageNumber < 1 then
            Error(RenderError.PageOutOfRange(pageNumber, 0))
        else
            toolkit.Backend.RenderToSvg(pageNumber, options)

    /// Render the full document to PDF bytes (multi-page).
    let renderToPdf (toolkit: Toolkit) (options: PdfOptions) : Result<byte[], RenderError> =
        toolkit.Backend.RenderToPdf(options)

    /// Round-trip the loaded document back out as MEI.
    let getMei (toolkit: Toolkit) : Result<string, RenderError> = toolkit.Backend.GetMei()

    /// Render the loaded document to a Standard MIDI File.
    let renderToMidi (toolkit: Toolkit) : Result<byte[], RenderError> = toolkit.Backend.RenderToMidi()

    /// Look up MEI element IDs sounding at a given time offset (ms).
    let getElementsAtTime (toolkit: Toolkit) (timeMs: int) : Result<string[], RenderError> =
        if timeMs < 0 then
            Error(RenderError.RenderFailed(sprintf "timeMs must be non-negative (got %d)" timeMs))
        else
            toolkit.Backend.GetElementsAtTime(timeMs)

    /// Look up MIDI values (pitch, duration, start time) for an MEI
    /// element by `xml:id`. Accepts `null` for defence against C#
    /// callers.
    let getMidiValuesForElement (toolkit: Toolkit) (elementId: string | null) : Result<MidiValues, RenderError> =
        match elementId with
        | null -> Error(RenderError.RenderFailed "elementId cannot be null")
        | s when String.IsNullOrWhiteSpace s -> Error(RenderError.RenderFailed "elementId cannot be empty")
        | s -> toolkit.Backend.GetMidiValuesForElement(s)

    /// Convenience: dispose the toolkit (equivalent to `(toolkit :>
    /// IDisposable).Dispose()`). Prefer `use` over an explicit call.
    let dispose (toolkit: Toolkit) : unit = (toolkit :> IDisposable).Dispose()
