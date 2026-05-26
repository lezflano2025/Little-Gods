using Godot;
using LittleGods.Anim;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// M3 P0: the src/anim contract types the parallel P1–P3 agents build against
/// (LimbChain, IkResult, LimbType, Gait, Pose). Pure value types — no scene
/// tree — so these run headless with the rest of the unit suite.
[TestSuite]
public class AnimContractTests
{
    [TestCase]
    public void Pose_rest_is_all_identity()
    {
        var p = Pose.Rest(3);
        AssertThat(p.BoneCount).IsEqual(3);
        for (int i = 0; i < 3; i++)
        {
            AssertThat(p.Delta(i) == Transform3D.Identity).IsTrue();
        }
    }

    [TestCase]
    public void Pose_with_replaces_one_bone_and_leaves_the_original_at_rest()
    {
        var rest = Pose.Rest(3);
        var moved = new Transform3D(Basis.Identity, new Vector3(0f, 1f, 0f));
        var bent = rest.With(1, moved);

        AssertFloat(bent.Delta(1).Origin.Y).IsEqualApprox(1f, 1e-5f);
        AssertThat(bent.Delta(0) == Transform3D.Identity).IsTrue();
        // Immutable: the receiver is unchanged.
        AssertThat(rest.Delta(1) == Transform3D.Identity).IsTrue();
    }

    [TestCase]
    public void Pose_with_grows_when_index_is_beyond_bone_count()
    {
        var p = Pose.Rest(2).With(5, new Transform3D(Basis.Identity, Vector3.Up));
        AssertThat(p.BoneCount).IsEqual(6);
        AssertFloat(p.Delta(5).Origin.Y).IsEqualApprox(1f, 1e-5f);
        AssertThat(p.Delta(3) == Transform3D.Identity).IsTrue();
    }

    [TestCase]
    public void Pose_delta_out_of_range_is_identity()
    {
        var p = Pose.Rest(2);
        AssertThat(p.Delta(5) == Transform3D.Identity).IsTrue();
        AssertThat(p.Delta(-1) == Transform3D.Identity).IsTrue();
        // Default-constructed (empty) pose is safe to read.
        var empty = new Pose();
        AssertThat(empty.BoneCount).IsEqual(0);
        AssertThat(empty.Delta(0) == Transform3D.Identity).IsTrue();
    }

    [TestCase]
    public void LimbChain_total_length_sums_segments_and_keeps_slot()
    {
        var c = new LimbChain(0, 1, 2, 2, 0.8f, 1.2f, "left_hip");
        AssertFloat(c.TotalLength).IsEqualApprox(2.0f, 1e-5f);
        AssertThat(c.RootBone).IsEqual(1);
        AssertThat(c.FootBone).IsEqual(2);
        AssertThat(c.SlotName).IsEqual("left_hip");
    }

    [TestCase]
    public void Gait_leg_count_tracks_phase_offsets()
    {
        var g = new Gait(1.5f, 0.6f, new[] { 0f, 0.5f, 0.5f, 0f });
        AssertThat(g.LegCount).IsEqual(4);
        AssertFloat(g.CadenceHz).IsEqualApprox(1.5f, 1e-5f);
        AssertFloat(g.DutyFactor).IsEqualApprox(0.6f, 1e-5f);
        // Null offsets degrade to an empty (no-leg) gait, never null.
        var none = new Gait(1f, 0.5f, null!);
        AssertThat(none.LegCount).IsEqual(0);
    }

    [TestCase]
    public void IkResult_carries_joint_positions_and_reach_flag()
    {
        var r = new IkResult(new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f), reachable: false);
        AssertThat(r.Reachable).IsFalse();
        AssertFloat(r.Knee.Y).IsEqualApprox(1f, 1e-5f);
        AssertFloat(r.End.Z).IsEqualApprox(1f, 1e-5f);
    }
}
