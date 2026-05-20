# M1 plan — Editor data model

**Window:** Weeks 5–8 (4 weeks, 20 working days).
**Spec:** PRD §7 M1.
**Owner sub-agent:** `creature-editor-dev`.

## Acceptance test (lifted from PRD)

A creature designed in the 2D editor:
1. **saves** to a `.tres` recipe file,
2. **reloads identically** (byte-equal after canonicalisation; visually identical in the editor),
3. **occupies <10 KB** uncompressed on disk.

An automated test enforces (1)+(2)+(3) on a fixture creature in CI. Manual playtest covers the UX feel.

## Architectural contract

These are non-negotiable for M1 (PRD §6, restated for this milestone):

- **Recipe = ordered Part IDs + per-Part transforms + paint + metadata.** No baked geometry, no mesh references.
- **`Part`, `Attachment`, `Morph`, `Recipe`** are all C# `Godot.Resource` subclasses in `src/creature/`. UI is GDScript in `scripts/editor/` and `scenes/editor/`. **Don't cross the layer.**
- **Determinism**: snap, symmetry, and recipe serialization must produce byte-identical output for the same inputs. No `DateTime.Now`, no unsynchronized RNG. Tests assume this.
- **`.tres` recipes are committed text** and reviewed in PRs. No binary recipe format.
- **One Rigblock framework**: even though M1 only authors creature parts, the abstraction must be general enough to host tools/vehicles/buildings later (PRD §6 invariant 2). Don't bake "creature" assumptions into `Part`.

## Phase plan

### P1 — C# data model + GdUnit4 (3 days)

**Deliverables**
- `src/creature/Part.cs` — Resource. Fields: `Id` (string, immutable), `DisplayName`, `Kind` (enum: Spine, Limb, Head, Mouth, **Other**), `AttachmentPoints` (array of `AttachmentPoint`), `PaintRegions` (string[]), `Footprint2D` (Vector2 — for the 2D editor's drag radius).
- `src/creature/AttachmentPoint.cs` — value type. `Name` (string), `LocalPosition` (Vector3), `LocalNormal` (Vector3), `AllowedKinds` (PartKind flags).
- `src/creature/Attachment.cs` — Resource. `ParentPartIndex` (int), `ParentSlotName` (string), `ChildPartId` (string), `LocalTransform` (Transform3D), `MorphIndex` (int — into Recipe.Morphs).
- `src/creature/Morph.cs` — Resource. `Stretch` (Vector3, default 1,1,1), `Twist` (float, default 0), `PaintTint` (Color, default white).
- `src/creature/Recipe.cs` — Resource. `FormatVersion` (int, =1), `SpinePartId` (string), `Attachments` (Array of Attachment), `Morphs` (Array of Morph), `Paint` (Dictionary), `Metadata` (Dictionary). Static `Save(path)`, `Load(path)`.
- `src/creature/PartRegistry.cs` — autoload exception (per CLAUDE.md "only for genuine cross-cutting services"). Maps `Id -> Part`. Throws on duplicate registration. Tests can swap the registry.
- `addons/gdUnit4/` — vendored from GdUnit4 v6.1.3 release.
- `tests/Tests.csproj` — separate test project referencing GdUnit4.
- `tests/unit/CreatureDataModelTests.cs` — Part round-trip, Recipe round-trip, Recipe size <10KB, AttachmentPoint default-allows-all, Morph identity-default invariants.

**Acceptance**: `dotnet build` clean, `tools/headless_test.ps1` runs the new unit tests, all green.

### P2 — Initial Rigblock library (2 days)

**Deliverables** (9 hand-authored Part `.tres` resources)
- `assets/rigblock/spine_basic.tres` — 1 spine. 4 attachment slots: head, tail, left-shoulder, right-shoulder. Body axis +Z.
- `assets/rigblock/limb_walker.tres`, `limb_runner.tres`, `limb_wing.tres`, `limb_tail.tres` — 4 limbs. Each has a "root" slot (where it attaches to a parent) and 0–1 child slots ("tip" for chained limbs).
- `assets/rigblock/head_predator.tres`, `head_herbivore.tres` — 2 heads. Slots: jaw, neck.
- `assets/rigblock/mouth_beak.tres`, `mouth_fang.tres` — 2 mouths. Slot: jaw-mount only.
- `tests/unit/RigblockLibraryTests.cs` — every part in the library loads, every attachment slot has a unique name within its part, every part's `Kind` matches its filename prefix.

**Out of scope for M1**: Blender meshes for these parts. The 2D editor renders parts as flat shapes (circle for head/mouth, capsule for limb/spine) using the Part's `Footprint2D`. Blender pipeline lands in M2.

**Acceptance**: All 9 parts load; library tests pass.

### P3 — Save / load (2 days)

**Deliverables**
- `Recipe.Save(string absPath)` — serializes to `.tres` via Godot's built-in `ResourceSaver`. Embeds `FormatVersion=1`.
- `Recipe.Load(string absPath)` — reads `.tres`, validates `FormatVersion`, returns Recipe or throws with a clear error.
- `Recipe.CanonicalBytes()` — deterministic byte representation for the round-trip test (sorts dictionaries by key, etc).
- `Recipe.SizeBytes()` — actual on-disk size after save.
- `tests/unit/RecipeRoundTripTests.cs`:
  - **Round-trip test**: fixture creature → Save → Load → `CanonicalBytes` equal.
  - **Size invariant**: fixture creature recipe `SizeBytes < 10240`.
  - **Format-version test**: loading a v0 file throws `RecipeVersionException`.
  - **Idempotent save**: saving the same recipe twice produces byte-identical output.

**Acceptance**: All recipe tests green. Recipe for "6-leg 2-arm tail" fixture creature is well under 10 KB.

### P4 — 2D blueprint editor (8 days, the long phase)

**Deliverables**
- `scenes/editor/CreatureEditor.tscn` — main editor scene. Layout:
  - Left: part palette (`PartPalette.tscn`) — scrollable list of available Parts grouped by Kind.
  - Center: workspace (`Workspace.tscn`) — top-down view, infinite pannable / zoomable canvas.
  - Right: properties panel (`Properties.tscn`) — selected-part transform, morph sliders, paint tint.
  - Top: file menu, symmetry toggle, save/load buttons, recipe-size readout.
- `scripts/editor/CreatureEditor.gd` — orchestrates the three panels. Holds the in-memory Recipe.
- `scripts/editor/PartPalette.gd`, `Workspace.gd`, `Properties.gd` — per-panel controllers.
- **Drag/drop**: from palette to workspace creates a new Attachment. Workspace snaps the dragged part's nearest AttachmentPoint to the nearest visible AttachmentPoint of an already-placed part within snap radius.
- **Visual rendering**: each Part rendered as its `Footprint2D` shape (circle / capsule / rounded rect) with attachment points as small green dots; selected part shows a blue outline; symmetry mirror shown as a translucent ghost.
- **Selection + delete**: click selects, Delete key removes; deleting a part with children prompts a confirm-or-orphan dialog.
- `scripts/editor/RecipeBuilder.gd` — converts the in-memory editor state to a Recipe and vice versa. The single integration point between UI (GDScript) and data model (C#).

**Acceptance**: a human can drag spine onto the canvas, drag two limbs onto its shoulder slots, save, close the editor, reopen the recipe, see the identical layout.

### P5 — Symmetry + acceptance + polish (3 days)

**Deliverables**
- **Symmetry toggle**: when on, dragging a part onto the left side of the spine mirrors a copy onto the right (and vice versa) as a single undo-step. Mirrored pair lives in the Recipe as **two distinct Attachments** with a shared `MirrorGroupId` so the symmetry can be turned off and the pair edited independently.
- `tests/unit/SymmetryTests.cs` — mirror geometry is exact for an attachment placed at `(x, y, z) -> (-x, y, z)`; toggling symmetry off then on doesn't double-mirror; deleting one of a mirrored pair clears the partner's `MirrorGroupId`.
- `tests/headless/recipe_roundtrip.tscn` — end-to-end automated test: programmatically build a 6-leg-2-arm-1-tail creature via Recipe API, save, load, walk both recipes, assert structural equality. Calls `get_tree().quit(0|1)` per M0 harness convention.
- Recipe size readout in the editor footer; turns red when >9 KB (early warning before the 10 KB hard limit).
- `docs/creature-data-model.md` — formal spec of the Recipe format, version negotiation, mirror semantics.
- ADR if any architectural decision came up that diverges from CLAUDE.md / PRD (e.g. if PartRegistry-as-autoload turned out to be wrong).

**Acceptance**: PRD M1 acceptance test (above) passes end-to-end in CI.

## Total estimate

| Phase | Days | Cumulative | Slack |
|-|-|-|-|
| P1 data model + GdUnit4 | 3 | 3 | |
| P2 Rigblock library | 2 | 5 | |
| P3 save/load | 2 | 7 | |
| P4 2D editor | 8 | 15 | |
| P5 symmetry + acceptance | 3 | 18 | |
| **Slack** | 2 | **20** | within Weeks 5–8 |

## Risks

| Risk | Likelihood | Impact | Mitigation |
|-|-|-|-|
| 2D editor UX takes longer than P4's 8 days | **High** | Medium | Land P1–P3 first (data model is the load-bearing piece); a thin editor that only does drag-drop-snap-save is acceptable for M1, polish slides into M2 prep |
| Recipe size creeps past 10 KB with realistic creatures | Medium | Medium | Profile early in P3 with a max-attachment fixture (e.g. 20 attachments); if tight, switch `LocalTransform` to a packed 12-float array instead of `Transform3D` |
| GdUnit4 + .NET integration is fiddly on Windows | Medium | Low | If GdUnit4 C# support is shaky on Godot 4.6, fall back to xUnit + a thin Godot-runtime bridge; capture the choice in an ADR |
| Symmetry semantics turn out to be design-ambiguous mid-P5 | Medium | Low | Lock the spec in P1 (in `Part.cs` doc-comments); decide *now* that mirrored pairs are two recipe entries linked by `MirrorGroupId`, not a single "symmetric" entry |
| Autoload exception for `PartRegistry` is the wrong call | Low | Low | If tests prove painful to write because of the singleton, refactor to constructor-injected registry in M2; ADR captures the reason |
| Blender pipeline anxiety leaks into M1 | Low | Medium | Explicitly out-of-scope — visual = 2D footprints only. Don't open Blender during M1. |

## Out of scope for M1 (defer to M2)

- 3D mesh / preview
- Metaball field, marching cubes
- Auto-skinning
- Any motion or IK
- Blender → Godot import pipeline
- Procedural texture / paint masks (just a per-part tint for now)
- Multi-spine creatures (the spine is fixed as the recipe's root part)
- Recipe sharing via Supabase (M5)

## Open questions to resolve before P4

- **Workspace navigation**: pan with middle mouse + zoom with wheel (Godot UI convention) or arrow keys / WASD? *Decide before P4 starts.*
- **Undo / redo depth**: M1 ships with at least single-undo on every place / delete / morph-change. Full undo stack: M1 or M2? *Decide before P4 starts.*
- **Where do recipes save by default**: `user://recipes/` (Godot's user data dir) or alongside the project? *Decide before P3 starts; default to `user://recipes/` for portability.*

## Definition of done (one-liner)

> A human draws a creature in the 2D blueprint editor, hits Save, restarts the editor, hits Load, gets the same creature. A CI test does the same thing programmatically and asserts byte-equal recipes. Recipe is <10 KB. We move to M2.
