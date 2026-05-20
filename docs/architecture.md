# Architecture — Little Gods

**Status:** Skeleton (M0). Filled in as modules land.

## Module boundaries

```
┌──────────────────────────────────────────────────────────────┐
│                        Godot scene tree                      │
│   (UI, signals, autoloads, .tscn — GDScript)                 │
└──────────┬───────────────────────────────────────────────────┘
           │ calls into
           ▼
┌──────────────────────────────────────────────────────────────┐
│                    Procedural runtime (C#)                   │
│                                                              │
│   src/math/             pure math, no Godot deps             │
│   src/creature/         Part / Attachment / Morph, recipe    │
│   src/mesh/      (M2)   metaball field, marching cubes       │
│   src/anim/      (M3)   IK solver, gait controller           │
│   src/agent/     (M4)   behavior tree runtime                │
└──────────┬───────────────────────────────────────────────────┘
           │ persists to
           ▼
┌──────────────────────────────────────────────────────────────┐
│   .tres on disk (M1)    Supabase (M5) gallery + auth         │
└──────────────────────────────────────────────────────────────┘
```

## Data flow — single creature, one editor frame

1. UI (GDScript) emits a "part dragged" signal.
2. Editor controller (C#) updates the in-memory `Recipe`.
3. `Recipe` → metaball field generator → mesh (C#, target **<50 ms p95**).
4. Mesh + auto-skin weights handed back to a `MeshInstance3D` (thin GDScript wrapper).
5. Frame renders.

## Determinism contract

Every procedural module accepts an explicit `ulong seed`. No module reads `DateTime`, `Environment.TickCount`, or any unsynchronized RNG. Tests assume this. A unit test that fails this contract is a release blocker.

## To be filled

- **M1** — `Part` / `Attachment` / `Morph` C# resource shapes; `Recipe` serialization (`.tres` text format, <10 KB).
- **M2** — Metaball field math, marching-cubes implementation, auto-skin algorithm (inverse distance to 4 nearest bones, normalized).
- **M3** — Two-bone analytic IK solver, gait phase model (per-limb phase offsets), limb-type classifier (leg / arm / wing / tail).
- **M4** — Behavior tree node taxonomy, blackboard schema, LimboAI integration.
- **M5** — Supabase schema (`creatures`, `users`, `gallery_metadata`), recipe upload/download flow, auth.
