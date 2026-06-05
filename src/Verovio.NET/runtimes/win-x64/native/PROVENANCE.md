# Vendored `libverovio.dll` provenance

This directory contains a precompiled binary build of [Verovio](https://github.com/rism-digital/verovio)
(LGPL-3.0), produced from upstream source via the documented CMake `BUILD_AS_LIBRARY=ON`
option. The binary is vendored into `Verovio.NET` (Apache-2.0) and consumed
via P/Invoke through a process-relevant dynamic-linking boundary — same
posture as SQLite.NET, libgit2sharp, and similar .NET libraries that
bundle LGPL-licensed native code.

## Upstream pin

| Field                | Value                                                                          |
| -------------------- | ------------------------------------------------------------------------------ |
| Repository           | https://github.com/rism-digital/verovio                                        |
| Git tag              | `version-6.2.0`                                                                |
| Git commit SHA       | `43f806031bfff2c64003fc8ddd9910820445f6ab`                                     |
| License              | LGPL-3.0-or-later                                                              |
| Build options        | `-DBUILD_AS_LIBRARY=ON -DCMAKE_BUILD_TYPE=Release -DCMAKE_WINDOWS_EXPORT_ALL_SYMBOLS=ON -G "Visual Studio 18 2026"` |
| Target               | win-x64                                                                        |
| Toolchain            | MSVC v145 (Visual Studio Community 2026 Insiders, 18.7.11811.120)              |
| DLL SHA-256          | `F2E916851275D7F3B9858604D9C7BB640972369CA6F4DAE83730B6430132EF58`             |
| Build date           | 2026-06-04                                                                     |
| DLL size             | 21,596,160 bytes                                                               |

### `CMAKE_WINDOWS_EXPORT_ALL_SYMBOLS` required

Upstream Verovio's `cmake/CMakeLists.txt` does `add_definitions(-DCMAKE_WINDOWS_EXPORT_ALL_SYMBOLS)` when `BUILD_AS_LIBRARY=ON` — but that's a preprocessor define, not the CMake variable assignment the symbol-export logic expects (which is `set(CMAKE_WINDOWS_EXPORT_ALL_SYMBOLS ON)`). The default upstream build therefore produces a Windows DLL with **zero** exported symbols, and every P/Invoke fails with `EntryPointNotFoundException`. The script passes the variable on the command line (`-DCMAKE_WINDOWS_EXPORT_ALL_SYMBOLS=ON`) to work around it without patching upstream. **TODO:** open an upstream issue / PR. The export-all flag inflates the DLL ~20× (1MB → 21MB) because every internal C++ symbol is exported alongside the c_wrapper.h surface; an explicit `__declspec(dllexport)` annotation pass on c_wrapper.h would be a more selective fix.

## Build provenance

The DLL was built from clean upstream sources by running
`./run.ps1 -Vendor` in this repository — see [`scripts/build-libverovio.ps1`](../../../../scripts/build-libverovio.ps1)
for the exact CMake invocation. The script clones the pinned tag into
`external/verovio/`, runs CMake + MSBuild, and copies the resulting DLL
into this directory.

## Verification

Consumers can verify the vendored binary against an independent build:

```powershell
git clone https://github.com/rism-digital/verovio external/verovio
cd external/verovio
git checkout version-6.2.0
cmake -B build -DBUILD_AS_LIBRARY=ON -DCMAKE_BUILD_TYPE=Release cmake
cmake --build build --config Release
# Compare build/Release/libverovio.dll against the vendored copy.
```

## Bump procedure

Upstream Verovio releases are tracked on
[GitHub Releases](https://github.com/rism-digital/verovio/releases). To
upgrade:

1. Update the version tag in [`scripts/build-libverovio.ps1`](../../../../scripts/build-libverovio.ps1).
2. Run `pwsh ./run.ps1 -Vendor` from the repository root.
3. Run the snapshot test suite. Snapshot drift is expected on minor
   layout changes; review and update goldens deliberately.
4. Update the upstream pin row above and the README "Vendored upstream"
   section.

## License notice

`libverovio.dll` is distributed under the GNU Lesser General Public
License v3.0 or later. Consumers receive both the LGPL terms (covering
the DLL) and the Apache-2.0 terms (covering the Verovio.NET adapter)
when installing the NuGet package. End-user obligations are documented
in the package's `LICENSE` file.
