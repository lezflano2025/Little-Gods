using Godot;
using LittleGods.Creature;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

[TestSuite]
public class RecipeBuilderTests
{
    private PartRegistry _registry = null!;

    [Before]
    public void Setup()
    {
        _registry = new PartRegistry();
        _registry.LoadLibrary();
    }

    [TestCase]
    public void Add_attachment_appends_to_recipe()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        var idx = b.AddAttachment(-1, "head", "head_predator");
        AssertThat(idx).IsEqual(0);
        AssertThat(b.Recipe.Attachments.Count).IsEqual(1);
        AssertThat(b.Recipe.Attachments[0].ChildPartId).IsEqual("head_predator");
        AssertThat(b.Revision).IsEqual(1ul);
    }

    [TestCase]
    public void Symmetry_off_only_places_one_attachment()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.SymmetryEnabled = false;
        var indices = b.AddAttachmentMaybeMirrored(-1, "left_shoulder", "limb_walker", Transform3D.Identity);
        AssertThat(indices.Length).IsEqual(1);
        AssertThat(b.Recipe.Attachments.Count).IsEqual(1);
    }

    [TestCase]
    public void Symmetry_on_mirrors_left_to_right_with_group_id()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.SymmetryEnabled = true;
        var transform = new Transform3D(Basis.Identity, new Vector3(0.3f, 0, 0.6f));
        var indices = b.AddAttachmentMaybeMirrored(-1, "left_shoulder", "limb_walker", transform);

        AssertThat(indices.Length).IsEqual(2);
        AssertThat(b.Recipe.Attachments.Count).IsEqual(2);

        var primary = b.Recipe.Attachments[indices[0]];
        var mirror  = b.Recipe.Attachments[indices[1]];

        AssertThat(primary.ParentSlotName).IsEqual("left_shoulder");
        AssertThat(mirror.ParentSlotName).IsEqual("right_shoulder");
        AssertThat(mirror.LocalTransform.Origin.X).IsEqual(-primary.LocalTransform.Origin.X);

        AssertThat(primary.MirrorGroupId).IsNotEqual("");
        AssertThat(primary.MirrorGroupId).IsEqual(mirror.MirrorGroupId);
    }

    [TestCase]
    public void Symmetry_on_with_non_mirrorable_slot_only_places_one()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.SymmetryEnabled = true;
        // "head" doesn't start with left_/right_, so no mirror possible
        var indices = b.AddAttachmentMaybeMirrored(-1, "head", "head_predator", Transform3D.Identity);
        AssertThat(indices.Length).IsEqual(1);
        AssertThat(b.Recipe.Attachments.Count).IsEqual(1);
        AssertThat(b.Recipe.Attachments[0].MirrorGroupId).IsEqual("");
    }

    [TestCase]
    public void MirrorSlotName_handles_left_and_right_and_neither()
    {
        AssertThat(RecipeBuilder.MirrorSlotName("left_shoulder")).IsEqual("right_shoulder");
        AssertThat(RecipeBuilder.MirrorSlotName("right_hip")).IsEqual("left_hip");
        AssertThat(RecipeBuilder.MirrorSlotName("head")).IsNull();
        AssertThat(RecipeBuilder.MirrorSlotName("tail")).IsNull();
    }

    [TestCase]
    public void Remove_attachment_clears_mirror_partner_group_id()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.SymmetryEnabled = true;
        var indices = b.AddAttachmentMaybeMirrored(-1, "left_shoulder", "limb_walker", Transform3D.Identity);
        AssertThat(indices.Length).IsEqual(2);

        b.RemoveAttachment(indices[0]);
        // After removing index 0, the former index 1 is now at index 0
        AssertThat(b.Recipe.Attachments.Count).IsEqual(1);
        AssertThat(b.Recipe.Attachments[0].MirrorGroupId).IsEqual("");
    }

    [TestCase]
    public void Remove_attachment_orphans_children_to_spine()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        var headIdx = b.AddAttachment(-1, "head", "head_predator");
        var mouthIdx = b.AddAttachment(headIdx, "jaw", "mouth_fang");

        b.RemoveAttachment(headIdx);
        // mouth is left over, now at index 0, parent must be -1 (spine)
        AssertThat(b.Recipe.Attachments.Count).IsEqual(1);
        AssertThat(b.Recipe.Attachments[0].ChildPartId).IsEqual("mouth_fang");
        AssertThat(b.Recipe.Attachments[0].ParentPartIndex).IsEqual(-1);
    }

    [TestCase]
    public void Remove_attachment_decrements_higher_parent_indices()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        var a = b.AddAttachment(-1, "head", "head_predator");
        var bIdx = b.AddAttachment(-1, "tail", "limb_tail");
        var c = b.AddAttachment(bIdx, "tip", "limb_walker");  // c references b (index 1)

        b.RemoveAttachment(a);
        // a was index 0; b was index 1; c was index 2.
        // After remove: b is now at index 0, c at index 1.
        // c's parent was 1, now must be 0.
        AssertThat(b.Recipe.Attachments.Count).IsEqual(2);
        AssertThat(b.Recipe.Attachments[1].ParentPartIndex).IsEqual(0);
    }

    [TestCase]
    public void SiblingsInMirrorGroup_returns_other_members_only()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.SymmetryEnabled = true;
        var pair = b.AddAttachmentMaybeMirrored(-1, "left_shoulder", "limb_walker", Transform3D.Identity);

        var siblingsOfPrimary = b.SiblingsInMirrorGroup(pair[0]);
        AssertThat(siblingsOfPrimary.Count).IsEqual(1);
        AssertThat(siblingsOfPrimary[0]).IsEqual(pair[1]);
    }
}
