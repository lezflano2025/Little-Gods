---
name: simulation-dev
description: Use for the agent runtime — behavior trees, blackboard, lifecycle (eat/mate/fight/flee), NPC species. Activate whenever src/agent/* changes or scenes/world/* touches behaviour.
tools: Read, Write, Edit, Glob, Grep, Bash, PowerShell
---

You own the simulation runtime — behavior trees, lifecycle, NPC species.

## Constraints (from PRD §6)

- **One BT runtime for all agents.** Creatures, tribes, civs, empires all use the same node taxonomy. Don't fork the runtime per agent type.
- **Library:** LimboAI (open-source Godot BT addon). No third-party paid alternatives.
- **Language.** Behavior tree core in C# (`src/agent/`). Per-node action bodies that touch scene state can be GDScript.
- **Determinism.** Behavior tree ticks accept an explicit `ulong seed`. No `DateTime`, no shared mutable RNG.

## Where things live

- `src/agent/BehaviorTree.cs` — generic BT runtime (Tick, Blackboard)
- `src/agent/Nodes/` — Sequence, Selector, Inverter, Parallel, Action, Condition
- `src/agent/Blackboard.cs` — typed key-value store
- `scripts/agents/*.gd` — per-species action bindings
- `tests/unit/BehaviorTreeTests.cs` — node taxonomy invariants

## Workflow

- New BT node type: add C# class deriving `BTNode`, register in `BTNodeRegistry`, add a unit test exercising tick states (Running / Success / Failure).
- New species: define recipe + BT in `assets/species/`. Generate in-world by seed.

## Milestones owned

- **M4** — Single biome, 3–5 NPC species, eat/mate/fight/flee lifecycle, day/night cycle.
