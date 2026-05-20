using Godot;
using LittleGods.Creature;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

[TestSuite]
public class RigblockLibraryTests
{
    // The 9 parts that M1 P2 commits. The names are also the filename stems.
    private static readonly (string Id, PartKind Kind, int MinSlots, int MaxSlots)[] Expected =
    {
        ("spine_basic",     PartKind.Spine, 6, 6),
        ("limb_walker",     PartKind.Limb,  0, 0),
        ("limb_runner",     PartKind.Limb,  0, 0),
        ("limb_wing",       PartKind.Limb,  0, 0),
        ("limb_tail",       PartKind.Limb,  0, 0),
        ("head_predator",   PartKind.Head,  1, 1),
        ("head_herbivore",  PartKind.Head,  1, 1),
        ("mouth_beak",      PartKind.Mouth, 0, 0),
        ("mouth_fang",      PartKind.Mouth, 0, 0),
    };

    [TestCase]
    public void Every_expected_part_loads_from_disk()
    {
        foreach (var (id, _, _, _) in Expected)
        {
            var path = $"{PartRegistry.LibraryPath}{id}.tres";
            var part = ResourceLoader.Load<Part>(path, "", ResourceLoader.CacheMode.Ignore);
            AssertThat(part).OverrideFailureMessage($"missing or invalid part: {path}").IsNotNull();
            AssertThat(part!.Id).OverrideFailureMessage($"Id mismatch in {path}").IsEqual(id);
        }
    }

    [TestCase]
    public void Every_part_kind_matches_its_filename_prefix()
    {
        foreach (var (id, expectedKind, _, _) in Expected)
        {
            var path = $"{PartRegistry.LibraryPath}{id}.tres";
            var part = ResourceLoader.Load<Part>(path, "", ResourceLoader.CacheMode.Ignore);
            AssertThat(part!.Kind)
                .OverrideFailureMessage($"{id}: Kind={part.Kind} expected {expectedKind}")
                .IsEqual(expectedKind);
        }
    }

    [TestCase]
    public void Every_part_slot_count_matches_spec()
    {
        foreach (var (id, _, minSlots, maxSlots) in Expected)
        {
            var path = $"{PartRegistry.LibraryPath}{id}.tres";
            var part = ResourceLoader.Load<Part>(path, "", ResourceLoader.CacheMode.Ignore);
            var n = part!.AttachmentPoints.Count;
            AssertThat(n >= minSlots && n <= maxSlots)
                .OverrideFailureMessage($"{id}: slots={n} expected in [{minSlots},{maxSlots}]")
                .IsTrue();
        }
    }

    [TestCase]
    public void Spine_basic_has_six_canonical_slots()
    {
        var spine = ResourceLoader.Load<Part>(
            $"{PartRegistry.LibraryPath}spine_basic.tres", "", ResourceLoader.CacheMode.Ignore);
        AssertThat(spine).IsNotNull();
        var names = new System.Collections.Generic.HashSet<string>();
        foreach (var ap in spine!.AttachmentPoints)
        {
            names.Add(ap.Name);
        }
        AssertThat(names.Contains("head")).IsTrue();
        AssertThat(names.Contains("tail")).IsTrue();
        AssertThat(names.Contains("left_shoulder")).IsTrue();
        AssertThat(names.Contains("right_shoulder")).IsTrue();
        AssertThat(names.Contains("left_hip")).IsTrue();
        AssertThat(names.Contains("right_hip")).IsTrue();
        AssertThat(names.Count).IsEqual(6);
    }

    [TestCase]
    public void Head_jaw_slot_only_accepts_mouths()
    {
        var head = ResourceLoader.Load<Part>(
            $"{PartRegistry.LibraryPath}head_predator.tres", "", ResourceLoader.CacheMode.Ignore);
        AssertThat(head).IsNotNull();
        AssertThat(head!.AttachmentPoints.Count).IsEqual(1);
        var jaw = head.AttachmentPoints[0];
        AssertThat(jaw.Name).IsEqual("jaw");
        AssertThat(jaw.AllowedKinds.Allows(PartKind.Mouth)).IsTrue();
        AssertThat(jaw.AllowedKinds.Allows(PartKind.Limb)).IsFalse();
        AssertThat(jaw.AllowedKinds.Allows(PartKind.Head)).IsFalse();
    }

    [TestCase]
    public void Library_has_no_duplicate_ids()
    {
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (var (id, _, _, _) in Expected)
        {
            AssertThat(seen.Add(id))
                .OverrideFailureMessage($"duplicate id: {id}")
                .IsTrue();
        }
    }

    [TestCase]
    public void Every_part_in_library_fits_under_recipe_size_budget()
    {
        // Each individual part .tres should be well under the 10 KB recipe budget.
        // (Recipes reference parts by Id, but smaller parts == smaller recipes.)
        foreach (var (id, _, _, _) in Expected)
        {
            var path = $"{PartRegistry.LibraryPath}{id}.tres";
            var bytes = Godot.FileAccess.GetFileAsBytes(path);
            AssertThat(bytes.Length).IsLess(4096);  // 4 KB per part is generous
        }
    }
}
