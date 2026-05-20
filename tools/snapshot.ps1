#!/usr/bin/env pwsh
# tools/snapshot.ps1 - run every scene in tests/snapshots/*.tscn
# and capture deterministic PNGs into tests/snapshots/_actual/.
#
# Snapshot scenes use a SubViewport with a fixed seed, camera, light,
# and exit themselves once the PNG is written.
#
# NOTE: --headless forces Godot's dummy renderer (no textures available).
# We deliberately run with a real renderer. On Windows dev this flashes a
# window briefly. On Linux CI you must run under xvfb-run.

$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $PSScriptRoot
$snapshotsDir = Join-Path $projectDir 'tests/snapshots'
$actualDir = Join-Path $snapshotsDir '_actual'
if (-not (Test-Path $actualDir)) { New-Item -ItemType Directory -Path $actualDir | Out-Null }

# Resolve godot on PATH (Windows convenience)
$isWindowsHost = ($null -eq $PSVersionTable.Platform) -or ($PSVersionTable.Platform -eq 'Win32NT')
if ($isWindowsHost -and -not (Get-Command godot -ErrorAction SilentlyContinue) -and (Test-Path 'C:\tools\bin\godot.cmd')) {
    $env:Path = "C:\tools\bin;" + $env:Path
}
if (-not (Get-Command godot -ErrorAction SilentlyContinue)) {
    throw "godot not on PATH - run tools/setup-dev.ps1 first"
}

$scenes = @(Get-ChildItem -Path $snapshotsDir -Filter '*.tscn' -ErrorAction SilentlyContinue)
if ($scenes.Count -eq 0) {
    Write-Host "(no snapshot scenes in $snapshotsDir)" -ForegroundColor DarkYellow
    exit 0
}

$failed = 0
foreach ($s in $scenes) {
    $resPath = "res://tests/snapshots/$($s.Name)"
    $name = [System.IO.Path]::GetFileNameWithoutExtension($s.Name)
    Write-Host "=== snapshot: $name ===" -ForegroundColor Cyan

    # Run with a real renderer; snapshot script will self-quit.
    & godot --rendering-driver opengl3 --quit-after 600 --path $projectDir $resPath
    $code = $LASTEXITCODE

    $outPng = Join-Path $actualDir "$name.png"
    if ($code -ne 0) {
        Write-Host "  FAIL ($name): godot exited $code" -ForegroundColor Red
        $failed++
    } elseif (-not (Test-Path $outPng)) {
        Write-Host "  FAIL ($name): no PNG at $outPng" -ForegroundColor Red
        $failed++
    } else {
        $size = (Get-Item $outPng).Length
        Write-Host "  OK   ($name): $outPng  ($size bytes)" -ForegroundColor Green
    }
}

if ($failed -gt 0) {
    Write-Error "$failed snapshot(s) failed"
    exit 1
}

Write-Host "=== All snapshots captured ===" -ForegroundColor Green
exit 0
