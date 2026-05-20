# PostToolUse hook: auto-format .cs (csharpier) and .gd (gdformat) after edits.
# Silently skips if the formatter isn't installed (so a fresh clone doesn't break).

try {
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
} catch {
    exit 0
}

$path = $payload.tool_input.file_path
if (-not $path) { exit 0 }
if (-not (Test-Path $path)) { exit 0 }

$ext = [System.IO.Path]::GetExtension($path).ToLowerInvariant()

switch ($ext) {
    '.cs' {
        $tool = Get-Command csharpier -ErrorAction SilentlyContinue
        if ($tool) {
            & csharpier format $path 2>&1 | Out-Null
        }
    }
    '.gd' {
        $tool = Get-Command gdformat -ErrorAction SilentlyContinue
        if ($tool) {
            & gdformat $path 2>&1 | Out-Null
        }
    }
}

exit 0
