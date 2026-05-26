using System;

namespace LittleGods.World;

/// <summary>
/// Deterministic 2-D value noise and fractional Brownian motion (fBm).
/// </summary>
/// <remarks>
/// <para>
/// <b>Lattice value noise</b>: for each integer lattice corner (ix, iz) we
/// derive a pseudo-random float in [0, 1) using an fmix32-style integer hash
/// of (ix, iz, seed).  The four surrounding corner values are blended with
/// bilinear interpolation after applying a smootherstep fade
/// <c>t³(t(6t−15)+10)</c> to the fractional parts.
/// </para>
/// <para>
/// <b>fBm output range</b>: <see cref="Fbm"/> sums <c>octaves</c> octaves
/// whose corner values lie in [0, 1). Each octave contributes amplitude
/// <c>gain^i</c>. The un-shifted sum is in
/// [0, totalWeight) where totalWeight = Σ gain^i. After re-centring
/// (subtracting 0.5 × totalWeight) the range is
/// <c>[−totalWeight/2, +totalWeight/2)</c>.
/// For the default 4 octaves with gain 0.5 the total weight is
/// 1 + 0.5 + 0.25 + 0.125 = 1.875, so the range is
/// <b>[−0.9375, +0.9375)</b> — comfortably inside [−1, 1].
/// </para>
/// <para>No <see cref="System.Random"/>, no Godot noise classes. Pure static.</para>
/// </remarks>
public static class ValueNoise
{
    // ──────────────────────────────────────────────────────────────────────────
    // Integer hash
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// fmix32-style integer hash of lattice coordinates + seed.
    /// Mixes with large odd constants and xorshifts to achieve good avalanche.
    /// Returns a value in [0, 1) by taking the top 24 bits / 2²⁴.
    /// </summary>
    private static float HashToFloat(int ix, int iz, ulong seed)
    {
        // Fold coordinates and seed into one 32-bit word deterministically.
        uint h = (uint)ix * 0x9E3779B1u
               ^ (uint)iz * 0x85EBCA6Bu
               ^ (uint)(seed       & 0xFFFFFFFF) * 0xC2B2AE35u
               ^ (uint)(seed >> 32 & 0xFFFFFFFF) * 0x6C62272Eu;

        // fmix32 finaliser
        h ^= h >> 16;
        h *= 0x45D9F3Bu;
        h ^= h >> 16;
        h *= 0xC4CEB9FEu;
        h ^= h >> 16;

        // Top 24 bits → [0, 1)
        return (h >> 8) * (1.0f / 16777216.0f);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Smootherstep fade and bilinear interp
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Smootherstep: t³(t(6t−15)+10), maps [0,1]→[0,1] with zero 1st+2nd derivative at endpoints.</summary>
    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    /// <summary>Linear interpolation.</summary>
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // ──────────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Single-octave value noise at (<paramref name="x"/>, <paramref name="z"/>)
    /// with the given <paramref name="seed"/>. Returns a value in [0, 1).
    /// </summary>
    public static float Noise(float x, float z, ulong seed)
    {
        int ix = (int)MathF.Floor(x);
        int iz = (int)MathF.Floor(z);
        float fx = x - ix;
        float fz = z - iz;

        float u = Fade(fx);
        float v = Fade(fz);

        // Four lattice corners
        float c00 = HashToFloat(ix,     iz,     seed);
        float c10 = HashToFloat(ix + 1, iz,     seed);
        float c01 = HashToFloat(ix,     iz + 1, seed);
        float c11 = HashToFloat(ix + 1, iz + 1, seed);

        // Bilinear interpolation
        float bot = Lerp(c00, c10, u);
        float top = Lerp(c01, c11, u);
        return Lerp(bot, top, v);
    }

    /// <summary>
    /// Fractional Brownian motion: sum of <paramref name="octaves"/> noise layers
    /// with lacunarity 2.0 and gain 0.5, re-centred so the result is near 0.
    /// </summary>
    /// <remarks>
    /// Output range: [−totalWeight/2, +totalWeight/2) where
    /// totalWeight = Σ_{i=0}^{octaves−1} 0.5^i = 2(1 − 0.5^octaves).
    /// For octaves = 4: totalWeight = 1.875, range = [−0.9375, +0.9375).
    /// For octaves = 1: totalWeight = 1.0,   range = [−0.5,    +0.5).
    /// </remarks>
    public static float Fbm(float x, float z, ulong seed, int octaves)
    {
        float value = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float totalWeight = 0f;

        for (int i = 0; i < octaves; i++)
        {
            value       += Noise(x * frequency, z * frequency, seed + (ulong)i) * amplitude;
            totalWeight += amplitude;
            amplitude   *= 0.5f;
            frequency   *= 2.0f;
        }

        // Re-centre: raw sum ∈ [0, totalWeight), shift to [−totalWeight/2, +totalWeight/2)
        return value - totalWeight * 0.5f;
    }
}
