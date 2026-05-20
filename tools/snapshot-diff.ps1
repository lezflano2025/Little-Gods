#!/usr/bin/env pwsh
# tools/snapshot-diff.ps1 - compare tests/snapshots/_actual/*.png against
# tests/snapshots/golden/*.png. Fails if any actual differs from its golden
# beyond DIFF_TOLERANCE_PCT pixel diff.
#
# Cross-platform: drives Godot itself to read both PNGs and compute the diff,
# so it works without ImageMagick or System.Drawing.

[CmdletBinding()]
param(
    [double]$ToleranceFraction = 0.02,    # 2% of pixels may differ
    [int]$ChannelTolerance = 8,           # per-channel u8 tolerance
    [switch]$UpdateGolden                 # if set: copy _actual/* over golden/* and exit 0
)

$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $PSScriptRoot
$actualDir = Join-Path $projectDir 'tests/snapshots/_actual'
$goldenDir = Join-Path $projectDir 'tests/snapshots/golden'
$diffDir = Join-Path $projectDir 'tests/snapshots/_diff'

if ($UpdateGolden) {
    if (-not (Test-Path $goldenDir)) { New-Item -ItemType Directory -Path $goldenDir | Out-Null }
    Get-ChildItem $actualDir -Filter '*.png' | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $goldenDir $_.Name) -Force
        Write-Host "updated golden: $($_.Name)" -ForegroundColor Yellow
    }
    exit 0
}

if (-not (Test-Path $goldenDir)) {
    throw "no golden images at $goldenDir - run with -UpdateGolden after capturing baselines"
}
if (-not (Test-Path $actualDir)) {
    throw "no actual images at $actualDir - run tools/snapshot.ps1 first"
}

# Resolve godot
$isWindowsHost = ($null -eq $PSVersionTable.Platform) -or ($PSVersionTable.Platform -eq 'Win32NT')
if ($isWindowsHost -and -not (Get-Command godot -ErrorAction SilentlyContinue) -and (Test-Path 'C:\tools\bin\godot.cmd')) {
    $env:Path = "C:\tools\bin;" + $env:Path
}

if (-not (Test-Path $diffDir)) { New-Item -ItemType Directory -Path $diffDir | Out-Null }

$goldens = @(Get-ChildItem $goldenDir -Filter '*.png')
$failed = 0
foreach ($g in $goldens) {
    $name = $g.Name
    $actual = Join-Path $actualDir $name
    if (-not (Test-Path $actual)) {
        Write-Host "  MISSING ($name): no actual at $actual" -ForegroundColor Red
        $failed++
        continue
    }

    # Invoke Godot to do the diff (cross-platform image API).
    $diffOut = Join-Path $diffDir $name
    $args = @(
        '--headless',
        '--quit-after', '120',
        '--path', $projectDir,
        '-s', 'res://tools/diff_images.gd',
        '--',
        ('res://tests/snapshots/golden/' + $name),
        ('res://tests/snapshots/_actual/' + $name),
        ('res://tests/snapshots/_diff/' + $name),
        $ChannelTolerance.ToString()
    )
    $out = & godot @args 2>&1
    $code = $LASTEXITCODE

    $diffLine = ($out | Select-String -Pattern '^\[diff\] ').ForEach{ $_.Line }
    Write-Host "  $name -> $diffLine"

    if ($code -ne 0) {
        Write-Host "  FAIL ($name): diff script exit $code" -ForegroundColor Red
        $failed++
        continue
    }

    # Parse "[diff] frac=0.0123 ..." from output
    $m = [regex]::Match(($diffLine -join ' '), 'frac=([0-9.]+)')
    if (-not $m.Success) {
        Write-Host "  WARN ($name): could not parse diff fraction" -ForegroundColor Yellow
        continue
    }
    $frac = [double]$m.Groups[1].Value
    if ($frac -gt $ToleranceFraction) {
        Write-Host "  FAIL ($name): $frac > $ToleranceFraction (see _diff/$name)" -ForegroundColor Red
        $failed++
    } else {
        Write-Host "  OK   ($name): $frac <= $ToleranceFraction" -ForegroundColor Green
    }
}

if ($failed -gt 0) {
    Write-Error "$failed snapshot(s) outside tolerance"
    exit 1
}

Write-Host "=== All snapshots within tolerance ===" -ForegroundColor Green
exit 0
