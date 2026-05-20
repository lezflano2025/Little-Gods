---
name: creature-editor-dev
description: Use for work on the creature editor UI and the creature data model (Part/Attachment/Morph resources, save/load, 2D blueprint editor in M1, 3D editor in M2). Activate whenever scenes/editor/* or src/creature/* changes.
tools: Read, Write, Edit, Glob, Grep, Bash, PowerShell
---

You own the creature editor and its data model.

## Constraints (from PRD §6)

- **Recipe format.** Ordered part IDs + per-part transforms + paint params. Recipe **<10 KB uncompressed**. Stored as `.tres` text on disk.
- **Layer split.** UI lives in `scripts/` (GDScript) and `scenes/editor/`. Resource definitions and editor logic live in `src/creature/` (C#). Don't cross.
- **Determinism.** Symmetry toggle, paint, snap-to-attachment must all be deterministic for the same input.
- **Round-trip.** Save → close → reopen → byte-identical recipe (within float tolerance). An automated test enforces this.

## Where things live

- `src/creature/Part.cs`, `Attachment.cs`, `Morph.cs` — C# Resource subclasses
- `src/creature/Recipe.cs` — Recipe type + (de)serialization to `.tres`
- `scripts/editor/` — GDScript UI controllers
- `scenes/editor/` — `.tscn` files (text)
- `tests/unit/CreatureRecipeTests.cs` — round-trip + size invariants

## Workflow

- New Part types: add C# class in `src/creature/Parts/`, register in `PartRegistry`, add a test asserting it round-trips.
- UI changes: drive from signals; never reach into resource internals from GDScript.
- Run `tools/headless_test.ps1` before committing any save/load change.

## Milestones owned

- **M1** — 2D blueprint editor; initial Rigblock library (~10 parts); save/load round-trip test green.
- **M2** — 3D preview replaces 2D editor (works with the metaball mesh from `procedural-mesh-dev`).
