/// <summary>
/// Tests for DeterministicRng (SplitMix64). Verifies sequence reproducibility,
/// value ranges, Fork independence, and inverted-range edge cases.
/// </summary>

using GdUnit4;
using static GdUnit4.Assertions;
using LittleGods.World;

namespace LittleGods.Tests;

[TestSuite]
public class DeterministicRngTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 1. Same seed → same sequence
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void SameSeed_ProducesIdenticalULongSequence()
    {
        var a = new DeterministicRng(42UL);
        var b = new DeterministicRng(42UL);

        for (int i = 0; i < 20; i++)
            AssertThat(a.NextULong()).IsEqual(b.NextULong());
    }

    [TestCase]
    public void SameSeed_ProducesIdenticalFloatSequence()
    {
        var a = new DeterministicRng(99999UL);
        var b = new DeterministicRng(99999UL);

        for (int i = 0; i < 20; i++)
            AssertFloat(a.NextFloat()).IsEqual(b.NextFloat());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. Different seeds diverge
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void DifferentSeeds_FirstOutputDiffers()
    {
        var a = new DeterministicRng(1UL);
        var b = new DeterministicRng(2UL);

        // Almost certainly different; if it ever collides we'll know something
        // very strange happened with the hash.
        bool anyDiff = false;
        for (int i = 0; i < 10; i++)
            if (a.NextULong() != b.NextULong()) { anyDiff = true; break; }

        AssertBool(anyDiff).IsTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. NextFloat in [0, 1)
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void NextFloat_AlwaysInZeroToOne()
    {
        var rng = new DeterministicRng(777UL);
        for (int i = 0; i < 1000; i++)
        {
            float f = rng.NextFloat();
            AssertBool(f >= 0f).IsTrue();
            AssertBool(f <  1f).IsTrue();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Range stays within [min, max)
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void Range_ValuesStayWithinBounds()
    {
        var rng = new DeterministicRng(123UL);
        const float lo = -5f, hi = 3f;
        for (int i = 0; i < 500; i++)
        {
            float v = rng.Range(lo, hi);
            AssertBool(v >= lo).IsTrue();
            AssertBool(v <  hi).IsTrue();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. RangeInt stays within [minInclusive, maxExclusive)
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void RangeInt_ValuesStayWithinBounds()
    {
        var rng = new DeterministicRng(555UL);
        for (int i = 0; i < 500; i++)
        {
            int v = rng.RangeInt(3, 10);
            AssertBool(v >= 3).IsTrue();
            AssertBool(v <  10).IsTrue();
        }
    }

    [TestCase]
    public void RangeInt_InvertedRange_ReturnsMin()
    {
        var rng = new DeterministicRng(1UL);
        AssertInt(rng.RangeInt(5, 3)).IsEqual(5);
    }

    [TestCase]
    public void RangeInt_EmptyRange_ReturnsMin()
    {
        var rng = new DeterministicRng(1UL);
        AssertInt(rng.RangeInt(7, 7)).IsEqual(7);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. Fork produces a repeatable but independent stream
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void Fork_SameSaltProducesIdenticalChild()
    {
        var parent1 = new DeterministicRng(42UL);
        var parent2 = new DeterministicRng(42UL);

        var child1 = parent1.Fork(0xDEADBEEFUL);
        var child2 = parent2.Fork(0xDEADBEEFUL);

        for (int i = 0; i < 20; i++)
            AssertThat(child1.NextULong()).IsEqual(child2.NextULong());
    }

    [TestCase]
    public void Fork_ChildStreamDiffersFromParent()
    {
        var parent = new DeterministicRng(42UL);
        // Snapshot state: record parent output before fork
        var snapshot = new DeterministicRng(42UL);
        ulong parentOut = snapshot.NextULong();

        var parent2 = new DeterministicRng(42UL);
        var child = parent2.Fork(0xCAFEBABEUL);
        ulong childOut = child.NextULong();

        // Child first output should differ from parent's first output
        AssertBool(parentOut != childOut).IsTrue();
    }

    [TestCase]
    public void Fork_DifferentSaltsProduceDifferentChildren()
    {
        var parentA = new DeterministicRng(7UL);
        var parentB = new DeterministicRng(7UL);

        var childA = parentA.Fork(1UL);
        var childB = parentB.Fork(2UL);

        bool anyDiff = false;
        for (int i = 0; i < 10; i++)
            if (childA.NextULong() != childB.NextULong()) { anyDiff = true; break; }

        AssertBool(anyDiff).IsTrue();
    }
}
