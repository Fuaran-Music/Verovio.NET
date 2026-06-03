namespace Verovio.NET

open System

// ============================================================================
//  IVerovioBackend — the substitution seam between the public API and a
//  concrete Verovio runtime. The Wasm backend (Verovio.NET.Wasm) and the
//  future Native backend (Verovio.NET.Native) each implement this
//  interface; the `Verovio` module dispatches through it.
//
//  Method shape mirrors the upstream Verovio Toolkit's public method
//  surface (see https://book.verovio.org/toolkit-reference/toolkit-methods.html).
//  Backend implementations are responsible for:
//
//    * Process / instance lifecycle (the upstream toolkit is a stateful
//      object; backends own its instantiation and disposal).
//    * Marshalling option records to the toolkit's native option shape
//      (the wasm backend serialises to JSON; the native backend
//      P/Invokes through a C string buffer).
//    * Translating toolkit failures into the `LoadError` / `RenderError`
//      DUs defined in `Types.fs`.
//
//  The interface is `IDisposable` because every concrete backend owns
//  unmanaged resources (a wasm instance, a P/Invoke handle); the public
//  `Verovio` module surfaces `dispose` accordingly.
// ============================================================================

/// Substitution seam between the public `Verovio` module and a concrete
/// Verovio runtime. Implementations live in `Verovio.NET.Wasm` and
/// (future) `Verovio.NET.Native`. Consumer code should not depend on this
/// interface directly; the `Verovio` module is the stable public API.
type IVerovioBackend =
    inherit IDisposable

    /// Backend identifier for diagnostics ("Wasm" / "Native" / custom).
    abstract member Name: string

    /// Load a score document into the backend's toolkit instance.
    /// Subsequent renders / queries operate on this document until
    /// `loadData` is called again. Multi-document workflows should pool
    /// backends.
    abstract member LoadData: input: string * options: LoadOptions -> Result<unit, LoadError>

    /// Structural summary of the currently-loaded document. Fails with
    /// `NoDocumentLoaded` if no document is loaded.
    abstract member GetDocumentInfo: unit -> Result<DocumentInfo, RenderError>

    /// Render a single page to SVG. Page indices are 1-based per
    /// Verovio's convention. `pageNumber` outside `[1, pageCount]` fails
    /// with `PageOutOfRange`.
    abstract member RenderToSvg: pageNumber: int * options: RenderOptions -> Result<string, RenderError>

    /// Render the full document to PDF bytes (multi-page). Used by the
    /// Worksheet builder export path in downstream consumers.
    abstract member RenderToPdf: options: PdfOptions -> Result<byte[], RenderError>

    /// Round-trip the loaded document back out as MEI. Verovio may
    /// normalise + add layout information during the round-trip; the
    /// returned MEI is canonical for the toolkit's interpretation.
    abstract member GetMei: unit -> Result<string, RenderError>

    /// Render the loaded document to a Standard MIDI File.
    abstract member RenderToMidi: unit -> Result<byte[], RenderError>

    /// Query elements at a given time offset (in milliseconds since the
    /// start of the score). Returns the MEI `xml:id` of each element
    /// sounding at that time. Backing for playback synchronisation
    /// (highlight the currently-sounding note).
    abstract member GetElementsAtTime: timeMs: int -> Result<string[], RenderError>

    /// Look up the MIDI values (pitch, duration in ms, start time in ms)
    /// for an element by its MEI `xml:id`. Backing for click-to-play
    /// interactions.
    abstract member GetMidiValuesForElement: elementId: string -> Result<MidiValues, RenderError>

/// MIDI realisation of a single Verovio element. Returned by
/// `IVerovioBackend.GetMidiValuesForElement`.
and [<Struct>] MidiValues =
    { Pitch: byte
      Duration: int
      Time: int }
