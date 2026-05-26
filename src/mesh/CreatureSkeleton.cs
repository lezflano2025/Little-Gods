using Godot;

namespace LittleGods.Mesh;

/// The resolved skeleton of one creature: an ordered array of world-space
/// bones. Bone 0 is always the root (the spine part placed at origin); each
/// subsequent bone corresponds to Recipe.Attachments[i] at index i + 1.
///
/// Produced by SkeletonResolver. Consumed by MetaballField (M2 P1 B) and
/// AutoSkinner (M2 P1 C).
public sealed class CreatureSkeleton
{
    public Bone[] Bones { get; }

    /// AABB enclosing every bone segment grown by its radius. Empty skeletons
    /// report a zero-size box at the origin.
    public Aabb Bounds { get; }

    public CreatureSkeleton(Bone[] bones)
    {
        Bones = bones ?? System.Array.Empty<Bone>();
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
