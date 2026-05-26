# M3 P1–P3 contract — three parallel animation modules

Status: **locked for M3 P1–P3.** This is the seam the three parallel agents build against (the M3 analogue of `m2-contract.md`). The shared types below already exist in `src/anim/` and on `CreatureSkeleton` (landed in P0, branch `m3-p0-contract-resolver`). Agents implement against them and **must not change** the signatures without updating this doc.

All new module code is in namespace `LittleGods.Anim`, pure C#, **no Godot scene-tree types** (Godot value structs — `Vector3`, `Transform3D`, `Basis` — are fine). The skeleton it reads (`CreatureSkeleton`, `Bone`, `LimbChain`) lives in `LittleGods.Mesh`.

**Determinism (PRD invariant 4):** every function below is a pure function of its arguments. No `DateTime.Now`, no `Time.GetTicksMsec()`. Elapsed time enters as an explicit `double seconds` so a 60 s run is byte-reproducible. Any RNG takes an explicit seed (none is needed for P1–P3).

## Shared contract types (P0 — do not modify)

Defined in `src/anim/`:

- `enum LimbType { Leg, Arm, Wing, Tail, Other }` — functional role of a limb, assigned by the classifier (P2).
- `readonly struct IkResult { Vector3 Knee; Vector3 End; bool Reachable; }` — output of the IK solver (P1). `End` is the foot. `Reachable == false` ⇒ the solver clamped to full extension; output is always finite.
- `readonly struct Gait { float CadenceHz; float DutyFactor; float[] PhaseOffsets; int LegCount; }` — locomotion parameters (P3). `PhaseOffsets[k] ∈ [0,1)` per leg.
- `readonly struct Pose { int BoneCount; static Pose Rest(int); Transform3D Delta(int); Pose With(int, Transform3D); }` — per-bone LOCAL transform deltas applied on top of rest: `posedLocal_i = restLocal_i * Delta(i)`. Identity ⇒ rest (undeformed). Immutable. Built by the locomotion driver (P4), written to the `Skeleton3D` by `CreaturePreview.ApplyPose` (P0/PR-B).

Defined in `src/mesh/` (skeleton topology, produced by `SkeletonResolver`, ADR-0003):

- `readonly struct LimbChain { int AttachmentIndex; int RootBone; int KneeBone; int FootBone; float UpperLength; float LowerLength; string SlotName; float TotalLength; }`
  - `RootBone` = upper bone (its `Head` is the hip), `KneeBone` = lower bone (its `Head` is the knee), `FootBone` = the bone whose `Tail` is the foot (`== KneeBone` for a 2-bone chain).
  - `AttachmentIndex` → `recipe.Attachments[AttachmentIndex].ChildPartId` lets the classifier recover the part (e.g. tell `limb_wing` from `limb_walker`).
- `sealed class CreatureSkeleton { Bone[] Bones; LimbChain[] LimbChains; Aabb Bounds; int Count; }` — `LimbChains` is in attachment order; empty when no `PartKind.Limb` parts.
- `readonly struct Bone { Vector3 Head, Tail; float RadiusHead, RadiusTail; int ParentIndex; ... }` (unchanged from M2).

Read hip / knee / foot world positions from the chain like so:
```csharp
Vector3 hip  = skeleton.Bones[chain.RootBone].Head;   // == Bones[KneeBone].Head's parent joint
Vector3 knee = skeleton.Bones[chain.KneeBone].Head;
Vector3 foot = skeleton.Bones[chain.FootBone].Tail;
```

## Agent A — Two-bone analytic IK  (`src/anim/TwoBoneIk.cs`)

**Signature**
```csharp
namespace LittleGods.Anim;

public static class TwoBoneIk
{
    /// Closed-form law-of-cosines solve for one 2-bone limb.
    ///   root     : hip position (world)
    ///   upperLen : hip->knee rest length  (> 0)
    ///   lowerLen : knee->foot rest length (> 0)
    ///   target   : desired foot position (world)
    ///   pole     : hint the knee bends toward (world direction or point off the
    ///              root->target axis); need not be normalised. Degenerate poles
    ///              fall back to a stable default so the result is never NaN.
    /// Returns knee + foot world positions (IkResult).
    public static IkResult Solve(Vector3 root, float upperLen, float lowerLen,
                                 Vector3 target, Vector3 pole);
}
```

**Requirements**
- Law of cosines for the knee bend; place the knee in the plane spanned by `(target - root)` and the pole, on the pole side.
- **Reach clamping (never NaN):** if `|target - root| >= upperLen + lowerLen`, return full extension toward the target (`Reachable = false`). If `|target - root| <= |upperLen - lowerLen|`, clamp to the minimum-fold configuration (`Reachable = false`). Otherwise solve exactly (`Reachable = true`, foot ≈ target).
- Degenerate-safe: `target == root`, zero-length pole, and `upperLen`/`lowerLen` near zero all return finite output.
- Pure / deterministic.

**Tests** (`tests/unit/TwoBoneIkTests.cs`)
- Reachable target: foot ≈ target (within 1e-4); `Reachable` true; `|root→knee| ≈ upperLen`, `|knee→foot| ≈ lowerLen`.
- Over-reach: foot lies on the `root→target` ray at distance `upperLen + lowerLen`; `Reachable` false; finite.
- Target at root: no NaN/Inf.
- Pole vector flips the knee to the expected side.
- Close target folds without NaN.

## Agent B — Limb-type classifier  (`src/anim/LimbClassifier.cs`)

**Signature**
```csharp
namespace LittleGods.Anim;

public static class LimbClassifier
{
    /// One LimbType per skeleton.LimbChains[i], SAME ORDER and length.
    /// The load-bearing legs the gait drives are the chains whose result is
    /// LimbType.Leg (the integrator filters on that).
    public static LimbType[] Classify(
        LittleGods.Creature.Recipe recipe,
        LittleGods.Creature.PartRegistry registry,
        CreatureSkeleton skeleton);
}
```

**Requirements** (heuristic, deterministic, order-stable — tune against the bundled creatures)
- `SlotName` contains `"tail"` ⇒ `Tail`.
- The chain's part (`recipe.Attachments[chain.AttachmentIndex].ChildPartId` → `registry.Get`) id contains `"wing"` ⇒ `Wing`.
- Else a `"hip"` / `"shoulder"` / `"leg"` slot ⇒ `Leg` (the load-bearing set for locomotion). World orientation (`foot.Y` vs `hip.Y`, lateral splay) may refine `Leg` vs `Arm` for non-quadruped morphologies.
- Otherwise `Arm` / `Other`.
- Result length **equals** `skeleton.LimbChains.Length`; never null.

**Tests** (`tests/unit/LimbClassifierTests.cs`)
- The bundled quadruped (2 shoulder + 2 hip `limb_walker`) → four `Leg`.
- A creature with `limb_wing` on a shoulder → `Wing`, not `Leg`.
- A `tail` slot (`limb_tail`) → `Tail`.
- Deterministic and order-stable across repeated calls.
- Empty `LimbChains` → empty array.

## Agent C — Gait controller  (`src/anim/GaitController.cs`)

**Signature**
```csharp
namespace LittleGods.Anim;

public static class GaitController
{
    /// Phase-offset preset for a given number of load-bearing legs.
    ///   biped    (2): alternating      (0, 0.5)
    ///   quadruped(4): diagonal pairs   (0, 0.5, 0.5, 0)
    ///   hexapod  (6): alternating tripods
    ///   octopod  (8): metachronal wave
    /// Other counts: a sensible evenly-spread fallback. DutyFactor default ~0.6.
    public static Gait ForLegCount(int legCount, float cadenceHz);

    /// Cycle phase in [0,1) for leg k at elapsed time t. Pure, wraps cleanly.
    ///   phase = frac(t * gait.CadenceHz + gait.PhaseOffsets[k])
    public static float PhaseOf(in Gait gait, int leg, double seconds);

    /// True when leg k is in stance (planted) at time t: phase < DutyFactor.
    public static bool IsStance(in Gait gait, int leg, double seconds);
}
```

**Requirements**
- Presets match the table above per leg count; `PhaseOffsets.Length == legCount`.
- `PhaseOf` is deterministic, in `[0,1)`, and wraps with no discontinuity in stance/swing classification across the cycle boundary.
- `GaitController` depends ONLY on `Gait` + primitives — not on the classifier's output type (the integrator wires them together at P4).

**Tests** (`tests/unit/GaitControllerTests.cs`)
- Offsets match the preset per leg count (2/4/6/8).
- At any sampled time, the number of legs in stance is ≥ the static-stability minimum for that gait (e.g. quadruped ≥ 2 down).
- `PhaseOf` deterministic given time; `PhaseOf(t)` and `PhaseOf(t + 1/Cadence)` agree (clean wrap).
- Duty factor governs the stance fraction.

## Integration (P4, central — not an agent's job)

`Locomotion` (P4) calls `LimbClassifier.Classify` → keeps the `Leg` chains → `GaitController.ForLegCount` → per tick computes each leg's foot target → `TwoBoneIk.Solve` → builds a `Pose` → `CreaturePreview.ApplyPose`. Agents do **not** touch `Locomotion`, `CreatureMesher`, `GodotMeshBuilder`, `CreaturePreview`, the resolver, or any `.tscn`.

## Agent ground rules (M1/M2 lessons)

- **Strict file scope.** Each agent writes only its one module file above plus its one test file. No edits to P0 contract types, the resolver, the Godot bridge, or another agent's files.
- **Do not run Godot or the test runner.** The `.godot` cache races across processes; the lead builds and runs the suite centrally between phases. Write the code and the tests; do not execute them.
- **Pure C#, deterministic, explicit `double seconds`** — no clock, no unseeded RNG.
- Keep files focused and under the 800-line cap (coding-style rule).
