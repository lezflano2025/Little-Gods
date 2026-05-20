# PreToolUse hook: warn (non-blocking) when an Edit/Write touches engine config.
# Per CLAUDE.md / PRD §0, drift in these files should go through an ADR.

try {
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
} catch {
    exit 0
}

$path = $payload.tool_input.file_path
if (-not $path) { exit 0 }

$leaf = [System.IO.Path]::GetFileName($path)

$guarded = @('project.godot', 'LittleGods.csproj', 'LittleGods.sln', 'global.json')

if ($guarded -contains $leaf) {
    Write-Output "[hook] WARNING: editing $leaf - engine/build config. Per CLAUDE.md, persistent changes here should be justified in an ADR (docs/adr/)."
    # Non-blocking
    exit 0
}

exit 0
