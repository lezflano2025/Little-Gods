using Godot;

namespace LittleGods.Mesh;

/// A single skeletal bone: a world-space line segment with a radius at each
/// end and an index to its parent bone (-1 for the root). One bone is produced
/// per placed part (see SkeletonResolver). The metaball field grows spheres
/// along the segment; the auto-skinner weights vertices by distance to it.
///
/// Immutable value type (PRD coding-style: no mutation).
public readonly struct Bone
{
    public readonly Vector3 Head;
    public readonly Vector3 Tail;
    public readonly float RadiusHead;
    public readonly float RadiusTail;

    /// Index into the owning CreatureSkeleton.Bones, or -1 for the root.
    public readonly int ParentIndex;

    public Bone(Vector3 head, Vector3 tail, float radiusHead, float radiusTail, int parentIndex)
    {
        Head = head;
        Tail = tail;
        RadiusHead = radiusHead;
        RadiusTail = radiusTail;
        ParentIndex = parentIndex;
    }

    public float Length => Head.DistanceTo(Tail);

    /// Parametric closest point on the segment to p, as t in [0, 1] from Head
    /// to Tail. Degenerate (zero-length) bones clamp to Head (t = 0).
    public float ClosestT(Vector3 p)
    {
        Vector3 ab = Tail - Head;
        float lenSq = ab.LengthSquared();
        if (lenSq <= 1e-12f)
        {
            return 0f;
        }
        return Mathf.Clamp((p - Head).Dot(ab) / lenSq, 0f, 1f);
    }

    public Vector3 ClosestPoint(Vector3 p) => Head.Lerp(Tail, ClosestT(p));

    /// Shortest distance from p to the bone segment.
    public float DistanceTo(Vector3 p) => p.DistanceTo(ClosestPoint(p));

    /// Interpolated radius at the segment point nearest p.
    public float RadiusAt(Vector3 p) => Mathf.Lerp(RadiusHead, RadiusTail, ClosestT(p));

    /// Largest radius along the bone, used for conservative bounds padding.
    public float MaxRadius => Mathf.Max(RadiusHead, RadiusTail);
}
