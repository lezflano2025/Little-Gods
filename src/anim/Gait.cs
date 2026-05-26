using System;

namespace LittleGods.Anim;

/// Locomotion gait parameters (M3 P3): cadence, duty factor, and a per-leg
/// phase offset. The gait controller is a pure, deterministic function of
/// (Gait, elapsed seconds) — no clock reads (PRD invariant 4).
///
/// Presets are selected by leg count:
///   biped    — alternating (0, 0.5)
///   quadruped— diagonal pairs (0, 0.5, 0.5, 0)
///   hexapod  — alternating tripods (0/0.5)
///   octopod  — metachronal wave
///
/// Immutable value type.
public readonly struct Gait
{
    /// Full stride cycles per second.
    public readonly float CadenceHz;

    /// Fraction of the cycle a leg is in stance (foot planted), in (0, 1).
    /// 0.5 ⇒ stance and swing equal; > 0.5 ⇒ more legs down at once (a more
    /// statically stable walk).
    public readonly float DutyFactor;

    /// Phase offset per leg in [0, 1), indexed parallel to the leg set the gait
    /// drives. Leg k's cycle phase = frac(t · CadenceHz + PhaseOffsets[k]).
    /// Never null; an empty array means "no legs driven".
    public readonly float[] PhaseOffsets;

    public Gait(float cadenceHz, float dutyFactor, float[] phaseOffsets)
    {
        CadenceHz = cadenceHz;
        DutyFactor = dutyFactor;
        PhaseOffsets = phaseOffsets ?? Array.Empty<float>();
    }

    /// Number of legs this gait drives.
    public int LegCount => PhaseOffsets.Length;
}
