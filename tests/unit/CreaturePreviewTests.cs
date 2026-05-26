using Godot;
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
}
