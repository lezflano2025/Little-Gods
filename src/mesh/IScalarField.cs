using Godot;

namespace LittleGods.Mesh;

/// A scalar field f: R^3 -> R sampled by the mesher. The marching-cubes
/// implementation (M2 P1, agent A) consumes this; the metaball field
/// (M2 P1, agent B) implements it. This interface is the seam between them.
///
/// Implementations MUST be pure and deterministic: Sample(p) returns the same
/// value for the same p, with no RNG and no clock (PRD invariant 4).
public interface IScalarField
{
    /// Field value at world-space point p. By convention the iso-surface is
    /// extracted where Sample(p) == isoLevel for a chosen isoLevel; the field
    /// is higher inside the surface and falls off to ~0 far away.
    float Sample(Vector3 p);

    /// Tight-ish axis-aligned bound containing all points where the field is
    /// meaningfully above zero. Marching cubes only voxelises this volume, so
    /// a loose bound costs performance and a too-tight bound clips the mesh.
    Aabb Bounds { get; }
}
