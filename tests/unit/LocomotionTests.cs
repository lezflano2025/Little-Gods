using Godot;
using LittleGods.Anim;
using LittleGods.Creature;
using LittleGods.Mesh;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// M3 P4: the deterministic locomotion tick — gait -> foot targets -> IK -> Pose,
/// plus body advance/bob. The headline test is that forward-kinematics through
/// the computed Pose lands each foot on its planned target (validating the
/// IK->bone-delta conversion against a real Skeleton3D).
[TestSuite]
public class LocomotionTests
{
    private PartRegistry _registry = null!;

    [Before]
    public void Setup()
    {
        _registry = new PartRegistry();
        _registry.LoadLibrary();
    }

    /// Bundled quadruped: 2 shoulders + 2 hips (limb_walker), mirrored.
    private CreatureSkeleton Quadruped()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.SymmetryEnabled = true;
        b.AddAttachmentMaybeMirrored(-1, "left_shoulder", "limb_walker", Transform3D.Identity);
        b.AddAttachmentMaybeMirrored(-1, "left_hip", "limb_walker", Transform3D.Identity);
        return SkeletonResolver.Resolve(b.Recipe, _registry);
    }

    private static int[] AllLegs(CreatureSkeleton s)
    {
        var idx = new int[s.LimbChains.Length];
        for (int i = 0; i < idx.Length; i++)
        {
            idx[i] = i;
        }
        return idx;
    }

    private static bool Finite(Vector3 v)
        => !float.IsNaN(v.X) && !float.IsInfinity(v.X)
        && !float.IsNaN(v.Y) && !float.IsInfinity(v.Y)
        && !float.IsNaN(v.Z) && !float.IsInfinity(v.Z);

    private static bool Finite(Transform3D t)
        => Finite(t.Origin) && Finite(t.Basis.X) && Finite(t.Basis.Y) && Finite(t.Basis.Z);

    [TestCase]
    public void No_nan_over_sixty_seconds()
    {
        var skel = Quadruped();
        var legs = AllLegs(skel);
        var gait = GaitController.ForLegCount(legs.Length, 1f);

        for (double t = 0; t <= 60.0; t += 0.05)
        {
            var r = Locomotion.Tick(skel, legs, gait, LocomotionParams.Default, t);
            AssertThat(Finite(r.BodyPosition)).IsTrue();
            for (int i = 0; i < skel.Count; i++)
            {
                AssertThat(Finite(r.Pose.Delta(i))).IsTrue();
            }
            foreach (var f in r.FootTargets)
            {
                AssertThat(Finite(f)).IsTrue();
            }
        }
    }

    [TestCase]
    public void Stance_feet_sit_on_the_ground_plane()
    {
        var skel = Quadruped();
        var legs = AllLegs(skel);
        var gait = GaitController.ForLegCount(legs.Length, 1f);
        var p = LocomotionParams.Default;

        for (double t = 0; t < 4.0; t += 0.07)
        {
            var r = Locomotion.Tick(skel, legs, gait, p, t);
            for (int i = 0; i < legs.Length; i++)
            {
                if (r.InStance[i])
                {
                    // World foot Y = body Y + body-local foot Y; on the ground (0).
                    float worldFootY = r.BodyPosition.Y + r.FootTargets[i].Y;
                    AssertFloat(worldFootY).IsEqualApprox(0f, 1e-3f);
                }
            }
        }
    }

    [TestCase]
    public void Stance_foot_does_not_skate()
    {
        var skel = Quadruped();
        var legs = AllLegs(skel);
        var gait = GaitController.ForLegCount(legs.Length, 1f);
        var p = LocomotionParams.Default;

        // Leg 0 (phase offset 0) is in stance while phase < duty (t in [0,0.6)).
        var r1 = Locomotion.Tick(skel, legs, gait, p, 0.10);
        var r2 = Locomotion.Tick(skel, legs, gait, p, 0.30);
        AssertThat(r1.InStance[0]).IsTrue();
        AssertThat(r2.InStance[0]).IsTrue();

        // The planted foot holds its WORLD position (body advance cancels the
        // body-local slide) — no skating.
        float worldZ1 = r1.BodyPosition.Z + r1.FootTargets[0].Z;
        float worldZ2 = r2.BodyPosition.Z + r2.FootTargets[0].Z;
        AssertFloat(worldZ1).IsEqualApprox(worldZ2, 1e-3f);
    }

    [TestCase]
    public void Body_advances_one_stride_per_cycle()
    {
        var skel = Quadruped();
        var legs = AllLegs(skel);
        var p = LocomotionParams.Default;
        var gait = GaitController.ForLegCount(legs.Length, p.CadenceHz);

        var a = Locomotion.Tick(skel, legs, gait, p, 0.0);
        var b = Locomotion.Tick(skel, legs, gait, p, 1.0 / p.CadenceHz);
        AssertFloat(b.BodyPosition.Z - a.BodyPosition.Z).IsEqualApprox(p.StrideLength, 1e-3f);
    }

    [TestCase]
    public void Pose_drives_each_foot_to_its_ik_target()
    {
        var skel = Quadruped();
        var legs = AllLegs(skel);
        var gait = GaitController.ForLegCount(legs.Length, 1f);
        var p = LocomotionParams.Default;
        var r = Locomotion.Tick(skel, legs, gait, p, 0.10);

        Skeleton3D skel3d = AutoFree(GodotMeshBuilder.BuildSkeleton3D(skel))!;
        ApplyPose(skel3d, r.Pose);

        // The targets here are reachable (body height < leg length), so forward
        // kinematics through the Pose must land each foot on its planned target.
        for (int i = 0; i < legs.Length; i++)
        {
            LimbChain chain = skel.LimbChains[legs[i]];
            Vector3 footTip = GlobalPose(skel3d, chain.FootBone) * new Vector3(0f, 0f, chain.LowerLength);
            AssertFloat(footTip.DistanceTo(r.FootTargets[i])).IsEqualApprox(0f, 5e-3f);
        }
    }

    [TestCase]
    public void Tick_is_deterministic()
    {
        var skel = Quadruped();
        var legs = AllLegs(skel);
        var gait = GaitController.ForLegCount(legs.Length, 1f);

        var a = Locomotion.Tick(skel, legs, gait, LocomotionParams.Default, 1.234);
        var b = Locomotion.Tick(skel, legs, gait, LocomotionParams.Default, 1.234);

        AssertFloat(a.BodyPosition.Z).IsEqualApprox(b.BodyPosition.Z, 1e-6f);
        for (int i = 0; i < skel.Count; i++)
        {
            AssertThat(a.Pose.Delta(i).IsEqualApprox(b.Pose.Delta(i))).IsTrue();
        }
    }

    // Mirror of CreaturePreview.ApplyPose for the FK validation.
    private static void ApplyPose(Skeleton3D s, Pose pose)
    {
        int n = s.GetBoneCount();
        for (int i = 0; i < n; i++)
        {
            Transform3D local = s.GetBoneRest(i) * pose.Delta(i);
            s.SetBonePosePosition(i, local.Origin);
            s.SetBonePoseRotation(i, local.Basis.GetRotationQuaternion());
            s.SetBonePoseScale(i, local.Basis.Scale);
        }
    }

    // Global pose from local-pose accumulation (valid off the scene tree).
    private static Transform3D GlobalPose(Skeleton3D s, int bone)
    {
        Transform3D t = s.GetBonePose(bone);
        int pa = s.GetBoneParent(bone);
        while (pa >= 0)
        {
            t = s.GetBonePose(pa) * t;
            pa = s.GetBoneParent(pa);
        }
        return t;
    }
}
