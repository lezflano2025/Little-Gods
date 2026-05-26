using Godot;
using LittleGods.Mesh;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// M2 P1: MetaballField — Wyvill kernel sum over skeleton bones.
/// All skeletons constructed in-code; no external assets required.
[TestSuite]
public class MetaballFieldTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// A single horizontal bone from (0,0,0) to (1,0,0) with radius 0.3.
    private static CreatureSkeleton SingleBone() =>
        new(new[] { new Bone(new Vector3(0, 0, 0), new Vector3(1, 0, 0), 0.3f, 0.3f, -1) });

    private const float IsoLevel = 0.5f;

    // -----------------------------------------------------------------------
    // On-bone value is well above isoLevel
    // -----------------------------------------------------------------------

    [TestCase]
    public void Sample_at_bone_midpoint_is_well_above_isoLevel()
    {
        var field = new MetaballField(SingleBone(), IsoLevel);
        // Midpoint of the segment is on the bone (d == 0).
        float val = field.Sample(new Vector3(0.5f, 0, 0));
        // At d == 0: contribution = (1 - 0)^3 = 1.0
        AssertThat(val >= IsoLevel).IsTrue();
        AssertFloat(val).IsEqualApprox(1.0f, 0.001f);
    }

    [TestCase]
    public void Sample_at_bone_head_is_well_above_isoLevel()
    {
        var field = new MetaballField(SingleBone(), IsoLevel);
        float val = field.Sample(new Vector3(0, 0, 0));
        AssertThat(val >= IsoLevel).IsTrue();
    }

    // -----------------------------------------------------------------------
    // At exactly one radius perpendicular off the bone → ≈ isoLevel
    // -----------------------------------------------------------------------

    [TestCase]
    public void Sample_at_one_radius_perpendicular_is_approx_isoLevel()
    {
        // Bone from (0,0,0) to (1,0,0), radius 0.3.
        // Point at (0.5, 0.3, 0): perpendicular distance from midpoint == 0.3 == r.
        var field = new MetaballField(SingleBone(), IsoLevel);
        float val = field.Sample(new Vector3(0.5f, 0.3f, 0));
        AssertFloat(val).IsEqualApprox(IsoLevel, 0.02f);
    }

    [TestCase]
    public void Sample_at_one_radius_in_z_direction_is_approx_isoLevel()
    {
        var field = new MetaballField(SingleBone(), IsoLevel);
        float val = field.Sample(new Vector3(0.5f, 0, 0.3f));
        AssertFloat(val).IsEqualApprox(IsoLevel, 0.02f);
    }

    // -----------------------------------------------------------------------
    // Far away → ≈ 0
    // -----------------------------------------------------------------------

    [TestCase]
    public void Sample_far_from_bone_is_approximately_zero()
    {
        // R = 0.3 / 0.454 ≈ 0.661; point at y == 5 is several R away.
        var field = new MetaballField(SingleBone(), IsoLevel);
        float val = field.Sample(new Vector3(0.5f, 5f, 0));
        AssertThat(val < 1e-4f).IsTrue();
    }

    [TestCase]
    public void Sample_far_past_tail_is_zero()
    {
        var field = new MetaballField(SingleBone(), IsoLevel);
        float val = field.Sample(new Vector3(10f, 0, 0));
        AssertThat(val < 1e-4f).IsTrue();
    }

    // -----------------------------------------------------------------------
    // Two overlapping bones fuse (summed value > single-bone value)
    // -----------------------------------------------------------------------

    [TestCase]
    public void Two_overlapping_bones_exceed_single_bone_at_midpoint()
    {
        // Bone A along X, bone B along Y, both passing near origin.
        var boneA = new Bone(new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), 0.3f, 0.3f, -1);
        var boneB = new Bone(new Vector3(0, -0.5f, 0), new Vector3(0, 0.5f, 0), 0.3f, 0.3f, -1);

        var skelOne = new CreatureSkeleton(new[] { boneA });
        var skelTwo = new CreatureSkeleton(new[] { boneA, boneB });

        var fieldOne = new MetaballField(skelOne, IsoLevel);
        var fieldTwo = new MetaballField(skelTwo, IsoLevel);

        // At the origin both bones contribute → two-bone value > one-bone value.
        var probe = new Vector3(0, 0, 0);
        float valOne = fieldOne.Sample(probe);
        float valTwo = fieldTwo.Sample(probe);

        AssertThat(valTwo > valOne).IsTrue();
    }

    [TestCase]
    public void Two_bones_fuse_above_isoLevel_at_midpoint_between_them()
    {
        // Two parallel bones side by side, close enough that their influence
        // regions overlap at the midpoint between them.
        // Bone A at y = 0, bone B at y = 0.2 (< R ≈ 0.661).
        var boneA = new Bone(new Vector3(-1, 0, 0), new Vector3(1, 0, 0), 0.3f, 0.3f, -1);
        var boneB = new Bone(new Vector3(-1, 0.2f, 0), new Vector3(1, 0.2f, 0), 0.3f, 0.3f, -1);

        var skelTwo = new CreatureSkeleton(new[] { boneA, boneB });
        var fieldTwo = new MetaballField(skelTwo, IsoLevel);

        // Midpoint between the bones, on the X centreline.
        float val = fieldTwo.Sample(new Vector3(0, 0.1f, 0));
        AssertThat(val > IsoLevel).IsTrue();
    }

    // -----------------------------------------------------------------------
    // Bounds enclosure: every above-iso point must lie inside Bounds
    // -----------------------------------------------------------------------

    [TestCase]
    public void Bounds_encloses_all_above_isoLevel_points_on_coarse_grid()
    {
        var field = new MetaballField(SingleBone(), IsoLevel);

        // Sample a region larger than Bounds in all directions.
        const int steps = 20;
        float margin = 2f;
        var lo = new Vector3(-margin, -margin, -margin);
        var hi = new Vector3(3f + margin, margin, margin);
        float dx = (hi.X - lo.X) / steps;
        float dy = (hi.Y - lo.Y) / steps;
        float dz = (hi.Z - lo.Z) / steps;

        for (int ix = 0; ix <= steps; ix++)
        for (int iy = 0; iy <= steps; iy++)
        for (int iz = 0; iz <= steps; iz++)
        {
            var p = new Vector3(
                lo.X + ix * dx,
                lo.Y + iy * dy,
                lo.Z + iz * dz);

            float val = field.Sample(p);
            if (val > IsoLevel)
            {
                AssertThat(field.Bounds.HasPoint(p)).IsTrue();
            }
        }
    }

    [TestCase]
    public void Bounds_encloses_all_above_isoLevel_points_multi_bone_skeleton()
    {
        var boneA = new Bone(new Vector3(0, 0, 0), new Vector3(2, 0, 0), 0.4f, 0.2f, -1);
        var boneB = new Bone(new Vector3(2, 0, 0), new Vector3(2, 1.5f, 0), 0.2f, 0.35f, 0);
        var skel = new CreatureSkeleton(new[] { boneA, boneB });
        var field = new MetaballField(skel, IsoLevel);

        const int steps = 20;
        float margin = 2f;
        Aabb search = field.Bounds.Grow(margin);
        Vector3 searchSize = search.Size;

        for (int ix = 0; ix <= steps; ix++)
        for (int iy = 0; iy <= steps; iy++)
        for (int iz = 0; iz <= steps; iz++)
        {
            var p = search.Position + new Vector3(
                ix * searchSize.X / steps,
                iy * searchSize.Y / steps,
                iz * searchSize.Z / steps);

            float val = field.Sample(p);
            if (val > IsoLevel)
            {
                AssertThat(field.Bounds.HasPoint(p)).IsTrue();
            }
        }
    }

    // -----------------------------------------------------------------------
    // Determinism
    // -----------------------------------------------------------------------

    [TestCase]
    public void Two_instances_on_same_skeleton_return_identical_samples()
    {
        var skel = SingleBone();
        var fieldA = new MetaballField(skel, IsoLevel);
        var fieldB = new MetaballField(skel, IsoLevel);

        var probes = new[]
        {
            new Vector3(0, 0, 0),
            new Vector3(0.5f, 0, 0),
            new Vector3(0.5f, 0.3f, 0),
            new Vector3(0.5f, 0.661f, 0),
            new Vector3(5, 5, 5),
        };

        foreach (var p in probes)
        {
            AssertFloat(fieldA.Sample(p)).IsEqualApprox(fieldB.Sample(p), 1e-6f);
        }
    }

    [TestCase]
    public void Same_instance_returns_identical_result_on_repeated_calls()
    {
        var field = new MetaballField(SingleBone(), IsoLevel);
        var probe = new Vector3(0.5f, 0.15f, 0.1f);
        float first = field.Sample(probe);
        float second = field.Sample(probe);
        AssertFloat(first).IsEqualApprox(second, 1e-6f);
    }

    // -----------------------------------------------------------------------
    // Edge cases: empty skeleton, zero-radius bone
    // -----------------------------------------------------------------------

    [TestCase]
    public void Empty_skeleton_returns_zero_everywhere()
    {
        var field = new MetaballField(new CreatureSkeleton(System.Array.Empty<Bone>()), IsoLevel);
        AssertFloat(field.Sample(new Vector3(0, 0, 0))).IsEqualApprox(0f, 1e-6f);
        AssertFloat(field.Sample(new Vector3(100, 100, 100))).IsEqualApprox(0f, 1e-6f);
    }

    [TestCase]
    public void Empty_skeleton_bounds_is_zero_sized()
    {
        var field = new MetaballField(new CreatureSkeleton(System.Array.Empty<Bone>()), IsoLevel);
        var b = field.Bounds;
        AssertFloat(b.Size.X).IsEqualApprox(0f, 1e-6f);
        AssertFloat(b.Size.Y).IsEqualApprox(0f, 1e-6f);
        AssertFloat(b.Size.Z).IsEqualApprox(0f, 1e-6f);
    }

    [TestCase]
    public void Zero_radius_bone_does_not_produce_nan_or_divide_by_zero()
    {
        var bone = new Bone(new Vector3(0, 0, 0), new Vector3(1, 0, 0), 0f, 0f, -1);
        var field = new MetaballField(new CreatureSkeleton(new[] { bone }), IsoLevel);
        float val = field.Sample(new Vector3(0.5f, 0, 0));
        // Must be finite (0, since r <= 0 is skipped).
        AssertThat(float.IsNaN(val)).IsFalse();
        AssertThat(float.IsInfinity(val)).IsFalse();
        AssertFloat(val).IsEqualApprox(0f, 1e-6f);
    }

    [TestCase]
    public void Degenerate_point_bone_head_equals_tail_does_not_throw()
    {
        var bone = new Bone(new Vector3(0, 0, 0), new Vector3(0, 0, 0), 0.3f, 0.3f, -1);
        var field = new MetaballField(new CreatureSkeleton(new[] { bone }), IsoLevel);
        float val = field.Sample(new Vector3(0, 0, 0));
        AssertThat(float.IsNaN(val)).IsFalse();
        AssertThat(float.IsInfinity(val)).IsFalse();
        // At d == 0 of a point bone: contribution == 1.0
        AssertFloat(val).IsEqualApprox(1.0f, 0.001f);
    }

    // -----------------------------------------------------------------------
    // Bounds size is positive for a real bone
    // -----------------------------------------------------------------------

    [TestCase]
    public void Bounds_has_positive_size_for_real_skeleton()
    {
        var field = new MetaballField(SingleBone(), IsoLevel);
        AssertThat(field.Bounds.Size.X > 0f).IsTrue();
        AssertThat(field.Bounds.Size.Y > 0f).IsTrue();
        AssertThat(field.Bounds.Size.Z > 0f).IsTrue();
    }
}
