# Dev Setup

## Quick start (Windows)

```powershell
git clone https://github.com/lezflano2025/Little-Gods.git C:\dev\Little-Gods
cd C:\dev\Little-Gods
.\tools\setup-dev.ps1     # installs .NET 8, Godot 4.6.3, csharpier, gdtoolkit
# restart shell so PATH/DOTNET_ROOT take effect
.\tools\headless_test.ps1 # should print "All headless tests passed"
```

## What `setup-dev.ps1` installs

| Tool | Version | Why |
|-|-|-|
| .NET 8 SDK | 8.0.x | C# compilation for Godot.NET.Sdk |
| Godot | 4.6.3-stable (.NET edition) | engine, pinned in ADR-0001 |
| `godot.cmd` wrapper | — | exposes `godot` on PATH; calls the version-stamped exe |
| `csharpier` | latest | C# formatter; invoked by the post-edit hook |
| `gdtoolkit` (`gdformat`, `gdlint`) | latest | GDScript formatter + linter |

Locations:

- Godot: `C:\tools\Godot\Godot_v4.6.3-stable_mono_win64\`
- Wrappers: `C:\tools\bin\godot.cmd`, `C:\tools\bin\godot-editor.cmd`
- .NET: `C:\Program Files\dotnet\`

## Verify install

```powershell
godot --version          # 4.6.3.stable.mono.official.7d41c59c4
dotnet --version         # 8.0.x
csharpier --version
gdformat --version
```

## Running tests locally

```powershell
.\tools\headless_test.ps1
```

This builds the C# solution, imports the Godot project, then runs every
`tests/headless/*.tscn`. Each scene's script is expected to call
`get_tree().quit(0)` on pass or `quit(1)` on fail. The harness fails
if any scene exits non-zero.

## CI

GitHub Actions runs the same harness on Ubuntu — see
[`.github/workflows/ci.yml`](../.github/workflows/ci.yml). The CI uses
PowerShell 7 (`pwsh`) to run `tools/headless_test.ps1` cross-platform.

## MCP servers (Claude Code)

See [`docs/claude-setup.md`](./claude-setup.md) (created when MCP servers
are wired in M0 P4).

## GdUnit4 (deferred to M1)

PRD §5.3 calls for GdUnit4 unit tests on pure C# code. M0 ships only
the scene-level headless harness above; GdUnit4 wiring lands together
with the first `tests/unit/*.cs` test in M1, since there's nothing yet
to unit-test until the creature data model exists.
