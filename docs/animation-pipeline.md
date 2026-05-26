# Animation pipeline — M2: metaball skin generation

**Status:** M2 complete. The M3 half (IK, gait, animation) is deferred; see the bottom of this document.

This document covers the M2 pipeline: how a `Recipe` becomes an `ArrayMesh` + `Skeleton3D` visible in the 3D editor preview. The code is the authoritative reference; this document explains the *why* behind each step.

---

## Bone model

One bone per placed part. Bones are world-space line segments `(Head, Tail)` with a radius at each end (`RadiusHead`, `RadiusTail`) and a `ParentIndex`.

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

## Deferred to M3

- **Two-bone analytic IK.** Limb chain retargeting; real joint orientations.
- **Limb-type classification.** Distinguishing leg / arm / wing / tail from generic `PartKind`.
- **Gait phase model.** Per-limb phase offsets; footstep timing.
- **Bind-pose animation.** Actual `Skeleton3D` pose animation; M2 emits a rest-pose skeleton only.
- **Orientation mirroring.** M2 mirrors attachment *positions* only (X-flip); M3 mirrors orientation when IK retargeting arrives.
