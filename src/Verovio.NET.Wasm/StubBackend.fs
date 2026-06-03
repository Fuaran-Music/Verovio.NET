namespace Verovio.NET.Wasm

open System
open Verovio.NET

// ============================================================================
//  WasmBackend — placeholder implementation at Phase 03. Throws
//  NotImplementedException from every IVerovioBackend method so downstream
//  consumers can wire against the package today; Phase 04 swaps this
//  module for a Wasmtime-hosted implementation of the upstream
//  verovio-toolkit-wasm blob.
//
//  Public surface (the `create` function + the `WasmBackend` type) is
//  the same as what Phase 04 will ship; consumer code written against
//  this stub will not need to change.
// ============================================================================

/// Placeholder Wasm backend. Implementation pending Phase 04 — see
/// `roadmap/phases/04-verovio-net-wasm-backend.md`. Calling any method on
/// the toolkit returned by this backend throws
/// `NotImplementedException` with a descriptive message.
type WasmBackend internal () =
    static let pendingMessage =
        "Verovio.NET.Wasm backend implementation lands in Phase 04 (see roadmap/phases/04-verovio-net-wasm-backend.md). "
        + "At Phase 03, Verovio.NET.Wasm ships only the public-API shape so downstream consumers can wire against it; "
        + "the Wasmtime-hosted verovio-toolkit-wasm runtime is not yet provided."

    interface IVerovioBackend with
        member _.Name = "Wasm (stub)"

        member _.LoadData(_input: string, _options: LoadOptions) : Result<unit, LoadError> =
            raise (NotImplementedException pendingMessage)

        member _.GetDocumentInfo() : Result<DocumentInfo, RenderError> =
            raise (NotImplementedException pendingMessage)

        member _.RenderToSvg(_pageNumber: int, _options: RenderOptions) : Result<string, RenderError> =
            raise (NotImplementedException pendingMessage)

        member _.RenderToPdf(_options: PdfOptions) : Result<byte[], RenderError> =
            raise (NotImplementedException pendingMessage)

        member _.GetMei() : Result<string, RenderError> =
            raise (NotImplementedException pendingMessage)

        member _.RenderToMidi() : Result<byte[], RenderError> =
            raise (NotImplementedException pendingMessage)

        member _.GetElementsAtTime(_timeMs: int) : Result<string[], RenderError> =
            raise (NotImplementedException pendingMessage)

        member _.GetMidiValuesForElement(_elementId: string) : Result<MidiValues, RenderError> =
            raise (NotImplementedException pendingMessage)

    interface IDisposable with
        member _.Dispose() = ()

[<RequireQualifiedAccess>]
module WasmBackend =
    /// Construct a Wasm backend. At Phase 03 this returns a stub that
    /// throws on every operation; Phase 04 swaps in a real
    /// implementation behind the same signature.
    let create () : IVerovioBackend = new WasmBackend() :> IVerovioBackend
