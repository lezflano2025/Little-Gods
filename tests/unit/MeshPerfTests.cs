using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using LittleGods.Creature;
using LittleGods.Mesh;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// M2 P4: mesh regeneration performance. PRD §8 budget is <50 ms p95 for a
/// typical creature on a 4-core CPU. This benchmarks the full
/// CreatureMesher.Build pipeline (resolve + field + marching cubes + skin).
///
/// Timing tests are inherently machine-dependent; the threshold has margin and
/// the measured numbers are printed so regressions are visible in CI logs.
[TestSuite]
public class MeshPerfTests
{
    private PartRegistry _registry = null!;

    [Before]
    public void Setup()
    {
        _registry = new PartRegistry();
        _registry.LoadLibrary();
    }

    private Recipe BuildTypicalCreature()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.SymmetryEnabled = true;
        b.AddAttachmentMaybeMirrored(-1, "left_shoulder", "limb_walker", Transform3D.Identity);
        b.AddAttachmentMaybeMirrored(-1, "left_hip", "limb_runner", Transform3D.Identity);
        b.AddAttachment(-1, "tail", "limb_tail");
        int head = b.AddAttachment(-1, "head", "head_predator");
        b.AddAttachment(head, "jaw", "mouth_fang");
        return b.Recipe;
    }

    private static (double min, double median, double p95) Measure(System.Action build, int iterations)
    {
        build(); // warm up JIT + caches
        var times = new List<double>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            build();
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        int p95Index = Mathf.Clamp((int)System.Math.Ceiling(iterations * 0.95) - 1, 0, iterations - 1);
        return (times[0], times[iterations / 2], times[p95Index]);
    }

    [TestCase]
    public void Regen_p95_under_50ms_at_default_cell_size()
    {
        var recipe = BuildTypicalCreature();
        var gp = GridParams.Default;

        int verts = CreatureMesher.Build(recipe, _registry, gp).Mesh.VertexCount;
        var (min, median, p95) = Measure(() => CreatureMesher.Build(recipe, _registry, gp), 24);

        GD.Print($"[perf] cell={gp.CellSize} verts={verts} min={min:F2} median={median:F2} p95={p95:F2} ms");
        AssertThat(p95 < 50.0)
            .OverrideFailureMessage($"regen p95 {p95:F2} ms exceeds 50 ms budget (cell {gp.CellSize}, {verts} verts)")
            .IsTrue();
    }
}
