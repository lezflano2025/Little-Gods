#!/usr/bin/env pwsh
# tools/headless_test.ps1 - cross-platform headless test runner.
# Builds C#, runs every scene in tests/headless/ headless,
# expects each scene's script to call get_tree().quit(0|1) for pass/fail.

$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $PSScriptRoot

# Windows dev convenience: ensure our installed paths are reachable.
$isWindowsHost = ($null -eq $PSVersionTable.Platform) -or ($PSVersionTable.Platform -eq 'Win32NT')
if ($isWindowsHost) {
    if (Test-Path 'C:\Program Files\dotnet\dotnet.exe') {
        $env:DOTNET_ROOT = 'C:\Program Files\dotnet'
        if (-not ($env:Path -like '*C:\Program Files\dotnet*')) {
            $env:Path = "C:\Program Files\dotnet;" + $env:Path
        }
    }
    if (-not (Get-Command godot -ErrorAction SilentlyContinue) -and (Test-Path 'C:\tools\bin\godot.cmd')) {
        $env:Path = "C:\tools\bin;" + $env:Path
    }
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$godot  = Get-Command godot  -ErrorAction SilentlyContinue
if (-not $dotnet) { throw "dotnet not on PATH - run tools/setup-dev.ps1 first" }
if (-not $godot)  { throw "godot not on PATH - run tools/setup-dev.ps1 first" }

Push-Location $projectDir
try {
    Write-Host "=== dotnet build ===" -ForegroundColor Cyan
    & dotnet build LittleGods.sln -c Debug -nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)" }

    Write-Host "=== godot --import ===" -ForegroundColor Cyan
    # Run import; ignore exit code (Godot 4 occasionally returns non-zero
    # from --import even on success).
    & godot --headless --import 2>&1 | Out-Null

    Write-Host "=== unit tests (GdUnit4) ===" -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'unit_test.ps1')
    if ($LASTEXITCODE -ne 0) { throw "unit tests failed (exit $LASTEXITCODE)" }

    Write-Host "=== headless scene tests ===" -ForegroundColor Cyan
    $scenes = @(Get-ChildItem -Path (Join-Path $projectDir 'tests/headless') -Filter '*.tscn' -ErrorAction SilentlyContinue)
    if ($scenes.Count -eq 0) {
        Write-Host "  (no scenes in tests/headless/ - skipping)" -ForegroundColor DarkYellow
    } else {
        $failed = 0
        foreach ($s in $scenes) {
            $resPath = "res://tests/headless/$($s.Name)"
            Write-Host "  -> $resPath" -ForegroundColor Yellow
            & godot --headless --quit-after 600 $resPath
            $code = $LASTEXITCODE
            if ($code -ne 0) {
                Write-Host "  FAIL: $resPath (exit $code)" -ForegroundColor Red
                $failed++
            } else {
                Write-Host "  PASS: $resPath" -ForegroundColor Green
            }
        }
        if ($failed -gt 0) { throw "$failed scene test(s) failed" }
    }
} finally {
    Pop-Location
}

Write-Host "=== All headless tests passed ===" -ForegroundColor Green
exit 0
