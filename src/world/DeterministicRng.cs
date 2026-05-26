using Godot;

namespace LittleGods.World;

/// Deterministic, platform-independent pseudo-random generator (SplitMix64).
///
/// PRD invariant 4: all procedural systems are pure functions of a seed. We do
/// NOT use System.Random — its algorithm is not contractually stable across
/// runtimes, and a sim that must replay byte-identically from a seed (and be
/// unit-tested for it) needs an RNG we fully own. SplitMix64 is a single-word
/// state generator with good distribution and no allocation.
///
/// A reference type on purpose: an AgentState holds one and advances it in
/// place, so passing the agent around shares the same stream (a struct field
/// would be copied and lose its advance).
public sealed class DeterministicRng
{
    private ulong _state;

    public DeterministicRng(ulong seed) => _state = seed;

    /// Next 64 random bits (SplitMix64).
    public ulong NextULong()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public uint NextUInt() => (uint)(NextULong() >> 32);

    /// Uniform float in [0, 1) from the top 24 bits.
    public float NextFloat() => (NextULong() >> 40) * (1.0f / 16777216.0f);

    /// Uniform double in [0, 1) from the top 53 bits.
    public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

    /// Uniform float in [min, max).
    public float Range(float min, float max) => min + (max - min) * NextFloat();

    /// Uniform int in [minInclusive, maxExclusive). Empty/inverted range => min.
    public int RangeInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) return minInclusive;
        ulong span = (ulong)(maxExclusive - minInclusive);
        return minInclusive + (int)(NextULong() % span);
    }

    /// A point on the XZ unit circle (heading) — handy for wander/scatter.
    public Vector3 OnUnitCircleXz()
    {
        float a = Range(0f, Mathf.Tau);
        return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
    }

    /// Derive an independent child stream deterministically (e.g. a per-agent
    /// RNG from a master world seed). Advances this stream once.
    public DeterministicRng Fork(ulong salt) => new(NextULong() ^ salt);
}
