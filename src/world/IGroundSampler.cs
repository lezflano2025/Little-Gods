using Godot;

namespace LittleGods.World;

/// The seam between terrain and everything that needs to sit on it (locomotion
/// foot planting, agent spawning, the body riding the ground). Pure and
/// deterministic: HeightAt(x, z) is a function of world XZ only.
///
/// Defined as an abstraction so the flat-ground path (FlatGround) keeps every
/// M3 locomotion test unchanged, while the M4 world supplies HeightmapTerrain.
public interface IGroundSampler
{
    /// Terrain height (world Y) under world position (x, z).
    float HeightAt(float x, float z);

    /// Unit surface normal at (x, z). FlatGround returns +Y.
    Vector3 NormalAt(float x, float z);
}
