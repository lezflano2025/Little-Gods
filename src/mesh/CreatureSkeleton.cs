using Godot;
using LittleGods.Anim;

namespace LittleGods.Mesh;

/// The resolved skeleton of one creature: an ordered array of world-space
/// bones. Bone 0 is always the root (the spine part placed at origin).
///
/// Bones are NOT 1:1 with attachments (ADR-0003): a non-limb part contributes
/// one bone, a PartKind.Limb part contributes a two-bone chain (upper + lower
/// joined at a knee). Use <see cref="LimbChains"/> and each bone's ParentIndex
/// rather than assuming "bone = attachment + 1". Bones are in topological order
/// (every bone's parent precedes it), so a single forward pass can accumulate
/// global transforms.
///
/// Produced by SkeletonResolver. Consumed by MetaballField + AutoSkinner (M2)
/// and the animation layer (M3) via <see cref="LimbChains"/>.
public sealed class CreatureSkeleton
{
    public Bone[] Bones { get; }

    /// The two-bone limb chains recorded during resolution (ADR-0003), in
    /// attachment order. Empty when the creature has no PartKind.Limb parts.
    /// The animation layer (IK / classifier / gait / foot planner) drives the
    /// skeleton through these rather than scanning bones.
    public LimbChain[] LimbChains { get; }

    /// AABB enclosing every bone segment grown by its radius. Empty skeletons
    /// report a zero-size box at the origin.
    public Aabb Bounds { get; }

    public CreatureSkeleton(Bone[] bones, LimbChain[]? limbChains = null)
    {
        Bones = bones ?? System.Array.Empty<Bone>();
        LimbChains = limbChains ?? System.Array.Empty<LimbChain>();
        Bounds = ComputeBounds(Bones);
    }

    public int Count => Bones.Length;

    private static Aabb ComputeBounds(Bone[] bones)
    {
        if (bones.Length == 0)
        {
            return new Aabb(Vector3.Zero, Vector3.Zero);
        }

        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        foreach (var b in bones)
        {
            Expand(ref min, ref max, b.Head, b.RadiusHead);
            Expand(ref min, ref max, b.Tail, b.RadiusTail);
        }

        return new Aabb(min, max - min);
    }

    private static void Expand(ref Vector3 min, ref Vector3 max, Vector3 center, float radius)
    {
        min.X = Mathf.Min(min.X, center.X - radius);
        min.Y = Mathf.Min(min.Y, center.Y - radius);
        min.Z = Mathf.Min(min.Z, center.Z - radius);
        max.X = Mathf.Max(max.X, center.X + radius);
        max.Y = Mathf.Max(max.Y, center.Y + radius);
        max.Z = Mathf.Max(max.Z, center.Z + radius);
    }
}
