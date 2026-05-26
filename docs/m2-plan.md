# M2 plan — Metaball skin generation

**Window:** Weeks 9–14 (6 weeks, 30 working days).
**Spec:** PRD §7 M2.
**Owner sub-agent:** `procedural-mesh-dev`.

## Acceptance test (lifted from PRD)

> Player drags parts around in 3D; mesh updates in real time at 60 FPS; saved creature reloads with identical mesh (within float tolerance).

Decomposed into enforceable gates:

1. **Real-time regen** — editing the recipe (place / delete / morph) regenerates the skin mesh in **<50 ms p95** on the target 4-core CPU, and the 3D preview holds **60 FPS** while dragging.
2. **Deterministic reload** — `Recipe` → mesh is a pure function. Save → close → reopen → regenerate yields a mesh whose vertex / index / normal / weight arrays are **equal within float tolerance** to the original. A CI test enforces this.
3. **3D preview replaces the 2D blueprint** — the center pane is now a real-time 3D viewport, not the M1 flat canvas.

Manual playtest covers feel; the automated round-trip + a rendered snapshot guard regressions.

## Architectural contract

Non-negotiable for M2 (PRD §6 invariants, restated):

- **C# does all mesh math; GDScript does UI** (invariant 5). Marching cubes, metaball field, skeleton resolution, and auto-skinning live in `src/mesh/` as pure C# with **no Godot scene-tree dependency** (Godot *structs* — `Vector3`, `Transform3D`, `Aabb` — are fine; nodes are not). The 3D preview's camera and input are GDScript; the mesher is C#.
- **Determinism** (invariant 4). Mesh generation takes **no RNG and no clock** — it is fully determined by `(Recipe, PartRegistry, GridParams)`. Same input → bit-stable output (parallelism uses an order-stable merge; the canonical compare path is serial). A test that finds nondeterminism is a release blocker.
- **No baked geometry in the recipe** (invariant 1). The mesh is *regenerated* from the recipe every load; it is never serialized. The recipe stays <10 KB. The metaball skin is the creature — there are no authored body meshes to reference.
- **One Rigblock framework** (invariant 2). Skeleton derivation reads only generic `Part` / `Attachment` data; no "creature"-specific branching. The same path must later skin a vehicle or building.
- **`.tres` / `.tscn` committed as text** (invariant 8). The regenerated 9-part library and the new 3D preview scene are reviewed as text.
- **Open-source only** (invariant 7). Marching-cubes tables are ported from a public-domain / permissively-licensed source (Paul Bourke / Lorensen-Cline / `dario-zubovic/metaballs`); provenance noted in the file header.

### The bone model (locked here, refined in M3)

M1 shipped no skeleton. M2 introduces one, and **locks the minimal definition now** so the field generator and the skinner agree:

- A **bone** is a world-space line segment `(Head, Tail)` with a radius at each end and a parent index.
- **One bone per placed part.** `Head` = the world position of the parent slot anchor the part attaches to (the spine's origin for the root). `Tail` = `Head + R · (axis · BoneLength)`, where `R` is the part's composed world rotation, `axis` is the part's local bone axis (`+Z` by the M1 spine convention), and `BoneLength` comes from the part. Radii come from the part's `RadiusStart` / `RadiusEnd`, scaled by `Morph.Stretch`.
- `ParentIndex` mirrors `Attachment.ParentPartIndex` (`-1` → the spine/root bone).

This is deliberately the simplest defensible skeleton. Multi-bone limb chains and joint frames arrive in M3 when IK needs them; M2 only needs something to grow spheres along and skin vertices to.

## Phase plan

### P0 — Contracts, skeleton foundation, Part 3D fields (3 days, central)

The shared spine. I write this directly (not delegated) because every P1 agent builds on these types.

**Deliverables**
- `src/mesh/IScalarField.cs` — `float Sample(in Vector3 p)` + `Aabb Bounds { get; }`. The seam between the field (B) and marching cubes (A).
- `src/mesh/Bone.cs` — `readonly struct Bone { Vector3 Head, Tail; float RadiusHead, RadiusTail; int ParentIndex; }` + `DistanceTo(Vector3)` (point-to-segment).
- `src/mesh/CreatureSkeleton.cs` — `Bone[] Bones` + world-space Aabb + lookup helpers.
- `src/mesh/MeshData.cs` — `Vector3[] Vertices; int[] Indices; Vector3[] Normals;` + `IsEmpty`, `IsManifoldish()` (every index in range, triangle count = indices/3).
- `src/mesh/SkinData.cs` — flattened `int[] BoneIndices` (4 per vertex) + `float[] Weights` (4 per vertex), Godot `ARRAY_BONES`/`ARRAY_WEIGHTS` layout.
- `src/mesh/SkeletonResolver.cs` — `Recipe + PartRegistry → CreatureSkeleton`. Walks `Attachments` in order, composes `Transform3D` parent→child (slot anchor · `LocalTransform` · morph twist), applies `Morph.Stretch` to bone length + radii, emits one `Bone` per placed part. **Pure, deterministic, central.**
- **Part 3D fields** (additive — `FormatVersion` stays 1 per `creature-data-model.md` policy): `BoneLength` (float, default 1.0), `RadiusStart` (float, default 0.5), `RadiusEnd` (float, default 0.5). Update `tools/build_rigblock_library.gd` and **regenerate** the 9 `.tres` (committed as text).
- `docs/m2-contract.md` — the type/interface contract the three P1 agents build against (the M2 analogue of `m1-p4-contract.md`).
- `tests/unit/SkeletonResolverTests.cs` — single spine → 1 bone at the right world transform; spine + 2 mirrored limbs → 3 bones, mirror bone X-flipped; morph stretch scales length + radii; parent indices preserved; deterministic across two runs.

**Acceptance:** `dotnet build` clean; `SkeletonResolver` tests green; the 9 regenerated parts still load and pass `RigblockLibraryTests`.

### P1 — Three parallel pure-C# math modules (6 days wall-clock, 3 agents)

Independent given P0's types. Dispatched as a **team of three agents in parallel**, each with a strict, non-overlapping file scope. Per the M1 lesson, agents do **not** run Godot or the test runner (avoids `.godot` cache races) — they write code + tests against the P0 contract; I build, run, and validate centrally between phases.

- **Agent A — Marching cubes** (`src/mesh/MarchingCubes.cs`, `src/mesh/MarchingCubesTables.cs`)
  Consumes `IScalarField`, produces `MeshData`. Ports the canonical 256-entry edge + triangle tables (public-domain, provenance in header). Gradient-estimated normals, consistent winding.
  *Tests:* table integrity (all 256 cube configs index valid edges, never reference a non-intersected edge); empty/below-iso field → empty mesh; single analytic sphere field → closed manifold, vertex count in expected band, outward normals, watertight Euler check; determinism (same field → identical arrays).

- **Agent B — Metaball field** (`src/mesh/MetaballField.cs`)
  Consumes `CreatureSkeleton`, implements `IScalarField` by summing a smooth falloff (Wyvill / metaball kernel) over capsule samples along each bone. `Bounds` = union of per-bone AABBs padded by the kernel support radius.
  *Tests:* value on a bone is well above iso; value at the bone radius ≈ iso threshold; value far away → 0; two overlapping bones blend (joint value > either alone); `Bounds` encloses all influence; determinism.

- **Agent C — Auto-skinner** (`src/mesh/AutoSkinner.cs`)
  Consumes `Bone[]` + a vertex list, produces `SkinData`: 4 nearest bones by point-to-segment distance, weight ∝ inverse distance, normalized to sum 1, clamped to ≤4 nonzero.
  *Tests:* vertex on a bone → that bone's weight ≈ 1; vertex equidistant between two bones → ≈ 0.5/0.5; weights always sum to 1; never more than 4 nonzero; nearest-4 selection correct against a hand-built skeleton; no divide-by-zero when a vertex coincides with a bone endpoint.

**Acceptance:** each module's GdUnit4 C# tests green in isolation; `dotnet build` clean with all three landed.

### P2 — Pipeline integration + Godot ArrayMesh bridge (5 days, central)

**Deliverables**
- `src/mesh/CreatureMesher.cs` — orchestrates `Recipe → SkeletonResolver → MetaballField → MarchingCubes → AutoSkinner → (MeshData, SkinData)`. Pure C#, deterministic. Accepts a `GridParams` (cell size, iso level, AABB padding).
- `src/mesh/GodotMeshBuilder.cs` — `(MeshData, SkinData) → ArrayMesh` (with `ARRAY_BONES` + `ARRAY_WEIGHTS`) and `CreatureSkeleton → Skeleton3D` (bind pose from bone transforms).
- `tests/unit/CreatureMesherTests.cs` — full pipeline on the M1 fixture creature yields a non-empty, in-range, watertight-ish mesh; **determinism** test (same recipe twice → identical vertex/index/normal/weight arrays); skin weights present for every vertex and reference valid bone indices.

**Acceptance:** pipeline tests green; a fixture creature round-trips Recipe → mesh deterministically.

### P3 — 3D preview scene (5 days, central)

Replaces the M1 2D blueprint center pane.

**Deliverables**
- `scenes/editor/Preview3D.tscn` + `scripts/editor/Preview3D.gd` — `Camera3D` turntable orbit (MMB-drag + wheel zoom, Blender/Godot convention), neutral clay `StandardMaterial3D`, holds the generated `MeshInstance3D` + `Skeleton3D`.
- `src/mesh/CreaturePreview.cs` — a `Node3D` exposing an **instance method** `Rebuild(Recipe)` (GDScript cannot call C# statics — the M1 interop lesson; expose an instance method, not a static).
- Wire into `CreatureEditor.tscn`: the center pane swaps `Workspace.tscn` → `Preview3D.tscn`; `PartPalette` and `Properties` panes are retained.
- **Live regen**: on recipe edit (place / delete / morph), debounce ~80 ms, call `Rebuild`.

**Acceptance (manual):** drag a part in the editor → the 3D skin updates in the viewport; orbit/zoom work; clay-shaded creature is recognizable.

### P4 — Performance: <50 ms p95 + 60 FPS drag (4 days, central)

**Deliverables**
- Grid tightened to the field AABB; `GridParams.CellSize` tuned for the quality/speed trade; optional **parallel** marching cubes across Z-slabs with an **order-stable** merge (preserves determinism).
- Field-sample caching across the shared cube corners.
- `tests/unit/MeshPerfTests.cs` — benchmark regen on a representative ~20-attachment creature; assert **p95 < 50 ms** (with margin; tagged so it can be skipped on slow CI runners, but runs locally on target hardware).
- Manual 60 FPS drag check with frame-time capture.

**Acceptance:** p95 regen < 50 ms on the 4-core target; preview holds 60 FPS while dragging.

### P5 — Determinism round-trip + M2 acceptance + CI + docs (4 days, central)

**Deliverables**
- `tests/unit/M2AcceptanceTests.cs` — build fixture → mesh A; `Save` → `Load` → regenerate → mesh B; assert vertex / index / normal / weight arrays equal within epsilon. This is the PRD M2 gate.
- `tools/snapshot.ps1` (extend) — render the 3D preview of a fixture creature off a known recipe; `tools/snapshot-diff.ps1` compares to a committed golden. CI runs under **xvfb + `--rendering-driver opengl3`** (never `--headless` — see `feedback-godot-headless-snapshots`).
- `.github/workflows/ci.yml` — add the M2 mesh tests + the 3D snapshot job.
- `docs/animation-pipeline.md` — **start it** (PRD references it). Document the M2 half: skeleton derivation, metaball field model, auto-skin weighting. The IK/gait half lands in M3.
- `docs/architecture.md` — flip the M2 "To be filled" row to done; record the realized `src/mesh/` layout.
- `docs/creature-data-model.md` — amend with the Part 3D fields; confirm `FormatVersion` stays 1.
- ADR-0002 *if* a reviewer judges the Part field addition a spec change (the data-model policy already sanctions additive fields, so a doc amendment may suffice — decide in review).

**Acceptance:** PRD M2 acceptance passes end-to-end in CI; snapshot diff clean.

## Total estimate

| Phase | Days | Cumulative | Notes |
|-|-|-|-|
| P0 contracts + skeleton + Part fields | 3 | 3 | central, unblocks the agents |
| P1 three math modules | 6 | 9 | 3 parallel agents (wall-clock) |
| P2 pipeline + Godot bridge | 5 | 14 | central |
| P3 3D preview scene | 5 | 19 | central |
| P4 performance | 4 | 23 | central |
| P5 determinism + acceptance + CI + docs | 4 | 27 | central |
| **Slack** | 3 | **30** | within Weeks 9–14 |

## Risks

| Risk | Likelihood | Impact | Mitigation |
|-|-|-|-|
| Marching-cubes table / winding bugs (classic) | **High** | Medium | Port canonical tables verbatim; table-integrity test; single-sphere golden test; watertight Euler check |
| <50 ms p95 not met at acceptable visual resolution | Medium | **High** | AABB-bounded grid, parallel MC, sample caching; if still tight, lower live-preview resolution and re-mesh at higher res only on save |
| Determinism breaks under parallel MC (FP reordering) | Medium | Medium | Order-stable slab merge; serial canonical compare path; "within float tolerance" epsilon in the acceptance test |
| Skeleton-from-recipe under-specified ("what is a bone?") | Medium | Medium | Locked above: one bone per placed part, `Head` = slot anchor, `Tail` = `+BoneLength` along local axis. Refine in M3, not M2 |
| Metaball blend yields non-manifold / blobby skin | Medium | Medium | Tune kernel + iso threshold; this is also the art-direction knob (see open questions) |
| GDScript↔C# interop friction in the 3D preview | Medium | Low | Instance-method bridge node (`CreaturePreview.Rebuild`), never a C# static called from GDScript (M1 lesson) |
| xvfb / opengl3 snapshot flakiness in CI | Low | Low | Harness already solved in M1; reuse `snapshot.ps1` + xvfb pattern |
| Art-direction decision (PRD §10) stalls M2 | Low | Medium | Decoupled: M2 ships neutral clay; art direction is decided *against* the live P3 preview and touches only the shading layer |

## Out of scope for M2 (defer)

- **Authored Blender part meshes / texturing.** The metaball clay skin *is* the creature in M2; detail parts render as folded-in metaball blobs or simple primitives. Authored meshes are deferred (and gated on the art-direction decision).
- Any motion / IK / gait — **M3**.
- Procedural paint masks / PBR materials — clay + per-part tint only for M2.
- LOD, GPU-compute marching cubes — CPU only for M2.
- Behavior trees, biome, gameplay loop — **M4**.
- Supabase / creature sharing — **M5**.

## Open questions

- **Detail parts (mouth / eyes / feet) in M2** — fold into the metaball field as extra spheres, render as simple attached primitives, or defer entirely? *Recommend: fold spine/limbs/head into the body field; render mouths as primitives or small blobs; authored detail meshes deferred past M2.* **Decide before P1.**
- **Default grid resolution / cell size** — the quality-vs-`<50 ms` knob. **Decide before P2** (benchmark-driven).
- **Camera controls** — turntable orbit on MMB-drag + wheel zoom (Blender/Godot convention). **Decide before P3** (default: yes).
- **Art direction** (PRD §10: Spore-cartoon vs semi-real No-Man's-Sky) — *owner decision (Lee), surfaced but non-blocking.* M2 engineering is material-agnostic; **decide against the live P3 preview**, since that is the first artifact that makes the choice concrete.

## Definition of done (one-liner)

> A player drags parts in a real-time 3D preview; the metaball skin regenerates under 50 ms and holds 60 FPS; saving and reloading reproduces a bit-stable mesh (within float tolerance). A CI test asserts the round-trip and a rendered snapshot guards the visual. We move to M3 — procedural animation, the critical milestone.
