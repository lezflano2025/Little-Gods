using Godot;
using LittleGods.Anim;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// <summary>
/// Unit tests for TwoBoneIk.Solve.
/// M3 P1 — see docs/m3-contract.md (Agent A tests).
/// </summary>
[TestSuite]
public class TwoBoneIkTests
{
    // ── helpers ────────────────────────────────────────────────────────────

    private static void AssertVec3Approx(Vector3 actual, Vector3 expected, float eps = 1e-4f)
    {
        AssertFloat(actual.X).IsEqualApprox(expected.X, eps);
        AssertFloat(actual.Y).IsEqualApprox(expected.Y, eps);
        AssertFloat(actual.Z).IsEqualApprox(expected.Z, eps);
    }

    private static void AssertFinite(Vector3 v)
    {
        AssertThat(!float.IsNaN(v.X) && !float.IsInfinity(v.X)).IsTrue();
        AssertThat(!float.IsNaN(v.Y) && !float.IsInfinity(v.Y)).IsTrue();
        AssertThat(!float.IsNaN(v.Z) && !float.IsInfinity(v.Z)).IsTrue();
    }

    // ── test cases ─────────────────────────────────────────────────────────

    /// <summary>
    /// Case 1 — Reachable target: foot ≈ target; limb segment lengths preserved.
    /// root=(0,0,0), upper=lower=1, target=(1.5,0,0), pole=(0,1,0).
    /// </summary>
    [TestCase]
    public void ReachableTarget_FootAtTarget_SegmentLengthsCorrect()
    {
        var root   = Vector3.Zero;
        var target = new Vector3(1.5f, 0f, 0f);
        var pole   = new Vector3(0f, 1f, 0f);

        IkResult r = TwoBoneIk.Solve(root, 1f, 1f, target, pole);

        AssertThat(r.Reachable).IsTrue();
        AssertVec3Approx(r.End, target);

        float upperActual = r.Knee.DistanceTo(root);
        float lowerActual = r.End.DistanceTo(r.Knee);
        AssertFloat(upperActual).IsEqualApprox(1f, 1e-4f);
        AssertFloat(lowerActual).IsEqualApprox(1f, 1e-4f);
    }

    /// <summary>
    /// Case 2 — Over-reach: foot at distance u+l along ray; Reachable false; finite.
    /// target=(5,0,0), u=l=1 → foot≈(2,0,0).
    /// </summary>
    [TestCase]
    public void OverReach_FullExtension_ReachableFalse()
    {
        var root   = Vector3.Zero;
        var target = new Vector3(5f, 0f, 0f);
        var pole   = new Vector3(0f, 1f, 0f);

        IkResult r = TwoBoneIk.Solve(root, 1f, 1f, target, pole);

        AssertThat(r.Reachable).IsFalse();
        AssertVec3Approx(r.End, new Vector3(2f, 0f, 0f));
        AssertFinite(r.Knee);
        AssertFinite(r.End);
    }

    /// <summary>
    /// Case 3 — Target at root: no NaN / Inf in any output field.
    /// </summary>
    [TestCase]
    public void TargetAtRoot_NoNaNOrInfinity()
    {
        var root   = Vector3.Zero;
        var target = Vector3.Zero;          // degenerate: target == root
        var pole   = new Vector3(0f, 1f, 0f);

        IkResult r = TwoBoneIk.Solve(root, 1f, 1f, target, pole);

        AssertFinite(r.Knee);
        AssertFinite(r.End);
    }

    /// <summary>
    /// Case 4 — Pole side: knee.Y flips sign when pole flips from +Y to −Y.
    /// Same reachable target (1.5,0,0); upper=lower=1.
    /// </summary>
    [TestCase]
    public void PoleSide_KneeBendsTowardPole()
    {
        var root   = Vector3.Zero;
        var target = new Vector3(1.5f, 0f, 0f);

        IkResult rPos = TwoBoneIk.Solve(root, 1f, 1f, target, new Vector3(0f,  1f, 0f));
        IkResult rNeg = TwoBoneIk.Solve(root, 1f, 1f, target, new Vector3(0f, -1f, 0f));

        AssertThat(rPos.Reachable).IsTrue();
        AssertThat(rNeg.Reachable).IsTrue();
        AssertThat(rPos.Knee.Y > 0f).IsTrue();
        AssertThat(rNeg.Knee.Y < 0f).IsTrue();
    }

    /// <summary>
    /// Case 5 — Under-reach (close target): Reachable false; knee and foot finite.
    /// u=1, l=1, target=(0.1,0,0) — within |u-l|=0 exclusion zone (both lengths equal).
    /// Because u==l the under-reach band collapses; dist=0.1 < |1-1|=0 is actually
    /// never true, so dist=0.1 is within reach. Test with u=2, l=1, target=0.1 instead,
    /// where dist=0.1 < |2-1|=1 triggers under-reach.
    /// </summary>
    [TestCase]
    public void UnderReach_ReachableFalse_FiniteOutputs()
    {
        var root   = Vector3.Zero;
        // u=2, l=1 → |u-l|=1; target dist=0.1 < 1 → under-reach
        var target = new Vector3(0.1f, 0f, 0f);
        var pole   = new Vector3(0f, 1f, 0f);

        IkResult r = TwoBoneIk.Solve(root, 2f, 1f, target, pole);

        AssertThat(r.Reachable).IsFalse();
        AssertFinite(r.Knee);
        AssertFinite(r.End);
    }

    /// <summary>
    /// Bonus — Degenerate pole (zero vector) does not produce NaN.
    /// </summary>
    [TestCase]
    public void DegeneratePole_NoNaN()
    {
        var root   = Vector3.Zero;
        var target = new Vector3(1.5f, 0f, 0f);
        var pole   = Vector3.Zero; // degenerate

        IkResult r = TwoBoneIk.Solve(root, 1f, 1f, target, pole);

        AssertFinite(r.Knee);
        AssertFinite(r.End);
    }

    /// <summary>
    /// Bonus — Negative / near-zero lengths are guarded; outputs finite.
    /// </summary>
    [TestCase]
    public void NearZeroLengths_FiniteOutputs()
    {
        var root   = Vector3.Zero;
        var target = new Vector3(1f, 0f, 0f);
        var pole   = new Vector3(0f, 1f, 0f);

        IkResult r = TwoBoneIk.Solve(root, -5f, 0f, target, pole);

        AssertFinite(r.Knee);
        AssertFinite(r.End);
    }
}
