namespace Verovio.NET.Internal

open System
open System.Runtime.InteropServices

// ============================================================================
//  Interop — DllImport bindings against upstream Verovio's
//  `tools/c_wrapper.h` (see
//  https://github.com/rism-digital/verovio/blob/version-6.2.0/tools/c_wrapper.h).
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
//  Phase 49 (Verovio.NET phase 05): full c_wrapper.h coverage — all 61
//  upstream functions are declared below. The Phase 04 surface was
//  demand-driven (16/61); the bind-out is now completeness-driven so
//  consumers have the entire upstream toolkit available without
//  upstream-side patches.
// ============================================================================

/// DllImport surface against `libverovio` (the vendored upstream native
/// library). Internal — consumers go through the public `Toolkit` class.
module Interop =

    [<Literal>]
    let private LibraryName = "libverovio"

    // ── Native resolver ─────────────────────────────────────────────────
    //
    // The vendored `libverovio` ships under `runtimes/<rid>/native/` to
    // match the NuGet packaging convention. When `Verovio.NET` is
    // consumed as a NuGet package, the .NET runtime populates the
    // deps.json `runtimeTargets` for that asset and resolves it
    // automatically. When `Verovio.NET` is consumed via `ProjectReference`
    // (typical for in-repo dev + sibling consumer apps), no
    // `runtimeTargets` entry is generated — the runtime then only probes
    // the application's base directory, so P/Invoke fails with
    // `DllNotFoundException` even though the DLL is sitting at
    // `bin/.../runtimes/win-x64/native/libverovio.dll`.
    //
    // The custom resolver below bridges the gap: on first load it
    // probes the conventional `runtimes/<rid>/native/` layout relative
    // to the consuming assembly + AppContext.BaseDirectory, falling
    // through to the default resolver when no vendored copy is found
    // (preserving the NuGet-package code path and any future
    // RID-specific override the consumer wires up).

    let private rid () =
        if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
            match RuntimeInformation.ProcessArchitecture with
            | Architecture.X64 -> "win-x64"
            | Architecture.Arm64 -> "win-arm64"
            | Architecture.X86 -> "win-x86"
            | a -> $"win-{a}".ToLowerInvariant()
        elif RuntimeInformation.IsOSPlatform OSPlatform.Linux then
            match RuntimeInformation.ProcessArchitecture with
            | Architecture.X64 -> "linux-x64"
            | Architecture.Arm64 -> "linux-arm64"
            | a -> $"linux-{a}".ToLowerInvariant()
        elif RuntimeInformation.IsOSPlatform OSPlatform.OSX then
            match RuntimeInformation.ProcessArchitecture with
            | Architecture.X64 -> "osx-x64"
            | Architecture.Arm64 -> "osx-arm64"
            | a -> $"osx-{a}".ToLowerInvariant()
        else
            ""

    let private nativeFileName () =
        if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
            "libverovio.dll"
        elif RuntimeInformation.IsOSPlatform OSPlatform.OSX then
            "libverovio.dylib"
        else
            "libverovio.so"

    let private candidateDirs () : string list =
        let interopAsmDir: string =
            let asm = System.Reflection.Assembly.GetExecutingAssembly()
            let loc = asm.Location

            if String.IsNullOrEmpty loc then
                ""
            else
                match IO.Path.GetDirectoryName loc with
                | null -> ""
                | dir -> dir

        [ AppContext.BaseDirectory; interopAsmDir ]
        |> List.filter (String.IsNullOrEmpty >> not)
        |> List.distinct

    let private tryLoadFrom (dir: string) : nativeint =
        let r = rid ()
        let fileName = nativeFileName ()

        let candidates =
            [ if r <> "" then
                  IO.Path.Combine(dir, "runtimes", r, "native", fileName)
              IO.Path.Combine(dir, fileName) ]

        candidates
        |> List.tryFind IO.File.Exists
        |> Option.bind (fun path ->
            match NativeLibrary.TryLoad path with
            | true, h -> Some h
            | _ -> None)
        |> Option.defaultValue (nativeint 0)

    let private resolver
        (libraryName: string)
        (assembly: System.Reflection.Assembly)
        (searchPath: System.Nullable<DllImportSearchPath>)
        : nativeint =
        if libraryName <> LibraryName then
            nativeint 0
        else
            candidateDirs ()
            |> List.tryPick (fun d ->
                let h = tryLoadFrom d
                if h = nativeint 0 then None else Some h)
            |> Option.defaultValue (nativeint 0)

    let private resolverDelegate = DllImportResolver(resolver)

    /// Registers the custom native-library resolver against the
    /// Verovio.NET assembly. Idempotent — a second registration would
    /// throw `InvalidOperationException`, which we swallow so multiple
    /// AppDomains / load contexts that re-enter this module don't
    /// crash. Called from the `do` initializer below at module load.
    let private registerResolver () =
        try
            let asm = System.Reflection.Assembly.GetExecutingAssembly()
            NativeLibrary.SetDllImportResolver(asm, resolverDelegate)
        with :? InvalidOperationException ->
            ()

    do registerResolver ()

    // ── Global logging toggles (process-wide; not per-toolkit) ──────────
    //
    // Upstream's enableLog / enableLogToBuffer flip C-static flags in
    // the libverovio process; they are not per-Toolkit. The public
    // surface in `VerovioLogging` documents the thread-safety
    // implications — these mutate a global write target shared by
    // every Toolkit instance.

    /// Toggle stderr console logging (process-global).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern void enableLog(bool value)

    /// Toggle in-memory log buffering (process-global). When on,
    /// `vrvToolkit_getLog` returns the accumulated buffer; when off,
    /// log messages are either dropped or routed to stderr depending
    /// on `enableLog`.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern void enableLogToBuffer(bool value)

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

    /// Load a score document from a file path. Upstream reads the file
    /// directly; we surface a `FileNotFound` pre-flight check at the
    /// public API layer so the failure cause is distinguishable from
    /// "parse failed".
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_loadFile(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string filename)

    /// Load a zip-packed MEI archive from a base64-encoded string.
    /// Useful for in-memory delivery of multi-resource MEI bundles
    /// (e.g. MEI + ancillary metadata + glyph overrides).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_loadZipDataBase64(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string data)

    /// Load a zip-packed MEI archive from raw bytes. `length` is the
    /// byte count; data is a raw byte pointer (we marshal a managed
    /// byte[] to a pinned native buffer at the public API layer).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_loadZipDataBuffer(nativeint tkPtr, nativeint data, int length)

    /// Set the input format by string name (mei, musicxml, humdrum, pae,
    /// abc). Cheaper than the JSON-options path when only the input
    /// format needs to change.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_setInputFrom(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string inputFrom)

    /// Set the output format by string name. Mirrors `setInputFrom` for
    /// the output side; affects round-trip via `getMEI` / `getHumdrum`.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_setOutputTo(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string outputTo)

    // ── Options ─────────────────────────────────────────────────────────

    /// Set toolkit options via a JSON string. The schema is the same as
    /// the upstream toolkit's `--option` flag and the JS toolkit's
    /// `setOptions` parameter.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_setOptions(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string options)

    /// Reset all toolkit options to their defaults.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern void vrvToolkit_resetOptions(nativeint tkPtr)

    /// Returns a JSON object describing every option Verovio understands
    /// (name, type, default, range, doc). The introspection entry point
    /// for callers building a dynamic options UI.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getAvailableOptions(nativeint tkPtr)

    /// Returns a JSON object with the default value of every option.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getDefaultOptions(nativeint tkPtr)

    /// Returns the currently-active option set as JSON.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getOptions(nativeint tkPtr)

    /// Returns the human-readable `--help`-style option summary string.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getOptionUsageString(nativeint tkPtr)

    /// Re-run document layout against the current options. Required
    /// after a `setOptions` call that changes a layout-affecting
    /// option (e.g. `breaks`, `pageWidth`, `scale`) AFTER `loadData`
    /// has already computed the initial layout. (Upstream declares
    /// this `void`; the v0.1.x binding mistakenly typed it `bool`.
    /// The Phase 05 sweep corrects the signature.)
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern void vrvToolkit_redoLayout(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string options)

    /// Re-run the pitch / position layout pass for the current page
    /// only. Lighter than a full `redoLayout`; used after an `edit`
    /// that only adjusts pitch / position of existing elements.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern void vrvToolkit_redoPagePitchPosLayout(nativeint tkPtr)

    // ── Scale (top-level convenience) ───────────────────────────────────

    /// Get the current render scale (percent, where 100 = 1:1).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int vrvToolkit_getScale(nativeint tkPtr)

    /// Set the render scale by integer percent. Cheaper than the
    /// JSON-options path when only the scale changes.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_setScale(nativeint tkPtr, int scale)

    // ── Resource path ───────────────────────────────────────────────────

    /// Returns the SMuFL / Verovio-data resource path the toolkit is
    /// currently using.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getResourcePath(nativeint tkPtr)

    /// Update the toolkit's resource-lookup path. Returns false if the
    /// path is not a valid Verovio resource directory.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_setResourcePath(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string path)

    // ── Determinism (xml:id seed) ───────────────────────────────────────

    /// Seed the RNG used to mint xml:ids for elements that lack one in
    /// the source MEI. The same seed against the same source produces
    /// byte-identical SVG output across processes / machines — the
    /// underpinning of snapshot tests and content-addressable engraving
    /// pipelines.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern void vrvToolkit_resetXmlIdSeed(nativeint tkPtr, int seed)

    // ── Rendering — in-memory ──────────────────────────────────────────

    /// Render a single page to SVG. Returns a `const char*` owned by
    /// Verovio; caller converts via `ptrToStringOrNull`.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_renderToSVG(nativeint tkPtr, int pageNo, bool xmlDeclaration)

    /// Render the full document to a Standard MIDI File (base64-encoded
    /// per Verovio convention).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_renderToMIDI(nativeint tkPtr)

    /// Render the loaded document back out as Plaine and Easie code
    /// (monophonic-only ASCII format).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_renderToPAE(nativeint tkPtr)

    /// Render the document's `<expansion>` map as JSON (the
    /// section-expansion plan resolved by Verovio).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_renderToExpansionMap(nativeint tkPtr)

    /// Render the time-map (per-event onset/offset + sounding-element
    /// IDs) as JSON.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_renderToTimemap(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string cOptions)

    /// Round-trip the loaded document back out as MEI. `options` is a
    /// JSON option string (empty `"{}"` for defaults).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getMEI(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string options)

    /// Round-trip the loaded document back out as Humdrum **kern.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getHumdrum(nativeint tkPtr)

    /// One-shot: load `data` + render page 1 to SVG. Convenience for
    /// callers that don't iterate pages.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_renderData(
        nativeint tkPtr,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string data,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string options
    )

    // ── Rendering — to file ────────────────────────────────────────────

    /// Save the current document state to a file (format inferred from
    /// the toolkit's current output-to setting). `c_options` is a JSON
    /// option string (empty `"{}"` for defaults).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_saveFile(
        nativeint tkPtr,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string filename,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string cOptions
    )

    /// Render page `pageNo` to an SVG file at `filename`.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_renderToSVGFile(
        nativeint tkPtr,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string filename,
        int pageNo
    )

    /// Render the full document to a Standard MIDI File at `filename`.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_renderToMIDIFile(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string filename)

    /// Render the document to a PAE file at `filename`.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_renderToPAEFile(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string filename)

    /// Render the expansion map to a JSON file at `filename`.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_renderToExpansionMapFile(
        nativeint tkPtr,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string filename
    )

    /// Render the time-map to a JSON file at `filename`.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_renderToTimemapFile(
        nativeint tkPtr,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string filename,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string cOptions
    )

    /// Write Humdrum output to a file. Sibling to `getHumdrum` for
    /// callers that prefer the file path.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_getHumdrumFile(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string filename)

    // ── Format converters ──────────────────────────────────────────────

    /// Normalise a Humdrum document by round-tripping through Verovio's
    /// parser + emitter. Useful for canonicalising third-party Humdrum
    /// before downstream consumption.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_convertHumdrumToHumdrum(
        nativeint tkPtr,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string humdrumData
    )

    /// Convert a Humdrum document to a Standard MIDI File (base64
    /// encoded, like `renderToMIDI`).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_convertHumdrumToMIDI(
        nativeint tkPtr,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string humdrumData
    )

    /// Convert an MEI document to Humdrum **kern (one-shot; doesn't
    /// disturb the toolkit's loaded document).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_convertMEIToHumdrum(
        nativeint tkPtr,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string meiData
    )

    // ── Document queries ────────────────────────────────────────────────

    /// Page count of the loaded document. Returns 0 if no document is
    /// loaded.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int vrvToolkit_getPageCount(nativeint tkPtr)

    /// Returns the 1-based page index containing the element with the
    /// given xml:id, or 0 if the element is not in the loaded
    /// document.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern int vrvToolkit_getPageWithElement(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string xmlId)

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

    /// Returns the MEI attribute set of the named element as a JSON
    /// object (string -> string map).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getElementAttr(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string xmlId)

    /// Returns the JSON array of xml:ids generated by Verovio's
    /// `<expansion>` resolution for the given source element.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getExpansionIdsForElement(
        nativeint tkPtr,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string xmlId
    )

    /// Given an expansion-introduced element id, returns the originating
    /// source-MEI element's xml:id.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getNotatedIdForElement(
        nativeint tkPtr,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string xmlId
    )

    /// Returns the realised onset time (ms) of the named element. -1
    /// if the element is not part of the timed content.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern double vrvToolkit_getTimeForElement(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string xmlId)

    /// Returns a JSON object with the score-time + real-time
    /// onset/offset of the named element (one entry per sounding
    /// instance, since expansion can sound an element multiple times).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getTimesForElement(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string xmlId)

    /// Returns the loaded document's xml:id (the `<mei xml:id="...">`
    /// at the root). Empty string if none.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getID(nativeint tkPtr)

    /// Returns Verovio's descriptive-features summary for the loaded
    /// document as JSON. Schema is upstream-defined and version-tied;
    /// raw JSON for v0 — typed model is a follow-up phase.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getDescriptiveFeatures(
        nativeint tkPtr,
        [<MarshalAs(UnmanagedType.LPUTF8Str)>] string options
    )

    // ── Selection / editor ─────────────────────────────────────────────

    /// Constrain subsequent renders to a JSON-described selection
    /// (measure range, time range, or element list). Returns false if
    /// the selection JSON is malformed.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_select(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string selection)

    /// Apply an editor action described by a JSON object
    /// (`{action: "drag"|"insert"|"delete"|"set", param: {...}}`).
    /// Returns false if the action JSON is malformed or the operation
    /// failed.
    ///
    /// Phase 49 ships raw-string passthrough; a typed `EditorAction`
    /// DU lands in a follow-up phase once the upstream action-schema
    /// surface is enumerated against a Pro-tier editor consumer.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern bool vrvToolkit_edit(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string editorAction)

    /// Returns JSON describing the result of the last `edit` call
    /// (`{status: "OK"|"FAILURE", message: "..."}` per upstream
    /// convention).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_editInfo(nativeint tkPtr)

    // ── Validation ─────────────────────────────────────────────────────

    /// Validate a PAE (Plaine and Easie) document; returns a JSON
    /// report of errors and warnings keyed by line / token.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_validatePAE(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string data)

    /// Validate a PAE file at `filename`; same report shape as
    /// `validatePAE`.
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_validatePAEFile(nativeint tkPtr, [<MarshalAs(UnmanagedType.LPUTF8Str)>] string filename)

    // ── Logging (per-toolkit retrieval) ────────────────────────────────

    /// Drain the toolkit's accumulated log buffer (only meaningful when
    /// `enableLogToBuffer(true)` has been set).
    [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint vrvToolkit_getLog(nativeint tkPtr)

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
