#Requires -Version 7.0
<#
.SYNOPSIS
  Verovio.NET entry point: verify (fantomas + build + tests + sample
  console smoke), with optional pack into ..\local-nuget-feed\.

.DESCRIPTION
  Verovio.NET is library-only in v0 (no long-running app). The default
  mode is verify.

    pwsh ./run.ps1                  # default — verify: tool restore +
                                    #           fantomas --check + build +
                                    #           Expecto suite + sample
                                    #           console smoke
    pwsh ./run.ps1 -Pack            # verify, then dotnet pack the three
                                    #           Verovio.NET.* packages into
                                    #           ..\local-nuget-feed\

  Switches stack: -SkipFormat / -SkipBuild / -SkipTests / -SkipSample for
  fast iteration inside the verify mode.

.EXAMPLE
  pwsh ./run.ps1

  Full verify.

.EXAMPLE
  pwsh ./run.ps1 -SkipFormat -SkipBuild

  Re-test after a code edit.

.EXAMPLE
  pwsh ./run.ps1 -Pack

  Verify + drop packages into ..\local-nuget-feed\ for inner-loop
  consumption by fuaran-music's Renderer.Engraving (Phase 05+).
#>
[CmdletBinding()]
param(
    [switch] $SkipFormat,
    [switch] $SkipBuild,
    [switch] $SkipTests,
    [switch] $SkipSample,
    [switch] $Pack
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Write-Step {
    param([string] $message)
    Write-Host ""
    Write-Host "── $message ──────────────────────────────────────────────" -ForegroundColor Cyan
}

# ── External Node CLI helpers ─────────────────────────────────────────────
#
# Per the workspace CLAUDE.md "Sibling launcher conventions" section: never
# invoke npm / npx / pnpm via `& npm` from inside a .ps1 — the Node 22.x
# `npm.ps1` shim has a substring-slice bug that silently corrupts arguments
# when invoked through the call operator. Always route through these
# helpers, which use Start-Process under the hood.
#
# Wired up at Phase 03 ahead of Phase 04, which uses Invoke-Npx to vendor
# the upstream `verovio-toolkit-wasm` blob from the npm package.

function Invoke-Npm {
    [CmdletBinding()]
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Args)
    & cmd /c "npm $($Args -join ' ')"
    if ($LASTEXITCODE -ne 0) { throw "npm $($Args -join ' ') failed (exit $LASTEXITCODE)" }
}

function Invoke-Npx {
    [CmdletBinding()]
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Args)
    & cmd /c "npx $($Args -join ' ')"
    if ($LASTEXITCODE -ne 0) { throw "npx $($Args -join ' ') failed (exit $LASTEXITCODE)" }
}

function Invoke-Pnpm {
    [CmdletBinding()]
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Args)
    & cmd /c "pnpm $($Args -join ' ')"
    if ($LASTEXITCODE -ne 0) { throw "pnpm $($Args -join ' ') failed (exit $LASTEXITCODE)" }
}

# ── Project layout ───────────────────────────────────────────────────────

$sln = "Verovio.NET.slnx"
$testProjects = @(
    "src/Verovio.NET.Tests/Verovio.NET.Tests.fsproj"
)
$sampleProject = "samples/Verovio.NET.Samples.Console/Verovio.NET.Samples.Console.fsproj"
$packageProjects = @(
    "src/Verovio.NET/Verovio.NET.fsproj"
)
$localFeed = Join-Path (Split-Path $PSScriptRoot -Parent) 'local-nuget-feed'

# ── Steps ────────────────────────────────────────────────────────────────

Write-Step "dotnet tool restore"
dotnet tool restore
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet tool restore failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }

if (-not $SkipFormat) {
    Write-Step "fantomas --check"
    dotnet fantomas --check .
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Fantomas check failed — run 'dotnet fantomas .' to format in place."
        exit $LASTEXITCODE
    }
}

if (-not $SkipBuild) {
    Write-Step "dotnet build $sln -c Release"
    dotnet build $sln -c Release
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
}

if (-not $SkipTests) {
    foreach ($project in $testProjects) {
        Write-Step "Expecto: $project"
        # Expecto console runner — `dotnet run --project`, NOT `dotnet test`
        # (`dotnet test` silently no-ops on Expecto consoles). `--no-build`
        # MUST precede `--project` or `dotnet run` forwards it to Expecto.
        #
        # `--sequenced` — libverovio holds process-global state (xml:id
        # RNG, log toggles, font/resource path). Concurrent Expecto
        # tests across that state cause intermittent native-heap
        # corruption under parallel execution; sequenced execution
        # eliminates the race for the cost of ~1s wall-clock on the
        # current 90-test suite.
        if ($SkipBuild) {
            dotnet run --project $project -c Release -- --sequenced
        }
        else {
            dotnet run --no-build --project $project -c Release -- --sequenced
        }
        if ($LASTEXITCODE -ne 0) { Write-Error "$project failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
    }
}

if (-not $SkipSample) {
    Write-Step "Sample console smoke: $sampleProject"
    # The sample's smoke test at Phase 03 is "load a fixture, attempt a
    # render, catch the documented NotImplementedException from the stub
    # backend, exit 0". Phase 04 swaps the stub for a real backend; the
    # sample then asserts non-empty SVG output. The sample exits 0 in both
    # phases.
    if ($SkipBuild) {
        dotnet run --project $sampleProject -c Release
    }
    else {
        dotnet run --no-build --project $sampleProject -c Release
    }
    if ($LASTEXITCODE -ne 0) { Write-Error "Sample console failed (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
}

if ($Pack) {
    Write-Step "dotnet pack -> $localFeed"
    if (-not (Test-Path $localFeed)) {
        New-Item -ItemType Directory -Path $localFeed | Out-Null
    }
    foreach ($project in $packageProjects) {
        dotnet pack $project -c Release --no-build --output $localFeed
        if ($LASTEXITCODE -ne 0) { Write-Error "Pack failed for $project (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
    }
}

Write-Host ""
Write-Host "✓ Verify passed." -ForegroundColor Green
exit 0
