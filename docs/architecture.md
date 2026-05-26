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
- **M2** — Done. `src/mesh/` module layout:

  | Type | File | Role |
  |-|-|-|
  | `interface IScalarField` | `IScalarField.cs` | Seam between field and marching cubes: `Sample(Vector3)` + `Aabb Bounds` |
  | `readonly struct Bone` | `Bone.cs` | World-space segment (Head, Tail, RadiusHead, RadiusTail, ParentIndex); `DistanceTo`, `ClosestPoint`, `RadiusAt`, `Length`, `MaxRadius` |
  | `class CreatureSkeleton` | `CreatureSkeleton.cs` | `Bone[]` + world-space `Aabb` + `Count` |
  | `class MeshData` | `MeshData.cs` | `Vector3[] Vertices/Normals`, `int[] Indices`; `IsEmpty`, `TriangleCount`, `VertexCount` |
  | `class SkinData` | `SkinData.cs` | Flat `int[] BoneIndices`, `float[] Weights` — 4 per vertex; `VertexCount` |
  | `static SkeletonResolver` | `SkeletonResolver.cs` | `Recipe + PartRegistry → CreatureSkeleton`; pure, deterministic |
  | `sealed MetaballField` | `MetaballField.cs` | Wyvill kernel sum over bones; implements `IScalarField`; `R = r / 0.454` calibration |
  | `static MarchingCubes` | `MarchingCubes.cs` | Bourke-table polygoniser; indexed/watertight; gradient normals; serial march + parallel field passes |
  | `MarchingCubesTables` | `MarchingCubesTables.cs` | 256-entry edge + triangle tables (public-domain, Bourke / Lorensen-Cline) |
  | `static AutoSkinner` | `AutoSkinner.cs` | 4-nearest-bones by segment distance; inverse-distance weights normalised to 1 |
  | `static CreatureMesher` | `CreatureMesher.cs` | Orchestrates full pipeline; `GridParams` (CellSize, IsoLevel) |
  | `static GodotMeshBuilder` | `GodotMeshBuilder.cs` | `(MeshData, SkinData) → ArrayMesh`; `CreatureSkeleton → Skeleton3D` |
  | `partial class CreaturePreview` | `CreaturePreview.cs` | `Node3D` bridge; `Rebuild(Recipe, PartRegistry)` instance method for GDScript |

- **M3** — Done. Procedural animation in `src/anim/` (pure C#, deterministic, time enters as an explicit `double seconds`):

  | Type | File | Role |
  |-|-|-|
  | `readonly struct LimbChain` | `LimbChain.cs` | Resolved 2-bone limb: root/knee/foot bone indices, segment lengths, slot (ADR-0003) |
  | `readonly struct IkResult` | `IkResult.cs` | IK output: knee + foot positions + Reachable flag |
  | `enum LimbType` | `LimbType.cs` | Leg / Arm / Wing / Tail / Other |
  | `readonly struct Gait` | `Gait.cs` | Cadence, duty factor, per-leg phase offsets |
  | `readonly struct Pose` | `Pose.cs` | Per-bone local transform deltas (`posedLocal = rest · delta`) |
  | `struct JiggleParams` | `Jiggle.cs` | Spring stiffness + damping |
  | `static TwoBoneIk` | `TwoBoneIk.cs` | Law-of-cosines 2-bone IK; reach-clamped, degenerate-safe |
  | `static LimbClassifier` | `LimbClassifier.cs` | `LimbType` per chain from slot + part id |
  | `static GaitController` | `GaitController.cs` | Phase presets by leg count (biped / quadruped / hexapod / octopod); `PhaseOf`, `IsStance` |
  | `static Locomotion` | `Locomotion.cs` | Per-tick gait → foot targets → IK → `Pose` + body advance/bob |
  | `static Jiggle` | `Jiggle.cs` | Spring-damped secondary motion (deterministic) |

  Also M3: `SkeletonResolver` splits `PartKind.Limb` into 2-bone chains (ADR-0003); `GodotMeshBuilder.BuildSkin` + `CreaturePreview.ApplyPose` / `WalkTick` make posing the `Skeleton3D` deform the skin. The **Hecker gate** — 2/4/6/8-leg creatures walk 60 s with no IK breaks via one code path — passes (`HeckerWalkTests`).
- **M4** — Behavior tree node taxonomy, blackboard schema, LimboAI integration.
- **M5** — Supabase schema (`creatures`, `users`, `gallery_metadata`), recipe upload/download flow, auth.
