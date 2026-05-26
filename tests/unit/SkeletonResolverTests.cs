using Godot;
using LittleGods.Creature;
using LittleGods.Mesh;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// M2 P0: SkeletonResolver turns a Recipe into world-space bones.
/// Loads the real Rigblock library, so it exercises the regenerated .tres
/// bone fields (BoneLength / RadiusStart / RadiusEnd).
[TestSuite]
public class SkeletonResolverTests
{
    private PartRegistry _registry = null!;

    [Before]
    public void Setup()
    {
        _registry = new PartRegistry();
        _registry.LoadLibrary();
    }

    [TestCase]
    public void Single_spine_yields_one_centred_root_bone()
    {
        var recipe = new Recipe { SpinePartId = "spine_basic" };
        var skel = SkeletonResolver.Resolve(recipe, _registry);

        AssertThat(skel.Count).IsEqual(1);
        var root = skel.Bones[0];
        AssertThat(root.ParentIndex).IsEqual(-1);
        // spine_basic.BoneLength == 2.0, centred on +Z -> head z=-1, tail z=+1.
        AssertFloat(root.Head.Z).IsEqualApprox(-1.0f, 1e-4f);
        AssertFloat(root.Tail.Z).IsEqualApprox(1.0f, 1e-4f);
        AssertFloat(root.Length).IsEqualApprox(2.0f, 1e-4f);
        AssertFloat(root.RadiusHead).IsEqualApprox(0.45f, 1e-4f);
    }

    [TestCase]
    public void Spine_plus_head_yields_two_bones_chained_to_root()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.AddAttachment(-1, "head", "head_predator");

        var skel = SkeletonResolver.Resolve(b.Recipe, _registry);

        AssertThat(skel.Count).IsEqual(2);
        var headBone = skel.Bones[1];
        AssertThat(headBone.ParentIndex).IsEqual(0);
        // "head" slot is at (0,0,1); bone head sits on that anchor.
        AssertFloat(headBone.Head.Z).IsEqualApprox(1.0f, 1e-4f);
        // head_predator.BoneLength == 0.7 along +Z.
        AssertFloat(headBone.Tail.Z).IsEqualApprox(1.7f, 1e-4f);
    }

    [TestCase]
    public void Child_anchors_in_parents_frame_not_world_origin()
    {
        // spine -> head (slot z=1) -> mouth (head's "jaw" slot at z=0.4).
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        int head = b.AddAttachment(-1, "head", "head_predator");
        b.AddAttachment(head, "jaw", "mouth_fang");

        var skel = SkeletonResolver.Resolve(b.Recipe, _registry);

        AssertThat(skel.Count).IsEqual(3);
        AssertThat(skel.Bones[1].ParentIndex).IsEqual(0);
        AssertThat(skel.Bones[2].ParentIndex).IsEqual(1);
        // jaw anchor = head anchor (z=1) composed with jaw slot (z=0.4) -> z=1.4.
        AssertFloat(skel.Bones[2].Head.Z).IsEqualApprox(1.4f, 1e-4f);
    }

    [TestCase]
    public void Mirrored_limbs_are_x_flipped()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.SymmetryEnabled = true;
        b.AddAttachmentMaybeMirrored(-1, "left_shoulder", "limb_walker", Transform3D.Identity);

        var skel = SkeletonResolver.Resolve(b.Recipe, _registry);

        AssertThat(skel.Count).IsEqual(3);
        var left = skel.Bones[1];
        var right = skel.Bones[2];
        // left_shoulder x=-0.3, right_shoulder x=+0.3.
        AssertFloat(left.Head.X).IsEqualApprox(-0.3f, 1e-4f);
        AssertFloat(right.Head.X).IsEqualApprox(0.3f, 1e-4f);
        AssertFloat(left.Head.X).IsEqualApprox(-right.Head.X, 1e-4f);
    }

    [TestCase]
    public void Morph_stretch_scales_bone_length_and_radius()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        int idx = b.AddAttachment(-1, "left_shoulder", "limb_walker");
        b.Recipe.Morphs.Add(new Morph { Stretch = new Vector3(2f, 2f, 3f) });
        b.Recipe.Attachments[idx].MorphIndex = 0;

        var skel = SkeletonResolver.Resolve(b.Recipe, _registry);

        var limb = skel.Bones[idx + 1];
        // limb_walker: BoneLength 1.2 * stretch.Z 3 = 3.6; RadiusStart 0.22 * ((2+2)/2)=0.44.
        AssertFloat(limb.Length).IsEqualApprox(3.6f, 1e-3f);
        AssertFloat(limb.RadiusHead).IsEqualApprox(0.44f, 1e-3f);
    }

    [TestCase]
    public void Bones_extend_along_their_slot_normal()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        int tailIdx = b.AddAttachment(-1, "tail", "limb_tail");            // slot normal (0,0,-1)
        int shoulderIdx = b.AddAttachment(-1, "left_shoulder", "limb_walker"); // slot normal (-1,0,0)

        var skel = SkeletonResolver.Resolve(b.Recipe, _registry);

        // Tail grows toward -Z (backward), not onto world +Z.
        var tail = skel.Bones[tailIdx + 1];
        AssertThat(tail.Tail.Z - tail.Head.Z < -0.1f).IsTrue();

        // Left shoulder limb grows toward -X (splays left).
        var shoulder = skel.Bones[shoulderIdx + 1];
        AssertThat(shoulder.Tail.X - shoulder.Head.X < -0.1f).IsTrue();
    }

    [TestCase]
    public void Unknown_spine_yields_empty_skeleton()
    {
        var recipe = new Recipe { SpinePartId = "does_not_exist" };
        var skel = SkeletonResolver.Resolve(recipe, _registry);
        AssertThat(skel.Count).IsEqual(0);
    }

    [TestCase]
    public void Resolution_is_deterministic()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.SymmetryEnabled = true;
        b.AddAttachmentMaybeMirrored(-1, "left_shoulder", "limb_walker", Transform3D.Identity);
        b.AddAttachmentMaybeMirrored(-1, "left_hip", "limb_runner", Transform3D.Identity);
        b.AddAttachment(-1, "head", "head_predator");

        var a = SkeletonResolver.Resolve(b.Recipe, _registry);
        var c = SkeletonResolver.Resolve(b.Recipe, _registry);

        AssertThat(a.Count).IsEqual(c.Count);
        for (int i = 0; i < a.Count; i++)
        {
            AssertFloat(a.Bones[i].Head.X).IsEqualApprox(c.Bones[i].Head.X, 1e-6f);
            AssertFloat(a.Bones[i].Head.Y).IsEqualApprox(c.Bones[i].Head.Y, 1e-6f);
            AssertFloat(a.Bones[i].Head.Z).IsEqualApprox(c.Bones[i].Head.Z, 1e-6f);
            AssertFloat(a.Bones[i].Tail.X).IsEqualApprox(c.Bones[i].Tail.X, 1e-6f);
            AssertFloat(a.Bones[i].Tail.Y).IsEqualApprox(c.Bones[i].Tail.Y, 1e-6f);
            AssertFloat(a.Bones[i].Tail.Z).IsEqualApprox(c.Bones[i].Tail.Z, 1e-6f);
            AssertThat(a.Bones[i].ParentIndex).IsEqual(c.Bones[i].ParentIndex);
        }
    }

    [TestCase]
    public void Skeleton_bounds_enclose_all_bones()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.AddAttachment(-1, "head", "head_predator");
        var skel = SkeletonResolver.Resolve(b.Recipe, _registry);

        var bounds = skel.Bounds;
        foreach (var bone in skel.Bones)
        {
            AssertThat(bounds.HasPoint(bone.Head)).IsTrue();
            AssertThat(bounds.HasPoint(bone.Tail)).IsTrue();
        }
    }
}
