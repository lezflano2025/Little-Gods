using Godot;
using LittleGods.Anim;
using LittleGods.Creature;
using LittleGods.Mesh;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// M3 P2 — see docs/m3-contract.md §"Agent B — Limb-type classifier".
///
/// Verifies that LimbClassifier.Classify produces a LimbType array that is
/// parallel to skeleton.LimbChains and correctly identifies Leg, Wing, Tail,
/// and the edge cases (no chains, determinism).
[TestSuite]
public class LimbClassifierTests
{
    private PartRegistry _registry = null!;

    [Before]
    public void Setup()
    {
        _registry = new PartRegistry();
        _registry.LoadLibrary();
    }

    // -----------------------------------------------------------------------
    // Case 1: Quadruped (2 shoulder + 2 hip limb_walker via symmetry)
    //         All four chains must classify as Leg.
    // -----------------------------------------------------------------------
    [TestCase]
    public void Quadruped_all_four_chains_classify_as_Leg()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.SymmetryEnabled = true;
        b.AddAttachmentMaybeMirrored(-1, "left_shoulder", "limb_walker", Transform3D.Identity);
        b.AddAttachmentMaybeMirrored(-1, "left_hip",      "limb_walker", Transform3D.Identity);

        var skel  = SkeletonResolver.Resolve(b.Recipe, _registry);
        var types = LimbClassifier.Classify(b.Recipe, _registry, skel);

        AssertThat(skel.LimbChains.Length).IsEqual(4);
        AssertThat(types.Length).IsEqual(skel.LimbChains.Length);
        foreach (var t in types)
        {
            AssertThat(t).IsEqual(LimbType.Leg);
        }
    }

    // -----------------------------------------------------------------------
    // Case 2: Wing on a shoulder slot classifies as Wing, not Leg.
    // -----------------------------------------------------------------------
    [TestCase]
    public void Wing_part_on_shoulder_classifies_as_Wing()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.AddAttachment(-1, "left_shoulder", "limb_wing");

        var skel  = SkeletonResolver.Resolve(b.Recipe, _registry);
        var types = LimbClassifier.Classify(b.Recipe, _registry, skel);

        AssertThat(skel.LimbChains.Length).IsGreater(0);
        AssertThat(types.Length).IsEqual(skel.LimbChains.Length);

        // The wing chain must be Wing, not Leg.
        bool hasWing = false;
        foreach (var t in types)
        {
            if (t == LimbType.Wing) { hasWing = true; }
            AssertThat(t).IsNotEqual(LimbType.Leg);
        }
        AssertThat(hasWing).IsTrue();
    }

    // -----------------------------------------------------------------------
    // Case 3: Tail slot classifies as Tail.
    // -----------------------------------------------------------------------
    [TestCase]
    public void Tail_slot_classifies_as_Tail()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.AddAttachment(-1, "tail", "limb_tail");

        var skel  = SkeletonResolver.Resolve(b.Recipe, _registry);
        var types = LimbClassifier.Classify(b.Recipe, _registry, skel);

        AssertThat(skel.LimbChains.Length).IsGreater(0);
        AssertThat(types.Length).IsEqual(skel.LimbChains.Length);
        AssertThat(types[0]).IsEqual(LimbType.Tail);
    }

    // -----------------------------------------------------------------------
    // Case 4: Determinism — two calls on the same inputs produce equal arrays.
    // -----------------------------------------------------------------------
    [TestCase]
    public void Classify_is_deterministic()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.SymmetryEnabled = true;
        b.AddAttachmentMaybeMirrored(-1, "left_shoulder", "limb_walker", Transform3D.Identity);
        b.AddAttachmentMaybeMirrored(-1, "left_hip",      "limb_walker", Transform3D.Identity);
        b.AddAttachment(-1, "tail", "limb_tail");

        var skel   = SkeletonResolver.Resolve(b.Recipe, _registry);
        var first  = LimbClassifier.Classify(b.Recipe, _registry, skel);
        var second = LimbClassifier.Classify(b.Recipe, _registry, skel);

        AssertThat(first.Length).IsEqual(second.Length);
        for (int i = 0; i < first.Length; i++)
        {
            AssertThat(first[i]).IsEqual(second[i]);
        }
    }

    // -----------------------------------------------------------------------
    // Case 5: Spine-only recipe (no attachments) -> empty array.
    // -----------------------------------------------------------------------
    [TestCase]
    public void Spine_only_recipe_returns_empty_array()
    {
        var recipe = new Recipe { SpinePartId = "spine_basic" };
        var skel   = SkeletonResolver.Resolve(recipe, _registry);
        var types  = LimbClassifier.Classify(recipe, _registry, skel);

        AssertThat(skel.LimbChains.Length).IsEqual(0);
        AssertThat(types.Length).IsEqual(0);
    }
}
