using System.Collections.Generic;
using Godot;
using LittleGods.Anim;
using LittleGods.Mesh;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// M3 P6 — the Hecker acceptance gate (PRD §7 M3):
///
///   Creatures with 2, 4, 6 and 8 legs all walk plausibly for 60 seconds with
///   no visible IK breaks.
///
/// This is the automated half: for each leg count, run 60 s of the SAME
/// deterministic Locomotion path and assert no NaN/Inf, stance feet on the
/// ground plane, no joint over-reach (the IK never pops to full stretch), and
/// no foot skate. A walk render per leg count is the human-review half.
///
/// The bundled spine_basic has only four leg slots, so multi-leg creatures are
/// built as synthetic skeletons here — which is exactly what the gate measures:
/// that the locomotion CODE generalises by leg count (no per-morphology
/// special-casing). Authoring 6/8-leg rigblock parts for recipe-built creatures
/// is content follow-up, separate from this animation gate.
[TestSuite]
public class HeckerWalkTests
{
    private const float Dt = 0.05f;        // 20 Hz sampling over the run
    private const float Duration = 60f;    // the PRD's 60 seconds
    private const float SkateBound = 0.02f; // max world foot drift per tick in stance

    /// Spine + `legCount` straight-down two-bone legs (each segment 0.7, total
    /// reach 1.4 > the 1.0 body height, so ground targets stay reachable). Legs
    /// alternate left/right along the spine. Returns the skeleton and the leg
    /// chain indices (every chain is a leg here).
    private static (CreatureSkeleton skel, int[] legs) BuildLegged(int legCount)
    {
        var bones = new List<Bone>
        {
            new Bone(new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 1f), 0.4f, 0.35f, -1), // spine
        };
        var chains = new List<LimbChain>();
        var legs = new List<int>();

        int pairs = legCount / 2;
        const float upper = 0.7f;
        const float lower = 0.7f;

        for (int p = 0; p < pairs; p++)
        {
            float z = pairs == 1 ? 0f : Mathf.Lerp(0.8f, -0.8f, p / (float)(pairs - 1));
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -0.4f : 0.4f;
                var hip  = new Vector3(sx, 0f, z);
                var knee = new Vector3(sx, -upper, z);
                var foot = new Vector3(sx, -(upper + lower), z);

                int upperIdx = bones.Count;
                bones.Add(new Bone(hip, knee, 0.18f, 0.15f, 0));         // parent = spine
                int lowerIdx = bones.Count;
                bones.Add(new Bone(knee, foot, 0.15f, 0.12f, upperIdx)); // parent = upper

                chains.Add(new LimbChain(legs.Count, upperIdx, lowerIdx, lowerIdx,
                    upper, lower, side == 0 ? "left_hip" : "right_hip"));
                legs.Add(chains.Count - 1);
            }
        }

        return (new CreatureSkeleton(bones.ToArray(), chains.ToArray()), legs.ToArray());
    }

    private static bool Finite(Vector3 v)
        => !float.IsNaN(v.X) && !float.IsInfinity(v.X)
        && !float.IsNaN(v.Y) && !float.IsInfinity(v.Y)
        && !float.IsNaN(v.Z) && !float.IsInfinity(v.Z);

    private static bool Finite(Transform3D t)
        => Finite(t.Origin) && Finite(t.Basis.X) && Finite(t.Basis.Y) && Finite(t.Basis.Z);

    /// The gate, parameterised by leg count. One assertion body, one code path.
    private void AssertWalksCleanly(int legCount)
    {
        var (skel, legs) = BuildLegged(legCount);
        AssertThat(legs.Length).IsEqual(legCount);

        var gait = GaitController.ForLegCount(legCount, 1f);
        var p = LocomotionParams.Default;

        var prevWorldFoot = new Vector3[legCount];
        var prevStance = new bool[legCount];

        for (float t = 0f; t <= Duration; t += Dt)
        {
            LocomotionResult r = Locomotion.Tick(skel, legs, gait, p, t);

            AssertThat(Finite(r.BodyPosition)).IsTrue();
            for (int i = 0; i < skel.Count; i++)
            {
                AssertThat(Finite(r.Pose.Delta(i))).IsTrue();
            }

            for (int i = 0; i < legCount; i++)
            {
                LimbChain chain = skel.LimbChains[legs[i]];
                Vector3 footLocal = r.FootTargets[i];
                AssertThat(Finite(footLocal)).IsTrue();

                Vector3 hip = skel.Bones[chain.RootBone].Head;

                // No joint over-reach: the foot target is always within the
                // leg's full length, so the IK never pops to full stretch.
                float reach = hip.DistanceTo(footLocal);
                AssertThat(reach <= chain.TotalLength + 1e-3f).IsTrue();

                Vector3 worldFoot = r.BodyPosition + footLocal;

                if (r.InStance[i])
                {
                    // Stance feet rest on the ground plane (world Y == 0).
                    AssertFloat(worldFoot.Y).IsEqualApprox(0f, 1e-3f);

                    // ...and do not skate while planted.
                    if (prevStance[i])
                    {
                        AssertThat(worldFoot.DistanceTo(prevWorldFoot[i]) <= SkateBound).IsTrue();
                    }
                }

                prevWorldFoot[i] = worldFoot;
                prevStance[i] = r.InStance[i];
            }
        }
    }

    [TestCase]
    public void Biped_walks_cleanly_for_sixty_seconds() => AssertWalksCleanly(2);

    [TestCase]
    public void Quadruped_walks_cleanly_for_sixty_seconds() => AssertWalksCleanly(4);

    [TestCase]
    public void Hexapod_walks_cleanly_for_sixty_seconds() => AssertWalksCleanly(6);

    [TestCase]
    public void Octopod_walks_cleanly_for_sixty_seconds() => AssertWalksCleanly(8);

    [TestCase]
    public void Same_path_drives_every_leg_count_deterministically()
    {
        // The gate's "generalises" clause: identical results on repeat, same call.
        foreach (int n in new[] { 2, 4, 6, 8 })
        {
            var (skel, legs) = BuildLegged(n);
            var gait = GaitController.ForLegCount(n, 1f);
            var a = Locomotion.Tick(skel, legs, gait, LocomotionParams.Default, 12.34);
            var b = Locomotion.Tick(skel, legs, gait, LocomotionParams.Default, 12.34);
            AssertFloat(a.BodyPosition.Z).IsEqualApprox(b.BodyPosition.Z, 1e-6f);
            for (int i = 0; i < skel.Count; i++)
            {
                AssertThat(a.Pose.Delta(i).IsEqualApprox(b.Pose.Delta(i))).IsTrue();
            }
        }
    }
}
