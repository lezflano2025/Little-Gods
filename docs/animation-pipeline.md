# Animation pipeline — metaball skin (M2) + procedural locomotion (M3)

**Status:** M2 + M3 complete. M2 (below) is the `Recipe → ArrayMesh + Skeleton3D` mesh pipeline; M3 (the "M3 — procedural animation" section at the bottom) is the IK / gait / locomotion layer that makes creatures walk. The code is the authoritative reference; this document explains the *why* behind each step.

---

## Bone model

> **M3 update (ADR-0003):** `PartKind.Limb` parts now resolve to a **two-bone chain** (upper + lower, knee at the midpoint, colinear at rest), so bones are no longer 1:1 with attachments. `SkeletonResolver` records a `LimbChain` per limb and maintains an attachment→tip-bone map (a child parents to its parent attachment's tip bone, never `i + 1`). The one-bone description below still holds for non-limb parts (spine, head, mouth). See "M3 — procedural animation".

One bone per placed part (non-limb). Bones are world-space line segments `(Head, Tail)` with a radius at each end (`RadiusHead`, `RadiusTail`) and a `ParentIndex`.

**Root (spine) bone — bone 0.**
Centred on its local +Z axis at the world origin:
```
Head = -Z * BoneLength / 2
Tail = +Z * BoneLength / 2
```

**Attachment bones — bone `i + 1` for `Recipe.Attachments[i]`.**
Each attachment's bone is anchored at its slot:
```
Head = world position of the parent slot anchor
Tail = Head + (slot world +Z) * BoneLength
```
`SkeletonResolver` composes a frame chain from the root outward, using each slot's `LocalPosition` and `LocalNormal` to orient a local basis (`BasisLookingAlong`) so that the part grows along the slot's outward normal — shoulders splay sideways, tails extend back, etc.

**Radii.** `RadiusHead = Part.RadiusStart * radiusScale`, `RadiusTail = Part.RadiusEnd * radiusScale`.

**Morph scaling.**
- `Morph.Stretch.Z` scales bone length.
- `(Morph.Stretch.X + Morph.Stretch.Y) / 2` scales both radii.
- `Morph.Twist` rotates the child frame about +Z (affects child attachment orientations, no-op on the round metaball skin itself).

**Parent indices.** `Bone.ParentIndex == 0` for direct spine children; `ParentPartIndex + 1` for deeper attachments; `-1` only on bone 0.

`SkeletonResolver.Resolve(Recipe, PartRegistry) → CreatureSkeleton` is pure and deterministic (no RNG, no clock).

---

## Metaball field

`MetaballField` implements `IScalarField` (`float Sample(Vector3 p)`, `Aabb Bounds`) as a sum of per-bone Wyvill smooth-falloff kernels.

**Kernel (Wyvill et al., 1986):**
```
contribution(d, R) = (1 - (d/R)^2)^3    for d < R, else 0
```
where `d` is the distance from `p` to the bone segment (`Bone.DistanceTo(p)`) and `R` is the *support radius*: `R = r / K`, `K = 0.454`.

**Calibration.** At `d == r` (bone surface), `(1 - K^2)^3 ≈ 0.5`. So a lone bone's `iso = 0.5` contour sits exactly at its `RadiusAt(p)` — the linearly interpolated radius along the segment.

**Blending.** Multiple bones sum their contributions. Where bones overlap (joints, where a limb meets the body) the summed field exceeds the single-bone value, fusing them into a continuous skin without seams.

**Bounds.** `Bounds` is the union of per-bone AABBs grown by `MaxRadius / K` (conservative; never clips the surface). Computed once in the constructor.

---

## Marching cubes

`MarchingCubes.Polygonise(IScalarField, cellSize, isoLevel) → MeshData`

Extracts the `isoLevel = 0.5` iso-surface from the field over `field.Bounds`.

- **Tables.** Canonical 256-entry edge and triangle tables ported from Paul Bourke / Lorensen-Cline (public domain). Source cited in `MarchingCubesTables.cs`.
- **Iso convention.** Field is high inside, falls to 0 outside. A corner bit is set when `value < isoLevel` (outside). Combined with outward normals = `(-gradient)`, this produces CCW front faces consistent with Godot's winding convention.
- **Edge interpolation.** Linear (`VertexInterp`), never midpoint snapping.
- **Watertight by index.** Every grid edge is keyed by an order-independent pair of flat corner indices. Triangles that share a grid edge share a vertex index. The mesh is watertight by construction.
- **Gradient normals.** Per-vertex outward normals from central differences: `n = -∇field`, normalised. Zero-length gradients fall back to `+Y`.
- **Determinism.** Cells marched in fixed `(z, y, x)` order; vertex ordering is byte-for-byte stable across runs.
- **Parallelism.** Two field-heavy passes (`SampleGrid` and `ComputeNormals`) use `Parallel.For` over Z-slabs / vertices respectively; each write is to a disjoint array slot, so the output is independent of thread scheduling. The vertex-emitting cell march is serial to preserve vertex order.

**Default `GridParams`:** `CellSize = 0.1`, `IsoLevel = 0.5`. P95 regen ≈ 23 ms on the 4-core target (under the 50 ms budget). `CreaturePreview.SetCellSize` overrides for preview vs. export quality.

---

## Auto-skinning

`AutoSkinner.Skin(Vector3[] vertices, CreatureSkeleton) → SkinData`

Assigns GPU-ready skin weights. For each vertex:

1. Compute `Bone.DistanceTo(vertex)` (point-to-segment) for every bone.
2. Partial-sort to find the 4 nearest bones (insertion sort; ties broken by ascending bone index for determinism).
3. Raw weight: `w_k = 1 / max(distance_k, 1e-5)` (epsilon guards on-bone vertices).
4. Normalise the 4 kept weights to sum 1.
5. Pad unused slots (when `boneCount < 4`) with bone index 0, weight 0.

Output layout: flat `int[] BoneIndices`, `float[] Weights`, 4 entries per vertex — matches Godot's `ARRAY_BONES` / `ARRAY_WEIGHTS` format directly.

Bind pose and animation deferred to M3. M2 emits weights and a rest-pose `Skeleton3D` only.

---

## Full pipeline

```
Recipe + PartRegistry + GridParams
  │
  ▼  SkeletonResolver.Resolve
CreatureSkeleton   (world-space Bone[])
  │
  ▼  new MetaballField(skeleton, isoLevel)
IScalarField       (Wyvill sum over bones)
  │
  ▼  MarchingCubes.Polygonise(field, cellSize, isoLevel)
MeshData           (vertices, normals, indices)
  │
  ▼  AutoSkinner.Skin(vertices, skeleton)
SkinData           (BoneIndices, Weights — 4 per vertex)
  │
  └─ (MeshData + SkinData) ──► GodotMeshBuilder.BuildArrayMesh  → ArrayMesh
     CreatureSkeleton       ──► GodotMeshBuilder.BuildSkeleton3D → Skeleton3D
                                                │
                                                ▼
                                          CreaturePreview.Rebuild(Recipe, PartRegistry)
                                          (Node3D bridge; exposes instance method
                                           so GDScript can call it without C# statics)
```

`CreatureMesher.Build` is the single entry point for the pure-C# half. `GodotMeshBuilder` is the only file that produces Godot scene-tree types. `CreaturePreview` bridges to GDScript.

---

## Determinism

The pipeline is a pure function of `(Recipe, PartRegistry, GridParams)`. No RNG, no clock reads anywhere in `src/mesh/`. The serial cell march, the stable partial-sort tie-break, and the disjoint-write parallel passes all combine so that the same inputs produce byte-identical vertex/index/normal/weight arrays. A CI round-trip test (`M2AcceptanceTests`) asserts this.

---

## Performance

| Metric | Target | Realized |
|-|-|-|
| P95 regen (4-core, CellSize 0.1) | < 50 ms | ≈ 23 ms |
| Editor preview frame rate | 60 FPS | held while dragging |

Key techniques: field evaluated only within the tight `Bounds` AABB; `Parallel.For` on corner-sample and normal-compute passes; per-cube corner-value cache eliminates redundant field evaluations; watertight index deduplication via `Dictionary<long, int>`.

---

## M3 — procedural animation

M3 makes creatures walk. All math is pure C# in `src/anim/`, deterministic with time as an explicit `double seconds` (no clock, no RNG). The contract types are frozen in `docs/m3-contract.md`.

### Skin binding (so posing deforms the mesh)

M2 emitted weights + a rest-pose `Skeleton3D` only. M3 adds `GodotMeshBuilder.BuildSkin`: a Godot `Skin` whose **bind pose for bone *i* is the inverse of that bone's global rest**. The skinning matrix `globalPose · bindPose` is the identity at rest (undeformed) and deforms as the skeleton is posed. `CreaturePreview` parents the mesh under the `Skeleton3D` and exposes `ApplyPose(Pose)` — local pose = `rest · delta`.

### Two-bone limbs (ADR-0003)

`SkeletonResolver` splits each `PartKind.Limb` into an upper (hip→knee) and lower (knee→foot) bone, colinear at rest. It records a `LimbChain` (root/knee/foot bone indices + segment lengths + slot) per limb. The auto-skinner (4-nearest by segment distance) adapts automatically to the extra bones.

### Two-bone IK

`TwoBoneIk.Solve(root, upperLen, lowerLen, target, pole)` — closed-form law of cosines. The knee is placed in the plane spanned by `(target − root)` and the pole hint. Reach-clamped: an out-of-range target returns full extension toward it (`Reachable = false`) rather than NaN; degenerate inputs (target at root, zero pole, tiny/negative lengths) stay finite.

### Limb classification + gait

`LimbClassifier.Classify` tags each `LimbChain` as `Leg / Arm / Wing / Tail / Other` from its slot name and part id. `GaitController.ForLegCount` returns a phase-offset preset by leg count — biped (alternating), quadruped (diagonal pairs), hexapod (alternating tripod), octopod (metachronal wave) — and `PhaseOf` / `IsStance` evaluate a leg's cycle phase at time *t*.

### Locomotion tick

`Locomotion.Tick(skeleton, legChainIndices, gait, params, seconds)` is the integrator:

1. Advance the body forward (`StrideLength · CadenceHz`, one stride per cycle) with a vertical bob.
2. Per leg: read the gait phase → plan a foot target. **Stance** feet are planted on the ground plane (their body-local Z slides back exactly as fast as the body advances, so they hold world position — no skating). **Swing** feet arc forward, lifted by a sine.
3. Solve each leg with `TwoBoneIk`.
4. Convert the IK knee/foot into Skeleton3D **local pose deltas**: since a leg's upper bone parents to the (unposed) spine, `delta_upper = restGlobal_upper⁻¹ · posedGlobal_upper`; the lower hangs off the posed upper. The result is a `Pose`.

`CreaturePreview.WalkTick(seconds)` wires this for GDScript: classify legs → gait by leg count → tick → `ApplyPose` → return the body position (the node advances + bobs). The body-local pose + node-space body transform keep the IK math frame-stable.

### Secondary motion

`Jiggle.Step` is a deterministic spring-damped follower: a point chases a moving anchor, so when the body accelerates it lags then springs back (tail / belly jiggle). Semi-implicit Euler with an explicit, clamped dt.

### The Hecker gate

`HeckerWalkTests` runs the **same** locomotion path on synthetic 2-, 4-, 6- and 8-leg skeletons for 60 deterministic seconds each, asserting: no NaN/Inf, stance feet on the ground plane, no joint over-reach (the IK never pops to full stretch), and no foot skate. **All four leg counts pass on one code path** — the PRD §7 M3 acceptance gate, automated half. A walk render per leg count is the human-review half (`tests/snapshots/preview3d_walk.tscn` covers the quadruped; 6/8-leg renders await hexapod/octopod rigblock parts — content follow-up).

### Still open (M3 polish / later)

- **Per-leg pole / stance tuning** for more natural knee direction and stance width (mechanics are correct; this is visual polish).
- **Balance shift** (COM over the support polygon) beyond the vertical bob.
- **6/8-leg rigblock parts** so high-leg-count creatures are recipe-built (and renderable), not just synthetic test skeletons.
- **Orientation mirroring** in the resolver (M2 mirrors attachment *positions* only); IK retargeting already makes both sides walk regardless.
