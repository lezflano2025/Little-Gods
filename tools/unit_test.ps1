#!/usr/bin/env pwsh
# tools/unit_test.ps1 - run GdUnit4 C# unit tests under tests/unit/.
#
# GdUnit4's CLI tool refuses --headless (it warns about InputEvents not
# being delivered). On Windows, runtest.cmd minimizes a window briefly.
# On Linux CI, wrap with xvfb-run.

$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $PSScriptRoot
$isWindowsHost = ($null -eq $PSVersionTable.Platform) -or ($PSVersionTable.Platform -eq 'Win32NT')

if ($isWindowsHost) {
    if (Test-Path 'C:\Program Files\dotnet\dotnet.exe') {
        $env:DOTNET_ROOT = 'C:\Program Files\dotnet'
        # Prepend unconditionally so the 64-bit SDK host wins over any x86
        # dotnet (C:\Program Files (x86)\dotnet) that ships without an SDK and
        # would otherwise be found first on PATH.
        $env:Path = "C:\Program Files\dotnet;" + $env:Path
    }
    if (-not $env:GODOT_BIN) {
        $env:GODOT_BIN = 'C:\tools\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe'
    }
}

Push-Location $projectDir
try {
    if ($isWindowsHost) {
        & .\addons\gdUnit4\runtest.cmd -a res://tests/unit
    } else {
        # Linux / macOS - use runtest.sh
        & ./addons/gdUnit4/runtest.sh -a res://tests/unit
    }
    $code = $LASTEXITCODE
} finally {
    Pop-Location
}

if ($code -ne 0) {
    Write-Error "GdUnit4 exited $code"
    exit 1
}

Write-Host "=== Unit tests passed ===" -ForegroundColor Green
exit 0
