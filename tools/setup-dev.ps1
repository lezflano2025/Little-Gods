# tools/setup-dev.ps1 — install developer tooling for Little Gods.
# Idempotent. Run after cloning. Requires PowerShell 5.1+ and an internet connection.

$ErrorActionPreference = 'Stop'

Write-Host "=== Little Gods dev setup ===" -ForegroundColor Cyan

# --- .NET 8 SDK -----------------------------------------------------------
$dotnet = & "C:\Program Files\dotnet\dotnet.exe" --list-sdks 2>$null
if ($LASTEXITCODE -ne 0 -or -not ($dotnet -match '^8\.')) {
    Write-Host "Installing .NET 8 SDK via winget..." -ForegroundColor Yellow
    winget install --id Microsoft.DotNet.SDK.8 --source winget --accept-source-agreements --accept-package-agreements --silent
}
$env:DOTNET_ROOT = 'C:\Program Files\dotnet'
$env:Path = "C:\Program Files\dotnet;" + $env:Path

# --- Godot 4.6.3-stable .NET edition --------------------------------------
$godotDir = 'C:\tools\Godot\Godot_v4.6.3-stable_mono_win64'
if (-not (Test-Path "$godotDir\Godot_v4.6.3-stable_mono_win64.exe")) {
    Write-Host "Downloading Godot 4.6.3-stable .NET edition..." -ForegroundColor Yellow
    if (-not (Test-Path 'C:\tools\Godot')) { New-Item -ItemType Directory -Path 'C:\tools\Godot' | Out-Null }
    $url = 'https://github.com/godotengine/godot/releases/download/4.6.3-stable/Godot_v4.6.3-stable_mono_win64.zip'
    $zip = 'C:\tools\Godot\Godot_v4.6.3-stable_mono_win64.zip'
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath 'C:\tools\Godot' -Force
}

# --- godot.cmd wrapper on PATH --------------------------------------------
if (-not (Test-Path 'C:\tools\bin')) { New-Item -ItemType Directory -Path 'C:\tools\bin' | Out-Null }
@'
@echo off
"C:\tools\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe" %*
'@ | Out-File -FilePath 'C:\tools\bin\godot.cmd' -Encoding ascii -Force

@'
@echo off
"C:\tools\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe" %*
'@ | Out-File -FilePath 'C:\tools\bin\godot-editor.cmd' -Encoding ascii -Force

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($userPath -notlike '*C:\tools\bin*') {
    [Environment]::SetEnvironmentVariable('Path', "$userPath;C:\tools\bin", 'User')
}

# --- csharpier (C# formatter) ---------------------------------------------
if (-not (Get-Command csharpier -ErrorAction SilentlyContinue)) {
    Write-Host "Installing csharpier..." -ForegroundColor Yellow
    & "C:\Program Files\dotnet\dotnet.exe" tool install -g csharpier
}

# --- gdtoolkit (GDScript formatter + linter) ------------------------------
if (-not (Get-Command gdformat -ErrorAction SilentlyContinue)) {
    Write-Host "Installing gdtoolkit (requires Python + pip)..." -ForegroundColor Yellow
    pipx install gdtoolkit 2>$null
    if ($LASTEXITCODE -ne 0) {
        pip install --user gdtoolkit
    }
}

Write-Host "=== Setup complete ===" -ForegroundColor Green
Write-Host "Restart your shell to pick up PATH/DOTNET_ROOT changes."
Write-Host "Then: godot --version  should report 4.6.3.stable.mono"
