# PreToolUse hook: when a Bash/PowerShell tool call contains `git commit`,
# require headless tests to pass first. Per CLAUDE.md: "do not commit if
# headless_test.ps1 fails."

$ErrorActionPreference = 'Stop'

try {
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
} catch {
    # Couldn't parse stdin — bail open (don't block work due to hook bug).
    exit 0
}

$cmd = $payload.tool_input.command
if (-not $cmd) { exit 0 }

# Only gate `git commit`. `git commit --amend` is also gated.
if ($cmd -notmatch '\bgit\s+commit\b') { exit 0 }

# Resolve project root via env var, fall back to two-up from this script.
$projectDir = $env:CLAUDE_PROJECT_DIR
if (-not $projectDir) {
    $projectDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
}

$harness = Join-Path $projectDir 'tools/headless_test.ps1'

# If the headless harness hasn't landed yet (we're mid-M0), don't block.
if (-not (Test-Path $harness)) {
    Write-Output "[hook] tools/headless_test.ps1 not present yet — skipping gate (expected during M0)."
    exit 0
}

# Run the harness. Capture output so we can show it to Claude on failure.
$out = & powershell -NoProfile -ExecutionPolicy Bypass -File $harness 2>&1
$exit = $LASTEXITCODE

if ($exit -ne 0) {
    Write-Output $out
    [Console]::Error.WriteLine("[hook] headless tests failed (exit $exit). Commit blocked. Fix tests or revert before committing.")
    exit 2  # blocking
}

exit 0
