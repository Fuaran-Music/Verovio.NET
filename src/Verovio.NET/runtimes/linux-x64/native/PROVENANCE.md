# Vendored `libverovio.so` (linux-x64) provenance

This directory contains a precompiled binary build of [Verovio](https://github.com/rism-digital/verovio)
(LGPL-3.0) for the **linux-x64** runtime, produced from upstream source via the
documented CMake `BUILD_AS_LIBRARY=ON` option in a reproducible Debian
container. The binary is vendored into `Verovio.NET` (Apache-2.0) and consumed
via P/Invoke through a process-relevant dynamic-linking boundary — same
posture as the win-x64 binary (see
[`../../win-x64/native/PROVENANCE.md`](../../win-x64/native/PROVENANCE.md)).

## Upstream pin

| Field                | Value                                                                          |
| -------------------- | ------------------------------------------------------------------------------ |
| Repository           | https://github.com/rism-digital/verovio                                        |
| Git tag              | `version-6.2.0`                                                                |
| Git commit SHA       | `43f806031bfff2c64003fc8ddd9910820445f6ab`                                     |
| License              | LGPL-3.0-or-later                                                              |
| Build options        | `-DBUILD_AS_LIBRARY=ON -DCMAKE_BUILD_TYPE=Release` (in `cmake/`)               |
| Target               | linux-x64                                                                      |
| Toolchain            | g++ + CMake on `debian:bookworm-slim` (see `scripts/Dockerfile.libverovio-linux`) |
| SO SHA-256           | `D6228B01CBB249C688DC0BFB4E537DD2DDAAE272A6466B091CFD887BB1F6E1AD`             |
| Build date           | 2026-06-15                                                                     |
| SO size              | 19,949,880 bytes                                                               |

### No `CMAKE_WINDOWS_EXPORT_ALL_SYMBOLS` needed

Unlike the Windows builds, the Linux ELF build needs no symbol-export
workaround: ELF exports all non-`static` symbols by default, so the
`c_wrapper.h` surface is visible to `dlopen`/`dlsym`/P-Invoke without the
`CMAKE_WINDOWS_EXPORT_ALL_SYMBOLS` flag the Windows DLLs require.

## Build provenance

Built from clean upstream sources by running
`./scripts/build-libverovio-linux.ps1` in this repository — see
[`scripts/build-libverovio-linux.ps1`](../../../../scripts/build-libverovio-linux.ps1)
and [`scripts/Dockerfile.libverovio-linux`](../../../../scripts/Dockerfile.libverovio-linux)
for the exact build. The Dockerfile clones the pinned tag, runs CMake + make
in a Debian container, and its `scratch` export stage hands `libverovio.so`
back to the host via a BuildKit `--output`. The commit SHA above is captured
inside the container (`git rev-parse HEAD`) so the provenance stays honest.

## Validation status

- **Static:** ELF 64-bit LSB shared object, x86-64; full `c_wrapper.h` export
  surface present (verified with `nm -D` — see Close-out).
- **Runtime:** the Verovio.NET Expecto suite's `skipUnlessNative` cohort runs
  (not skips) and passes in a `mcr.microsoft.com/dotnet/sdk:10.0` Linux
  container, exercising `Toolkit.Create()` + SVG render against this `.so`.

## Verification

Consumers can verify the vendored binary against an independent build:

```bash
docker run --rm -v "$PWD:/out" debian:bookworm-slim bash -c '
  apt-get update && apt-get install -y git cmake g++ make &&
  git clone --depth 1 --branch version-6.2.0 https://github.com/rism-digital/verovio /v &&
  cd /v/cmake && cmake -DBUILD_AS_LIBRARY=ON -DCMAKE_BUILD_TYPE=Release . &&
  make -j"$(nproc)" verovio && cp libverovio.so /out/'
# Compare /out/libverovio.so against the vendored copy.
```

## License notice

`libverovio.so` is distributed under the GNU Lesser General Public License
v3.0 or later. Consumers receive both the LGPL terms (covering the `.so`) and
the Apache-2.0 terms (covering the Verovio.NET adapter) when installing the
NuGet package.
