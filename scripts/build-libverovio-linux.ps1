#Requires -Version 7.0
<#
.SYNOPSIS
  Build libverovio.so (linux-x64) from upstream Verovio source via a
  reproducible Docker build and vendor it under
  src/Verovio.NET/runtimes/linux-x64/native/.

.DESCRIPTION
  The Linux counterpart of scripts/build-libverovio.ps1 (which covers the
  Windows RIDs). A .so needs a Linux toolchain, so the compile runs inside
  a Debian container defined by scripts/Dockerfile.libverovio-linux. The
  Dockerfile's final `export` stage is a `scratch` image carrying only the
  artefacts, so a BuildKit `--output type=local` writes them straight to a
  host directory — no `docker create` / `docker cp`.

  Output:
    * runtimes/linux-x64/native/libverovio.so          (vendored binary)
  Reported for the PROVENANCE.md row (not auto-written):
    * upstream commit SHA, SHA-256, size.

  verovio-data (the SMuFL font + glyph-bbox tree) is NOT re-vendored per
  RID on disk — the files are byte-identical across RIDs at a given
  upstream tag, so the repo keeps one physical copy under
  runtimes/win-x64/native/verovio-data/ and the NuGet pack step ships it
  into each RID's package path (see Verovio.NET.fsproj). Pass
  -ExtractData to also drop the container's data/ tree under
  runtimes/linux-x64/native/verovio-data/ for verification against the
  vendored copy.

  Requirements:
    * Docker with BuildKit (Docker Desktop, or dockerd inside WSL2). The
      `--output` flag needs BuildKit, which is the default in modern
      Docker. On a host without Docker, run the equivalent CMake build
      inside WSL/another Linux box by hand — see the Dockerfile RUN steps.

  Keep $VerovioTag in lockstep with scripts/build-libverovio.ps1's
  default and the PROVENANCE.md pin rows.

.EXAMPLE
  pwsh ./scripts/build-libverovio-linux.ps1

.EXAMPLE
  pwsh ./scripts/build-libverovio-linux.ps1 -ExtractData
#>
[CmdletBinding()]
param(
    [string] $VerovioTag = "version-6.2.0",
    [switch] $ExtractData
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

$docker = Get-Command docker -ErrorAction SilentlyContinue
if (-not $docker) {
    Write-Error @"
Docker not found on PATH.

The linux-x64 libverovio.so build needs a Linux build toolchain. Options:

  1. Docker Desktop (Windows) — uses a WSL2 backend:
         wsl --install            # then reboot
         winget install Docker.DockerDesktop

  2. dockerd inside WSL2 (no Docker Desktop):
         wsl --install            # then reboot; install docker.io in the distro

  3. No Docker at all — run the equivalent CMake build by hand on any
     Linux box / CI runner (Debian/Ubuntu):
         apt-get install -y git cmake g++ make
         git clone --depth 1 --branch $VerovioTag https://github.com/rism-digital/verovio
         cd verovio/cmake && cmake -DBUILD_AS_LIBRARY=ON -DCMAKE_BUILD_TYPE=Release . && make -j\$(nproc) verovio
     then copy cmake/libverovio.so into
     src/Verovio.NET/runtimes/linux-x64/native/.
"@
    exit 1
}
Write-Host "  docker: $($docker.Source)"

# Confirm the daemon is actually reachable (Docker Desktop not started is a
# common foot-gun — `docker` resolves but `docker info` hangs/fails).
& docker info --format '{{.ServerVersion}}' *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Error "The Docker CLI is installed but the daemon is not reachable. Start Docker Desktop (or the WSL dockerd) and retry."
    exit 1
}
Write-Host "  docker daemon: reachable"

# ── Build + extract ─────────────────────────────────────────────────────

$dockerfile = Join-Path $ScriptRoot "Dockerfile.libverovio-linux"
$outDir = Join-Path $ScriptRoot "_linux-out"

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Write-Step "docker build (linux-x64) @ $VerovioTag"
# DOCKER_BUILDKIT=1 forces BuildKit even on older Docker where it isn't the
# default — `--output` requires it.
$env:DOCKER_BUILDKIT = "1"
& docker build `
    -f $dockerfile `
    --build-arg "VEROVIO_TAG=$VerovioTag" `
    --target export `
    --output "type=local,dest=$outDir" `
    $RepoRoot
if ($LASTEXITCODE -ne 0) { Write-Error "docker build failed (exit $LASTEXITCODE)"; exit 1 }

# ── Vendor the .so ──────────────────────────────────────────────────────

Write-Step "Vendoring libverovio.so → runtimes/linux-x64/native/"

$soSource = Join-Path $outDir "libverovio.so"
if (-not (Test-Path $soSource)) {
    Write-Error "Build completed but $soSource was not produced."
    exit 1
}

$vendorDir = Join-Path $RepoRoot "src\Verovio.NET\runtimes\linux-x64\native"
New-Item -ItemType Directory -Force -Path $vendorDir | Out-Null
$vendorPath = Join-Path $vendorDir "libverovio.so"
Copy-Item $soSource $vendorPath -Force

if ($ExtractData) {
    Write-Step "Extracting verovio-data for verification"
    $dataSource = Join-Path $outDir "verovio-data"
    $dataDest = Join-Path $vendorDir "verovio-data"
    if (Test-Path $dataDest) { Remove-Item $dataDest -Recurse -Force }
    Copy-Item $dataSource $dataDest -Recurse -Force
    Write-Host "  verovio-data → $dataDest"
}

$hash = (Get-FileHash $vendorPath -Algorithm SHA256).Hash
$size = (Get-Item $vendorPath).Length
$gitSha = (Get-Content (Join-Path $outDir "COMMIT_SHA") -ErrorAction SilentlyContinue | Select-Object -First 1)

Write-Host ""
Write-Host "✓ libverovio.so vendored for linux-x64." -ForegroundColor Green
Write-Host ""
Write-Host "Next: update src/Verovio.NET/runtimes/linux-x64/native/PROVENANCE.md"
Write-Host "  Target RID:      linux-x64"
Write-Host "  Upstream tag:    $VerovioTag"
Write-Host "  Upstream commit: $gitSha"
Write-Host "  SHA-256:         $hash"
Write-Host "  Size (bytes):    $size"

# Tidy the BuildKit export dir; the vendored copy is the artefact of record.
Remove-Item $outDir -Recurse -Force
exit 0
