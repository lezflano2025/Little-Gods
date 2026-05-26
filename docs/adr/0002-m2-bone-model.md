# ADR-0002: M2 metaball bone model and Part mesh-profile fields

- **Status:** Accepted
- **Date:** 2026-05-26
- **Deciders:** Lee Flanagan
- **Milestone:** M2

## Context

M1 shipped no skeleton; `Part` carried only 2D blueprint data (`Footprint2D`). M2 requires bones — line segments the metaball field grows spheres along — and a way to skin the resulting mesh vertices back to those bones. Before the M2 P1 agents could write the field, marching-cubes, and skinning modules in parallel, the bone definition had to be locked. Additionally, `Part` needed new fields to express the bone's shape profile (length, radii at each end) without breaking existing `.tres` files.

## Decision

One bone per placed part. The root (spine) bone is centred on its local +Z axis at the world origin (`Head = -Z * L/2`, `Tail = +Z * L/2`). Each attachment bone is anchored at the slot: `Head` = the slot anchor's world position; `Tail` = `Head + (slot world +Z) * BoneLength`. `SkeletonResolver` orients each attachment frame so its +Z aligns with the slot's `LocalNormal`, causing parts to grow along the outward direction of their socket.

`Part` gains three additive fields (defaults preserve existing behaviour; `FormatVersion` stays 1):

| Field | Default | Role |
|-|-|-|
| `BoneLength` | `1.0` | Bone length along local +Z, in world units |
| `RadiusStart` | `0.5` | Metaball radius at the bone head |
| `RadiusEnd` | `0.5` | Metaball radius at the bone tail |

`Morph.Stretch.Z` scales length; the average of `Morph.Stretch.X/Y` scales both radii. `Bone.ParentIndex` mirrors `Attachment.ParentPartIndex` (`-1` for the root).

## Consequences

- **Positive:**
  - Simplest defensible skeleton: one bone per part, derived purely from generic `Attachment`/`Part` data (PRD invariant 2 — works for non-creature recipes).
  - Pure and deterministic (`SkeletonResolver` reads no RNG or clock).
  - Additive `Part` fields: no migration path needed, old `.tres` files load with coded defaults.
- **Negative:**
  - Mirroring is position-only (X-flip). Basis/orientation mirroring is deferred to M3, when IK retargeting makes it necessary.
  - A multi-bone limb (e.g. upper-arm + forearm) is not representable in M2; each placed part is still exactly one bone.
- **Risks:**
  - Limb chains with a single bone per part may look blobby at joints. Tuning the Wyvill kernel and `RadiusStart`/`RadiusEnd` values is the main art-direction lever; the design is revisited in M3.
- **Reversal cost:** Medium. `BoneLength`/`RadiusStart`/`RadiusEnd` are already exported fields on `Part` and referenced by `SkeletonResolver`, `MetaballField`, and the rigblock library. Replacing the bone model in M3 means updating `SkeletonResolver` (possibly to emit multiple bones per part) but does not require a `FormatVersion` bump unless the fields are removed rather than reinterpreted.

## Alternatives considered

- **Derive bone extents from `Footprint2D`.** Rejected — `Footprint2D` is a 2D blueprint concept (hit-testing, rendering); overloading it with 3D bone geometry conflates two orthogonal concerns and complicates future authored-mesh work.
- **Multi-bone limb chains now.** Rejected — premature before IK. M2 only needs something to grow spheres along and to skin vertices to; a single bone per part is sufficient. Chains arrive in M3 when the IK solver can consume them.

## References

- `docs/m2-plan.md` §"The bone model (locked here, refined in M3)"
- `docs/m2-contract.md` §"Shared types"
- `docs/creature-data-model.md` §"Format version policy"
- `src/mesh/SkeletonResolver.cs`
- ADR-0001 (Godot version)
