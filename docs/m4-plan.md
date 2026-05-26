# M4 plan — Creature stage MVP (the first *game*)

**Window:** Weeks 23–35 (13 weeks).
**Spec:** PRD §7 M4.
**Owner sub-agent:** `simulation-dev`.
**Status:** M3 is complete — the Hecker gate passed on the generalised path, so the project proceeds. M4 turns the creature *editor + animation* into a playable *stage*: a creature lives in a biome, hunts/flees/mates, dies, and the player re-edits it between lives.

## Acceptance (lifted from PRD)

> A new player can play **30 minutes without crashes**, design **3 creature iterations**, and reach a **"this is fun" moment** as measured by playtesters.

Decomposed into enforceable checks:
1. **Stable** — a 30-minute session runs with no crash and no unbounded slowdown (frame time stays within budget with the target population on screen).
2. **The loop closes** — play → die → re-edit (mutation slot) → respawn → play, with carried consequences (HP/hunger/lifecycle), at least 3 times.
3. **Alive world** — 3–5 NPC species wander/hunt/flee/mate via one behaviour-tree runtime; day/night advances; a minimal HUD shows HP / hunger / DNA budget.
4. **Fun** — a playtester self-reports a "this is fun" moment. Subjective; de-risked by a playable vertical slice landing early (P4).

## Architectural contract (PRD §6 invariants, restated for M4)

- **One Behaviour Tree runtime for ALL agents, agent-type-blind** (invariant 3). Creatures now; tribes/civs/empires later run the *same* runtime. Tasks read/write a generic blackboard; the runtime never branches on "is this a creature".
- **Determinism given a seed** (invariant 4). World gen, species gen, and the simulation tick are pure functions of `(seed, state, dt)`. No `DateTime.Now`; the sim clock is an explicit accumulated `double`. RNG is seeded and threaded through state. A fixed-seed N-second sim is reproducible (and testable).
- **C# does the simulation; GDScript drives UI + scene glue** (invariant 5). BT tasks, perception, needs, interactions, species gen, terrain gen — all C# in `src/agent/` + `src/world/`. HUD, world scene wiring, input — GDScript.
- **Reuse the creature pipeline** (invariant 2). NPC species are `Recipe`s fed through the existing `SkeletonResolver → CreatureMesher → Locomotion`. No parallel creature representation. M4 extends locomotion from a flat plane to terrain; it does not fork it.
- **Open-source deps only** (invariant 7). LimboAI is MIT — eligible. Any addon is vendored as text and committed.
- **No singletons except genuine cross-cutting autoloads** (invariant 6) — e.g. the sim clock / world service may be an autoload; agents are not.

### The bridge M4 must resolve first

M3 plants feet on a **flat ground plane (y = 0)**. M4 has **terrain**, so a stance foot must plant on the *terrain height under it* (a raycast / heightmap sample), and the body must ride the average ground height + slope. This terrain-aware foot planting is the first real piece of P0 — it is the seam between M3 locomotion and an M4 world, and it is where "walking looks broken on slopes" risk lives.

## Phase plan

### P0 — World skeleton + BT-runtime decision + terrain locomotion (central)
The load-bearing prerequisite; I write this, it unblocks the parallel P1–P3 agents.
- **ADR — Behaviour-tree runtime.** LimboAI addon vs a small custom C# BT runtime. *Recommend LimboAI* (PRD names it; MIT; mature) **iff** it cleanly drives C# tasks against a generic blackboard and stays agent-type-blind; otherwise a ~few-hundred-line custom C# BT (Sequence/Selector/Parallel/Decorator/Action + blackboard) that we fully control. Decide in an ADR before P1.
- **Terrain.** A single ~1 km² procedural heightmap biome (deterministic from seed); a `TerrainHeight(x, z)` sampler + collision. *Recommend heightmap over voxel* for M4.
- **Terrain locomotion.** Extend `Locomotion` foot targets to plant on `TerrainHeight` under each foot; body height tracks local ground; clamp to reachable. (Possibly an `IGroundSampler` seam so the flat-plane path stays for tests.)
- **`src/agent/` + `src/world/` contract** (the M4 analogue of `m3-contract.md`, written as `docs/m4-contract.md`): `Blackboard`, `BtStatus { Running, Success, Failure }`, `IBtTask`, `AgentState` (transform, needs, species id, rng), `Perception` query shape, `WorldServices` (ground sampler, spatial queries, time-of-day).
- **World scene skeleton** (`scenes/world/World.tscn`): terrain + the player creature + a follow camera + the sim tick loop.

### P1 — BT runtime + core tasks: idle, wander (parallel agent A)
The chosen runtime + the simplest tasks (`Idle`, `Wander` — pick a nearby point, steer to it). Deterministic; unit-tested with a fake world.

### P2 — Perception + steering (parallel agent B)
`Perception`: nearest food / threat / mate within sense radius (spatial query). `Steering`: turn-and-walk toward / flee from a target, driving `Locomotion`'s forward/turn. Pure, deterministic.

### P3 — Needs + lifecycle (parallel agent C)
`Needs` (HP, hunger, age) integrated with explicit dt; thresholds (starving drains HP, age → death). Lifecycle state machine (alive → dead). Pure/deterministic.

*(P1–P3 are independent C# modules against P0's contract — a team of 3 parallel agents, M2/M3 discipline: strict file scopes, no godot/test runs, central validation.)*

### P4 — Interaction loop + VERTICAL SLICE (central, early go/no-go)
Wire perception + steering + needs + BT into the **eat / mate / fight** interactions with consequences, and get **one creature + one prey species living** in the biome convincingly before adding more. Hunt → catch → eat (hunger down); fight (HP down, can die); mate (spawn offspring). **This is the early "is it fun?" gate** — if the core loop is not engaging with one predator + one prey, rethink before scaling species.

### P5 — Species generation (central)
3–5 NPC species, each a deterministic `Recipe` from a seed (reusing the editor data model + part library), with per-species needs/behaviour params. A spawner maintains a population in the biome.

### P6 — Death → respawn + mutation loop (central)
Player creature dies → respawn flow that opens the editor with a **mutation slot** (carry the recipe, allow edits within a DNA budget) → re-enter the world. The editor↔world handoff (recipe in/out), persisting the player's lineage across lives.

### P7 — Day/night cycle + minimal HUD (parallel where separable)
Day/night (lighting + the sim time-of-day already in `WorldServices`); HUD (HP, hunger, DNA budget) in GDScript.

### P8 — Playtest + acceptance (central)
Performance pass with the target on-screen population (spatial partitioning for perception; LOD/cull animation for distant agents); a 30-minute soak test (no crash, bounded frame time); a deterministic N-agent N-second headless sim test (no NaN, population stays bounded, no agent stuck). Playtest for the "fun" moment; record the result.

## Risks

| Risk | Likelihood | Impact | Mitigation |
|-|-|-|-|
| Core loop isn't fun | Medium | **Project-defining** | Vertical slice at P4 (1 predator + 1 prey) is the early fun gate before scaling species |
| BT runtime can't stay agent-type-blind / scale to tribes-civs | Medium | High | ADR in P0 weighs LimboAI vs a custom runtime we fully control; keep tasks blackboard-only |
| Walking looks broken on slopes / terrain seams | Medium | High | Terrain-aware foot planting lands in P0 behind tests; clamp to reachable; an `IGroundSampler` seam keeps the flat-plane path for unit tests |
| Perf collapses with many agents | Medium | High | Spatial partition for perception; tick budgeting / LOD for far agents; headless N-agent perf test in P8 |
| Determinism lost (clock/unseeded RNG in the sim) | Low | High | Explicit `double` sim clock + seeded RNG threaded through state; a fixed-seed reproducibility test |
| Scope creep into Thrive territory | High | Severe delay | Hard gate at M4; the PRD non-goals + "out of scope" below |

## Out of scope for M4 (defer)

- Supabase backend, in-game gallery, creature sharing — **M5**.
- Steam page, telemetry, crash reporting, settings menu, tutorial — **M5**.
- Audio beyond a stub (formant creature sounds, ambient music) — **M5**.
- Multi-biome / deeper ecosystem, tribes/civ/empire stages — post-EA (PRD §3 non-goals).
- 6/8-leg rigblock parts for recipe-built high-leg-count creatures (M3 visual follow-up) — fold into P5 species gen only if a species needs them.

## Open questions (resolve before the relevant phase — some want Lee's call)

- **BT runtime:** LimboAI addon vs a custom C# runtime? *Recommend LimboAI (PRD-named, MIT) pending a P0 spike that it drives C# tasks blackboard-only.* **ADR before P1.**
- **The biome + the 3–5 species:** what does the single biome look like, and which species (grazers / pack hunters / etc.)? Content/creative — **Lee's steer wanted**; default to 1 grazer + 2 hunters + 1 scavenger in a temperate-plains biome.
- **Terrain tech:** heightmap vs voxel? *Recommend heightmap for M4.*
- **Mac support:** best-effort or dropped? (PRD §10 says decide by M4.) **Lee's call.**
- **Player control scheme:** direct WASD drive of the creature vs point-to-move? Decide before P4. *Recommend direct drive (it showcases the procedural locomotion).*

## Definition of done (one-liner)

> A new player drives a procedurally-animated creature through a procedural biome with 3–5 living NPC species (one behaviour-tree runtime, day/night, HUD), survives or dies in an eat/mate/fight loop, re-edits the creature between lives, and plays 30 crash-free minutes that a playtester calls fun. Then we move to M5 (sharing + Early Access).
