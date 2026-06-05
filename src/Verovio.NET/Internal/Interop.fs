namespace Verovio.NET.Internal

open System
open System.Runtime.InteropServices

// ============================================================================
//  Interop — DllImport bindings against upstream Verovio's
//  `tools/c_wrapper.h` (see
//  https://github.com/rism-digital/verovio/blob/develop/tools/c_wrapper.h)
//
//  The C wrapper is the same surface upstream's own Emscripten build and
//  Go bindings consume; this is a proven, stable ABI across Verovio
//  versions.
//
//  String marshalling:
//    * Input strings (const char*) are marshalled as
//      [<MarshalAs(UnmanagedType.LPUTF8Str)>] string — the marshaler
//      copies into a temp UTF-8 buffer.
//    * Output strings (const char* return values) are returned as
//      `nativeint` and converted via `ptrToStringOrNull` below. This
//      keeps the nullable case explicit at the F# call site
//      (`string | null`) rather than relying on attribute-driven
//      nullness inference, which isn't yet ergonomic in F# 10 extern
//      declarations.
//
//  The returned `const char*` is owned by Verovio and freed on the next
//  toolkit call; `Marshal.PtrToStringUTF8` copies the bytes into a
//  managed string at the call boundary so the caller doesn't have to
//  worry about lifetime.
//
//  `void *tkPtr` is marshalled as `nativeint` (an opaque handle); the
//  managed side never dereferences it.
//
//  CallingConvention is `Cdecl` for all functions; this matches the
//  default `extern "C"` ABI on x64 Windows and Unix-like platforms.
//
//  The library name is `libverovio` (no extension); the .NET runtime
//  searches for `libverovio.dll` on Windows, `libverovio.so` on Linux,
//  `libverovio.dylib` on macOS. The win-x64 build is vendored under
//  `runtimes/win-x64/native/`; other RIDs land in follow-up phases.
//
//  Phase 04 surface: enough of c_wrapper.h to implement the public
//  `Toolkit` shape (constructor*, destructor, loadData, setOptions,
//  setInputFrom, renderToSVG, renderToMIDI, getMEI, getPageCount,
//  getElementsAtTime, getMIDIValuesForElement, getVersion). The
//  remaining ~30 wrapper functions land as subsequent phases need them.
// ============================================================================

/// DllImport surface against `libverovio` (the vendored upstream native
/// library). Internal — consumers go through the public `Toolkit` class.
module Interop =

    [<Literal>]
    let private LibraryName = "libverovio"

    // ── Lifecycle ────────────────────────────────────────────────────────

    /// Construct a toolkit with default resource path resolution.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_constructor()

    /// Construct a toolkit with an explicit resource path (where Verovio
    /// looks for SMuFL glyph fonts + supplementary resources).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_constructorResourcePath([<MarshalAs(UnmanagedType.LPUTF8Str)>] string resourcePath)

    /// Construct a toolkit without loading any resources (smaller
    /// footprint; only the analysis API works — no SVG render).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_constructorNoResource()

    /// Release a toolkit handle and its associated resources.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern void vrvToolkit_destructor(nativeint tkPtr)

    // ── Document loading ────────────────────────────────────────────────

    /// Load a score document from an in-memory string. The input format
    /// is whatever the toolkit's current `inputFrom` option says (set via
    /// `vrvToolkit_setOptions` or `vrvToolkit_setInputFrom`).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_loadData(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string data)

    /// Set the input format by string name (mei, musicxml, humdrum, pae,
    /// abc). Cheaper than the JSON-options path when only the input
    /// format needs to change.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_setInputFrom(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string inputFrom)

    // ── Options ─────────────────────────────────────────────────────────

    /// Set toolkit options via a JSON string. The schema is the same as
    /// the upstream toolkit's `--option` flag and the JS toolkit's
    /// `setOptions` parameter.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_setOptions(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string options)

    /// Reset all toolkit options to their defaults.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern void vrvToolkit_resetOptions(nativeint tkPtr)

    /// Re-run document layout against the current options. Required
    /// after a `setOptions` call that changes a layout-affecting
    /// option (e.g. `breaks`, `pageWidth`, `scale`) AFTER `loadData`
    /// has already computed the initial layout. Returns true on
    /// success.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_redoLayout(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string options)

    // ── Rendering ───────────────────────────────────────────────────────

    /// Render a single page to SVG. Returns a `const char*` owned by
    /// Verovio; caller converts via `ptrToStringOrNull`.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_renderToSVG(nativeint tkPtr, int pageNo, bool xmlDeclaration)

    /// Render the full document to a Standard MIDI File (base64-encoded
    /// per Verovio convention).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_renderToMIDI(nativeint tkPtr)

    /// Round-trip the loaded document back out as MEI. `options` is a
    /// JSON option string (empty `"{}"` for defaults).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getMEI(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string options)

    // ── Document queries ────────────────────────────────────────────────

    /// Page count of the loaded document. Returns 0 if no document is
    /// loaded.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int vrvToolkit_getPageCount(nativeint tkPtr)

    /// Query elements sounding at a given time offset (milliseconds since
    /// document start). Returns a JSON array of MEI element xml:ids.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getElementsAtTime(nativeint tkPtr, int millisec)

    /// Look up MIDI values (pitch + duration + start time) for an MEI
    /// element by xml:id. Returns a JSON object.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getMIDIValuesForElement(
        nativeint tkPtr,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string xmlId
    )

    // ── Version ─────────────────────────────────────────────────────────

    /// Upstream Verovio version string of the linked libverovio.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getVersion(nativeint tkPtr)

    // ── String marshalling helper ───────────────────────────────────────

    /// Convert an unmanaged UTF-8 string pointer to a managed string,
    /// or to `null` if the pointer is the null pointer. The bytes are
    /// copied; the caller doesn't take ownership of the unmanaged
    /// buffer.
    let ptrToStringOrNull (ptr: nativeint) : string | null =
        if ptr = nativeint 0 then
            null
        else
            Marshal.PtrToStringUTF8 ptr

    // ── Library availability probe ──────────────────────────────────────

    /// Probe whether libverovio is loadable on the current process. Used
    /// by the public Toolkit class to surface a clean `Error
    /// NativeLibraryUnavailable` instead of letting `DllNotFoundException`
    /// escape into consumer code.
    ///
    /// Implementation calls `vrvToolkit_constructorNoResource` + the
    /// destructor; success means the library is loadable.
    let probeAvailability () : Result<unit, string> =
        try
            let handle = vrvToolkit_constructorNoResource ()

            if handle = nativeint 0 then
                Error "libverovio loaded but vrvToolkit_constructorNoResource returned a null handle"
            else
                vrvToolkit_destructor handle
                Ok()
        with
        | :? DllNotFoundException as ex -> Error(sprintf "libverovio not found on the current RID: %s" ex.Message)
        | :? EntryPointNotFoundException as ex ->
            Error(sprintf "libverovio loaded but a required entry point is missing: %s" ex.Message)
        | :? BadImageFormatException as ex ->
            Error(sprintf "libverovio is present but unloadable (architecture mismatch?): %s" ex.Message)
        | ex -> Error(sprintf "libverovio probe raised an unexpected exception: %s" ex.Message)
