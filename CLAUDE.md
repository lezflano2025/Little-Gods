# CLAUDE.md — Little Gods

**Ground truth:** Read [`PRD.md`](./PRD.md). Treat its "Technical Decisions" and "Architecture Invariants" sections as locked. Any change to those requires a new ADR in `docs/adr/`.

## Stack snapshot

- Godot **4.6.3-stable** (.NET edition) — locked, see [`docs/adr/0001-godot-version.md`](./docs/adr/0001-godot-version.md)
- .NET 8 SDK
- **C#** for procedural math (mesh, IK, gait, marching cubes). **GDScript** for UI / signals / scene glue.
- **GdUnit4** for C# unit tests. Headless scene tests for invariants. Pixel-diff snapshot tests for visual regressions.
- **Supabase** (from M5) for creature gallery + auth.

## Architecture invariants (do not violate without ADR)

1. Creatures serialize as **recipes** (ordered part IDs + transforms + paint), never as baked meshes. Recipe **<10 KB**.
2. **One Rigblock framework** for everything (creatures, tools, vehicles, buildings, ships).
3. **One Behavior Tree runtime** for all agents (creatures → tribes → civs → empires).
4. All procedural systems **deterministic given a seed**. No `DateTime.Now`, no unsynchronized RNG.
5. **C# does math, GDScript does UI.** Don't cross.
6. **No singletons** except Godot autoloads, and only for genuine cross-cutting services (audio, save, telemetry).
7. **No paid third-party Asset Store deps** for v1. Open source only.
8. `.tscn` and `.tres` are committed as text and reviewed in PRs. **No binary scene format.**

If a request would violate one of these, refuse and surface the conflict. Do not silently work around it.

## Where things live

```
PRD.md                  product spec, ground truth
CLAUDE.md               this file
docs/architecture.md    module boundaries, data flow
docs/adr/               architecture decision records (sequential, immutable once accepted)
docs/creature-data-model.md   (M1) Rigblock format, recipe serialization
docs/animation-pipeline.md    (M3) metaball skin, auto-skin, IK, gait
src/math/               pure C# math (testable in isolation, no Godot deps)
src/creature/           creature data model + runtime
scripts/                GDScript (UI, signal wiring, scene glue)
scenes/                 .tscn (text)
tests/unit/             GdUnit4
tests/headless/         headless scene tests
tests/snapshots/        golden PNGs + the deterministic scenes that produce them
tools/                  headless_test.ps1 / .sh, snapshot harness
.claude/agents/         subagent definitions
.github/workflows/      CI
```

## Workflow

- **Milestones.** Every non-trivial commit references its milestone (M0–M5). See PRD §7.
- **Tests.** Three layers — unit (GdUnit4), headless scene, visual snapshot. CI runs all three on PR. Red CI never merges.
- **ADRs.** To change a locked decision: write a new ADR in `docs/adr/NNNN-<kebab-title>.md` using the 0000 template. Don't edit accepted ADRs; supersede them with a new one.
- **Commits.** Format: `<type>(<scope>): <description>  [<milestone>]`. Types: feat, fix, refactor, docs, test, chore, perf, ci. Example: `feat(creature): add Part resource scaffold  [M1]`.
- **Branches.** Trunk-based with short feature branches. Squash merge.

## Subagents

Defined in `.claude/agents/`. Use them by role:

- `creature-editor-dev` — editor UI + data model (M1)
- `procedural-mesh-dev` — metaball mesher, auto-skinning (M2)
- `animation-dev` — IK, gait, retargeting (M3)
- `simulation-dev` — agent runtime, behavior trees (M4)
- `playtest-runner` — headless builds, screenshots, visual diffs

## MCP servers (set up in M0)

Project-level config lives in [`.mcp.json`](./.mcp.json). Install/verify per [`docs/claude-setup.md`](./docs/claude-setup.md).

- **godot-mcp** — `@coding-solo/godot-mcp` via `npx`. Coding-Solo's fork chosen over `tomyud1/godot-mcp` (PRD §5.2 listed tomyud1 first; Coding-Solo's is the actively-maintained npm package). Scene introspection + edits from Claude Code.
- **Blender MCP** — `blender-mcp` via `uvx` (server) plus the matching Blender add-on. Rigblock part authoring.
- **Supabase MCP** — installed globally on this machine via the Supabase Claude plugin (no project-level config). Used from M5 onward.

## Common commands

```powershell
# Run everything (unit tests + scene tests)
.\tools\headless_test.ps1

# Just C# unit tests (GdUnit4)
.\tools\unit_test.ps1

# Just visual snapshots (captures PNG)
.\tools\snapshot.ps1
.\tools\snapshot-diff.ps1

# Run a specific scene headless
godot --headless --quit-after 600 --path . res://tests/headless/<scene>.tscn

# Manual GdUnit4 invocation (Windows; uses runtest.cmd because
# GdUnit4 refuses --headless flag)
$env:GODOT_BIN = "C:\tools\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe"
.\addons\gdUnit4\runtest.cmd -a res://tests/unit
```

## Hooks (configured in `.claude/settings.json`)

- PreToolUse: block commit if `headless_test.ps1` exits non-zero
- PostToolUse: run `csharpier` on saved `.cs`, `gdformat` on saved `.gd`
- PreToolUse: warn on edits to `project.godot` (engine config drift — requires ADR)

## What to refuse

- Implementing work that violates an Architecture Invariant — surface the conflict; propose an ADR instead.
- Bumping the Godot minor version without superseding ADR-0001.
- Adding a paid Asset Store dependency.
- Putting mesh generation, IK, or physics math in GDScript.
- Putting scene / UI logic in C#.
- Using `DateTime.Now` or unsynchronized RNG in any generator.
