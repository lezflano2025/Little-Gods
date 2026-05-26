/// <summary>
/// M3 P3 — see docs/m3-contract.md
/// GdUnit4 tests for GaitController: presets, phase range, clean wrap,
/// static stability, and determinism.
/// </summary>

using Godot;
using LittleGods.Anim;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

[TestSuite]
public class GaitControllerTests
{
    // ──────────────────────────────────────────────────────────────
    // 1. Presets: leg count, phase offsets, duty factor
    // ──────────────────────────────────────────────────────────────

    [TestCase]
    public void Preset_Biped_LegCountAndOffsets()
    {
        var g = GaitController.ForLegCount(2, 1f);
        AssertInt(g.LegCount).IsEqual(2);
        AssertFloat(g.DutyFactor).IsEqualApprox(0.6f, 1e-5f);
        AssertFloat(g.PhaseOffsets[0]).IsEqualApprox(0f,   1e-5f);
        AssertFloat(g.PhaseOffsets[1]).IsEqualApprox(0.5f, 1e-5f);
    }

    [TestCase]
    public void Preset_Quadruped_LegCountAndOffsets()
    {
        var g = GaitController.ForLegCount(4, 1f);
        AssertInt(g.LegCount).IsEqual(4);
        AssertFloat(g.DutyFactor).IsEqualApprox(0.6f, 1e-5f);
        AssertFloat(g.PhaseOffsets[0]).IsEqualApprox(0f,   1e-5f);
        AssertFloat(g.PhaseOffsets[1]).IsEqualApprox(0.5f, 1e-5f);
        AssertFloat(g.PhaseOffsets[2]).IsEqualApprox(0.5f, 1e-5f);
        AssertFloat(g.PhaseOffsets[3]).IsEqualApprox(0f,   1e-5f);
    }

    [TestCase]
    public void Preset_Hexapod_LegCountAndOffsets()
    {
        var g = GaitController.ForLegCount(6, 1f);
        AssertInt(g.LegCount).IsEqual(6);
        AssertFloat(g.DutyFactor).IsEqualApprox(0.5f, 1e-5f);
        float[] expected = { 0f, 0.5f, 0f, 0.5f, 0f, 0.5f };
        for (int i = 0; i < 6; i++)
            AssertFloat(g.PhaseOffsets[i]).IsEqualApprox(expected[i], 1e-5f);
    }

    [TestCase]
    public void Preset_Octopod_LegCountAndOffsets()
    {
        var g = GaitController.ForLegCount(8, 1f);
        AssertInt(g.LegCount).IsEqual(8);
        AssertFloat(g.DutyFactor).IsEqualApprox(0.6f, 1e-5f);
        float[] expected = { 0f, 0.25f, 0.5f, 0.75f, 0f, 0.25f, 0.5f, 0.75f };
        for (int i = 0; i < 8; i++)
            AssertFloat(g.PhaseOffsets[i]).IsEqualApprox(expected[i], 1e-5f);
    }

    [TestCase]
    public void Preset_ZeroOrNegative_ReturnsEmptyOffsets()
    {
        var g0 = GaitController.ForLegCount(0, 1f);
        AssertInt(g0.LegCount).IsEqual(0);

        var gNeg = GaitController.ForLegCount(-3, 1f);
        AssertInt(gNeg.LegCount).IsEqual(0);
    }

    [TestCase]
    public void Preset_OtherN_EvenlySpread()
    {
        // 3 legs: offsets should be 0, 1/3, 2/3
        var g = GaitController.ForLegCount(3, 1f);
        AssertInt(g.LegCount).IsEqual(3);
        AssertFloat(g.DutyFactor).IsEqualApprox(0.6f, 1e-5f);
        AssertFloat(g.PhaseOffsets[0]).IsEqualApprox(0f,         1e-5f);
        AssertFloat(g.PhaseOffsets[1]).IsEqualApprox(1f / 3f,    1e-5f);
        AssertFloat(g.PhaseOffsets[2]).IsEqualApprox(2f / 3f,    1e-5f);
    }

    // ──────────────────────────────────────────────────────────────
    // 2. Phase range: PhaseOf always in [0, 1)
    // ──────────────────────────────────────────────────────────────

    [TestCase]
    public void PhaseOf_QuadrupedAllLegs_AlwaysInZeroToOne()
    {
        var g = GaitController.ForLegCount(4, 1f);
        double[] times = { 0.0, 0.1, 0.37, 0.99, 1.5, 7.3 };

        foreach (double t in times)
        {
            for (int leg = 0; leg < 4; leg++)
            {
                float phase = GaitController.PhaseOf(g, leg, t);
                AssertBool(phase >= 0f).IsTrue();
                AssertBool(phase <  1f).IsTrue();
            }
        }
    }

    [TestCase]
    public void PhaseOf_OutOfRangeLeg_ReturnsZero()
    {
        var g = GaitController.ForLegCount(4, 1f);
        AssertFloat(GaitController.PhaseOf(g, -1, 1.0)).IsEqualApprox(0f, 1e-7f);
        AssertFloat(GaitController.PhaseOf(g,  4, 1.0)).IsEqualApprox(0f, 1e-7f);
    }

    // ──────────────────────────────────────────────────────────────
    // 3. Clean wrap: PhaseOf(t) ≈ PhaseOf(t + 1/CadenceHz)
    // ──────────────────────────────────────────────────────────────

    [TestCase]
    public void PhaseOf_CleanWrap_CadenceOne()
    {
        var g = GaitController.ForLegCount(4, 1f);  // cadence 1 Hz
        double period = 1.0 / g.CadenceHz;          // == 1.0 s
        double t = 0.37;
        int leg = 2;

        float phaseA = GaitController.PhaseOf(g, leg, t);
        float phaseB = GaitController.PhaseOf(g, leg, t + period);

        AssertFloat(System.Math.Abs(phaseA - phaseB)).IsLess(1e-4f);
    }

    // ──────────────────────────────────────────────────────────────
    // 4. Static stability: minimum legs in stance at every sample
    // ──────────────────────────────────────────────────────────────

    [TestCase]
    public void StaticStability_Quadruped_MinTwoLegsInStance()
    {
        var g = GaitController.ForLegCount(4, 1f);
        int steps = 20;
        for (int i = 0; i < steps; i++)
        {
            double t = (double)i / steps;
            int stance = 0;
            for (int leg = 0; leg < g.LegCount; leg++)
                if (GaitController.IsStance(g, leg, t)) stance++;
            AssertBool(stance >= 2).IsTrue();
        }
    }

    [TestCase]
    public void StaticStability_Hexapod_MinThreeLegsInStance()
    {
        var g = GaitController.ForLegCount(6, 1f);
        int steps = 20;
        for (int i = 0; i < steps; i++)
        {
            double t = (double)i / steps;
            int stance = 0;
            for (int leg = 0; leg < g.LegCount; leg++)
                if (GaitController.IsStance(g, leg, t)) stance++;
            AssertBool(stance >= 3).IsTrue();
        }
    }

    [TestCase]
    public void StaticStability_Biped_MinOneLegsInStance()
    {
        var g = GaitController.ForLegCount(2, 1f);
        int steps = 20;
        for (int i = 0; i < steps; i++)
        {
            double t = (double)i / steps;
            int stance = 0;
            for (int leg = 0; leg < g.LegCount; leg++)
                if (GaitController.IsStance(g, leg, t)) stance++;
            AssertBool(stance >= 1).IsTrue();
        }
    }

    // ──────────────────────────────────────────────────────────────
    // 5. Determinism: identical args → identical result
    // ──────────────────────────────────────────────────────────────

    [TestCase]
    public void PhaseOf_Deterministic_SameResultOnTwoCalls()
    {
        var g = GaitController.ForLegCount(4, 2.5f);
        double t = 3.14159265358979;
        int leg = 1;

        float first  = GaitController.PhaseOf(g, leg, t);
        float second = GaitController.PhaseOf(g, leg, t);

        // Exact bitwise equality — no mutable state
        AssertFloat(first).IsEqual(second);
    }
}
