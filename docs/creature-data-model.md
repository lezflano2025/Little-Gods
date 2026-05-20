# Creature data model

Status: **M1 spec, locked.** Bumping any of the resource shapes below requires a `FormatVersion` bump on `Recipe` plus a migration in `Recipe.Load`. Adding fields is OK and forward-compatible; renaming or removing fields is not.

This document is the human-readable companion to the C# source under `src/creature/`. The code is authoritative; this is what to read first.

## What a creature is

A creature is a **Recipe** — an ordered set of part instances plus per-part morph + paint. Recipes serialise to `.tres` text under the 10 KB ceiling specified by PRD §6 invariant 1.

There is **no baked geometry** in a Recipe. Parts come from the on-disk Rigblock library (`assets/rigblock/*.tres`); the Recipe references them by `Id`.

The same abstraction is intended to host tools, vehicles, buildings, and ships in later milestones (PRD §6 invariant 2). Only the library content changes.

## Resources

### `Part`  (`src/creature/Part.cs`)

A Rigblock atom from the library. Immutable at runtime.

| Field | Type | Default | Notes |
|-|-|-|-|
| `Id` | `string` | `""` | Stable identifier. Convention: lowercase snake_case (`spine_basic`, `limb_walker`). Referenced by `Attachment.ChildPartId`. |
| `DisplayName` | `string` | `""` | UI-facing label. |
| `Kind` | `PartKind` | `Other` | One of `Spine, Limb, Head, Mouth, Other`. |
| `AttachmentPoints` | `Array<AttachmentPoint>` | empty | Named slots where child parts can connect. |
| `PaintRegions` | `string[]` | empty | Symbolic region names (e.g. `"back", "belly"`). |
| `Footprint2D` | `Vector2` | `(1, 1)` | M1 blueprint editor uses this to render the part as a 2D shape. M2 brings real 3D meshes. |

### `AttachmentPoint`  (`src/creature/AttachmentPoint.cs`)

A named socket on a `Part`.

| Field | Type | Default | Notes |
|-|-|-|-|
| `Name` | `string` | `""` | Unique within a Part. E.g. `"head"`, `"left_shoulder"`. Mirror convention: `left_X` <-> `right_X`. |
| `LocalPosition` | `Vector3` | `(0, 0, 0)` | Slot offset in the parent Part's local frame. |
| `LocalNormal` | `Vector3` | `(0, 1, 0)` | Outward direction at the slot. Used by M2/M3 for mesh stitching + IK. |
| `AllowedKinds` | `PartKindMask` | `All` | Bitmask of `PartKind`s this slot accepts. Defaults to `All`. |

### `Attachment`  (`src/creature/Attachment.cs`)

One placed instance of a Part in a Recipe.

| Field | Type | Default | Notes |
|-|-|-|-|
| `ParentPartIndex` | `int` | `-1` | `-1` = attached to the Recipe's spine. `0..N-1` = attached to `Recipe.Attachments[i]`. **Must reference only earlier indices** (no forward refs, no cycles). |
| `ParentSlotName` | `string` | `""` | Name of the AttachmentPoint on the parent Part. The validator enforces this exists. |
| `ChildPartId` | `string` | `""` | `Part.Id` of the part being attached. Resolved against the `PartRegistry` at runtime. |
| `LocalTransform` | `Transform3D` | identity | Offset of the placed Part from its slot anchor. |
| `MorphIndex` | `int` | `-1` | Index into `Recipe.Morphs`, or `-1` for identity. |
| `MirrorGroupId` | `string` | `""` | Empty = not mirrored. Non-empty = symmetry-paired with the other Attachment(s) sharing this id. |

### `Morph`  (`src/creature/Morph.cs`)

Per-attachment shape modifier. Identity defaults: no stretch, no twist, white tint.

| Field | Type | Default | Notes |
|-|-|-|-|
| `Stretch` | `Vector3` | `(1, 1, 1)` | Per-axis scale. |
| `Twist` | `float` | `0` | Rotation about the slot's normal, in radians. |
| `PaintTint` | `Color` | white | Multiplied with the Part's paint regions. |

### `Recipe`  (`src/creature/Recipe.cs`)

The top-level resource. Saved as `.tres` text.

| Field | Type | Default | Notes |
|-|-|-|-|
| `FormatVersion` | `int` | `1` | Bumped when the on-disk format changes. `Load` throws `RecipeVersionException` on mismatch. |
| `SpinePartId` | `string` | `""` | Root part. Must exist in registry; validator enforces. |
| `Attachments` | `Array<Attachment>` | empty | Ordered. Each may reference earlier siblings via `ParentPartIndex`. |
| `Morphs` | `Array<Morph>` | empty | Pool of Morph instances; Attachments reference by index. |
| `Paint` | `Dictionary` | empty | `region_name (string) -> Color` overrides. |
| `Metadata` | `Dictionary` | empty | Free-form (author, created_at as ISO-8601 string, tags). Editor populates on explicit save; data-layer code must NOT touch `DateTime.Now`. |

Constants:
- `Recipe.CurrentFormatVersion = 1`
- `Recipe.MaxRecipeBytes = 10 * 1024` (PRD §6 invariant 1)

## Mirroring

Symmetry is stored explicitly, not implied. When the editor places a part with Symmetry enabled and the chosen slot has a mirror partner (`left_*` <-> `right_*`), **two** Attachments are added: the primary at the original slot, and a mirror at the partner slot. Both share a unique `MirrorGroupId` so the pair can be:

- Identified as a unit (delete one, the other's group id is cleared but it stays placed — the user can edit it independently after).
- Re-toggled later (M1+ may expose a "unlink mirror" UX without removing the partner).

The mirror transform is the position-only X flip: `(x, y, z) -> (-x, y, z)`. Basis is preserved. Real orientation mirroring lands when IK retargeting arrives in M3.

`RecipeBuilder.MirrorSlotName` is the canonical implementation. The 2D editor's GDScript `Workspace.gd` and `CreatureEditor.gd` duplicate the 4-line helper because GDScript cannot call C# statics; this is documented inline in those files.

## Storage convention

User recipes live at `user://recipes/<slug>.tres`. The `RecipeStorage` static class owns the convention:

- `RecipeStorage.PathFor(slug)` — sanitises the slug (alphanumeric + `-_` only; empty becomes `"unnamed"`)
- `RecipeStorage.Save(recipe, slug)` — creates the directory if needed
- `RecipeStorage.Load(slug)` / `Exists` / `Delete` / `List`

The 2D editor uses the same convention via inlined GDScript helpers in `scripts/editor/CreatureEditor.gd`.

## Validation

`RecipeValidator.Validate(recipe, registry)` returns a list of `Issue` structs with stable `Code` values (so tests assert on codes, not message strings):

| Code | Meaning |
|-|-|
| `no_spine` | Recipe has empty `SpinePartId` |
| `unknown_spine` | Spine Id not in registry |
| `unknown_child` | Attachment references a Part Id not in registry |
| `forward_ref` | Attachment's `ParentPartIndex >= self` |
| `bad_parent_index` | `ParentPartIndex` < `-1` or beyond list |
| `bad_morph_index` | `MorphIndex >= Recipe.Morphs.Count` |
| `unknown_slot` | `ParentSlotName` isn't on the parent Part |
| `kind_not_allowed` | Child Kind not in slot `AllowedKinds` mask |

`RecipeValidator.IsValid` is the shortcut for `Validate(...).Count == 0`.

## Format version policy

- **Adding a field** to any of the above resources: keep `FormatVersion` at the current value. Loading an older `.tres` will silently default the missing field via the Godot resource binding.
- **Renaming or removing a field**: bump `FormatVersion`, add a migration in `Recipe.Load` that reads the older format and synthesises the new one. The existing `RecipeVersionException` path catches anything unmigrated.

When in doubt: bump. The version field is cheap; a botched migration is not.
