using System;
using Godot;
using static Godot.Mathf;
using LittleGods.Mesh;

namespace LittleGods.Anim;

/// Tunable, deterministic locomotion parameters. Forward speed is derived
/// (StrideLength * CadenceHz) so the body advances one stride per gait cycle.
/// M3 P4 — see docs/m3-plan.md. No clock, no RNG.
public readonly struct LocomotionParams
{
    /// Height of the body origin above the ground plane (world Y = 0).
    public readonly float BodyHeight;

    /// Distance the body advances per full gait cycle (world units).
    public readonly float StrideLength;

    /// Peak lift of a swing foot above the ground.
    public readonly float StepHeight;

    /// Vertical body-bob amplitude.
    public readonly float BobAmplitude;

    /// Gait cycles per second (matches the driving Gait.CadenceHz).
    public readonly float CadenceHz;

    public LocomotionParams(float bodyHeight, float strideLength, float stepHeight,
                            float bobAmplitude, float cadenceHz)
    {
        BodyHeight   = bodyHeight;
        StrideLength = strideLength;
        StepHeight   = stepHeight;
        BobAmplitude = bobAmplitude;
        CadenceHz    = cadenceHz;
    }

    /// Forward speed that keeps stance feet from skating: one stride per cycle.
    public float ForwardSpeed => StrideLength * CadenceHz;

    public static LocomotionParams Default { get; } =
        new(bodyHeight: 1.0f, strideLength: 0.8f, stepHeight: 0.22f,
            bobAmplitude: 0.05f, cadenceHz: 1.0f);
}

/// One tick of the locomotion driver: the body's world transform plus the
/// per-bone Pose that plants/swings each leg via IK.
public sealed class LocomotionResult
{
    /// Skeleton pose (bone-local deltas) to hand to CreaturePreview.ApplyPose.
    public Pose Pose { get; }

    /// World position of the body origin (forward advance + vertical bob).
    /// Apply to the preview node; the Pose is body-local.
    public Vector3 BodyPosition { get; }

    /// Per-leg foot target in BODY-LOCAL space (what the IK solved for), indexed
    /// parallel to the legChainIndices passed to Tick.
    public Vector3[] FootTargets { get; }

    /// Per-leg stance (true) / swing (false) flag, parallel to FootTargets.
    public bool[] InStance { get; }

    public LocomotionResult(Pose pose, Vector3 bodyPosition, Vector3[] footTargets, bool[] inStance)
    {
        Pose         = pose;
        BodyPosition = bodyPosition;
        FootTargets  = footTargets;
        InStance     = inStance;
    }
}

/// Procedural locomotion driver (M3 P4). Per tick it advances the gait, plans a
/// foot target for each leg (planted on the ground plane during stance, arcing
/// forward during swing), solves each leg with TwoBoneIk, and converts the IK
/// result into Skeleton3D-ready local pose deltas (Pose). Body bob rides the
/// stride phase.
///
/// Pure and deterministic: a function of (skeleton, legs, gait, params, seconds).
/// Elapsed time is the explicit `double seconds` — no clock, no RNG — so a 60 s
/// run is byte-reproducible (PRD invariant 4).
///
/// Foot targets are in BODY-LOCAL space: the caller moves the preview node to
/// BodyPosition and applies Pose to the body-local Skeleton3D. A stance foot is
/// held world-fixed (its body-local Z slides back exactly as fast as the body
/// advances), which is what stops foot skating.
public static class Locomotion
{
    public static LocomotionResult Tick(
        CreatureSkeleton skeleton,
        int[] legChainIndices,
        Gait gait,
        LocomotionParams p,
        double seconds)
    {
        int legCount = legChainIndices?.Length ?? 0;
        var footTargets = new Vector3[legCount];
        var inStance = new bool[legCount];
        var pose = Pose.Rest(skeleton.Count);

        // Body advance + bob.
        double cyc = seconds * p.CadenceHz;
        float cyclePhase = (float)(cyc - Math.Floor(cyc));
        float bob = p.BobAmplitude * Sin(Tau * 2f * cyclePhase); // two bobs per cycle
        float bodyY = p.BodyHeight + bob;
        var bodyPosition = new Vector3(0f, bodyY, (float)(p.ForwardSpeed * seconds));

        // Body-local Y of the world ground plane (y = 0).
        float groundLocalY = -bodyY;
        // Peak-to-peak foot travel that keeps a stance foot world-fixed.
        float halfTravel = p.StrideLength * gait.DutyFactor * 0.5f;

        for (int li = 0; li < legCount; li++)
        {
            int ci = legChainIndices![li];
            if (ci < 0 || ci >= skeleton.LimbChains.Length)
            {
                footTargets[li] = Vector3.Zero;
                continue;
            }

            LimbChain chain = skeleton.LimbChains[ci];
            Transform3D gUpper = RestGlobal(skeleton.Bones[chain.RootBone]);
            Transform3D gLower = RestGlobal(skeleton.Bones[chain.KneeBone]);
            Vector3 hip = gUpper.Origin;

            float phase = GaitController.PhaseOf(gait, li, seconds);
            bool stance = phase < gait.DutyFactor;
            inStance[li] = stance;

            float footZ;
            float lift;
            if (stance)
            {
                float s = gait.DutyFactor > 1e-6f ? phase / gait.DutyFactor : 0f;
                footZ = hip.Z + halfTravel * (1f - 2f * s); // front -> back
                lift = 0f;
            }
            else
            {
                float denom = 1f - gait.DutyFactor;
                float sw = denom > 1e-6f ? (phase - gait.DutyFactor) / denom : 0f;
                footZ = hip.Z - halfTravel + sw * 2f * halfTravel; // back -> front
                lift = p.StepHeight * Sin(Pi * sw);
            }

            var footTarget = new Vector3(hip.X, groundLocalY + lift, footZ);
            footTargets[li] = footTarget;

            // Knees bend toward the direction of travel (+Z).
            var pole = new Vector3(0f, 0f, 1f);
            IkResult ik = TwoBoneIk.Solve(hip, chain.UpperLength, chain.LowerLength, footTarget, pole);

            // Desired posed (body-local) globals for the two leg bones.
            Transform3D pUpper = AimTransform(hip, ik.Knee - hip);
            Transform3D pLower = AimTransform(ik.Knee, ik.End - ik.Knee);

            // Local pose deltas (posedLocal = restLocal * delta). The upper's
            // parent (spine) is unposed, so its delta collapses to
            // restGlobal^-1 * posedGlobal; the lower hangs off the posed upper.
            Transform3D dUpper = gUpper.AffineInverse() * pUpper;
            Transform3D dLower = gLower.AffineInverse() * gUpper * pUpper.AffineInverse() * pLower;

            pose = pose.With(chain.RootBone, dUpper).With(chain.KneeBone, dLower);
        }

        return new LocomotionResult(pose, bodyPosition, footTargets, inStance);
    }

    /// Global rest transform of a world-space bone: +Z along Head→Tail, origin
    /// at Head. Mirrors GodotMeshBuilder's rest convention so the deltas line up
    /// with the Skeleton3D the preview builds.
    private static Transform3D RestGlobal(Bone bone) => AimTransform(bone.Head, bone.Tail - bone.Head);

    /// Transform at `origin` whose +Z column points along `dir` (guarded).
    private static Transform3D AimTransform(Vector3 origin, Vector3 dir)
    {
        Vector3 d = dir.LengthSquared() > 1e-12f ? dir.Normalized() : Vector3.Back;
        Vector3 refUp = Abs(d.Dot(Vector3.Up)) < 0.99f ? Vector3.Up : Vector3.Right;
        Vector3 right = refUp.Cross(d).Normalized();
        Vector3 up    = d.Cross(right).Normalized();
        return new Transform3D(new Basis(right, up, d), origin);
    }
}
