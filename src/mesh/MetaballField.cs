using System;
using Godot;

namespace LittleGods.Mesh;

/// Implements IScalarField as a sum of per-bone Wyvill smooth-falloff kernels.
///
/// Kernel (Wyvill et al., "Data Structures for Soft Objects", 1986):
///   contribution(d, r) = (1 - (d/R)^2)^3   when d < R, else 0
///   where R = r / 0.454f  (support radius ≈ 2.2 * r)
///
/// At d == r:  (1 - (r/R)^2)^3 = (1 - 0.454^2)^3 ≈ 0.5
/// So the iso = 0.5 contour of a lone bone sits exactly at its RadiusAt(p).
///
/// Multiple bones sum their contributions; overlapping regions exceed the
/// single-bone value and fuse into a continuous skin.
///
/// Pure and deterministic: no RNG, no clock, same input → same output.
public sealed class MetaballField : IScalarField
{
    // Ratio of true bone radius to kernel support radius.
    // Chosen so that (1 - k^2)^3 == 0.5 at d == r (i.e., d/R == k == 0.454).
    private const float K = 0.454f;

    private readonly CreatureSkeleton _skeleton;
    private readonly float _isoLevel;

    /// Axis-aligned bounds that contain every point where Sample > isoLevel.
    /// Computed once in the constructor from each bone's segment endpoints
    /// grown by the kernel support radius R_max = MaxRadius / K.
    public Aabb Bounds { get; }

    /// <param name="skeleton">The creature skeleton to evaluate.</param>
    /// <param name="isoLevel">
    ///   Iso-level for surface extraction. Stored for reference; the kernel is
    ///   calibrated for isoLevel == 0.5f. Non-default values shift the surface
    ///   inward/outward but do not change the kernel or the bounds computation.
    /// </param>
    public MetaballField(CreatureSkeleton skeleton, float isoLevel = 0.5f)
    {
        _skeleton = skeleton ?? throw new System.ArgumentNullException(nameof(skeleton));
        _isoLevel = isoLevel;
        Bounds = ComputeBounds(skeleton);
    }

    /// Evaluates the metaball field at world-space point p.
    /// Returns the sum of Wyvill contributions from every bone.
    /// Returns 0 for empty skeletons or points beyond all support radii.
    public float Sample(Vector3 p)
    {
        float sum = 0f;

        foreach (ref readonly var bone in _skeleton.Bones.AsSpan())
        {
            float r = bone.RadiusAt(p);
            if (r <= 0f)
            {
                continue;
            }

            float R = r / K;  // kernel support radius
            float d = bone.DistanceTo(p);

            if (d >= R)
            {
                continue;
            }

            float t = d / R;
            float t2 = t * t;
            float inner = 1f - t2;
            sum += inner * inner * inner;  // (1 - (d/R)^2)^3
        }

        return sum;
    }

    // ---------------------------------------------------------------------------
    // Bounds computation
    // ---------------------------------------------------------------------------

    private static Aabb ComputeBounds(CreatureSkeleton skeleton)
    {
        if (skeleton.Count == 0)
        {
            return new Aabb(Vector3.Zero, Vector3.Zero);
        }

        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        foreach (ref readonly var bone in skeleton.Bones.AsSpan())
        {
            // Support radius for the largest radius on this bone.
            // Covers the worst-case extent; the actual support narrows toward the
            // smaller end, so this is always conservative (never clips the surface).
            float Rmax = bone.MaxRadius / K;

            ExpandPoint(ref min, ref max, bone.Head, Rmax);
            ExpandPoint(ref min, ref max, bone.Tail, Rmax);
        }

        return new Aabb(min, max - min);
    }

    private static void ExpandPoint(ref Vector3 min, ref Vector3 max, Vector3 center, float radius)
    {
        min.X = Mathf.Min(min.X, center.X - radius);
        min.Y = Mathf.Min(min.Y, center.Y - radius);
        min.Z = Mathf.Min(min.Z, center.Z - radius);
        max.X = Mathf.Max(max.X, center.X + radius);
        max.Y = Mathf.Max(max.Y, center.Y + radius);
        max.Z = Mathf.Max(max.Z, center.Z + radius);
    }
}
