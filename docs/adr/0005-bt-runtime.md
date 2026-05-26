# ADR-0005: Custom C# behaviour-tree runtime (not LimboAI)

- **Status:** Accepted
- **Date:** 2026-05-26
- **Deciders:** Lee Flanagan
- **Milestone:** M4

## Context

M4 introduces autonomous agents. PRD §6 invariant 3 mandates **one behaviour-tree runtime for every agent, agent-type-blind** — creatures now, tribes/civs/empires later run the *same* runtime, which never branches on "is this a creature". The PRD §10 tech notes name **LimboAI** (MIT) as a candidate addon.

Two further invariants constrain the choice hard:

- **Invariant 4 — determinism.** The simulation tick must be a pure function of `(seed, state, dt)`: no `DateTime.Now`, no unsynchronised RNG, the clock an explicit accumulated `double`. A fixed-seed N-second headless sim must be byte-reproducible and unit-tested (this is a release blocker per `docs/architecture.md`).
- **Invariant 5 — C# does the simulation.** BT tasks (perception, needs, interactions) are simulation logic; they belong in C# (`src/agent/`), reading/writing a generic blackboard. GDScript drives only UI + scene glue.

LimboAI is **not currently a dependency** — `addons/` holds only gdUnit4. Adopting it means vendoring a native **GDExtension** (C++), ABI-matched to the Godot 4.6.3 .NET build, and committed as a binary blob.

The decision (per `docs/m4-plan.md` P0): adopt LimboAI **iff** it cleanly drives C# tasks against a generic blackboard and stays agent-type-blind and deterministic-under-test; otherwise build a small custom C# runtime we fully control. This must be settled before P1.

## Decision

Build a **small, pure-C# behaviour-tree runtime** in `src/agent/`, not LimboAI.

The runtime is a few hundred lines: a `BtStatus { Running, Success, Failure }` enum; an `IBtTask` with one method `BtStatus Tick(Blackboard bb, double dt)`; the standard composites (`Sequence`, `Selector`, `Parallel`); decorators (`Inverter`, `Repeater`, `Cooldown`, `Succeeder`); leaves (`ActionTask`, `ConditionTask`); a generic typed `Blackboard`; and a `BehaviorTree` root that ticks from a seeded `AgentState`. Trees are assembled in C# (a fluent builder). It has **zero Godot dependency**, so it ticks and unit-tests headlessly and is deterministic by construction — the only time source is the `dt` passed in, the only randomness is the seeded RNG threaded through the blackboard/state.

## Consequences

- **Positive:**
  - **Deterministic by construction** — we own every line of the tick; nothing schedules behind our back, so invariant 4 (and the fixed-seed reproducibility test) holds trivially.
  - **Headless-testable** — tasks are plain C# touching only the blackboard; no Godot runtime, no GDExtension load, no editor needed to unit-test a behaviour. This keeps the whole sim inside the existing GdUnit4 C# harness.
  - **Invariant-aligned** — the BT *is* the simulation control flow, so keeping it in C# honours invariant 5; tasks read only the generic blackboard, so it stays agent-type-blind (invariant 3) and is reused verbatim for later agent stages.
  - **No native dependency** — nothing to vendor, ABI-match to each Godot build, or keep in sync; one less binary in a text-committed repo (invariant 8).
- **Negative:**
  - We write and maintain ~300–500 LOC + tests instead of pulling an addon. Bounded and well-understood, but it is ours to own.
  - **No built-in visual tree editor / debugger.** Trees are defined in code, not a graph UI.
- **Risks:** re-implementing a known pattern subtly wrong (composite return semantics, parallel policies). Mitigated by TDD against the canonical BT semantics (each composite/decorator has unit tests for its Running/Success/Failure transitions) before any agent uses it.
- **Reversal cost:** **Low–Medium.** `IBtTask` is the only seam agents depend on; swapping the engine behind it (e.g. adopting LimboAI later, or wrapping it) is an internal change, not a data migration. Trees authored in C# would need re-expressing if we ever moved to a resource-authored tree.

## Alternatives considered

- **LimboAI (GDExtension addon).** Rejected for the *runtime*. Its idiomatic path authors trees as editor resources and `BTTask` subclasses extend native Godot types, so unit-testing a task needs the Godot runtime + the extension loaded — heavier and more brittle than pure C#, and the native tick scheduling is not under our control, which weakens the determinism guarantee invariant 4 makes load-bearing. Its real strengths (visual editor, maturity, bundled HSM) don't outweigh routing a deterministic, headless-unit-tested simulation through an editor-centric native extension. **Kept in reserve as an optional editor-side *visualisation/debug* aid** layered over our runtime if we ever want a graph view — that use does not own the tick and so does not threaten determinism.
- **A general-purpose C# BT NuGet package (e.g. a third-party library).** Rejected — adds an external dependency for a pattern small enough to own outright, with no guarantee of the determinism/no-clock discipline this project requires.
- **Hard-coded state machines per species (no shared runtime).** Rejected — violates invariant 3 (one agent-type-blind runtime) and does not scale to the later tribe/civ/empire stages that must reuse it.

## References

- `docs/m4-plan.md` §P0, §"Architectural contract", and the Open questions BT-runtime entry
- PRD §6 (invariants 3, 4, 5), §7 M4, §10 (LimboAI mention)
- `docs/m4-contract.md` (the `IBtTask` / `Blackboard` / `BtStatus` seam this ADR fixes)
- LimboAI — https://github.com/limbonaut/limboai (MIT)
