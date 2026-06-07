namespace Verovio.NET

open Verovio.NET.Internal

// ============================================================================
//  VerovioLogging — process-global logging toggles.
//
//  Upstream's `enableLog` and `enableLogToBuffer` flip C-static flags
//  inside libverovio. They are NOT per-Toolkit — every Toolkit instance
//  shares the same global write target. We surface them on a separate
//  static class (rather than as `Toolkit` statics) so the global scope
//  is visible at the call site:
//
//    VerovioLogging.EnableConsole = true     // process-global
//    toolkit.DrainLog()                       // per-Toolkit
//
//  **Thread-safety:** the underlying C statics are not synchronised
//  against concurrent reads/writes from .NET worker threads. The
//  pragmatic model is "configure once at process start, drain
//  per-Toolkit on the same thread that constructed it". For a
//  multi-tenant server, surface log lines via the per-Toolkit
//  `DrainLog` call rather than relying on the global console mode.
// ============================================================================

/// Process-global logging configuration for libverovio. Mutates
/// upstream's C statics — not per-Toolkit. See class doc for thread-
/// safety considerations.
[<AbstractClass; Sealed>]
type VerovioLogging =
    /// Toggle stderr console output. When true, libverovio writes
    /// log lines (warnings, info, debug per the build's verbosity)
    /// to stderr.
    static member EnableConsole(value: bool) : unit = Interop.enableLog value

    /// Toggle in-memory buffering. When true, log lines accumulate
    /// inside each Toolkit handle and are surfaced via
    /// `Toolkit.DrainLog`. When false, lines are routed to the
    /// console (if `EnableConsole(true)` has also been set) or
    /// dropped.
    ///
    /// The two toggles compose: set both for "console + buffered";
    /// set only Buffer for "captured, no console"; set only Console
    /// for "console-only, no per-Toolkit retrieval".
    static member EnableBuffer(value: bool) : unit = Interop.enableLogToBuffer value
