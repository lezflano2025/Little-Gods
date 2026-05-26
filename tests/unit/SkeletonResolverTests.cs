using Godot;
using LittleGods.Anim;
using LittleGods.Creature;
using LittleGods.Mesh;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// SkeletonResolver turns a Recipe into world-space bones. Loads the real
/// Rigblock library, so it exercises the .tres bone fields (BoneLength /
/// RadiusStart / RadiusEnd).
///
/// M3 (ADR-0003): PartKind.Limb parts resolve to a two-bone chain, so bones are
/// no longer 1:1 with attachments — tests use LimbChains and ParentIndex.
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

    /// First limb chain attached at the given slot (order-independent lookup).
    private static LimbChain ChainForSlot(CreatureSkeleton skel, string slot)
    {
        foreach (var c in skel.LimbChains)
        {
            if (c.SlotName == slot)
            {
                return c;
            }
        }
        return default;
    }

    [TestCase]
    public void Single_spine_yields_one_centred_root_bone()
    {
        var recipe = new Recipe { SpinePartId = "spine_basic" };
        var skel = SkeletonResolver.Resolve(recipe, _registry);

        AssertThat(skel.Count).IsEqual(1);
        AssertThat(skel.LimbChains.Length).IsEqual(0);
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

        // head is PartKind.Head -> a single bone (not a chain).
        AssertThat(skel.Count).IsEqual(2);
        AssertThat(skel.LimbChains.Length).IsEqual(0);
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
    public void Limb_part_resolves_to_a_two_bone_chain()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.AddAttachment(-1, "left_hip", "limb_runner");

        var skel = SkeletonResolver.Resolve(b.Recipe, _registry);

        // spine + upper + lower = 3 bones; exactly one limb chain.
        AssertThat(skel.Count).IsEqual(3);
        AssertThat(skel.LimbChains.Length).IsEqual(1);

        var chain = skel.LimbChains[0];
        AssertThat(chain.AttachmentIndex).IsEqual(0);
        AssertThat(chain.RootBone).IsEqual(1);
        AssertThat(chain.KneeBone).IsEqual(2);
        AssertThat(chain.FootBone).IsEqual(2);
        AssertThat(chain.SlotName).IsEqual("left_hip");

        // upper parents to the root spine, lower parents to the upper.
        AssertThat(skel.Bones[1].ParentIndex).IsEqual(0);
        AssertThat(skel.Bones[2].ParentIndex).IsEqual(1);

        // The knee is the shared joint (upper.Tail == lower.Head).
        AssertFloat(skel.Bones[1].Tail.DistanceTo(skel.Bones[2].Head)).IsEqualApprox(0f, 1e-5f);

        // Colinear at rest: upper and lower point the same way (straight limb).
        var hipToKnee = (skel.Bones[1].Tail - skel.Bones[1].Head).Normalized();
        var kneeToFoot = (skel.Bones[2].Tail - skel.Bones[2].Head).Normalized();
        AssertFloat(hipToKnee.Dot(kneeToFoot)).IsEqualApprox(1f, 1e-4f);
    }

    [TestCase]
    public void Child_of_a_limb_parents_to_the_limb_foot_bone()
    {
        // A part attached to a limb must parent to the limb's TIP bone (foot),
        // proving the attachment->tip-bone map (never "attachment + 1").
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        int limb = b.AddAttachment(-1, "left_hip", "limb_runner");   // bones 1 (upper), 2 (lower)
        b.AddAttachment(limb, "tip", "mouth_fang");                  // bone 3, child of the limb

        var skel = SkeletonResolver.Resolve(b.Recipe, _registry);

        AssertThat(skel.Count).IsEqual(4);
        AssertThat(skel.Bones[3].ParentIndex).IsEqual(2); // the foot (lower) bone, not the upper
    }

    [TestCase]
    public void Mirrored_limbs_split_into_chains_and_are_x_flipped()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.SymmetryEnabled = true;
        b.AddAttachmentMaybeMirrored(-1, "left_shoulder", "limb_walker", Transform3D.Identity);

        var skel = SkeletonResolver.Resolve(b.Recipe, _registry);

        // spine (1) + two limbs x 2 bones (4) = 5 bones; 2 chains.
        AssertThat(skel.Count).IsEqual(5);
        AssertThat(skel.LimbChains.Length).IsEqual(2);

        var left = skel.Bones[skel.LimbChains[0].RootBone];
        var right = skel.Bones[skel.LimbChains[1].RootBone];
        // left_shoulder x=-0.3, right_shoulder x=+0.3.
        AssertFloat(left.Head.X).IsEqualApprox(-0.3f, 1e-4f);
        AssertFloat(right.Head.X).IsEqualApprox(0.3f, 1e-4f);
        AssertFloat(left.Head.X).IsEqualApprox(-right.Head.X, 1e-4f);
    }

    [TestCase]
    public void Morph_stretch_scales_limb_chain_length_and_radius()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        int idx = b.AddAttachment(-1, "left_shoulder", "limb_walker");
        b.Recipe.Morphs.Add(new Morph { Stretch = new Vector3(2f, 2f, 3f) });
        b.Recipe.Attachments[idx].MorphIndex = 0;

        var skel = SkeletonResolver.Resolve(b.Recipe, _registry);

        var chain = skel.LimbChains[0];
        // limb_walker BoneLength 1.2 * stretch.Z 3 = 3.6 total, split 50/50.
        AssertFloat(chain.TotalLength).IsEqualApprox(3.6f, 1e-3f);
        AssertFloat(chain.UpperLength).IsEqualApprox(1.8f, 1e-3f);
        AssertFloat(chain.LowerLength).IsEqualApprox(1.8f, 1e-3f);

        var upper = skel.Bones[chain.RootBone];
        // RadiusStart 0.22 * ((2+2)/2)=0.44 at the hip.
        AssertFloat(upper.RadiusHead).IsEqualApprox(0.44f, 1e-3f);
    }

    [TestCase]
    public void Limb_chains_extend_along_their_slot_normal()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.AddAttachment(-1, "tail", "limb_tail");             // slot normal (0,0,-1)
        b.AddAttachment(-1, "left_shoulder", "limb_walker");  // slot normal (-1,0,0)

        var skel = SkeletonResolver.Resolve(b.Recipe, _registry);
        AssertThat(skel.LimbChains.Length).IsEqual(2);

        // Tail chain grows toward -Z (backward), not onto world +Z.
        var tailRoot = skel.Bones[ChainForSlot(skel, "tail").RootBone];
        AssertThat(tailRoot.Tail.Z - tailRoot.Head.Z < -0.1f).IsTrue();

        // Left shoulder chain grows toward -X (splays left).
        var shoulderRoot = skel.Bones[ChainForSlot(skel, "left_shoulder").RootBone];
        AssertThat(shoulderRoot.Tail.X - shoulderRoot.Head.X < -0.1f).IsTrue();
    }

    [TestCase]
    public void Quadruped_resolves_expected_bone_count_and_four_chains()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.SymmetryEnabled = true;
        b.AddAttachmentMaybeMirrored(-1, "left_shoulder", "limb_walker", Transform3D.Identity); // front pair
        b.AddAttachmentMaybeMirrored(-1, "left_hip", "limb_runner", Transform3D.Identity);      // rear pair

        var skel = SkeletonResolver.Resolve(b.Recipe, _registry);

        // spine (1) + 4 legs x 2 bones (8) = 9 bones; 4 chains.
        AssertThat(skel.Count).IsEqual(9);
        AssertThat(skel.LimbChains.Length).IsEqual(4);

        // Every chain references valid, distinct upper/lower bones.
        foreach (var c in skel.LimbChains)
        {
            AssertThat(c.RootBone).IsGreater(0);
            AssertThat(c.KneeBone).IsEqual(c.RootBone + 1);
            AssertThat(skel.Bones[c.KneeBone].ParentIndex).IsEqual(c.RootBone);
        }
    }

    [TestCase]
    public void Unknown_spine_yields_empty_skeleton()
    {
        var recipe = new Recipe { SpinePartId = "does_not_exist" };
        var skel = SkeletonResolver.Resolve(recipe, _registry);
        AssertThat(skel.Count).IsEqual(0);
        AssertThat(skel.LimbChains.Length).IsEqual(0);
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
        AssertThat(a.LimbChains.Length).IsEqual(c.LimbChains.Length);
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
