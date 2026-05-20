# Product Requirements Document

**Project:** Spore Spiritual Successor (working title)
**Owner:** Lee Flanagan
**Status:** Draft v1 — pre-prototype
**Last updated:** 2026-05-20

---

## 0. How to use this document

This PRD is the ground-truth context for Claude Code on this project. It supersedes any prior conversation. When Claude Code is asked to work on this project, it should:

1. Re-read this PRD if it's been more than one session since last read.
2. Treat the "Technical Decisions" and "Architecture Invariants" sections as locked. Propose changes via written ADR (architecture decision record) in `docs/adr/`, not in passing.
3. Refuse to implement work that violates an invariant. Surface the conflict; don't silently work around it.
4. Reference the relevant Milestone (M0–M5) on every non-trivial PR/commit message.
5. Update the "Open Questions" section when a decision gets made; remove items when resolved.

Linked documents (create when first needed):
- `docs/architecture.md` — system architecture, module boundaries
- `docs/creature-data-model.md` — Rigblock format, recipe serialization
- `docs/animation-pipeline.md` — metaball skin, auto-skin, IK, gait
- `docs/adr/` — architecture decision records
- `CLAUDE.md` — Claude Code project rules (linked from this PRD)

---

## 1. Vision

A spiritual successor to Spore (2008) built as a playable prototype with a clean path to a serious project. The lead feature is a procedural creature editor with retargeted procedural animation that lets players design arbitrary creature morphologies and watch them move convincingly. The game extends into multi-stage gameplay (creature → ... → space) across multiple shipped releases, but ships value at every release.

The product passes or fails on a single test: a 6-legged, 2-armed creature with a tail, designed by a player in 60 seconds, walks across the screen and the player says "that's mine."

---

## 2. Experience Pillars

1. **"That's my creature."** Ownership lands the moment a player-made creature moves. Animation must be plausible for arbitrary morphologies — the Chris Hecker test (2 arms, 7 legs, 2 mouths) is a real benchmark, not a metaphor.
2. **Creator-first.** The editor is the lead product. If the editor doesn't delight, no stage matters.
3. **Show, don't simulate.** Choose visible fun over invisible depth at every fork. This is the explicit anti-Thrive axiom.
4. **Ship-ready at every milestone.** Each phase ends with a playable, shareable build, even if internal-only.

---

## 3. Scope

### In scope for v1.0 (prototype-to-Early-Access, ~12 months)

- Procedural creature editor (Rigblock-style part system)
- Metaball skin generation with auto-skinning
- Procedural animation (heuristic IK + gait phases, no ML)
- Single creature stage: one biome, predator/prey/mate loop, evolve via editor between lives
- Local creature persistence (`.tres` resources)
- Cloud creature sharing via Supabase
- Steam Early Access launch
- Desktop only: Windows + Linux. Mac best-effort.

### Out of scope for v1.0 (deferred, prioritised)

1. Space stage (most likely v2)
2. Multi-biome creature stage / deeper ecosystem
3. PNG-steganography sharing (the cute Spore-style export)
4. Tribal stage
5. Civilization stage
6. Cell/microbe stage
7. Real-time multiplayer
8. Mobile, console
9. Localization (English only at v1.0)

### Explicit non-goals (will not build, ever, in this product)

- Scientifically accurate biology. This is a game, not a simulator. The Thrive failure mode is taken seriously.
- Custom engine. Godot only.
- ML-based animation in v1. Heuristic IK is sufficient and is what Spore actually shipped.
- Procedural narrative / AI-written quests.
- NFTs, blockchain, generative-AI-of-the-week features.

---

## 4. Target user

- Player who loved Spore's creature creator and was disappointed by everything around it.
- Spends >30 min in editors before playing.
- Shares creations on social media.
- Comfortable with Early Access; tolerates rough edges; cares about creator agency.

Out of scope as target users: hardcore strategy/RTS players (wait for civ stage), simulation purists (this is not Thrive), mobile-first gamers.

---

## 5. Technical Decisions (locked)

### 5.1 Stack

- **Engine:** Godot 4.3.x (pinned to a single minor; do not chase 4.4/4.5 without an ADR).
- **Languages:**
  - **C#** for procedural math: metaball mesh generation, marching cubes, IK solver, gait controller, planet LOD (future), agent simulation hot paths.
  - **GDScript** for UI, signal wiring, scene glue, editor tooling.
- **Backend:** Supabase (Postgres + Auth + Storage). Creature recipes stored as JSON; user accounts; gallery metadata.
- **Asset authoring:** Blender for Rigblock parts. No other DCC tools.
- **Source control:** Git, GitHub. Trunk-based with feature branches; squash merges.
- **CI:** GitHub Actions running headless Godot tests on push to main and PR.
- **Distribution:** Steam (primary). itch.io (secondary, free demo builds).
- **Telemetry:** Minimal — opt-in crash reports + opt-in anonymous gameplay events. No third-party SDK; write events to Supabase directly.

### 5.2 Claude Code workflow

- **Primary IDE:** Claude Code via terminal. Godot editor used only for visual checks and final scene authoring.
- **MCP servers (install day 1):**
  - `godot-mcp` (start with tomyud1's; fall back to Coding-Solo's if instability)
  - Blender MCP (BlenderMCP)
  - Supabase MCP (official Anthropic)
- **Skills:**
  - Godot Games skill (Anthropic-ecosystem)
  - Randroids-Dojo Godot-Claude-Skills marketplace plugin
- **Subagents (define in `.claude/agents/`):**
  - `creature-editor-dev` — owns editor UI + data model
  - `procedural-mesh-dev` — owns metaball mesher, auto-skinning
  - `animation-dev` — owns IK, gait, retargeting
  - `simulation-dev` — owns agent runtime, behavior trees
  - `playtest-runner` — launches headless builds, captures screenshots, runs visual diffs
- **Hooks:** enforce C# formatting (csharpier), GDScript formatting (gdformat), and "do not commit if `headless_test.sh` fails."
- **CLAUDE.md** stays under 200 lines, links out to detailed docs by filename.

### 5.3 Testing

Three layers, all required:

1. **Unit tests** (GdUnit4) for pure C# code — metaball math, IK math, planet quadtree, recipe serialization. Target >70% line coverage on these modules.
2. **Headless scene tests** — launch a `.tscn` headless, assert on node properties after N frames. Used for creature-load round-trips and editor invariants.
3. **Visual snapshot tests** — render deterministic scenes (fixed seed, fixed camera) and diff PNG output against committed golden images. Used for creature appearance regressions.

CI runs all three on every PR. PRs are not merged with red CI.

---

## 6. Architecture Invariants

These are locked. Violation requires an ADR.

1. **Creatures serialize as recipes, never as baked meshes.** A recipe is an ordered list of part IDs + per-part transforms/morphs + paint params + metadata. Recipe must fit in **<10 KB** uncompressed.
2. **One Rigblock framework covers everything.** Creature parts, tools, clothing, vehicles, buildings, ships — all use the same Part/Attachment/Morph abstraction. Different libraries, same code.
3. **One Behavior Tree runtime for all agents** — individual creatures, tribes, civs, empires. The runtime is agent-type-blind.
4. **All procedural systems are deterministic given a seed.** No `DateTime.Now`, no unsynchronized RNG. Every generator takes a seed; tests assume it.
5. **C# does math; GDScript does UI.** Don't put physics or mesh generation in GDScript. Don't put UI logic in C#.
6. **No singletons except Godot autoloads, and only for genuine cross-cutting services** (audio, save system, telemetry). Per-feature state lives in the scene tree.
7. **No third-party paid Asset Store dependencies for v1.** Open source only. Reduces lock-in risk and licence headaches.
8. **`.tscn` and `.tres` files are committed as text and reviewed in PRs.** No binary scene formats.

---

## 7. Milestones

### M0 — Project skeleton + Claude Code harness (Weeks 1–4)

**Deliverables:**
- Repo created, folder structure per `docs/architecture.md`, `CLAUDE.md` written
- Godot 4.3.x installed, project initialized, C# + GDScript both compile
- `godot-mcp` MCP server wired to Claude Code, verified
- Blender MCP installed and verified
- Headless build script (`tools/headless_test.sh`) runs and exits 0
- Visual snapshot harness produces deterministic screenshots
- One end-to-end loop verified: Claude Code edits a `.tscn`, runs headless, takes a screenshot, self-corrects

**Acceptance:** Claude Code can complete the "make a cube fall and screenshot it" task with zero manual editor intervention.

### M1 — Editor data model (Weeks 5–8)

**Deliverables:**
- `Part`, `Attachment`, `Morph` C# resources defined
- 2D top-down "blueprint" editor: drag parts onto a spine, snap to attachment points, save/load
- Initial Rigblock library: 1 spine type, 4 limb types, 2 head types, 2 mouth types (~10 parts total)
- Symmetry toggle in the editor
- Save → close → reopen → identical creature (lossless round-trip test, automated)

**Acceptance:** A creature designed in the 2D editor saves, reloads identically, and the recipe is <10 KB.

### M2 — Metaball skin generation (Weeks 9–14)

**Deliverables:**
- Marching cubes implementation in C# (port from `dario-zubovic/metaballs` or equivalent; Claude Code writes tests)
- Metaball field generation from Part spheres-along-bones
- Mesh regenerates in <50ms for a typical creature on target hardware (4-core CPU)
- Auto-skinning: vertex weights = inverse distance to 4 nearest bones, normalized
- 3D preview replaces 2D blueprint editor

**Acceptance:** Player drags parts around in 3D; mesh updates in real time at 60 FPS; saved creature reloads with identical mesh (within float tolerance).

### M3 — Procedural animation (Weeks 15–22) — **the critical milestone**

**Deliverables:**
- Two-bone analytic IK solver (C#, tested)
- Limb-type classification from topology (leg / arm / wing / tail)
- Gait controller with phase offsets per limb
- Foot-target IK against ground plane
- Secondary motion (jiggle) layer
- Body bobbing/balance driven by gait phase

**Acceptance — the Hecker test:** creatures with 2, 4, 6, and 8 legs all walk plausibly without visible IK breaks across 60 seconds of locomotion. This is the gate. If it fails, the project pauses and reassesses before M4.

### M4 — Creature stage MVP (Weeks 23–35)

**Deliverables:**
- Single open biome (procedural terrain, ~1 km²)
- 3–5 NPC species, each procedurally generated from the editor's data model with random seeds
- Behavior trees: idle, wander, hunt, flee, mate (using LimboAI Godot addon)
- Eat/mate/fight loop with consequences (HP, hunger, lifecycle)
- "Death → respawn with mutation slot" loop: player edits creature between lives
- Day/night cycle
- Minimal HUD (HP, hunger, current "DNA" budget for editor)

**Acceptance:** A new player can play 30 minutes without crashes, design 3 creature iterations, and reach a "this is fun" moment as measured by playtesters.

### M5 — Polish + sharing + Early Access launch (Weeks 36–48)

**Deliverables:**
- Supabase backend for creature gallery
- In-game gallery: browse + download other players' creatures
- Steam page live with trailer + screenshots
- Crash reporting + telemetry
- Settings menu (graphics, audio, controls)
- Tutorial / first-time user experience
- Basic audio (procedural creature sounds via formant synth; ambient music)
- Steam Early Access launch

**Acceptance:** 1,000+ Steam wishlists at launch; <5% crash rate in first week; positive sentiment in first 50 reviews.

---

## 8. Success Criteria (measurable)

### Technical
- Editor: 60 FPS while dragging parts on target hardware (4-core CPU, mid-range GPU)
- Mesh regeneration: <50ms p95
- Save/load round-trips: 100% lossless (automated test enforces)
- Recipe size: <10 KB p95
- Crash rate at EA launch: <5% of sessions

### Product
- 1,000+ Steam wishlists at EA launch
- 70%+ positive reviews after 50 reviews
- Median session length: >30 minutes
- 30%+ of players create >3 creatures (instrumented via opt-in telemetry)

### Personal / process
- Ships M0–M5 within 12 months (±2 months tolerance)
- No engine migrations (this is the explicit anti-Thrive metric)
- No more than 2 architecture ADRs reversed

---

## 9. Risk Register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| M3 procedural animation doesn't generalize to extreme morphologies | Medium | Project-killing | Vertical slice early; M3 is gated. If failed, descope to "fixed gait library per limb-count class" |
| Scope creep into Thrive territory | High | Severe delay | Hard gate at each milestone; explicit non-goals list above |
| Godot 4.x breaking change in a point release | Low | Medium | Pin Godot version; only upgrade with ADR + test pass |
| Solo dev burnout | Medium | Severe | Visible shippable progress every 4 weeks; share builds publicly for motivation |
| Claude Code training data gaps on Godot 4.3+ features | Medium | Low/Medium | Prefer well-trodden patterns; verify Claude's Godot suggestions against current docs |
| Supabase pricing if gallery scales | Low | Low | Free tier covers <50k users; revisit at that scale |
| Steam EA launch underperforms (<500 wishlists) | Medium | Medium | itch.io fallback; pivot to free demo + Patreon if needed |

---

## 10. Open Questions (resolve before relevant milestone)

- **Art direction:** Spore-style stylized cartoon, or semi-realistic No-Man's-Sky-style? *Decide by M2.*
- **Music direction:** procedural ambient (Brian Eno-ish) vs commissioned soundtrack? *Decide by M5.*
- **Monetization post-EA:** flat one-time price vs paid expansions per stage? *Decide before EA launch.*
- **PNG-steganography sharing:** worth the effort for v1.0 or defer? *Decide at M5 planning.*
- **Mac support:** best-effort or dropped entirely? *Decide by M4.*
- **Animation v2:** does the heuristic IK survive playtester contact, or is ML-based retargeting needed for v1.5? *Defer decision until post-EA data.*

---

## 11. Inspirations + references (read these)

- Chris Hecker, "How To Animate a Character You've Never Seen Before" (GDC 2007)
- Chris Hecker, "Real-time Motion Retargeting to Highly Varied User-Created Morphologies" (SIGGRAPH 2008)
- Chris Hecker's "Liner Notes for Spore" — full system documentation
- Choy, Ingram, Quigley, Sharp, Willmott, "Rigblocks" (SIGGRAPH 2007 sketch)
- Sebastian Lague, "Coding Adventure: Procedural Planets" series (for eventual space stage)
- Thrive source code (GPLv3 — read, don't copy)

---

## 12. What this PRD is not

- Not a design document. Game design details live elsewhere.
- Not a content plan. Specific part libraries, biomes, creatures are detailed in `docs/content-plan.md` (to be created at M1).
- Not a marketing plan. Steam page, trailers, social presence are tracked separately.
- Not immutable. Open Questions get resolved; Locked decisions get reversed only via ADR; Milestones can slip but their definition of done cannot.
