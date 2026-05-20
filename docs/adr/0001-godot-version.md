# ADR-0001: Godot 4.6.3-stable instead of 4.3.x

- **Status:** Accepted
- **Date:** 2026-05-20
- **Deciders:** Lee Flanagan
- **Milestone:** M0

## Context

PRD §5.1 pins the engine to `Godot 4.3.x` and explicitly says "do not chase 4.4/4.5 without an ADR." At project kickoff (today, 2026-05-20), Godot **4.6.3-stable** was released the same day; 4.3.x is now two minor versions behind and outside the active maintenance window.

The PRD's intent in pinning was to avoid mid-development engine churn — not to lock onto a specific number for its own sake. Starting on the newest stable instead of the oldest LTS-ish track minimizes the chance of a forced upgrade later.

## Decision

Pin to **Godot 4.6.3-stable** (.NET edition) for the lifetime of v1.0. Same intent as the PRD's original pin: do not upgrade to 4.7+ without superseding this ADR.

## Consequences

- **Positive:**
  - Latest renderer, physics, and C# binding improvements available immediately.
  - Two fewer minor-version migrations expected before EA launch.
  - Snapshot-deterministic golden images won't need re-baselining mid-project.
- **Negative:**
  - Claude Code's training data on 4.6 is thinner than on 4.3. Mitigation: prefer well-trodden patterns, verify Claude's suggestions against current Godot 4.6 docs.
  - Some community MCP servers and tutorials assume earlier Godot versions. Mitigation: smoke-test each MCP install on day 1.
- **Risks:**
  - 4.6.3-stable was released the same day this ADR was written. Risk of regressions discovered in the field. Mitigation: pin to **4.6.3 exactly**, not "latest 4.6.x"; treat any upgrade as an ADR-worthy decision.
- **Reversal cost:** Low at M0 (no code yet). Medium by M2 (mesh code may depend on rendering APIs). High by M3+ (snapshot baselines invalidated).

## Alternatives considered

- **Honor PRD pin to 4.3.x.** Rejected — would start the project two minor versions behind and almost certainly require a forced upgrade before EA launch.
- **Use 4.5.x (last 4.5 patch).** Rejected — same churn risk; if upgrading off 4.3, may as well go to 4.6.
- **Track latest stable (rolling).** Rejected — violates the PRD §5.1 no-chasing spirit. Pinning to a specific patch is what the PRD actually asked for; only the version number changes.

## References

- PRD §5.1 (Stack)
- PRD §0 (Technical Decisions are locked; changes via ADR)
- https://github.com/godotengine/godot/releases/tag/4.6.3-stable
