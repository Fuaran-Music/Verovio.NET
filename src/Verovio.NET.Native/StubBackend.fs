namespace Verovio.NET.Native

open System
open Verovio.NET

// ============================================================================
//  NativeBackend — placeholder implementation. Throws
//  NotImplementedException from every IVerovioBackend method.
//
//  The Native backend is deferred indefinitely: Wasmtime's runtime cost
//  for Verovio's ~3 MB blob is acceptable at ScaleMastery-class load
//  profiles, and shipping a P/Invoke backend means maintaining a multi-
//  RID build matrix (win-x64 / linux-x64 / linux-arm64 / osx-x64 /
//  osx-arm64) including the upstream Verovio C++ build. We only revisit
//  if profiling at sight-reading.ai-class scale shows Wasmtime is the
//  bottleneck.
//
//  The placeholder ships so the package coordinate exists in the NuGet
//  feed (squatting on it; reserving the name) and so consumers writing
//  backend-agnostic code can reference it without conditional package
//  references.
// ============================================================================

/// Placeholder Native backend. Implementation deferred — see
/// `roadmap/phases/03-verovio-net-scaffold.md` and the Verovio.NET
/// sibling-role notes in workspace CLAUDE.md. Calling any method on the
/// toolkit returned by this backend throws `NotImplementedException`.
type NativeBackend internal () =
    static let deferredMessage =
        "Verovio.NET.Native backend is deferred. The Wasm backend (Verovio.NET.Wasm) is the MVP-shipped backend; "
        + "a P/Invoke implementation over a native libverovio build is not provided. "
        + "We revisit if profiling at sight-reading.ai-class scale shows Wasmtime is the bottleneck."

    interface IVerovioBackend with
        member _.Name = "Native (stub)"

        member _.LoadData(_input: string, _options: LoadOptions) : Result<unit, LoadError> =
            raise (NotImplementedException deferredMessage)

        member _.GetDocumentInfo() : Result<DocumentInfo, RenderError> =
            raise (NotImplementedException deferredMessage)

        member _.RenderToSvg(_pageNumber: int, _options: RenderOptions) : Result<string, RenderError> =
            raise (NotImplementedException deferredMessage)

        member _.RenderToPdf(_options: PdfOptions) : Result<byte[], RenderError> =
            raise (NotImplementedException deferredMessage)

        member _.GetMei() : Result<string, RenderError> =
            raise (NotImplementedException deferredMessage)

        member _.RenderToMidi() : Result<byte[], RenderError> =
            raise (NotImplementedException deferredMessage)

        member _.GetElementsAtTime(_timeMs: int) : Result<string[], RenderError> =
            raise (NotImplementedException deferredMessage)

        member _.GetMidiValuesForElement(_elementId: string) : Result<MidiValues, RenderError> =
            raise (NotImplementedException deferredMessage)

    interface IDisposable with
        member _.Dispose() = ()

[<RequireQualifiedAccess>]
module NativeBackend =
    /// Construct a Native backend stub. Throws `NotImplementedException`
    /// on every operation — the package coordinate exists for consumers
    /// writing backend-agnostic code, but the implementation is deferred.
    let create () : IVerovioBackend = new NativeBackend() :> IVerovioBackend
