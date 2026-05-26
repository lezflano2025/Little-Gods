/// <summary>
/// GdUnit4 tests for HeightmapTerrain (via ValueNoise fBm).
/// Covers: determinism across calls and instances, seed independence,
/// amplitude bounds, continuity, and normal-vector correctness.
/// </summary>

using System;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using LittleGods.World;

namespace LittleGods.Tests;

[TestSuite]
public class TerrainTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static HeightmapTerrain DefaultTerrain(ulong seed = 42UL)
        => new HeightmapTerrain(seed);

    // ──────────────────────────────────────────────────────────────────────────
    // 1. Determinism: same seed, same call → same result
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void HeightAt_SameSeed_SameCallTwice_IsIdentical()
    {
        var t = DefaultTerrain();
        float h1 = t.HeightAt(100f, 200f);
        float h2 = t.HeightAt(100f, 200f);
        AssertFloat(h1).IsEqual(h2);
    }

    [TestCase]
    public void HeightAt_TwoInstancesSameSeed_ProduceIdenticalResults()
    {
        var a = DefaultTerrain(12345UL);
        var b = DefaultTerrain(12345UL);

        float[] xs = { -300f, 0f, 50f, 511.5f };
        float[] zs = {  200f, 0f, -77f, -511.5f };

        for (int i = 0; i < xs.Length; i++)
            AssertFloat(a.HeightAt(xs[i], zs[i])).IsEqual(b.HeightAt(xs[i], zs[i]));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. Different seeds → different height at some sampled point
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void HeightAt_DifferentSeeds_DifferAtSomePoint()
    {
        var a = DefaultTerrain(1UL);
        var b = DefaultTerrain(2UL);

        bool anyDiff = false;
        for (float x = -200f; x <= 200f; x += 50f)
        {
            for (float z = -200f; z <= 200f; z += 50f)
            {
                if (!Mathf.IsEqualApprox(a.HeightAt(x, z), b.HeightAt(x, z)))
                {
                    anyDiff = true;
                    break;
                }
            }
            if (anyDiff) break;
        }

        AssertBool(anyDiff).IsTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Height stays within [-amplitude, +amplitude]
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void HeightAt_WithinAmplitudeBounds()
    {
        const float amplitude = 6f;
        var t = new HeightmapTerrain(seed: 99UL, amplitude: amplitude);

        for (float x = -512f; x <= 512f; x += 32f)
        {
            for (float z = -512f; z <= 512f; z += 32f)
            {
                float h = t.HeightAt(x, z);
                AssertBool(h >= -amplitude).IsTrue();
                AssertBool(h <=  amplitude).IsTrue();
            }
        }
    }

    [TestCase]
    public void HeightAt_CustomAmplitude_WithinBounds()
    {
        const float amplitude = 20f;
        var t = new HeightmapTerrain(seed: 7UL, amplitude: amplitude, octaves: 6);

        for (float x = -400f; x <= 400f; x += 40f)
        {
            for (float z = -400f; z <= 400f; z += 40f)
            {
                float h = t.HeightAt(x, z);
                AssertBool(h >= -amplitude).IsTrue();
                AssertBool(h <=  amplitude).IsTrue();
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Continuity: neighbouring samples are close
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void HeightAt_Continuity_SmallStepSmallDelta()
    {
        var t = DefaultTerrain(55UL);
        const float step = 0.1f;
        const float maxDelta = 0.5f;   // generous bound for amplitude=6 and baseFreq=1/120

        for (float x = -100f; x <= 100f; x += 10f)
        {
            for (float z = -100f; z <= 100f; z += 10f)
            {
                float h0 = t.HeightAt(x, z);
                float h1 = t.HeightAt(x + step, z);
                AssertBool(MathF.Abs(h1 - h0) < maxDelta).IsTrue();
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. NormalAt is unit length
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void NormalAt_IsUnitLength()
    {
        var t = DefaultTerrain();
        const float tol = 1e-4f;

        for (float x = -200f; x <= 200f; x += 40f)
        {
            for (float z = -200f; z <= 200f; z += 40f)
            {
                float len = t.NormalAt(x, z).Length();
                AssertFloat(len).IsEqualApprox(1f, tol);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. NormalAt average Y-component is positive (normals point generally up)
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void NormalAt_AverageYComponentIsPositive()
    {
        var t = DefaultTerrain();
        float sumY = 0f;
        int count  = 0;

        for (float x = -200f; x <= 200f; x += 20f)
        {
            for (float z = -200f; z <= 200f; z += 20f)
            {
                sumY += t.NormalAt(x, z).Y;
                count++;
            }
        }

        float avgY = sumY / count;
        // Terrain is gentle; average normal should lean clearly upward.
        AssertBool(avgY > 0.9f).IsTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. HalfExtent is sizeMeters / 2
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void HalfExtent_IsHalfOfSizeMeters()
    {
        var t = new HeightmapTerrain(seed: 1UL, sizeMeters: 512f);
        AssertFloat(t.HalfExtent).IsEqualApprox(256f, 1e-5f);
    }
}
