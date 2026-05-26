# M3 plan — Procedural animation (the critical milestone)

**Window:** Weeks 15–22 (8 weeks, 40 working days).
**Spec:** PRD §7 M3.
**Owner sub-agent:** `animation-dev`.
**Status:** This is the **gated** milestone. PRD §9 rates "procedural animation doesn't generalise to extreme morphologies" as *project-killing*. The mitigation is baked into this plan: a single-creature **vertical slice lands early (P4)**, and if generalisation fails the documented fallback is a *fixed gait library per limb-count class*.

## Acceptance test — the Hecker test (lifted from PRD)

> Creatures with **2, 4, 6, and 8 legs all walk plausibly without visible IK breaks across 60 seconds of locomotion.** This is the gate. If it fails, the project pauses and reassesses before M4.

Decomposed into enforceable checks (automated where possible, plus visual review):

1. **No IK breaks** — over a 60 s deterministic locomotion run per creature: feet stay within a small tolerance of the ground plane during stance; no joint exceeds its reach (no popping to full-stretch); no NaN/Inf in any bone transform.
2. **Plausible gait** — stance/swing phases alternate correctly for the leg count (biped alternating, quadruped diagonal, hexapod tripod, octopod metachronal); the body advances at a speed consistent with stride length × cadence (no foot skating beyond tolerance).
3. **Generalises** — the *same* code path animates 2/4/6/8 legs from their recipes with no per-creature special-casing (fallback: per-limb-count gait presets, still one code path).

Automated locomotion-sanity tests assert (1) and the foot-skate bound of (2); a rendered walk snapshot/clip per leg-count is the human review artdefact for "plausible".

## Architectural contract

Non-negotiable for M3 (PRD §6 invariants, restated):

- **C# does the animation math; GDScript only drives the walk-preview scene + camera** (invariant 5). IK solver, gait controller, limb classifier, foot planner, jiggle — all pure C# in `src/anim/`.
- **Determinism** (invariant 4). The animation tick is a **pure function of `(skeleton, gait params, elapsed time)`**. No `DateTime.Now`, no `Time.GetTicksMsec()` inside the math — elapsed time is passed in as an explicit `double seconds` so a 60 s run is byte-reproducible and unit-testable. RNG (e.g. gait jitter) takes an explicit seed.
- **Reuse, don't fork, the M2 skeleton** (invariant 2). M3 extends `CreatureSkeleton` / `Bone` / `SkeletonResolver`; it does not introduce a parallel skeleton type. The same path must animate non-creature rigs later.
- **One IK runtime for everything** (PRD invariant 3 is about behaviour trees, but the spirit applies): a single two-bone solver + one locomotion driver, not per-morphology hand-tuned animators.
- **No baked animation** — gait is generated at runtime from the recipe + gait params; nothing is keyframed per creature.

### The dependency M3 must resolve first

M2 (ADR-0002) gives every placed part **exactly one bone**, and M2 deferred real skin binding ("emits a rest-pose `Skeleton3D` only"). Two-bone analytic IK needs a **2-segment limb** (upper + lower with a knee), and animation is invisible until the mesh actually deforms when bones move. So **P0 is a prerequisite**, not polish:

- Wire a Godot `Skin` resource (bind pose = inverse bone global rest) so posing the `Skeleton3D` deforms the skinned `ArrayMesh`.
- Extend `SkeletonResolver` to emit a **2-bone chain for limb parts** (upper/lower joined at a mid knee), recorded in a new ADR-0003. Auto-skinning already weights to the N nearest bones, so it adapts to the extra bones with no change.

## Phase plan

### P0 — Animation foundation: skin binding + 2-bone limbs (6 days, central)

The load-bearing prerequisite. I write this; it unblocks the parallel P1–P3 agents.

**Deliverables**
- `src/anim/` created. Shared types the P1–P3 agents build against (the M3 analogue of `m2-contract.md`, written as `docs/m3-contract.md`):
  - `readonly struct LimbChain` — root bone index, mid (knee) bone index, end (foot) bone index, the two segment lengths, the limb's side/role slot.
  - `readonly struct IkResult` — joint (knee) + end positions (and a `Reachable` flag).
  - `enum LimbType { Leg, Arm, Wing, Tail, Other }`, `readonly struct Gait` (cadence, duty factor, per-limb phase offsets), `readonly struct Pose` (per-bone local `Transform3D` deltas to apply to the `Skeleton3D`).
- Extend `Bone`/`SkeletonResolver`: limb parts resolve to a **2-bone chain** (upper from slot anchor along the slot normal for `BoneLength·split`, lower continuing, foot at the tip). A `Part.LimbSegments` field (additive, default 1) or a resolver rule keyed on `PartKind.Limb`; **ADR-0003** records the choice and confirms `FormatVersion` stays 1.
- `GodotMeshBuilder`/`CreaturePreview`: build a `Skin` with bind poses so the existing skinned mesh deforms when the `Skeleton3D` is posed; add `ApplyPose(Pose)` to the preview.
- Tests: skeleton resolves the expected bone count for a multi-leg creature; a hand-applied knee bend visibly moves the foot bone; bind-pose round-trips (rest pose ⇒ undeformed mesh).
- Manual: a snapshot of a creature with one limb posed (knee bent) showing the skin deforming.

**Acceptance:** posing a bone deforms the skin in the preview; `dotnet build` clean; existing 101 tests still green.

### P1 — Two-bone analytic IK solver (4 days, parallel agent A)

`src/anim/TwoBoneIk.cs` — closed-form law-of-cosines solver: given root position, two segment lengths, a target, and a pole/hint vector → knee + end positions (`IkResult`). Pure, deterministic.
*Tests:* reachable target hits exactly; over-reach clamps to full extension toward target (no NaN); target at root degenerate-safe; pole vector controls knee direction; folds correctly when target is close.

### P2 — Limb-type classification from topology (3 days, parallel agent B)

`src/anim/LimbClassifier.cs` — from the recipe + resolved skeleton, classify each limb chain: `PartKind.Limb` + slot (`*_hip`/`*_shoulder`) + world orientation (downward ⇒ Leg; lateral/up ⇒ Arm/Wing; the `limb_tail`/`tail` slot ⇒ Tail). Returns `LimbType` per chain + which chains are **load-bearing legs** (the set the gait drives).
*Tests:* the bundled quadruped → 4 Legs; a winged creature → Wings not Legs; tail slot → Tail; classification is deterministic and order-stable.

### P3 — Gait controller + phase offsets (4 days, parallel agent C)

`src/anim/GaitController.cs` — given the leg set + elapsed time → each leg's phase ∈ [0,1) and stance/swing state. Phase-offset presets by leg count: biped 0.5 alternating; quadruped diagonal pairs (0, 0.5, 0.5, 0); hexapod tripod (0/0.5 alternating tripods); octopod metachronal wave. Cadence + duty factor parameters.
*Tests:* phase offsets match the preset per leg count; at any time the correct number of legs are in stance (static-stability sanity); deterministic given time; cycle wraps cleanly.

*(P1–P3 are independent pure-C# modules built against P0's types — dispatched as a team of 3 parallel agents, same discipline as M2 P1: strict file scopes, no godot/test runs, central validation. Wall-clock ≈ 5 days.)*

### P4 — Foot-target IK + body bob/balance + VERTICAL SLICE (8 days, central)

The integrator, and the **early de-risk**. Get **one quadruped walking convincingly** before generalising.

**Deliverables**
- `src/anim/Locomotion.cs` — per tick: advance gait → compute each leg's foot target (swing arc forward, stance planted on the ground plane, driven by body velocity) → solve each leg with `TwoBoneIk` → apply knee/foot rotations to the `Skeleton3D` → body bob (vertical sine on stride phase) + balance shift (COM stays over the support polygon).
- A `scenes/anim/WalkPreview.tscn` + GDScript driver: the creature walks in place (or across a ground plane) at a fixed deterministic timestep; orbit camera.
- Foot planting against a flat ground plane (y=0) for M3; real terrain is M4.
- Tests: foot targets stay on the ground during stance; body advances consistent with stride; no NaN over a 60 s tick.

**Acceptance (vertical slice):** the bundled quadruped walks plausibly for 60 s with feet planted and no visible breaks. **This is the early go/no-go** — if the quadruped can't be made to look right, invoke the descope (fixed gait library) before sinking P5/P6 effort.

### P5 — Secondary motion (jiggle) + generalise to 2 / 6 / 8 legs (6 days, central)

**Deliverables**
- `src/anim/Jiggle.cs` — spring-damped secondary motion on non-foot bones (tail, body), driven by acceleration; deterministic spring integration with explicit dt.
- Generalise the locomotion driver across leg counts using P3's presets — **one code path**, gait preset selected by leg count. Build 2-, 6-, and 8-leg test creatures from the recipe data model.
- Tests: jiggle is stable (no blow-up) and deterministic; each leg-count creature ticks 60 s without breaks.

**Acceptance:** 2/4/6/8-leg creatures all walk via the same driver; jiggle reads as alive, not noisy.

### P6 — The Hecker test + acceptance + docs (6 days, central)

**Deliverables**
- `tests/unit/HeckerWalkTests.cs` — for each of 2/4/6/8 legs: run 60 s of deterministic locomotion; assert no NaN, feet within ground tolerance during stance, no joint over-extension, foot-skate under bound. The automated half of the gate.
- Walk snapshots/clips per leg-count under xvfb + opengl3 (the human-review half); captured as CI artefacts (not pixel-golden — motion + GPU variance).
- `docs/animation-pipeline.md` — fill the "Deferred to M3" section: IK, gait, classification, foot planning, jiggle, the locomotion tick.
- `docs/architecture.md` — flip the M3 `src/anim/` row to done.
- ADR-0003 (2-bone limb model) finalised; ADR for the gait-preset approach if it diverged.
- **Go/no-go note**: explicitly record whether the Hecker test passed on the generalised path or the descoped fallback.

**Acceptance:** PRD M3 Hecker test passes end-to-end in CI (automated checks) + visual review across 2/4/6/8 legs.

## Total estimate

| Phase | Days | Cumulative | Notes |
|-|-|-|-|
| P0 skin bind + 2-bone limbs | 6 | 6 | central; unblocks agents |
| P1 two-bone IK | 4 | — | parallel agent A |
| P2 limb classifier | 3 | — | parallel agent B |
| P3 gait controller | 4 | 11 | parallel agent C (wall-clock ~5) |
| P4 foot-IK + body + vertical slice | 8 | 19 | central; early go/no-go |
| P5 jiggle + generalise 2/6/8 | 6 | 25 | central |
| P6 Hecker test + acceptance + docs | 6 | 31 | central |
| **Slack** | 9 | **40** | within Weeks 15–22; M3 is high-risk, slack is real |

## Risks

| Risk | Likelihood | Impact | Mitigation |
|-|-|-|-|
| Animation doesn't generalise to extreme morphologies | Medium | **Project-killing** | Vertical slice at P4 (one quadruped) is the early gate; documented fallback = fixed gait library per limb-count class (still one driver). Don't build P5/P6 on a broken P4. |
| 2-bone limb extension destabilises the M2 mesh/skin | Medium | High | P0 lands behind tests; auto-skinner already N-nearest so it adapts; bind-pose round-trip test guards the rest pose |
| Foot skating / sliding looks wrong | Medium | Medium | Lock stance feet in world space during the stance phase; only swing legs move; tune stride vs body speed |
| Two-bone IK pops at reach limits | Medium | Medium | Clamp to full extension toward target (never NaN); soften the last few degrees; pole vector keeps knees sane |
| Determinism lost via clock reads in the tick | Low | High | Tick takes explicit `double seconds`; no clock/RNG in `src/anim` math; a 60 s reproducibility test guards it |
| Skinning bind-pose math (Godot `Skin`) is fiddly | Medium | Medium | Isolated in P0 with a rest-pose-undeformed test; GodotMeshBuilder owns it; M2 already builds the rest skeleton |
| Scope blowout on "plausible" polish | Medium | Medium | "Plausible, no breaks" is the bar, not film-quality; jiggle + bob are the only secondary layers in M3 |

## Out of scope for M3 (defer)

- Behaviour trees / AI / LimboAI — **M4**.
- Real terrain foot placement (M3 uses a flat ground plane) — **M4**.
- Biome, NPC species, eat/mate/fight loop — **M4**.
- Multi-bone limbs beyond 2 segments (e.g. articulated spines, multi-joint tails) — only the 2-bone leg is required for the Hecker test.
- Animation blending/state machines, run vs walk transitions — single walk gait suffices for the gate.
- ML retargeting — PRD §10 explicitly defers this past EA.

## Open questions

- **2-bone limb representation** — split each limb *part* into 2 bones in `SkeletonResolver`, or add an explicit knee `AttachmentPoint`/segment field on `Part`? *Recommend: resolver splits `PartKind.Limb` parts into 2 bones (no library re-authoring); ADR-0003.* **Decide before P0.**
- **Gait preset source** — hardcoded presets per leg count, or a small data-driven gait table? *Recommend: a `Gait` preset table keyed by leg count, easy to tune.* **Decide before P3.**
- **Walk preview interaction** — walk in place (camera static) or translate across a ground plane (camera follows)? *Recommend: translate across ground; it surfaces foot-skating immediately.* **Decide before P4.**
- **Art direction** (still open from M2, PRD §10) — does not block M3; locomotion is material-agnostic. Surface to Lee against the walking preview.

## Definition of done (one-liner)

> Creatures with 2, 4, 6, and 8 legs all walk plausibly for 60 seconds with no IK breaks, driven by one procedural locomotion path from their recipes; a CI test asserts no NaN / feet-on-ground / no over-reach, and a walk clip per leg-count confirms it reads as alive. The Hecker gate is passed (or the descoped fallback is documented). We move to M4.
