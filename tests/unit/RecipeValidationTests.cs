using Godot;
using LittleGods.Creature;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

[TestSuite]
public class RecipeValidationTests
{
    private PartRegistry _registry = null!;

    [Before]
    public void Setup()
    {
        // Build a fresh registry that mirrors the disk library. We don't
        // depend on the autoload here - tests should be hermetic.
        _registry = new PartRegistry();
        _registry.LoadLibrary();
    }

    [TestCase]
    public void Valid_two_legged_predator_passes_validation()
    {
        var r = MakeValidPredator();
        var issues = RecipeValidator.Validate(r, _registry);
        if (issues.Count > 0)
        {
            foreach (var issue in issues)
            {
                GD.PrintErr($"  issue: {issue.Code} - {issue.Message}");
            }
        }
        AssertThat(issues.Count).IsEqual(0);
        AssertThat(RecipeValidator.IsValid(r, _registry)).IsTrue();
    }

    [TestCase]
    public void Empty_spine_id_is_reported()
    {
        var r = new Recipe { SpinePartId = "" };
        var issues = RecipeValidator.Validate(r, _registry);
        AssertThat(IssuesContainCode(issues, RecipeValidator.NoSpine)).IsTrue();
    }

    [TestCase]
    public void Unknown_spine_id_is_reported()
    {
        var r = new Recipe { SpinePartId = "no_such_spine" };
        var issues = RecipeValidator.Validate(r, _registry);
        AssertThat(IssuesContainCode(issues, RecipeValidator.UnknownSpine)).IsTrue();
    }

    [TestCase]
    public void Unknown_child_part_is_reported()
    {
        var r = new Recipe { SpinePartId = "spine_basic" };
        r.Attachments.Add(new Attachment
        {
            ParentPartIndex = -1,
            ParentSlotName = "head",
            ChildPartId = "no_such_part",
        });
        var issues = RecipeValidator.Validate(r, _registry);
        AssertThat(IssuesContainCode(issues, RecipeValidator.UnknownChild)).IsTrue();
    }

    [TestCase]
    public void Forward_parent_reference_is_reported()
    {
        var r = new Recipe { SpinePartId = "spine_basic" };
        // attachment 0 references parent index 5 (doesn't exist yet)
        r.Attachments.Add(new Attachment
        {
            ParentPartIndex = 5,
            ParentSlotName = "head",
            ChildPartId = "limb_walker",
        });
        var issues = RecipeValidator.Validate(r, _registry);
        AssertThat(IssuesContainCode(issues, RecipeValidator.ForwardRef) ||
                   IssuesContainCode(issues, RecipeValidator.BadParentIndex)).IsTrue();
    }

    [TestCase]
    public void Unknown_slot_name_is_reported()
    {
        var r = new Recipe { SpinePartId = "spine_basic" };
        r.Attachments.Add(new Attachment
        {
            ParentPartIndex = -1,
            ParentSlotName = "leg_42",   // not a slot on spine_basic
            ChildPartId = "limb_walker",
        });
        var issues = RecipeValidator.Validate(r, _registry);
        AssertThat(IssuesContainCode(issues, RecipeValidator.UnknownSlot)).IsTrue();
    }

    [TestCase]
    public void Kind_mismatch_with_slot_mask_is_reported()
    {
        // Try to attach a Limb to the spine's "head" slot (which accepts only Head kind)
        var r = new Recipe { SpinePartId = "spine_basic" };
        r.Attachments.Add(new Attachment
        {
            ParentPartIndex = -1,
            ParentSlotName = "head",
            ChildPartId = "limb_walker",
        });
        var issues = RecipeValidator.Validate(r, _registry);
        AssertThat(IssuesContainCode(issues, RecipeValidator.KindNotAllowed)).IsTrue();
    }

    [TestCase]
    public void Twenty_attachment_creature_still_under_10kb()
    {
        var r = new Recipe { SpinePartId = "spine_basic" };
        // 20 limbs distributed across the spine's 4 limb slots.
        // Spread positions so each attachment is structurally distinct.
        string[] slots = { "left_shoulder", "right_shoulder", "left_hip", "right_hip" };
        for (int i = 0; i < 20; i++)
        {
            r.Attachments.Add(new Attachment
            {
                ParentPartIndex = -1,
                ParentSlotName = slots[i % slots.Length],
                ChildPartId = "limb_walker",
                LocalTransform = new Transform3D(Basis.Identity, new Vector3(0.1f * i, 0, 0.05f * i)),
            });
        }

        var slug = "test_validation_big_creature";
        RecipeStorage.Save(r, slug);
        try
        {
            var path = RecipeStorage.PathFor(slug);
            var bytes = Godot.FileAccess.GetFileAsBytes(path);
            AssertThat(bytes.Length)
                .OverrideFailureMessage($"20-attachment recipe is {bytes.Length} bytes (limit {Recipe.MaxRecipeBytes})")
                .IsLess(Recipe.MaxRecipeBytes);
        }
        finally
        {
            RecipeStorage.Delete(slug);
        }
    }

    // ---- helpers ----

    private static bool IssuesContainCode(System.Collections.Generic.IEnumerable<RecipeValidator.Issue> issues, string code)
    {
        foreach (var i in issues)
        {
            if (i.Code == code) return true;
        }
        return false;
    }

    private static Recipe MakeValidPredator()
    {
        var r = new Recipe { SpinePartId = "spine_basic" };
        // Walker limbs on all four limb slots
        r.Attachments.Add(new Attachment { ParentPartIndex = -1, ParentSlotName = "left_shoulder",  ChildPartId = "limb_walker" });
        r.Attachments.Add(new Attachment { ParentPartIndex = -1, ParentSlotName = "right_shoulder", ChildPartId = "limb_walker" });
        r.Attachments.Add(new Attachment { ParentPartIndex = -1, ParentSlotName = "left_hip",       ChildPartId = "limb_walker" });
        r.Attachments.Add(new Attachment { ParentPartIndex = -1, ParentSlotName = "right_hip",      ChildPartId = "limb_walker" });
        // Tail at the tail slot
        r.Attachments.Add(new Attachment { ParentPartIndex = -1, ParentSlotName = "tail",           ChildPartId = "limb_tail" });
        // Head at the head slot, fang mouth attached to head's jaw
        r.Attachments.Add(new Attachment { ParentPartIndex = -1, ParentSlotName = "head",           ChildPartId = "head_predator" });
        r.Attachments.Add(new Attachment { ParentPartIndex = 5,  ParentSlotName = "jaw",            ChildPartId = "mouth_fang" });
        return r;
    }
}
