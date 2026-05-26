using Godot;
using LittleGods.Anim;
using LittleGods.Creature;
using LittleGods.Mesh;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// M2 P3: CreaturePreview exposes an instance Rebuild() so GDScript can
/// trigger mesh generation without calling static C# methods.
///
/// Memory discipline:
///   - CreaturePreview is a Node3D — every instance is wrapped in AutoFree so
///     GdUnit4 cleans up the node and its MeshInstance3D child together.
///   - PartRegistry is a Node — also wrapped in AutoFree.
[TestSuite]
public class CreaturePreviewTests
{
    private PartRegistry _registry = null!;

    [Before]
    public void Setup()
    {
        _registry = new PartRegistry();
        _registry.LoadLibrary();
    }

    // -------------------------------------------------------------------------
    // 7-attachment fixture (mirrored from M1AcceptanceTests)
    // -------------------------------------------------------------------------

    private static Recipe BuildSevenAttachmentRecipe(PartRegistry registry)
    {
        var builder = RecipeBuilder.ForNewCreature("spine_basic", registry);
        builder.SymmetryEnabled = true;

        builder.AddAttachmentMaybeMirrored(
            -1, "left_shoulder", "limb_walker",
            new Transform3D(Basis.Identity, new Vector3(0.2f, 0f, 0.6f)));

        builder.AddAttachmentMaybeMirrored(
            -1, "left_hip", "limb_walker",
            new Transform3D(Basis.Identity, new Vector3(0.2f, 0f, -0.5f)));

        builder.AddAttachment(-1, "tail", "limb_tail");

        int headIdx = builder.AddAttachment(-1, "head", "head_predator");
        builder.AddAttachment(headIdx, "jaw", "mouth_fang");

        return builder.Recipe;
    }

    // -------------------------------------------------------------------------
    // Test: valid 7-attachment creature produces geometry
    // -------------------------------------------------------------------------

    [TestCase]
    public void Rebuild_seven_attachment_creature_produces_geometry()
    {
        var preview = AutoFree(new CreaturePreview())!;

        // Use a coarser cell size so the test runs quickly.
        preview.SetCellSize(0.15f);

        var recipe = BuildSevenAttachmentRecipe(_registry);
        preview.Rebuild(recipe, _registry);

        AssertThat(preview.LastVertexCount).IsGreater(0);
        AssertThat(preview.LastTriangleCount).IsGreater(0);
    }

    // -------------------------------------------------------------------------
    // Test: unknown spine → zero geometry, no exception
    // -------------------------------------------------------------------------

    [TestCase]
    public void Rebuild_unknown_spine_yields_zero_geometry_no_crash()
    {
        var preview = AutoFree(new CreaturePreview())!;

        var badRecipe = new Recipe { SpinePartId = "nope" };
        preview.Rebuild(badRecipe, _registry);

        AssertThat(preview.LastVertexCount).IsEqual(0);
        AssertThat(preview.LastTriangleCount).IsEqual(0);
    }

    // -------------------------------------------------------------------------
    // Test: second Rebuild replaces the mesh cleanly
    // -------------------------------------------------------------------------

    [TestCase]
    public void Rebuild_called_twice_replaces_mesh_cleanly()
    {
        var preview = AutoFree(new CreaturePreview())!;

        preview.SetCellSize(0.15f);

        var recipe = BuildSevenAttachmentRecipe(_registry);

        // First Rebuild.
        preview.Rebuild(recipe, _registry);
        int firstVerts = preview.LastVertexCount;
        AssertThat(firstVerts).IsGreater(0);

        // Second Rebuild with the same recipe — mesh replaces without orphan or exception.
        preview.Rebuild(recipe, _registry);
        AssertThat(preview.LastVertexCount).IsGreater(0);
    }

    // -------------------------------------------------------------------------
    // M3 P0: posing the skeleton drives the skin.
    // -------------------------------------------------------------------------

    private static Recipe BuildOneLimbRecipe(PartRegistry registry)
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", registry);
        b.AddAttachment(-1, "left_hip", "limb_runner"); // one 2-bone limb chain
        return b.Recipe;
    }

    /// Global pose accumulated from the skeleton's LOCAL poses up the parent
    /// chain. Unlike Skeleton3D.GetBoneGlobalPose this reflects SetBonePose*
    /// immediately even off-tree (the cached global is only recomputed in-tree),
    /// which is what unit tests need.
    private static Transform3D GlobalPose(Skeleton3D s, int bone)
    {
        Transform3D t = s.GetBonePose(bone);
        int p = s.GetBoneParent(bone);
        while (p >= 0)
        {
            t = s.GetBonePose(p) * t;
            p = s.GetBoneParent(p);
        }
        return t;
    }

    /// World-space foot tip = lower bone origin transformed by its global pose,
    /// offset along the bone's local +Z by the lower segment length.
    private static Vector3 FootTip(Skeleton3D s, LimbChain chain)
        => GlobalPose(s, chain.FootBone) * new Vector3(0f, 0f, chain.LowerLength);

    [TestCase]
    public void ApplyPose_rest_is_idempotent_at_the_foot()
    {
        var preview = AutoFree(new CreaturePreview())!;
        preview.SetCellSize(0.2f);

        var recipe = BuildOneLimbRecipe(_registry);
        preview.Rebuild(recipe, _registry);

        var chain = SkeletonResolver.Resolve(recipe, _registry).LimbChains[0];
        var skel = preview.Skeleton!;

        preview.ApplyPose(Pose.Rest(skel.GetBoneCount()));        Vector3 first = GlobalPose(skel, chain.FootBone).Origin;

        preview.ApplyPose(Pose.Rest(skel.GetBoneCount()));        Vector3 second = GlobalPose(skel, chain.FootBone).Origin;

        AssertFloat(first.DistanceTo(second)).IsEqualApprox(0f, 1e-5f);
    }

    [TestCase]
    public void Bending_the_knee_swings_the_foot_about_a_fixed_knee_joint()
    {
        var preview = AutoFree(new CreaturePreview())!;
        preview.SetCellSize(0.2f);

        var recipe = BuildOneLimbRecipe(_registry);
        preview.Rebuild(recipe, _registry);

        var chain = SkeletonResolver.Resolve(recipe, _registry).LimbChains[0];
        var skel = preview.Skeleton!;

        preview.ApplyPose(Pose.Rest(skel.GetBoneCount()));        Vector3 restKnee = GlobalPose(skel, chain.KneeBone).Origin; // knee joint
        Vector3 restFoot = FootTip(skel, chain);

        AssertThat(preview.BendFirstKnee(Mathf.Pi / 3f)).IsTrue(); // 60 degrees
        // The knee joint (lower-bone origin) is the pivot — it does not move;
        // the foot tip swings well clear of its rest position.
        AssertFloat(restKnee.DistanceTo(GlobalPose(skel, chain.KneeBone).Origin))
            .IsEqualApprox(0f, 1e-3f);
        AssertThat(restFoot.DistanceTo(FootTip(skel, chain)) > 0.1f).IsTrue();
    }
}
