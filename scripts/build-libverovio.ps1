#Requires -Version 7.0
<#
.SYNOPSIS
  Build libverovio.dll from upstream Verovio source and vendor it under
  src/Verovio.NET/runtimes/win-x64/native/.

.DESCRIPTION
  Phase 04 vendor script. Clones the upstream rism-digital/verovio repo at
  the pinned version tag into external/verovio/ (gitignored), runs CMake
  with -DBUILD_AS_LIBRARY=ON, builds the Release configuration, and copies
  the resulting libverovio.dll into the runtimes layout so the NuGet pack
  step picks it up.

  Requirements:
    * CMake 3.15+ on PATH
    * Visual Studio 2022 Build Tools with "Desktop development with C++"
      workload installed (cl.exe via VsDevCmd)
    * git on PATH

  Bump procedure: update $VerovioTag below, run this script, run the
  test suite (the snapshot golden corpus may need regenerating), commit
  the new DLL + the PROVENANCE.md update.

.EXAMPLE
  pwsh ./scripts/build-libverovio.ps1
#>
[CmdletBinding()]
param(
    [string] $VerovioTag = "version-6.2.0",
    [switch] $SkipClone
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $PSCommandPath
$RepoRoot = Split-Path -Parent $ScriptRoot
Set-Location $RepoRoot

function Write-Step {
    param([string] $message)
    Write-Host ""
    Write-Host "── $message ──────────────────────────────────────────────" -ForegroundColor Cyan
}

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

# `-prerelease` so VS 2026 Insiders (channelId VisualStudio.18.Preview) is
# discovered alongside the GA channel. `-latest` then picks the highest
# version that has the C++ workload — typically the prerelease install when
# both are present.
$vsInstallPath = & $vswhere -latest -prerelease `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath
if (-not $vsInstallPath) {
    Write-Error @"
No Visual Studio install with the C++ build tools workload was found.

Run the Visual Studio Installer and add 'Desktop development with C++' to
your install. Or use the BuildTools edition:

    winget install Microsoft.VisualStudio.2022.BuildTools --override `
        "--add Microsoft.VisualStudio.Workload.VCTools --quiet --wait"
"@
    exit 1
}
Write-Host "  VS install: $vsInstallPath"

# Resolve the matching CMake generator from the major-version digit. VS 2022
# uses generator "Visual Studio 17 2022"; VS 2026 (Insiders, channel
# VisualStudio.18.Preview) uses "Visual Studio 18 2026". Both are accepted
# by CMake 3.31+; the workspace's pinned CMake 4.x release supports both
# unconditionally.
$vsVersion = & $vswhere -latest -prerelease `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
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

$buildDir = Join-Path $verovioDir "build"
Write-Step "CMake configure"

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
        -A x64
    if ($LASTEXITCODE -ne 0) { throw "CMake configure failed (exit $LASTEXITCODE)" }
}
finally { Pop-Location }

Write-Step "CMake build"
cmake --build $buildDir --config Release --target verovio
if ($LASTEXITCODE -ne 0) { Write-Error "CMake build failed (exit $LASTEXITCODE)"; exit 1 }

# ── Locate + vendor the DLL ─────────────────────────────────────────────

Write-Step "Vendoring libverovio.dll"

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

$vendorDir = Join-Path $RepoRoot "src\Verovio.NET\runtimes\win-x64\native"
New-Item -ItemType Directory -Force -Path $vendorDir | Out-Null
$vendorPath = Join-Path $vendorDir "libverovio.dll"
Copy-Item $candidate $vendorPath -Force

$hash = (Get-FileHash $vendorPath -Algorithm SHA256).Hash
Write-Host "  Vendored: $vendorPath"
Write-Host "  SHA-256:  $hash"

# Record the commit SHA so the PROVENANCE.md row stays honest.
Push-Location $verovioDir
$gitSha = git rev-parse HEAD
Pop-Location

Write-Host ""
Write-Host "✓ libverovio.dll vendored." -ForegroundColor Green
Write-Host ""
Write-Host "Next: update src/Verovio.NET/runtimes/win-x64/native/PROVENANCE.md"
Write-Host "  Upstream tag:    $VerovioTag"
Write-Host "  Upstream commit: $gitSha"
Write-Host "  SHA-256:         $hash"
exit 0
