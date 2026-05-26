using Godot;

namespace LittleGods.World;

/// <summary>
/// Deterministic procedural terrain that implements <see cref="IGroundSampler"/>
/// using fractional Brownian motion from <see cref="ValueNoise"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Height formula</b>: <c>HeightAt(x, z) = amplitude × fBm(x × baseFrequency, z × baseFrequency, octaves)</c>.
/// Because <see cref="ValueNoise.Fbm"/> is re-centred to [−totalWeight/2, +totalWeight/2)
/// the terrain straddles y = 0 and stays within [−amplitude, +amplitude).
/// For the default 4 octaves the fBm range is [−0.9375, +0.9375), so the
/// actual height range is [−amplitude × 0.9375, +amplitude × 0.9375) — safely
/// inside [−amplitude, +amplitude].
/// </para>
/// <para>
/// <b>Normals</b>: computed via central differences of <see cref="HeightAt"/>
/// with epsilon = 0.5 m, then normalised.
/// </para>
/// <para>Pure and deterministic: identical seed and parameters → identical output.</para>
/// </remarks>
public sealed class HeightmapTerrain : IGroundSampler
{
    private readonly ulong _seed;
    private readonly float _amplitude;
    private readonly float _baseFrequency;
    private readonly int   _octaves;
    private readonly float _sizeMeters;

    // Epsilon used for central-difference normal estimation (world units).
    private const float NormalEps = 0.5f;

    /// <summary>
    /// Creates a procedural heightmap terrain.
    /// </summary>
    /// <param name="seed">Deterministic seed (different seeds → different terrain).</param>
    /// <param name="amplitude">Peak-to-trough half-height in world units.</param>
    /// <param name="baseFrequency">Spatial frequency of the base noise layer (1/wavelength).</param>
    /// <param name="octaves">Number of fBm octave layers.</param>
    /// <param name="sizeMeters">Total side length of the playfield (used for <see cref="HalfExtent"/>).</param>
    public HeightmapTerrain(
        ulong seed,
        float amplitude     = 6f,
        float baseFrequency = 1f / 120f,
        int   octaves       = 4,
        float sizeMeters    = 1024f)
    {
        _seed          = seed;
        _amplitude     = amplitude;
        _baseFrequency = baseFrequency;
        _octaves       = octaves;
        _sizeMeters    = sizeMeters;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IGroundSampler
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Output is amplitude × fBm(...), which lies in
    /// [−amplitude × 0.9375, +amplitude × 0.9375) for the default 4 octaves.
    /// Guaranteed within [−amplitude, +amplitude] for any octave count.
    /// </remarks>
    public float HeightAt(float x, float z)
        => _amplitude * ValueNoise.Fbm(x * _baseFrequency, z * _baseFrequency, _seed, _octaves);

    /// <inheritdoc/>
    /// <remarks>
    /// Central differences with epsilon = 0.5 m, analytic normal
    /// <c>normalize(Vector3(-(hR−hL), 2ε, -(hF−hB)))</c>.
    /// Always unit length; points generally upward for reasonable terrain.
    /// </remarks>
    public Vector3 NormalAt(float x, float z)
    {
        float eps = NormalEps;
        float hL = HeightAt(x - eps, z);
        float hR = HeightAt(x + eps, z);
        float hB = HeightAt(x, z - eps);
        float hF = HeightAt(x, z + eps);

        // Surface tangents: dX = (2ε, hR−hL, 0), dZ = (0, hF−hB, 2ε)
        // Normal = dX × dZ (pointing up), simplified:
        var n = new Vector3(-(hR - hL), 2f * eps, -(hF - hB));
        return n.Normalized();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // World extent helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Half the side length of the world square (metres).</summary>
    public float HalfExtent => _sizeMeters * 0.5f;

    /// <summary>Axis-aligned bounding box of the terrain's XZ footprint.</summary>
    public Aabb Bounds => new Aabb(
        new Vector3(-HalfExtent, -_amplitude, -HalfExtent),
        new Vector3(_sizeMeters,  _amplitude * 2f, _sizeMeters));
}
