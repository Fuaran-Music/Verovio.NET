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
| Git commit SHA       | (recorded at build time — see commit log)                                      |
| License              | LGPL-3.0-or-later                                                              |
| Build options        | `-DBUILD_AS_LIBRARY=ON -DCMAKE_BUILD_TYPE=Release` (CMake defaults otherwise)  |
| Target               | win-x64                                                                        |
| Toolchain            | MSVC v143 (Visual Studio 2022 Build Tools)                                     |

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
