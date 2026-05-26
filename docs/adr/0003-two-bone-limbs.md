# ADR-0003: Two-bone limb chains for procedural animation

- **Status:** Accepted
- **Date:** 2026-05-26
- **Deciders:** Lee Flanagan
- **Milestone:** M3

## Context

ADR-0002 (M2) resolves every placed part to **exactly one bone**. M3's headline deliverable is a **two-bone analytic IK solver** and the Hecker walk test, which require each leg to have a **knee** — two segments (upper + lower) the IK can bend. A single rigid bone per limb cannot bend at a joint, so it cannot produce a believable leg.

We need limbs to carry a knee joint without re-authoring the 9-part rigblock library and without bumping `Recipe.FormatVersion` (the creature data is unchanged; only how the resolver interprets a limb changes).

## Decision

`SkeletonResolver` splits each `PartKind.Limb` part into a **2-bone chain**:

- **Upper bone** — from the slot anchor, along the slot's `LocalNormal`, for half of `BoneLength` (scaled by `Morph.Stretch.Z`).
- **Lower bone** — continues from the knee for the remaining half; the **foot** is its tip.
- **Knee** = the midpoint. The two sub-bones are **colinear at rest** (a straight limb); IK introduces the bend at runtime.
- Radii taper continuously: the knee uses the radius interpolated at the split point, so the two capsules join smoothly in the metaball field.

This is a **pure resolver rule keyed on `PartKind.Limb`** — there is **no new serialized field** on `Part`, and `FormatVersion` stays 1. Non-limb parts (spine, head, mouth) remain a single bone.

Because bones are no longer 1:1 with attachments, the resolver stops assuming `bone index = attachment index + 1`. It builds the bone list in attachment order, emits 1 or 2 bones per attachment, and maintains an **attachment → bone-index map** for parent links. It also records a `LimbChain` (root / knee / foot bone indices + the two segment lengths) per limb for the IK, gait, and foot-planning layers to consume.

## Consequences

- **Positive:**
  - Two-bone IK has a real knee to solve; legs bend believably (the Hecker test is reachable).
  - No library re-authoring and no `FormatVersion` bump — the split is derived, not stored.
  - The auto-skinner (4-nearest by segment distance) adapts automatically to the extra bones; the metaball field sums over the two shorter capsules, so the skin stays continuous.
- **Negative:**
  - The attachment→bone mapping is now indirect (a map, not `i + 1`). Downstream code must use the `LimbChain` records and the map, never assume `i + 1`. The M2 `SkeletonResolverTests` that assumed `i + 1` / specific counts are updated in the P0 implementation.
  - A limb's metaball skin is now two shorter capsules rather than one — cosmetically slightly different at the (rest-straight) knee. The M2 determinism acceptance is unaffected (meshing is still a pure function of the recipe).
- **Reversal cost:** Low–Medium. Pure resolver logic; reverting to one-bone limbs is a code change with no data migration.

## Alternatives considered

- **Explicit knee `AttachmentPoint` or a `LimbSegments` field on `Part`.** Rejected for M3 — adds a serialized field plus library re-authoring for no M3 benefit, since a uniform 2-bone rule covers every bundled limb. Revisit only if per-limb segment counts are ever needed (articulated tails, multi-joint arms).
- **Keep one bone and fake the bend by rotating the single bone.** Rejected — without a knee there is no believable leg bend, which defeats the entire point of two-bone IK and the Hecker test.

## References

- `docs/m3-plan.md` §P0 and §"The dependency M3 must resolve first"
- ADR-0002 (M2 one-bone-per-part model this supersedes for limbs)
- `src/mesh/SkeletonResolver.cs`
- Chris Hecker, "How To Animate a Character You've Never Seen Before" (GDC 2007)
