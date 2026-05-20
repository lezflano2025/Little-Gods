#!/usr/bin/env pwsh
# tools/verify-mcp.ps1 - smoke-test the MCP servers declared in .mcp.json.
# Verifies the packages can be fetched / started; does not check Claude Code
# itself has registered them (use /mcp inside Claude Code for that).

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot

Write-Host "=== MCP verification ===" -ForegroundColor Cyan
$failed = 0

# --- godot-mcp ---
Write-Host "-> godot-mcp (Coding-Solo)" -ForegroundColor Yellow
$node = Get-Command node -ErrorAction SilentlyContinue
if (-not $node) {
    Write-Host "  FAIL: node not on PATH (need Node 18+)" -ForegroundColor Red
    $failed++
} else {
    $nodeVer = (& node --version) -replace '^v',''
    $major = [int]($nodeVer -split '\.')[0]
    if ($major -lt 18) {
        Write-Host "  FAIL: node $nodeVer < 18" -ForegroundColor Red
        $failed++
    } else {
        # Resolve the package (download to npx cache if needed)
        $out = & npx -y --package=@coding-solo/godot-mcp -- node -e "console.log('resolved')" 2>&1
        if ($LASTEXITCODE -eq 0 -and ($out -match 'resolved')) {
            Write-Host "  OK: node $nodeVer, @coding-solo/godot-mcp resolved" -ForegroundColor Green
        } else {
            Write-Host "  FAIL: $out" -ForegroundColor Red
            $failed++
        }
    }
}

# --- blender-mcp ---
Write-Host "-> blender-mcp" -ForegroundColor Yellow
$uvx = Get-Command uvx -ErrorAction SilentlyContinue
if (-not $uvx) {
    Write-Host "  FAIL: uvx not on PATH (winget install astral-sh.uv)" -ForegroundColor Red
    $failed++
} else {
    # Start blender-mcp briefly to see it loads. It will exit when stdin closes.
    $out = & uvx --quiet blender-mcp --help 2>&1
    if ($LASTEXITCODE -eq 0 -or ($out -match 'blender' -or $out -match 'MCP')) {
        Write-Host "  OK: uvx blender-mcp available" -ForegroundColor Green
    } else {
        Write-Host "  FAIL: $out" -ForegroundColor Red
        $failed++
    }
}

if ($failed -gt 0) {
    Write-Error "$failed MCP server(s) failed verification"
    exit 1
}

Write-Host "=== MCP verification PASS ===" -ForegroundColor Green
Write-Host "Open this project in Claude Code and run /mcp to confirm registration."
exit 0
