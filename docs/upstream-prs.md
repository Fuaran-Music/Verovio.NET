# Upstream PR candidates

Ideas for upstream-Verovio PRs / issues that would reduce Verovio.NET's adapter-side workarounds. Not commitments — items here are pre-scoped but un-filed. Each section ends with a "Status" line so future-us can see at a glance what's been triaged, filed, accepted, rejected, or shipped.

Order: newest first.

---

## Per-`Doc` xml:id seed for same-thread multi-Toolkit determinism

**Filed**: not yet — held during Phase 49 close-out (2026-06-07).
**Trigger to revisit**: when a real consumer (likely a per-keystroke render path, or any server pooling toolkits behind a request handler) actually wants per-Toolkit deterministic IDs without the reseed-at-every-boundary workaround. Or sooner if we want to clear the workaround on aesthetic grounds.

### Symptom that motivates it

Two `Toolkit` instances on the **same thread** loading + rendering identical MEI produce non-identical SVG at every layout-time `<g id="…">`. The diff shape we saw during Phase 49:

```
svg1: <g id="l1pryqzq" class="system">
svg2: <g id="pxdki2q"  class="system">
```

### Why this happens

`Object::s_xmlIDCounter` is `thread_local` at `src/object.cpp:44-45` (Verovio `version-6.2.0`). On one thread, two toolkits share the same counter and consume from it as `Object`s are constructed — so even with identical input, the layout-time IDs diverge after the first toolkit's load shifts the thread's counter. Across threads the counters are isolated, so this is **not** a thread-safety problem; it's a determinism gap when more than one toolkit is in flight on the same thread.

### Current workaround in the binding

`Toolkit.fs` carries a sticky per-Toolkit `xmlIdSeed` field, and reseeds via `vrvToolkit_resetXmlIdSeed` at every `Load*` and at the start of every `RenderToSvg`. Works, but:

- Makes `ResetXmlIdSeed` semantics awkward — a user-set seed is sticky across the toolkit but re-applied at boundaries.
- Forces the binding to track its own sticky seed value on top of upstream's API.
- Consumers who want per-Toolkit seeds (e.g. `seed = hash(document)` for content-addressable engraving) can't get them through the natural surface without overriding the sticky field at the right moment.

### Proposed shape

Move the counters from `Object::` static to `Doc` instance state:

- `s_xmlIDCounter` / `s_objectCounter` → `Doc::m_xmlIDCounter` / `m_objectCounter`.
- `Object::GenerateID()` / `GenerateHashID()` read via the existing `GetDoc()` back-pointer.
- `Toolkit::ResetXmlIdSeed` writes to `m_doc.SetXmlIdSeed(seed)` instead of the static.
- `vrvToolkit_resetXmlIdSeed` C-wrapper signature unchanged; JS / Python / Go bindings unaffected.

Rough scope: ~50–150 lines internal plumbing, one read-site to refactor.

### Open design question (file as part of the issue, not the PR)

`Object`s are routinely constructed before they're attached to a `Doc` (XML parsers build subtrees first, attach later). They need an ID at construction time but `GetDoc()` is null. Three strategies, in order of preference:

1. **Deferred ID** — `Object` carries a "needs id" flag; ID minted lazily on first read or on `Doc` attach.
2. **Thread-local fallback counter** — orphan `Object`s mint from a thread-local counter; on `Doc` attach, remint from the doc's counter. Preserves backward-compatibility but doubles the bookkeeping.
3. **Constructor takes a `Doc*`** — invasive; would change every `Object`-derived constructor signature.

(1) feels least invasive but upstream should weigh in before the PR — the `thread_local` choice in `version-6.2.0` looks deliberate and might reflect a stability reason we don't know.

### Filing playbook

Issue text drafted at the end of this file. Describe the binding scenario in prose; link to `Verovio.NET` from the issue once it suits.

If upstream signals "yes, PR welcome" within a couple of weeks, the work item becomes a Verovio.NET maintenance task; if not, drop and leave the workaround in place.

### Status

`drafted`. Issue text ready (below); not filed.

---

## Per-Toolkit logging buffer (secondary, lower priority)

**Filed**: no.
**Trigger to revisit**: only after the seed PR lands or is declined; logging is a separate fight and not worth tackling first.

### Symptom

`enableLog` and `enableLogToBuffer` are process-global C booleans (`src/vrv.cpp`), and `logBuffer` is a process-global `std::vector<std::string>` mutated by `LogInfo` / `LogWarning` / `LogError` from any thread holding the toolkit. **This is the actual cause of the parallel-test heap corruption** we hit during Phase 49 testing (initially mis-attributed to the xml:id seed); concurrent `vector` reallocations from multiple threads writing to `logBuffer` race.

`run.ps1` works around it by passing `--sequenced` to Expecto. Documented inline.

### Why this is a harder PR than the seed

Logging is a namespace-level set of free functions hit from **hundreds of call sites** across the codebase. Per-Toolkit options:

- **Thread `Logger*` through every call site** — massive diff, breaks every internal caller, almost certainly rejected.
- **`thread_local` current-logger pointer set on Toolkit entry** — smaller diff but a leaky abstraction (every Toolkit method has to push/pop the pointer; deeply nested calls into upstream code paths get fragile).

Neither is comparable in scope or political cost to the seed PR.

### Status

`scoped`. Won't pursue unless / until the seed PR conversation goes well.

---

## CMake `BUILD_AS_LIBRARY` symbol-export shim (pre-existing)

**Filed**: no; tracked in [`src/Verovio.NET/runtimes/win-x64/native/PROVENANCE.md:27`](../src/Verovio.NET/runtimes/win-x64/native/PROVENANCE.md). Existed before this doc; recorded here for visibility.

### Symptom

Upstream's `BUILD_AS_LIBRARY=ON` doesn't set `CMAKE_WINDOWS_EXPORT_ALL_SYMBOLS=ON`, so the resulting DLL has no exported symbols on Windows. Our `scripts/build-libverovio.ps1` patches it locally.

### Status

`workaround in scripts/`. Not blocking, but a candidate cleanup that benefits any downstream consumer building libverovio with CMake on Windows.

---

# Appendix A: Drafted issue text — per-`Doc` xml:id seed

Verbatim from the Phase 49 drafting session. Edit before filing; copy into a fresh GitHub issue at https://github.com/rism-digital/verovio/issues/new.

---

**Title:** Per-`Doc` xml:id seed for same-thread multi-Toolkit determinism — feedback welcome before a PR?

**Hi! Quick design question from a binding author. Happy to do the PR work if there's appetite; filing this first since the `thread_local` design suggests an intentional choice I don't want to overlook.**

## Context

I maintain a .NET binding against `tools/c_wrapper.h` (pinned at `version-6.2.0`). The binding wants to offer a determinism contract: *given the same `(libverovio version, MEI input, render options)`, two `Toolkit` instances on the same machine produce byte-identical SVG.* This is useful for snapshot testing, content-addressable engraving pipelines, and any consumer that pools toolkits behind a request handler.

## What I observe

`Object::s_xmlIDCounter` and `Object::s_objectCounter` are `thread_local` (`src/object.cpp:44-45`). On one thread, two `Toolkit` instances share the same counter and consume from it as they construct `Object`s — so even with identical input MEI, `tk1->loadData(mei); tk2->loadData(mei); tk1->renderToSVG(1); tk2->renderToSVG(1);` produces SVG that differs at every layout-time element id (system / measure / staff `<g id="…">`).

Across threads the counters are isolated, so this is *not* a thread-safety problem — it's a determinism gap on the same thread when more than one `Toolkit` is in flight.

## Today's workaround in the binding

Reseed via `vrvToolkit_resetXmlIdSeed` at every load and at the start of every render. Works, but:

- Makes `ResetXmlIdSeed` semantics confusing to expose to consumers — a user-set seed is clobbered by the next render.
- Forces the binding to track its own sticky seed value on top of upstream's API.
- Means consumers who want per-toolkit seeds (e.g. seed-derived-from-document-hash for content-addressable output) can't get them through the natural surface.

## Proposed direction (open to alternatives)

Move the counter from `Object::` static to `Doc` instance state. Sketch:

- `s_xmlIDCounter` / `s_objectCounter` become `Doc` members (`m_xmlIDCounter`, `m_objectCounter`).
- `Object::GenerateID()` / `GenerateHashID()` read via the existing `GetDoc()` back-pointer.
- `Toolkit::ResetXmlIdSeed` writes to `m_doc.SetXmlIdSeed(seed)` instead of the static.
- `vrvToolkit_resetXmlIdSeed` C-wrapper signature unchanged. JS / Python / Go bindings unaffected.

Rough scope: ~50–150 lines, one read site to refactor (`GenerateID/GenerateHashID`), no per-subclass call sites to touch (every `Object` already has `GetDoc()`).

## The open design question

`Object`s are routinely constructed *before* they're attached to a `Doc` (XML parsers build subtrees first, attach later). They need an ID at construction time but `GetDoc()` is null. Options:

1. **Deferred ID** — `Object` carries a "needs id" flag; ID minted lazily on first read or on `Doc` attach.
2. **Thread-local fallback counter** — orphan `Object`s mint from a thread-local counter; on `Doc` attach, remint from the doc's counter. Preserves backward-compatibility but doubles the bookkeeping.
3. **Constructor takes a `Doc*`** — invasive; would change every `Object`-derived constructor signature.

(1) feels least invasive but I'd rather hear which shape you'd accept before sinking time into the wrong one.

## Why I'm filing instead of just opening a PR

The `thread_local` choice in `version-6.2.0` looks deliberate, not accidental. If there's a reason for the current design I'm not seeing — cross-toolkit ID stability for archival/diffing, deterministic IDs across a multi-pass build pipeline, something else — I'd rather know before opening a PR that fights that intent.

So: **is a per-`Doc` seed something you'd merge if I scoped it cleanly?** And if yes, do you have a preference on the orphan-Object strategy?

Either way, thanks for the engraver — it's been a pleasure to bind against.
