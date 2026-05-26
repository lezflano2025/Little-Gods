# M2 P1 contract — three parallel mesh modules

Status: **locked for M2 P1.** This is the seam the three parallel agents build against (the M2 analogue of `m1-p4-contract.md`). The shared types below already exist in `src/mesh/` (landed in P0). Agents implement against them and **must not change** the signatures without updating this doc.

All code is in namespace `LittleGods.Mesh`, pure C#, **no Godot scene-tree types** (Godot value structs — `Vector3`, `Aabb` — are fine). No RNG, no clock: same input → same output (PRD invariant 4).

## Shared types (P0, do not modify)

- `interface IScalarField { float Sample(Vector3 p); Aabb Bounds { get; } }`
- `readonly struct Bone { Vector3 Head, Tail; float RadiusHead, RadiusTail; int ParentIndex; ... }` — has `DistanceTo(p)`, `ClosestPoint(p)`, `RadiusAt(p)`, `Length`, `MaxRadius`.
- `sealed class CreatureSkeleton { Bone[] Bones; Aabb Bounds; int Count; }`
- `sealed class MeshData { Vector3[] Vertices; Vector3[] Normals; int[] Indices; ... }` — `IsWellFormed()`, `TriangleCount`, `Empty`.
- `sealed class SkinData { int[] BoneIndices; float[] Weights; const int InfluencesPerVertex = 4; ... }` — flat layout, 4 per vertex, groups sum to 1.

## Iso-surface convention

The field is **high inside** the body and falls to **0 far away**. Marching cubes extracts the surface where `Sample(p) == IsoLevel`. Default `IsoLevel = 0.5`. A bone's surface radius is where the field equals `IsoLevel`, so the metaball kernel must be tuned so that a lone bone's iso-contour sits at its `RadiusAt(p)`.

## Agent A — Marching cubes  (`src/mesh/MarchingCubes.cs`, `MarchingCubesTables.cs`)

**Signature**
```csharp
public static class MarchingCubes
{
    // Voxelise field.Bounds at the given cell size; extract the IsoLevel surface.
    public static MeshData Polygonise(IScalarField field, float cellSize, float isoLevel = 0.5f);
}
```

**Requirements**
- Port the canonical 256-entry edge table + triangle table (public-domain: Paul Bourke / Lorensen-Cline / `dario-zubovic/metaballs`). **Cite the source in the file header.**
- Linear interpolation along edges to the iso-crossing (no "snap to midpoint").
- Per-vertex normals from the field gradient (central differences), normalised, pointing **outward** (toward decreasing field).
- Consistent triangle winding (CCW front faces).
- Deterministic: cells marched in a fixed (x,y,z) order; identical field + params → identical arrays. (P4 may parallelise with an order-stable merge; keep a serial path.)

**Tests** (`tests/unit/MarchingCubesTests.cs`)
- Table integrity: every triangle entry references an edge that the matching edge-table bitmask marks as crossed; all indices in `[0,11]`.
- Below-iso / empty field → `MeshData.Empty`.
- A single analytic sphere field (provide a tiny test `IScalarField`) → non-empty, `IsWellFormed()`, vertices within a thin shell of the true radius, normals point outward, watertight (every edge shared by exactly 2 triangles).
- Determinism: polygonise twice → identical arrays.

## Agent B — Metaball field  (`src/mesh/MetaballField.cs`)

**Signature**
```csharp
public sealed class MetaballField : IScalarField
{
    public MetaballField(CreatureSkeleton skeleton, float isoLevel = 0.5f);
    public float Sample(Vector3 p);
    public Aabb Bounds { get; }
}
```

**Requirements**
- Field = sum over bones of a smooth, finite-support falloff of the distance to the bone segment, scaled so a lone bone's `IsoLevel` contour sits at its interpolated radius `RadiusAt(p)`. Wyvill / metaball polynomial kernel preferred (finite support, C¹).
- `Bounds` = `skeleton.Bounds` grown by the kernel support margin so the surface is never clipped.
- Overlapping bones blend (the joint value exceeds either bone alone) — this is what fuses limbs into the body.
- Pure / deterministic.

**Tests** (`tests/unit/MetaballFieldTests.cs`)
- On a bone segment → field well above `IsoLevel`.
- At a point one radius off the bone (perpendicular) → field ≈ `IsoLevel` (within tolerance).
- Far away (several radii) → field ≈ 0.
- Two overlapping bones: midpoint value > single-bone value there.
- `Bounds` contains every point where `Sample > IsoLevel` (sample a coarse grid to check).
- Determinism.

## Agent C — Auto-skinner  (`src/mesh/AutoSkinner.cs`)

**Signature**
```csharp
public static class AutoSkinner
{
    // For each vertex: 4 nearest bones by segment distance, weight = inverse
    // distance, normalised to sum 1. Fewer than 4 bones -> pad with weight 0.
    public static SkinData Skin(Vector3[] vertices, CreatureSkeleton skeleton);
}
```

**Requirements**
- Distance metric = `Bone.DistanceTo(vertex)` (point-to-segment).
- Weight ∝ `1 / max(distance, epsilon)` (no divide-by-zero when a vertex lies on a bone). Inverse-distance, then normalise the 4 kept weights to sum 1.
- Exactly `InfluencesPerVertex` (4) slots per vertex, flat layout (`v*4 + k`). Pad unused slots with bone index 0 and weight 0.
- Skeletons with fewer than 4 bones still produce valid, normalised weights.
- Pure / deterministic.

**Tests** (`tests/unit/AutoSkinnerTests.cs`)
- Vertex on a bone → that bone's weight ≈ 1.
- Vertex equidistant between two bones → ≈ 0.5 / 0.5.
- Every vertex's 4 weights sum to 1; never more than 4 nonzero.
- Nearest-4 selection correct against a hand-built skeleton with >4 bones.
- Vertex coincident with a bone endpoint → no NaN / Inf, weights sum to 1.
- Determinism.

## Integration (P2, central — not an agent's job)

`CreatureMesher` wires `Recipe → SkeletonResolver → MetaballField → MarchingCubes → AutoSkinner → (MeshData, SkinData)`. `GodotMeshBuilder` packs that into an `ArrayMesh` + `Skeleton3D`. Agents do **not** touch these files or the Godot bridge.

## Agent ground rules (M1 lessons)

- **Strict file scope.** Each agent writes only its two/three files above plus its one test file. No edits to P0 shared types, no edits to another agent's files.
- **Do not run Godot or the test runner.** The `.godot` cache races across processes; the lead builds and runs tests centrally between phases. Write the code and the tests; do not execute them.
- **No statics-from-GDScript concerns here** — this is all pure C#, called only by C# (P2). 
- Keep files focused and under the 800-line cap (coding-style rule).
