# Percussion render spike — Verovio.NET

**Status:** ✅ Complete · **Date:** 2026-06-23 · **Engine:** libverovio **6.2.0** (vendored)
**Host:** win-x64 (AMD64), native runtime-validated · **Adapter baseline:** `0.2.2-alpha`, F# 10 / .NET 10

## Purpose

Determine, by **rendering real MEI through the native `Toolkit`**, whether the percussion
notation a downstream percussion-engraving consumer needs is reachable through Verovio.NET's
current public surface — and, for each feature, the **canonical input-MEI shape** and the
**discriminating SVG marker** the consumer (and a regression suite) can rely on. This document is
the spec the percussion render-path *enablement* work is built on: it states which features cleared,
their MEI → SVG contract, and whether any typed option knob is load-bearing.

It is a **verdict + evidence** record, not an API change. No managed code was modified to run it.

## Headline verdict

| # | Feature | Verdict | Canonical MEI trigger | Discriminating SVG marker (SMuFL) |
|---|---------|---------|-----------------------|-----------------------------------|
| 1 | Percussion clef | ✅ Renders | `<staffDef clef.shape="perc" clef.line="3">` | `<g class="clef">` → `<use xlink:href="#E069…">` — `unpitchedPercussionClef1` |
| 2 | X noteheads | ✅ Renders | `<note head.shape="x">` | `class="notehead"` → `<use xlink:href="#E0A9…">` — `noteheadXBlack` (vs `#E0A4` noteheadBlack for normal) |
| 3 | Flam / drag grace | ✅ Renders | `<note grace="acc" …>` before the main `<note>` (one = flam, two = drag) | cue-sized `class="flag"` → `#E240` `flag8thUp` (8th flam) / `#E242` `flag16thUp` (16th drag). **No `grace`/`graceGrp` class in 6.2.0** — see caveat |
| 4 | Buzz tremolo (z roll) | ✅ Renders | `<bTrem><note stem.mod="z">` (buzz) / `stem.mod="3slash"` (measured) | `<g class="bTrem">` → `#E22A` `buzzRoll` (z) / `#E222` `tremolo3` (measured) |
| 5 | Sticking (R / L) | ✅ Renders **via `<dir>`** | `<dir place="below" startid="#id">R</dir>` | `<g class="dir">` → `<tspan>R</tspan>` / `<tspan>L</tspan>`. **MEI `<sticking>` is UNSUPPORTED** — see limitation |

**All five features render with `RenderOptions.Default` / `LoadOptions.Default`.** No Verovio option
knob was required for any of them — see *Option knobs* below.

## What "renders" means here

Each fixture was loaded with `Toolkit.Create()` → `LoadData(mei)` → `RenderToSvg(1)` at default
options, and the resulting SVG inspected for the discriminating glyph/class. "Renders" = `LoadData`
+ `RenderToSvg` both returned `Ok`, the SVG is well-formed, the feature's discriminating marker is
present, and **no Verovio backend warning was emitted** (warnings are surfaced through
`VerovioLogging` buffer mode).

The deterministic SVG glyph references take the form `xlink:href="#E069-<seed-suffix>"`, where the
suffix is the xml:id-seed-derived fragment (stable under the default seed per the determinism
contract). A robust assertion keys on the **`#E069` prefix**, not the full id.

### Per-feature evidence (verbatim from the rendered SVG)

```
Clef:        <use xlink:href="#E069-l19nj5e9" transform="translate(90, 1629) scale(0.72, 0.72)" />
X notehead:  <use xlink:href="#E0A9-l19nj5e9" transform="translate(1052, 997) scale(0.72, 0.72)" />
Buzz (z):    <g id="…" class="bTrem"> … <use xlink:href="#E22A-l19nj5e9" …/>   (buzzRoll)
Measured:    <use xlink:href="#E222-l19nj5e9" …/>                              (tremolo3)
Sticking:    <g id="…" class="dir"> … <tspan font-size="405px">R</tspan>
```

Glyph-code census per fixture (count × SMuFL codepoint):

```
percussion-clef       : 1×E069 (percClef)  2×E084 (timeSig4)  4×E0A4 (noteheadBlack)
percussion-xnotehead  : 1×E069             2×E084             4×E0A9 (noteheadXBlack)
percussion-grace-flam : 1×E069  2×E084  6×E0A4  + 1×E240 (flag8thUp) 2×E242 (flag16thUp)  [3×class="flag"]
percussion-buzz       : 1×E069  2×E084  4×E0A4  + 1×E222 (tremolo3)  1×E22A (buzzRoll)    [2×class="bTrem"]
percussion-sticking   : 1×E069  2×E084  4×E0A4  + 4×class="dir", tspan R/L ×4
```

## Option knobs — none load-bearing

Every feature is **fully input-MEI-driven** and reachable with `RenderOptions.Default`. The spike
found **no Verovio option** that had to be flipped to make any feature render. The contract for the
consumer is therefore *"feed this MEI shape, get this SVG"* — **no new typed API is justified** on
`RenderOptions` / `LoadOptions` by these five features. The enablement session should add **no
percussion API**; it states this explicitly and ships fixtures + tests + a README contract only.

## Limitations & caveats found

### L1 — MEI `<sticking>` element is unsupported in libverovio 6.2.0
The dedicated MEI 5.0 `<sticking>` control event is **rejected** by this engine build:

```
[Warning] Unsupported '<sticking>' within <measure>
```

The element is silently dropped (no glyph emitted). **Workaround (canonical for the consumer):**
encode R/L hand indications as below-staff `<dir>` control events
(`<dir place="below" startid="#note">R</dir>`), which render as `class="dir"` text. This is the
shape the committed `percussion-sticking.mei` fixture uses. If a future libverovio bump adds
`<sticking>` support, the consumer can migrate the encoding without an adapter change (still
input-MEI-driven).

### L2 — Grace notes carry no `grace`/`graceGrp` class in 6.2.0
Verovio 6.2.0 emits grace notes as ordinary `class="note"` groups with **cue sizing** (smaller
glyph scale; `cue` appears as a class modifier on dependent elements, e.g.
`class="ledgerLines above cue"`). There is **no `graceGrp` or `grace` class** to assert on. The
robust discriminating signal is the **cue-sized flag glyphs** (`#E240` / `#E242` via
`class="flag"`) — valid because the fixture's main strokes are quarter notes (flagless), so any
flag is a grace. A regression test should key on `class="flag"` + the flag glyph codes, not on a
"grace" class.

### L3 — Acciaccatura slash is a drawn path, not a glyph
`grace="acc"` (+ `stem.mod="1slash"`) produces a slashed grace, but the slash is rendered as an SVG
`<path>` stroke, **not** a `graceNoteSlash` SMuFL glyph (`#E56x` absent). Don't assert on a slash
glyph; the flam's discriminating marker is the cue-sized flag, per L2.

## Fixtures (spike evidence → enablement-suite inputs)

Committed under `src/Verovio.NET.Tests/fixtures/`:

| Fixture | Feature |
|---------|---------|
| `percussion-clef.mei` | percussion clef |
| `percussion-xnotehead.mei` | x noteheads |
| `percussion-grace-flam.mei` | flam + drag grace |
| `percussion-buzz-tremolo.mei` | buzz (z) + measured tremolo |
| `percussion-sticking.mei` | R/L sticking via `<dir>` |

All five load + render clean (zero warnings) on this host. They are the inputs the enablement
session promotes into a permanent `percussionRenderTests` suite (keeping the
`skipUnlessNative` / `ptest` gating).

## Reproduction

Renders were executed through a throwaway probe project (a `ProjectReference` to
`src/Verovio.NET/Verovio.NET.fsproj`, which copies the native `libverovio.dll` + `verovio-data`
into the probe's output and resolves them via the existing custom `DllImport` resolver). The probe
was discarded; no committed code was changed to run it. Any native-capable host can reproduce by
loading each fixture with `Toolkit.Create()` → `LoadData` → `RenderToSvg(1)` and grepping the SVG
for the markers above.

**Native-host note:** validated on win-x64. On a host that cannot load `libverovio` (e.g. win-arm64
without the native), these renders cannot execute and the downstream render tests `ptest`-skip by
design — the verdict above is *host-validated on win-x64* and should be treated as
*unverified-on-this-host* anywhere the native is absent.

## Hand-off to enablement

This spike clears all five features as **input-MEI-driven, no-new-API**. The enablement session
should:
1. Promote the five fixtures into a permanent `percussionRenderTests` suite asserting each
   discriminating marker above (respecting L1–L3).
2. Add a README "Percussion rendering" section mapping each canonical MEI shape → its SVG marker.
3. **Add no typed options** (none are load-bearing) and **bump no version** (docs/tests only) —
   unless the suite itself surfaces a new need.
