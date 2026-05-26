/// <summary>
/// M3 P3 — see docs/m3-contract.md
/// Pure, deterministic gait controller: phase-offset presets, per-leg phase
/// computation, and stance detection. No clock reads, no RNG.
/// </summary>

using System;

namespace LittleGods.Anim;

public static class GaitController
{
    /// <summary>
    /// Returns a preset <see cref="Gait"/> for the given number of load-bearing legs.
    /// PhaseOffsets.Length == legCount (or 0 when legCount &lt;= 0).
    /// DutyFactor: 0.5 for hexapod, 0.6 for all others.
    /// </summary>
    public static Gait ForLegCount(int legCount, float cadenceHz)
    {
        switch (legCount)
        {
            case 2:
                return new Gait(cadenceHz, 0.6f, new float[] { 0f, 0.5f });

            case 4:
                return new Gait(cadenceHz, 0.6f, new float[] { 0f, 0.5f, 0.5f, 0f });

            case 6:
                return new Gait(cadenceHz, 0.5f,
                    new float[] { 0f, 0.5f, 0f, 0.5f, 0f, 0.5f });

            case 8:
                return new Gait(cadenceHz, 0.6f,
                    new float[] { 0f, 0.25f, 0.5f, 0.75f, 0f, 0.25f, 0.5f, 0.75f });

            default:
                if (legCount <= 0)
                    return new Gait(cadenceHz, 0.6f, Array.Empty<float>());

                var offsets = new float[legCount];
                for (int k = 0; k < legCount; k++)
                    offsets[k] = k / (float)legCount;
                return new Gait(cadenceHz, 0.6f, offsets);
        }
    }

    /// <summary>
    /// Cycle phase in [0, 1) for leg <paramref name="leg"/> at elapsed time
    /// <paramref name="seconds"/>.
    /// Returns 0f when the leg index is out of range.
    /// </summary>
    public static float PhaseOf(in Gait gait, int leg, double seconds)
    {
        if (leg < 0 || leg >= gait.PhaseOffsets.Length)
            return 0f;

        double phase = seconds * gait.CadenceHz + gait.PhaseOffsets[leg];
        float f = (float)(phase - Math.Floor(phase));
        return f;
    }

    /// <summary>
    /// True when leg <paramref name="leg"/> is in stance (foot planted) at time
    /// <paramref name="seconds"/>: phase &lt; DutyFactor.
    /// </summary>
    public static bool IsStance(in Gait gait, int leg, double seconds)
        => PhaseOf(gait, leg, seconds) < gait.DutyFactor;
}
