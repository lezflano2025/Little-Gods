# Little Gods

A spiritual successor to Spore (2008) — playable prototype with a clean path to a serious project.
Lead feature: procedural creature editor with retargeted procedural animation.

The ground-truth spec is [`PRD.md`](./PRD.md). Project rules for Claude Code are in [`CLAUDE.md`](./CLAUDE.md).

## Stack

- Godot 4.6.3-stable (.NET edition) — see [`docs/adr/0001-godot-version.md`](./docs/adr/0001-godot-version.md)
- C# for procedural math (mesh, IK, gait), GDScript for UI/glue
- Supabase for creature gallery (M5)
- GitHub Actions for CI

## Layout

```
.claude/agents/   Claude Code subagent definitions
assets/rigblock/  Rigblock parts (authored in Blender)
docs/             architecture.md, adr/, animation-pipeline.md, ...
scenes/           Godot .tscn (text format, committed)
scripts/          GDScript
src/              C# (math, creature data model, runtime)
tests/            unit (GdUnit4), headless scenes, visual snapshots
tools/            headless_test.ps1/.sh, snapshot harness
```

## Status

**M0 — Project skeleton + Claude Code harness** (Weeks 1–4 of 48). See PRD §7.
