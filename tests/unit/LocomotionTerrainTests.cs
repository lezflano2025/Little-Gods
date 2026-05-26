using System.Collections.Generic;
using Godot;
using LittleGods.Anim;
using LittleGods.Mesh;
using LittleGods.World;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// M4 P0 — terrain-aware foot planting. `Locomotion.Tick` gains an optional
/// `IGroundSampler`: null (or a flat plane at 0) reproduces the M3 behaviour
/// exactly; an elevated or sloped sampler moves the planted feet AND the body
/// to match, with no foot skate. This is the seam between M3 flat-plane walking
/// and an M4 world with terrain.
[TestSuite]
public class LocomotionTerrainTests
{
    private const float Dt = 0.05f;

    /// A planar ground: height = baseY + slopeX*x + slopeZ*z. (Locomotion only
    /// calls HeightAt, never NormalAt, so the normal here is a stub.)
    private sealed class SlopeGround : IGroundSampler
    {
        private readonly float _base, _sx, _sz;
        public SlopeGround(float baseY, float slopeX, float slopeZ)
        {
            _base = baseY; _sx = slopeX; _sz = slopeZ;
        }
        public float HeightAt(float x, float z) => _base + _sx * x + _sz * z;
        public Vector3 NormalAt(float x, float z) => Vector3.Up;
    }

    /// Spine + a quadruped's four straight-down 2-bone legs (segment 0.7 each,
    /// total reach 1.4 > the 1.0 body height, so flat/gentle-slope targets stay
    /// reachable). Returns the skeleton and its leg chain indices.
    private static (CreatureSkeleton skel, int[] legs) BuildQuadruped()
    {
        var bones = new List<Bone>
        {
            new Bone(new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 1f), 0.4f, 0.35f, -1),
        };
        var chains = new List<LimbChain>();
        var legs = new List<int>();
        const float upper = 0.7f, lower = 0.7f;

        foreach (float z in new[] { 0.8f, -0.8f })
        {
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -0.4f : 0.4f;
                var hip = new Vector3(sx, 0f, z);
                var knee = new Vector3(sx, -upper, z);
                var foot = new Vector3(sx, -(upper + lower), z);
                int ui = bones.Count; bones.Add(new Bone(hip, knee, 0.18f, 0.15f, 0));
                int li = bones.Count; bones.Add(new Bone(knee, foot, 0.15f, 0.12f, ui));
                chains.Add(new LimbChain(legs.Count, ui, li, li, upper, lower,
                    side == 0 ? "left_hip" : "right_hip"));
                legs.Add(chains.Count - 1);
            }
        }
        return (new CreatureSkeleton(bones.ToArray(), chains.ToArray()), legs.ToArray());
    }

    [TestCase]
    public void Null_ground_matches_a_flat_plane_at_zero()
    {
        var (skel, legs) = BuildQuadruped();
        var gait = GaitController.ForLegCount(4, 1f);
        var p = LocomotionParams.Default;
        for (float t = 0f; t <= 4f; t += Dt)
        {
            LocomotionResult a = Locomotion.Tick(skel, legs, gait, p, t, null);
            LocomotionResult b = Locomotion.Tick(skel, legs, gait, p, t, FlatGround.Zero);
            AssertFloat(a.BodyPosition.Y).IsEqualApprox(b.BodyPosition.Y, 1e-5f);
            for (int i = 0; i < legs.Length; i++)
            {
                AssertFloat(a.FootTargets[i].Y).IsEqualApprox(b.FootTargets[i].Y, 1e-5f);
            }
        }
    }

    [TestCase]
    public void Body_and_stance_feet_ride_an_elevated_flat_ground()
    {
        var (skel, legs) = BuildQuadruped();
        var gait = GaitController.ForLegCount(4, 1f);
        var p = LocomotionParams.Default;
        var ground = new FlatGround(5f);

        for (float t = 0f; t <= 4f; t += Dt)
        {
            LocomotionResult r = Locomotion.Tick(skel, legs, gait, p, t, ground);

            // Body rides 5 + BodyHeight, within the bob band.
            AssertThat(r.BodyPosition.Y >= 5f + p.BodyHeight - p.BobAmplitude - 1e-3f).IsTrue();
            AssertThat(r.BodyPosition.Y <= 5f + p.BodyHeight + p.BobAmplitude + 1e-3f).IsTrue();

            for (int i = 0; i < legs.Length; i++)
            {
                if (!r.InStance[i]) continue;
                float worldFootY = r.BodyPosition.Y + r.FootTargets[i].Y;
                AssertFloat(worldFootY).IsEqualApprox(5f, 1e-3f);
            }
        }
    }

    [TestCase]
    public void Stance_feet_follow_a_slope()
    {
        var (skel, legs) = BuildQuadruped();
        var gait = GaitController.ForLegCount(4, 1f);
        var p = LocomotionParams.Default;
        var ground = new SlopeGround(0f, 0.1f, 0.2f);

        for (float t = 0f; t <= 4f; t += Dt)
        {
            LocomotionResult r = Locomotion.Tick(skel, legs, gait, p, t, ground);
            for (int i = 0; i < legs.Length; i++)
            {
                if (!r.InStance[i]) continue;
                float fx = r.BodyPosition.X + r.FootTargets[i].X;
                float fz = r.BodyPosition.Z + r.FootTargets[i].Z;
                float worldFootY = r.BodyPosition.Y + r.FootTargets[i].Y;
                AssertFloat(worldFootY).IsEqualApprox(ground.HeightAt(fx, fz), 1e-3f);
            }
        }
    }

    [TestCase]
    public void No_skate_on_a_slope()
    {
        var (skel, legs) = BuildQuadruped();
        var gait = GaitController.ForLegCount(4, 1f);
        var p = LocomotionParams.Default;
        var ground = new SlopeGround(1f, 0.05f, 0.1f);

        var prev = new Vector3[legs.Length];
        var had = new bool[legs.Length];

        for (float t = 0f; t <= 8f; t += Dt)
        {
            LocomotionResult r = Locomotion.Tick(skel, legs, gait, p, t, ground);
            for (int i = 0; i < legs.Length; i++)
            {
                var world = new Vector3(
                    r.BodyPosition.X + r.FootTargets[i].X,
                    r.BodyPosition.Y + r.FootTargets[i].Y,
                    r.BodyPosition.Z + r.FootTargets[i].Z);

                if (r.InStance[i] && had[i])
                {
                    AssertThat(world.DistanceTo(prev[i]) <= 0.02f).IsTrue();
                }
                prev[i] = world;
                had[i] = r.InStance[i];
            }
        }
    }
}
