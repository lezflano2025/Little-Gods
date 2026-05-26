using Godot;

namespace LittleGods.World;

/// A flat ground plane at a constant height (default 0). The M3 locomotion path
/// behaves exactly as before against this (or against a null sampler). Used as
/// the default ground and as a test fixture.
public sealed class FlatGround : IGroundSampler
{
    private readonly float _height;

    public FlatGround(float height = 0f) => _height = height;

    /// The world ground plane at y = 0.
    public static FlatGround Zero { get; } = new(0f);

    public float HeightAt(float x, float z) => _height;

    public Vector3 NormalAt(float x, float z) => Vector3.Up;
}
