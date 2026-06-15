#Requires -Version 7.0
<#
.SYNOPSIS
  Build libverovio.dll from upstream Verovio source and vendor it under
  src/Verovio.NET/runtimes/<rid>/native/ for a Windows RID (win-x64 or
  win-arm64).

.DESCRIPTION
  Vendor script for the Windows native binaries. Clones the upstream
  rism-digital/verovio repo at the pinned version tag into
  external/verovio/ (gitignored), runs CMake with -DBUILD_AS_LIBRARY=ON
  for the requested target architecture, builds the Release
  configuration, and copies the resulting libverovio.dll into the
  runtimes/<rid>/native/ layout so the NuGet pack step picks it up.

  The Linux native binary (libverovio.so / linux-x64) is built
  separately — it needs a Linux toolchain, so it goes through the
  containerised path in scripts/build-libverovio-linux.ps1 (Docker) or
  the equivalent CMake invocation inside WSL. This script covers the
  Windows RIDs only.

  Target RIDs:
    * win-x64   — native build on an x64 host; cross-build on an ARM64
                  host (CMake `-A x64` selects the x64 toolset).
    * win-arm64 — native build on an ARM64 host; cross-build on an x64
                  host (CMake `-A ARM64` selects the ARM64 cross toolset,
                  MSVC `Hostx64\arm64`). Requires the
                  "MSVC v143 - VS 2022 C++ ARM64 build tools" individual
                  component (Microsoft.VisualStudio.Component.VC.Tools.ARM64).

  Because CMake's Visual Studio generator drives the toolset selection
  via `-A`, either Windows RID can be produced from either host arch —
  one Windows machine with both the x64 and ARM64 C++ toolsets installed
  emits both DLLs.

  Requirements:
    * CMake 3.15+ on PATH
    * Visual Studio 2022 Build Tools with "Desktop development with C++"
      workload (x64 toolset). For -Rid win-arm64, additionally the
      "C++ ARM64 build tools" individual component.
    * git on PATH

  Bump procedure: update $VerovioTag below, run this script for each
  Windows RID, run the test suite (the snapshot golden corpus may need
  regenerating), commit the new DLL + the PROVENANCE.md update.

.EXAMPLE
  pwsh ./scripts/build-libverovio.ps1
  # Builds win-x64 (the default RID).

.EXAMPLE
  pwsh ./scripts/build-libverovio.ps1 -Rid win-arm64
  # Cross-builds win-arm64 from an x64 host (or natively on ARM64).

.EXAMPLE
  pwsh ./scripts/build-libverovio.ps1 -Rid win-arm64 -SkipClone
  # Reuse an already-cloned external/verovio for a second-arch build.
#>
[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string] $Rid = "win-x64",
    [string] $VerovioTag = "version-6.2.0",
    [switch] $SkipClone
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $PSCommandPath
$RepoRoot = Split-Path -Parent $ScriptRoot
Set-Location $RepoRoot

# Map the .NET RID to the CMake `-A` platform name. The Visual Studio
# generator uses `-A` to pick x64 vs ARM64 toolsets; this is what lets an
# x64 host cross-build win-arm64 and vice-versa.
$cmakeArch =
    switch ($Rid) {
        "win-x64" { "x64" }
        "win-arm64" { "ARM64" }
        default { Write-Error "Unsupported RID '$Rid'."; exit 1 }
    }

function Write-Step {
    param([string] $message)
    Write-Host ""
    Write-Host "── $message ──────────────────────────────────────────────" -ForegroundColor Cyan
}

Write-Step "Target: $Rid (CMake -A $cmakeArch)"

# ── Tool checks ─────────────────────────────────────────────────────────

Write-Step "Tool availability"

$cmake = Get-Command cmake -ErrorAction SilentlyContinue
if (-not $cmake) {
    Write-Error @"
CMake not found on PATH.

Install via one of:
    winget install Kitware.CMake
    choco install cmake
    https://cmake.org/download/

Then restart this shell (PATH is updated only for new sessions).
"@
    exit 1
}
Write-Host "  cmake: $($cmake.Source)"

# Find cl.exe via vswhere — handles VS Community, BuildTools, Professional,
# Enterprise variants without hard-coding paths.
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    Write-Error @"
Visual Studio's vswhere.exe not found at $vswhere.

Install Visual Studio 2022 Build Tools (or Community/Pro/Enterprise) with
the 'Desktop development with C++' workload:

    winget install Microsoft.VisualStudio.2022.BuildTools --override `
        "--add Microsoft.VisualStudio.Workload.VCTools --quiet --wait"
"@
    exit 1
}

# The required C++ toolset component depends on the target arch. win-x64
# needs the x64 toolset; win-arm64 needs the ARM64 toolset component
# (which ships the Hostx64\arm64 cross compilers, so it builds from an
# x64 host too).
$requiredComponent =
    switch ($Rid) {
        "win-x64" { "Microsoft.VisualStudio.Component.VC.Tools.x86.x64" }
        "win-arm64" { "Microsoft.VisualStudio.Component.VC.Tools.ARM64" }
    }

# `-prerelease` so VS 2026 Insiders (channelId VisualStudio.18.Preview) is
# discovered alongside the GA channel. `-products *` so the BuildTools SKU
# (which vswhere EXCLUDES from its default product set) is discovered
# alongside Community/Pro/Enterprise — without it, a BuildTools-only host
# reports "no VS install" even when the C++ toolsets are present. `-latest`
# then picks the highest version that has the required C++ toolset.
$vsInstallPath = & $vswhere -latest -prerelease -products * `
    -requires $requiredComponent `
    -property installationPath
if (-not $vsInstallPath) {
    $installHint =
        if ($Rid -eq "win-arm64") {
            @"
No Visual Studio install with the C++ ARM64 build tools was found.

The win-arm64 target needs the ARM64 toolset component. Add it to an
existing VS install via the Visual Studio Installer, or:

    winget install Microsoft.VisualStudio.2022.BuildTools --override `
        "--add Microsoft.VisualStudio.Workload.VCTools --add Microsoft.VisualStudio.Component.VC.Tools.ARM64 --quiet --wait"
"@
        }
        else {
            @"
No Visual Studio install with the C++ x64 build tools workload was found.

Run the Visual Studio Installer and add 'Desktop development with C++' to
your install. Or use the BuildTools edition:

    winget install Microsoft.VisualStudio.2022.BuildTools --override `
        "--add Microsoft.VisualStudio.Workload.VCTools --quiet --wait"
"@
        }
    Write-Error $installHint
    exit 1
}
Write-Host "  VS install: $vsInstallPath"
Write-Host "  Required toolset component: $requiredComponent"

# Resolve the matching CMake generator from the major-version digit. VS 2022
# uses generator "Visual Studio 17 2022"; VS 2026 (Insiders, channel
# VisualStudio.18.Preview) uses "Visual Studio 18 2026". Both are accepted
# by CMake 3.31+; the workspace's pinned CMake 4.x release supports both
# unconditionally.
$vsVersion = & $vswhere -latest -prerelease -products * `
    -requires $requiredComponent `
    -property installationVersion
$vsMajor = [int]($vsVersion -split '\.')[0]
$cmakeGenerator =
    switch ($vsMajor) {
        17 { "Visual Studio 17 2022" }
        18 { "Visual Studio 18 2026" }
        default {
            Write-Error "Unsupported VS major version $vsMajor (installationVersion=$vsVersion). Extend the switch in build-libverovio.ps1."
            exit 1
        }
    }
Write-Host "  VS version:   $vsVersion (generator: $cmakeGenerator)"

# ── Clone upstream ───────────────────────────────────────────────────────

$externalDir = Join-Path $RepoRoot "external"
$verovioDir = Join-Path $externalDir "verovio"

if (-not $SkipClone) {
    Write-Step "Cloning verovio @ $VerovioTag"

    if (Test-Path $verovioDir) {
        Push-Location $verovioDir
        try {
            git fetch --tags --depth 1 origin $VerovioTag
            git checkout $VerovioTag
        }
        finally { Pop-Location }
    }
    else {
        New-Item -ItemType Directory -Force -Path $externalDir | Out-Null
        git clone --depth 1 --branch $VerovioTag https://github.com/rism-digital/verovio $verovioDir
    }
}
else {
    if (-not (Test-Path $verovioDir)) {
        Write-Error "SkipClone set but $verovioDir does not exist."
        exit 1
    }
}

# ── CMake configure + build ─────────────────────────────────────────────

# Per-arch build directory so a second-arch build (with -SkipClone reusing
# the same clone) doesn't collide with the first arch's CMake cache, which
# pins the generator platform.
$buildDir = Join-Path $verovioDir "build-$Rid"
Write-Step "CMake configure ($Rid)"

Push-Location $verovioDir
try {
    # Verovio's CMakeLists lives at cmake/CMakeLists.txt, not the repo root.
    #
    # `-DCMAKE_WINDOWS_EXPORT_ALL_SYMBOLS=ON` is **required** on Windows.
    # Upstream Verovio's cmake/CMakeLists.txt does
    # `add_definitions(-DCMAKE_WINDOWS_EXPORT_ALL_SYMBOLS)`, which is wrong:
    # CMAKE_WINDOWS_EXPORT_ALL_SYMBOLS is a CMake variable, not a
    # preprocessor define. Without the correct syntax, the resulting DLL
    # has zero exported symbols and every P/Invoke call fails with
    # EntryPointNotFoundException. Passing it on the command line here
    # sets the CMake variable correctly without patching upstream.
    cmake -B $buildDir -S "cmake" `
        -DBUILD_AS_LIBRARY=ON `
        -DCMAKE_BUILD_TYPE=Release `
        -DCMAKE_WINDOWS_EXPORT_ALL_SYMBOLS=ON `
        -G $cmakeGenerator `
        -A $cmakeArch
    if ($LASTEXITCODE -ne 0) { throw "CMake configure failed (exit $LASTEXITCODE)" }
}
finally { Pop-Location }

Write-Step "CMake build ($Rid)"
cmake --build $buildDir --config Release --target verovio
if ($LASTEXITCODE -ne 0) { Write-Error "CMake build failed (exit $LASTEXITCODE)"; exit 1 }

# ── Locate + vendor the DLL ─────────────────────────────────────────────

Write-Step "Vendoring libverovio.dll → runtimes/$Rid/native/"

$candidate = @(
    Join-Path $buildDir "Release\verovio.dll"
    Join-Path $buildDir "Release\libverovio.dll"
    Join-Path $buildDir "verovio.dll"
    Join-Path $buildDir "libverovio.dll"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $candidate) {
    Write-Error "Could not locate the built libverovio.dll under $buildDir."
    exit 1
}
Write-Host "  Source: $candidate"

$vendorDir = Join-Path $RepoRoot "src\Verovio.NET\runtimes\$Rid\native"
New-Item -ItemType Directory -Force -Path $vendorDir | Out-Null
$vendorPath = Join-Path $vendorDir "libverovio.dll"
Copy-Item $candidate $vendorPath -Force

$hash = (Get-FileHash $vendorPath -Algorithm SHA256).Hash
$size = (Get-Item $vendorPath).Length
Write-Host "  Vendored: $vendorPath"
Write-Host "  SHA-256:  $hash"

# Record the commit SHA so the PROVENANCE.md row stays honest.
Push-Location $verovioDir
$gitSha = git rev-parse HEAD
Pop-Location

Write-Host ""
Write-Host "✓ libverovio.dll vendored for $Rid." -ForegroundColor Green
Write-Host ""
Write-Host "Next: update src/Verovio.NET/runtimes/$Rid/native/PROVENANCE.md"
Write-Host "  Target RID:      $Rid"
Write-Host "  CMake platform:  $cmakeArch"
Write-Host "  Upstream tag:    $VerovioTag"
Write-Host "  Upstream commit: $gitSha"
Write-Host "  SHA-256:         $hash"
Write-Host "  Size (bytes):    $size"
exit 0
