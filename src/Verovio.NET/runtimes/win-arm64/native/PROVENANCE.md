# Vendored `libverovio.dll` (win-arm64) provenance

This directory contains a precompiled binary build of [Verovio](https://github.com/rism-digital/verovio)
(LGPL-3.0) for the **win-arm64** runtime, produced from upstream source via the
documented CMake `BUILD_AS_LIBRARY=ON` option. The binary is vendored into
`Verovio.NET` (Apache-2.0) and consumed via P/Invoke through a process-relevant
dynamic-linking boundary — same posture as the win-x64 binary (see
[`../../win-x64/native/PROVENANCE.md`](../../win-x64/native/PROVENANCE.md)).

## Upstream pin

| Field                | Value                                                                          |
| -------------------- | ------------------------------------------------------------------------------ |
| Repository           | https://github.com/rism-digital/verovio                                        |
| Git tag              | `version-6.2.0`                                                                |
| Git commit SHA       | `43f806031bfff2c64003fc8ddd9910820445f6ab`                                     |
| License              | LGPL-3.0-or-later                                                              |
| Build options        | `-DBUILD_AS_LIBRARY=ON -DCMAKE_BUILD_TYPE=Release -DCMAKE_WINDOWS_EXPORT_ALL_SYMBOLS=ON -G "Visual Studio 17 2022" -A ARM64` |
| Target               | win-arm64                                                                      |
| Toolchain            | MSVC v143 — Visual Studio Build Tools 2022 (17.14.37328.6), ARM64 toolset 14.44.35207, Windows SDK 10.0.28000.0 |
| Host                 | x64 (cross-build via the `-A ARM64` VS-generator switch; MSVC `Hostx64\arm64` cross compiler) |
| DLL SHA-256          | `EF29807BA13C2D67ED8CCAC4885575EAE740936D610DCF7B9DD45E4402FC9EB6`             |
| Build date           | 2026-06-15                                                                     |
| DLL size             | 21,588,992 bytes                                                               |
| Machine type         | AA64 (ARM64) — verified via `dumpbin /headers`                                 |

### `CMAKE_WINDOWS_EXPORT_ALL_SYMBOLS` required

Same upstream `cmake/CMakeLists.txt` symbol-export bug as documented in the
win-x64 PROVENANCE: `BUILD_AS_LIBRARY=ON` does
`add_definitions(-DCMAKE_WINDOWS_EXPORT_ALL_SYMBOLS)` (a preprocessor define,
not the CMake variable), so the DLL ships with zero exported symbols unless
`-DCMAKE_WINDOWS_EXPORT_ALL_SYMBOLS=ON` is passed on the command line. The
vendor script passes it; the resulting DLL exports the full `c_wrapper.h`
surface (verified — `vrvToolkit_constructor`, `vrvToolkit_loadData`,
`vrvToolkit_renderToSVG`, `vrvToolkit_getVersion`, `vrvToolkit_destructor`,
`enableLog`, … present via `dumpbin /exports`).

## Build provenance

Built from clean upstream sources by running
`./scripts/build-libverovio.ps1 -Rid win-arm64` in this repository — see
[`scripts/build-libverovio.ps1`](../../../../scripts/build-libverovio.ps1) for
the exact CMake invocation. The script clones the pinned tag into
`external/verovio/`, runs CMake + MSBuild with the ARM64 toolset, and copies
the resulting DLL into this directory.

## Validation status

- **Static (on the x64 build host):** machine type confirmed AA64 (ARM64);
  full `c_wrapper.h` export surface present.
- **Runtime (on an ARM64 Windows host, 2026-06-15):** the Verovio.NET
  Expecto suite runs **90/90 (0 ignored)** — the `skipUnlessNative` cohort
  runs (not skips), so `Toolkit.Create()` + SVG render exercise this DLL
  natively on ARM64 Windows. Fully validated.

## Verification

Consumers can verify the vendored binary against an independent cross-build
from an x64 (or native ARM64) Windows host with the C++ ARM64 toolset:

```powershell
git clone https://github.com/rism-digital/verovio external/verovio
cd external/verovio
git checkout version-6.2.0
cmake -B build -S cmake -DBUILD_AS_LIBRARY=ON -DCMAKE_BUILD_TYPE=Release `
  -DCMAKE_WINDOWS_EXPORT_ALL_SYMBOLS=ON -G "Visual Studio 17 2022" -A ARM64
cmake --build build --config Release --target verovio
# Compare build/Release/verovio.dll against the vendored copy.
```

## License notice

`libverovio.dll` is distributed under the GNU Lesser General Public License
v3.0 or later. Consumers receive both the LGPL terms (covering the DLL) and
the Apache-2.0 terms (covering the Verovio.NET adapter) when installing the
NuGet package.
