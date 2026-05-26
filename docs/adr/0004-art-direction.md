# ADR-0004: Art direction — Spore-style stylized cartoon

- **Status:** Accepted
- **Date:** 2026-05-26
- **Deciders:** Lee Flanagan
- **Milestone:** M2 (resolves the PRD §10 open question "decide by M2")

## Context

PRD §10 left the visual direction open: "Spore-style stylized cartoon, or semi-realistic No-Man's-Sky-style? *Decide by M2.*" M2 produced a live 3D metaball creature rendered with a neutral clay material, which is the right artifact to decide against. The metaball geometry already yields soft, rounded, Spore-like forms.

## Decision

**Spore-style stylized cartoon.** Bright, saturated, smooth-shaded creatures with soft rim light and bold per-region paint — not semi-realistic PBR.

Concretely:
- Stylized, saturated albedo (per-creature paint via `Recipe.Paint` / `Morph.PaintTint`; a friendly saturated default before paint is set).
- Low-to-mid roughness, zero metallic; soft **rim light** for the rounded cartoon read.
- Forms stay rounded (the metaball skin already does this — no hard-surface detailing).
- A first-pass cartoon material ships on `CreaturePreview` now (stylized `StandardMaterial3D`). A fuller cel/toon shader with outline is a later refinement, tracked but not blocking.

## Consequences

- **Positive:** material-layer only — no impact on the M2 mesh pipeline or M3 locomotion (both are material-agnostic). Plays to the metaball aesthetic's strengths. Cheap to render (stylized PBR, not heavy realism).
- **Negative / deferred:** a true cel shader (banded lighting + silhouette outline) and the paint-region UI are future work; the first pass is a stylized `StandardMaterial3D`, not a custom toon shader.
- **Reversal cost:** Low. Pure material/shader layer; no geometry or data changes.

## Alternatives considered

- **Semi-realistic (No-Man's-Sky-style) PBR.** Rejected — fights the soft metaball forms, costs more to author and render, and is a poorer fit for a creature-creator where readability and charm beat realism.

## References

- PRD §10 (open question, now resolved)
- `src/mesh/CreaturePreview.cs` (preview material)
- ADR-0002 / ADR-0003 (bone + limb model the skin sits on)
