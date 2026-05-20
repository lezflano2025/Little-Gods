using Godot;
using LittleGods.Creature;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// M1 acceptance test (PRD section 7 M1):
/// "A creature designed in the 2D editor saves, reloads identically,
///  and the recipe is <10 KB."
///
/// This test exercises the data-layer round trip end-to-end using the
/// same RecipeBuilder API the editor invokes. The editor UI itself is
/// verified separately via the P4.2 visual snapshot
/// (tests/snapshots/_actual/editor_p42.png).
[TestSuite]
public class M1AcceptanceTests
{
    private PartRegistry _registry = null!;
    private const string AcceptanceSlug = "test_m1_acceptance";

    [Before]
    public void Setup()
    {
        _registry = new PartRegistry();
        _registry.LoadLibrary();
        // Pre-clean any leftover from a previous run.
        if (RecipeStorage.Exists(AcceptanceSlug))
        {
            RecipeStorage.Delete(AcceptanceSlug);
        }
    }

    [After]
    public void Cleanup()
    {
        if (RecipeStorage.Exists(AcceptanceSlug))
        {
            RecipeStorage.Delete(AcceptanceSlug);
        }
    }

    [TestCase]
    public void M1_creature_lifecycle_save_load_round_trip()
    {
        // Step 1: build a realistic creature via the editor's data-layer API.
        var builder = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        builder.SymmetryEnabled = true;

        // Symmetric pair on shoulders (becomes 2 attachments via mirror).
        var shoulderIndices = builder.AddAttachmentMaybeMirrored(
            -1, "left_shoulder", "limb_walker",
            new Transform3D(Basis.Identity, new Vector3(0.2f, 0, 0.6f)));
        AssertThat(shoulderIndices.Length).IsEqual(2);

        // Symmetric pair on hips.
        var hipIndices = builder.AddAttachmentMaybeMirrored(
            -1, "left_hip", "limb_walker",
            new Transform3D(Basis.Identity, new Vector3(0.2f, 0, -0.5f)));
        AssertThat(hipIndices.Length).IsEqual(2);

        // Tail (no mirror).
        builder.AddAttachment(-1, "tail", "limb_tail");

        // Head + mouth chained off the head.
        int headIdx = builder.AddAttachment(-1, "head", "head_predator");
        builder.AddAttachment(headIdx, "jaw", "mouth_fang");

        // Total: 2 + 2 + 1 + 1 + 1 = 7 attachments
        AssertThat(builder.Recipe.Attachments.Count).IsEqual(7);

        // Step 2: save to user storage (the convention RecipeStorage owns).
        var saveErr = RecipeStorage.Save(builder.Recipe, AcceptanceSlug);
        AssertThat((int)saveErr).IsEqual((int)Error.Ok);

        // Step 3: confirm the on-disk recipe is under the 10 KB ceiling.
        var path = RecipeStorage.PathFor(AcceptanceSlug);
        var sizeBytes = Godot.FileAccess.GetFileAsBytes(path).Length;
        AssertThat(sizeBytes)
            .OverrideFailureMessage($"recipe is {sizeBytes} bytes, ceiling {Recipe.MaxRecipeBytes}")
            .IsLess(Recipe.MaxRecipeBytes);

        // Step 4: load it back as if we'd closed the editor and reopened.
        var loaded = RecipeStorage.Load(AcceptanceSlug);
        AssertThat(loaded).IsNotNull();

        // Step 5: every part the recipe references is in the registry, every
        // slot exists on its parent, every kind is accepted by its slot.
        var issues = RecipeValidator.Validate(loaded, _registry);
        if (issues.Count > 0)
        {
            foreach (var issue in issues)
            {
                GD.PrintErr($"  validation issue: {issue.Code} - {issue.Message}");
            }
        }
        AssertThat(issues.Count)
            .OverrideFailureMessage($"loaded recipe has {issues.Count} validation issues")
            .IsEqual(0);

        // Step 6: structural identity check - every attachment field round-trips.
        var original = builder.Recipe;
        AssertThat(loaded.FormatVersion).IsEqual(original.FormatVersion);
        AssertThat(loaded.SpinePartId).IsEqual(original.SpinePartId);
        AssertThat(loaded.Attachments.Count).IsEqual(original.Attachments.Count);

        for (int i = 0; i < loaded.Attachments.Count; i++)
        {
            var l = loaded.Attachments[i];
            var o = original.Attachments[i];
            AssertThat(l.ParentPartIndex).IsEqual(o.ParentPartIndex);
            AssertThat(l.ParentSlotName).IsEqual(o.ParentSlotName);
            AssertThat(l.ChildPartId).IsEqual(o.ChildPartId);
            AssertThat(l.LocalTransform).IsEqual(o.LocalTransform);
            AssertThat(l.MorphIndex).IsEqual(o.MorphIndex);
            AssertThat(l.MirrorGroupId).IsEqual(o.MirrorGroupId);
        }
    }

    [TestCase]
    public void M1_mirrored_pair_share_group_id_after_round_trip()
    {
        var builder = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        builder.SymmetryEnabled = true;
        var pair = builder.AddAttachmentMaybeMirrored(
            -1, "left_shoulder", "limb_walker", Transform3D.Identity);

        var slug = "test_m1_mirror_roundtrip";
        RecipeStorage.Save(builder.Recipe, slug);
        try
        {
            var loaded = RecipeStorage.Load(slug);
            var groupA = loaded.Attachments[pair[0]].MirrorGroupId;
            var groupB = loaded.Attachments[pair[1]].MirrorGroupId;
            AssertThat(groupA).IsNotEqual("");
            AssertThat(groupA).IsEqual(groupB);
        }
        finally
        {
            RecipeStorage.Delete(slug);
        }
    }
}
