# M4 contract — agent runtime + world seam

Status: **locked for M4 P0–P3.** This is the seam the parallel P1–P3 agents build against (the M4 analogue of `m3-contract.md`). The shared types below already exist in `src/agent/` and `src/world/` and **compile** (landed in P0, branch `m4-p0-foundation`). Implement against them; **do not change their signatures** without updating this doc.

All new sim code is in namespace `LittleGods.Agent` (behaviour, agents) or `LittleGods.World` (terrain, services), pure C#, **no Godot scene-tree types** (Godot value structs — `Vector3`, `Transform3D`, `Basis` — are fine; never `Node`, `Resource`, `Skeleton3D`, `.tscn`). World scene wiring is GDScript in a later PR.

**Determinism (PRD invariant 4):** every function is a pure function of its inputs. No `DateTime.Now`, no `Time.GetTicksMsec()`. The sim clock enters as an explicit `double` (seconds / dt). All randomness comes from `DeterministicRng` (seeded). A fixed-seed N-second sim must replay byte-identically — there are unit tests for it.

**Agent-type-blind (PRD invariant 3):** the runtime and tasks never branch on "is this a creature". Everything is `AgentState` + `Blackboard` + `IWorldServices`.

`TreatWarningsAsErrors` is on — code must be **warning-clean** (XML-doc warning CS1591 is exempted). Files stay under **800 lines** (coding-style rule); prefer many small files.

## Frozen P0 seam types (already implemented — do not modify)

`src/world/`:
- `sealed class DeterministicRng` — SplitMix64. `NextULong/NextUInt/NextFloat()∈[0,1)/NextDouble()/Range(min,max)/RangeInt(lo,hi)/OnUnitCircleXz()/Fork(salt)`. A class (shared by reference) so an agent advances one stream over its life.
- `interface IGroundSampler { float HeightAt(float x,float z); Vector3 NormalAt(float x,float z); }` — terrain seam.
- `sealed class FlatGround : IGroundSampler` — constant height (default 0), normal +Y. `FlatGround.Zero`.
- `interface IWorldServices { IGroundSampler Ground; double ElapsedSeconds; double TimeOfDay; IReadOnlyList<AgentState> AgentsNear(Vector3 position, float radius); }` — the generic world view tasks query. `TimeOfDay ∈ [0,1)`.

`src/agent/`:
- `enum BtStatus { Running, Success, Failure }`.
- `readonly struct BtContext { AgentState Agent; Blackboard Blackboard; IWorldServices World; double Dt; }` — one tick's inputs.
- `interface IBtTask { BtStatus Tick(in BtContext ctx); }` — tasks are **stateless and shared** across all agents of a species; per-agent state lives in `ctx.Agent` / `ctx.Blackboard`.
- `sealed class Blackboard` — typed per-agent memory: `Set<T>/Get<T>/TryGet<T>/GetOr<T>/Has/Remove/Clear`.
- `sealed class AgentState` — `Id, SpeciesId; Transform3D Transform; Vector3 Velocity; Needs Needs; LifecycleState Lifecycle; DeterministicRng Rng; Position; Forward(=-Basis.Z); IsAlive`. **Mutable** sim entity (updated in place each tick).
- `readonly struct Needs { float Health, Hunger, Age; Newborn(); With(...); }` — fields only; P3 owns integration.
- `enum LifecycleState { Alive, Dead }`.
- `enum AgentRelation { Neutral, Prey, Predator, Mate }`.
- `static class Bb` — well-known blackboard keys (`MoveTarget, HasMoveTarget, Arrived, MoveThrottle, TurnRate, NearestPrey, NearestPredator, NearestMate`). Use these constants, don't invent string literals.

## Agent A — behaviour-tree runtime  (`src/agent/`)

Build the composites/decorators/leaves/factory that turn `IBtTask` into trees (ADR-0005). **Memoryless** semantics (re-evaluate from the first child each tick — reactive); no per-node mutable fields (trees are shared). Files to CREATE: `Composites.cs`, `Decorators.cs`, `Leaves.cs`, `BehaviorTree.cs`, `Bt.cs`. Test file: `tests/unit/BtRuntimeTests.cs`.

**Exact semantics** (unit-test each transition):

| Node | Tick rule |
|-|-|
| `Sequence` | children in order: first `Failure`⇒`Failure`; first `Running`⇒`Running` (stop); all `Success`⇒`Success` |
| `Selector` | children in order: first `Success`⇒`Success`; first `Running`⇒`Running` (stop); all `Failure`⇒`Failure` |
| `Parallel(ParallelPolicy.RequireAll, …)` | tick ALL children: any `Failure`⇒`Failure`; else all `Success`⇒`Success`; else `Running` |
| `Parallel(ParallelPolicy.RequireOne, …)` | tick ALL children: any `Success`⇒`Success`; else all `Failure`⇒`Failure`; else `Running` |
| `Inverter` | `Success`↔`Failure`; `Running` unchanged |
| `Succeeder` | `Success`/`Failure`⇒`Success`; `Running` unchanged |
| `Failer` | `Success`/`Failure`⇒`Failure`; `Running` unchanged |
| `ActionTask` | runs `Func<BtContext,BtStatus>` and returns it |
| `ConditionTask` | `Func<BtContext,bool>` ⇒ `Success` if true else `Failure` (never `Running`) |

**Public factory** (`static class Bt` — the ergonomic API P1 uses; note `Bt` = tree combinators, `Bb` = blackboard keys):
```csharp
public enum ParallelPolicy { RequireAll, RequireOne }

public static class Bt
{
    public static IBtTask Sequence(params IBtTask[] children);
    public static IBtTask Selector(params IBtTask[] children);
    public static IBtTask Parallel(ParallelPolicy policy, params IBtTask[] children);
    public static IBtTask Invert(IBtTask child);
    public static IBtTask Succeed(IBtTask child);
    public static IBtTask Fail(IBtTask child);
    public static IBtTask Action(System.Func<BtContext, BtStatus> fn);
    public static IBtTask Do(System.Action<BtContext> effect);     // run effect, return Success
    public static IBtTask Condition(System.Func<BtContext, bool> predicate);
    public static BehaviorTree Tree(IBtTask root);
}

public sealed class BehaviorTree
{
    public BehaviorTree(IBtTask root);
    public BtStatus Tick(in BtContext ctx);   // delegates to root
    public BtStatus LastStatus { get; }
}
```

**Requirements:** composites accept any child count (0 children: `Sequence`/`Parallel(RequireAll)`⇒`Success`, `Selector`/`Parallel(RequireOne)`⇒`Failure`). Pure given the children's behaviour; no allocation in `Tick` (store children in an array at construction). Deterministic.

## Agent B — terrain + world services  (`src/world/`)

Build the deterministic biome height function and the concrete world view. Files to CREATE: `ValueNoise.cs`, `HeightmapTerrain.cs`, `WorldServices.cs`. Test files: `tests/unit/DeterministicRngTests.cs`, `tests/unit/TerrainTests.cs`, `tests/unit/WorldServicesTests.cs`.

- **`ValueNoise`** — deterministic 2D value noise + fBm. Integer hash of `(ix, iz, seed)` → `[0,1)` (e.g. mix with `0x9E3779B1`-style constants + xorshifts; **no** `System.Random`, no Godot noise). Smootherstep fade, bilinear interp of the 4 lattice corners. fBm: sum `octaves` octaves, lacunarity 2, gain 0.5, result normalised to `[0,1)` (or `[-1,1]` — document which). Pure; same seed ⇒ identical everywhere.
- **`HeightmapTerrain : IGroundSampler`** — ctor `(ulong seed, float amplitude, float baseFrequency, int octaves, float sizeMeters)` (sensible defaults: amplitude ~6, baseFrequency ~1/120, octaves 4, size ~1024). `HeightAt(x,z) = amplitude * fBm(x*baseFrequency, z*baseFrequency)` (centre fBm so flat-ish biome straddles 0). `NormalAt` from central differences of `HeightAt` (small epsilon), normalised. Expose the world `sizeMeters` (a `float HalfExtent`/`Aabb`). Pure & deterministic.
- **`WorldServices : IWorldServices`** — holds an `IGroundSampler`, the sim clock, a `dayLengthSeconds` (default e.g. 300), and the current agent set. `ElapsedSeconds` advanceable (a settable property or `Advance(double dt)`); `TimeOfDay = frac(ElapsedSeconds / dayLengthSeconds)`. `AgentsNear` = O(n) distance filter over the registered agents (a `SetAgents(IReadOnlyList<AgentState>)` or ctor list is fine — **P8** adds spatial partitioning; keep it correct and simple now). Provide a ctor usable in tests: `(IGroundSampler ground, double dayLengthSeconds)`.

**Tests:** RNG — same seed reproduces the exact sequence; different seeds diverge; `Range`/`RangeInt` stay in bounds; `Fork` is independent & repeatable. Terrain — `HeightAt` identical for the same seed across calls; differs across seeds; stays within `[-amplitude, amplitude]`; neighbouring samples are close (continuity); `NormalAt` is unit-length and ≈ `+Y` over a near-flat patch. WorldServices — `TimeOfDay` wraps in `[0,1)` across a day boundary; `AgentsNear` returns exactly those within radius (boundary-inclusive is fine, document it); deterministic.

## Integration — NOT an agent's job (P0 lead, then P4)

- **P0 lead (me):** `Locomotion.Tick` gains a trailing `IGroundSampler? ground = null` parameter — `null` keeps the exact M3 flat-y=0 behaviour (all 156 M3 tests unchanged); non-null plants stance/swing feet on `ground.HeightAt(footWorldXZ)` and rides the body at `ground.HeightAt(bodyXZ) + BodyHeight + bob`. Tested in `tests/unit/LocomotionTerrainTests.cs`.
- **P2/P4 (later):** steering writes `Bb.MoveThrottle` / `Bb.TurnRate`; the agent tick applies them to `AgentState.Transform`/`Velocity` and drives `Locomotion`. **Variable-speed no-skate** (locking a stance foot to its plant position under non-constant speed) is a **P4** locomotion-bridge task — out of scope for P0/P1/P2. M3 locomotion advances at a constant `StrideLength·CadenceHz`; do not assume agent speed flows into it yet.

## Phase ownership & order

- **P1 (agent):** `IdleTask`, `WanderTask` (`src/agent/`, e.g. `Behaviors.cs` + `tests/unit/CoreTasksTests.cs`). Wander: if no `Bb.HasMoveTarget`, pick a point within a radius around the agent on the ground (`ctx.Agent.Rng`, `ctx.World.Ground`), set `Bb.MoveTarget`/`HasMoveTarget`; return `Running` until `Bb.Arrived`, then clear and `Success`. Does **not** implement steering (P2) — it only sets the target.
- **P2 (agent):** `Perception` (defines `readonly struct Sighting { AgentState Target; float Distance; Vector3 ToTarget; }`, buckets `AgentsNear` by `AgentRelation` into `Bb.NearestPrey/Predator/Mate`) + `Steering` (consume `Bb.MoveTarget` → write `Bb.MoveThrottle`/`TurnRate`). Takes a relation classifier `Func<AgentState self, AgentState other, AgentRelation>` (P5 supplies the species-based impl; tests use a fake).
- **P3 (agent):** `NeedsSystem.Integrate(in Needs, double dt, …) → Needs` (pure) + `Lifecycle.Step(...)`. Owns thresholds (hunger rises, starving drains Health, age → death).

## Agent ground rules (M2/M3 lessons — follow exactly)

- **Strict file scope.** Create only the files listed for your agent above, plus your test file(s). Do **not** edit the frozen P0 seam types, `Locomotion`, any other agent's files, or any `.tscn`.
- **Do NOT run Godot, `dotnet`, or the test runner.** The `.godot` cache races across processes; the lead builds and runs the suite centrally. Write the code and the tests; do not execute them.
- **Pure C#, deterministic, warning-clean, <800 lines/file.** No clock, no `System.Random`.
- **Test conventions (GdUnit4 C#):** `[TestSuite]` class, `[TestCase]` methods, `namespace LittleGods.Tests`, `using GdUnit4;` + `using static GdUnit4.Assertions;`. `AssertThat(int/bool/object)`, `AssertThat(bool).IsTrue()/IsFalse()`, `AssertFloat(f).IsEqualApprox(v, tol)`, `AssertThat(int).IsEqual(n)/IsGreater(n)`. Mirror an existing suite (e.g. `tests/unit/GaitControllerTests.cs`) for structure.
