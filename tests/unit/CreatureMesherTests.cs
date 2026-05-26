using Godot;
using LittleGods.Creature;
using LittleGods.Mesh;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// M2 P2: CreatureMesher wires Recipe → SkeletonResolver → MetaballField
/// → MarchingCubes → AutoSkinner and returns a CreatureMeshResult.
///
/// Uses a COARSE cell size (0.15 f) so the full pipeline runs quickly in CI
/// without sacrificing meaningful structural validation.
[TestSuite]
public class CreatureMesherTests
{
    private PartRegistry _registry = null!;

    // Coarse grid for fast tests; still fine enough to produce a non-empty mesh
    // for the 7-attachment creature (body diameter ≈ 0.9 u, 0.15 cell < half of that).
    private static readonly GridParams CoarseGrid = new GridParams(0.15f, 0.5f);

    [Before]
    public void Setup()
    {
        _registry = new PartRegistry();
        _registry.LoadLibrary();
    }

    // -----------------------------------------------------------------------
    // Helper: construct the canonical 7-attachment creature used in M1 tests.
    //   2 shoulders (mirrored) + 2 hips (mirrored) + tail + head + jaw = 7.
    // -----------------------------------------------------------------------
    private Recipe Build7AttachmentRecipe()
    {
        var builder = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        builder.SymmetryEnabled = true;

        builder.AddAttachmentMaybeMirrored(
            -1, "left_shoulder", "limb_walker",
            new Transform3D(Basis.Identity, new Vector3(0.2f, 0f, 0.6f)));

        builder.AddAttachmentMaybeMirrored(
            -1, "left_hip", "limb_walker",
            new Transform3D(Basis.Identity, new Vector3(0.2f, 0f, -0.5f)));

        builder.AddAttachment(-1, "tail",  "limb_tail");

        int headIdx = builder.AddAttachment(-1, "head", "head_predator");
        builder.AddAttachment(headIdx, "jaw", "mouth_fang");

        AssertThat(builder.Recipe.Attachments.Count).IsEqual(7);
        return builder.Recipe;
    }

    // -----------------------------------------------------------------------
    // 1. Mesh is non-empty and structurally valid.
    // -----------------------------------------------------------------------
    [TestCase]
    public void Full_creature_produces_non_empty_well_formed_mesh()
    {
        var recipe = Build7AttachmentRecipe();
        var result = CreatureMesher.Build(recipe, _registry, CoarseGrid);

        AssertThat(result.Mesh.IsEmpty).IsFalse();
        AssertThat(result.Mesh.IsWellFormed()).IsTrue();
    }

    // -----------------------------------------------------------------------
    // 2. Skin dimensions match the mesh and pass structural validation.
    // -----------------------------------------------------------------------
    [TestCase]
    public void Skin_vertex_count_matches_mesh_and_is_well_formed()
    {
        var recipe = Build7AttachmentRecipe();
        var result = CreatureMesher.Build(recipe, _registry, CoarseGrid);

        AssertThat(result.Skin.VertexCount).IsEqual(result.Mesh.VertexCount);
        AssertThat(result.Skin.IsWellFormed(result.Skeleton.Count)).IsTrue();
    }

    // -----------------------------------------------------------------------
    // 3. Skeleton has the expected bone count under the ADR-0003 two-bone-limb
    //    model: limb parts split into 2 bones, non-limb parts stay 1.
    //    The 7 attachments are 5 limbs (4 limb_walker + 1 limb_tail) + head +
    //    jaw (mouth), so: spine (1) + 5 limbs x 2 (10) + head (1) + jaw (1) = 13,
    //    with one LimbChain per limb (5).
    // -----------------------------------------------------------------------
    [TestCase]
    public void Skeleton_has_expected_bone_count()
    {
        var recipe = Build7AttachmentRecipe();
        var result = CreatureMesher.Build(recipe, _registry, CoarseGrid);

        AssertThat(result.Skeleton.Count).IsEqual(13);
        AssertThat(result.Skeleton.LimbChains.Length).IsEqual(5);
    }

    // -----------------------------------------------------------------------
    // 4. Determinism: two builds with identical args yield identical arrays
    //    (sampled at several positions to keep the test fast).
    // -----------------------------------------------------------------------
    [TestCase]
    public void Build_is_deterministic()
    {
        var recipe = Build7AttachmentRecipe();

        var r1 = CreatureMesher.Build(recipe, _registry, CoarseGrid);
        var r2 = CreatureMesher.Build(recipe, _registry, CoarseGrid);

        // Structural lengths must be identical.
        AssertThat(r1.Mesh.Vertices.Length).IsEqual(r2.Mesh.Vertices.Length);
        AssertThat(r1.Mesh.Indices.Length).IsEqual(r2.Mesh.Indices.Length);

        // Sample a spread of vertex positions (first, mid, last).
        int n = r1.Mesh.Vertices.Length;
        int mid = n / 2;
        int last = n - 1;

        AssertFloat(r1.Mesh.Vertices[0].X).IsEqualApprox(r2.Mesh.Vertices[0].X, 1e-6f);
        AssertFloat(r1.Mesh.Vertices[0].Y).IsEqualApprox(r2.Mesh.Vertices[0].Y, 1e-6f);
        AssertFloat(r1.Mesh.Vertices[0].Z).IsEqualApprox(r2.Mesh.Vertices[0].Z, 1e-6f);

        AssertFloat(r1.Mesh.Vertices[mid].X).IsEqualApprox(r2.Mesh.Vertices[mid].X, 1e-6f);
        AssertFloat(r1.Mesh.Vertices[mid].Y).IsEqualApprox(r2.Mesh.Vertices[mid].Y, 1e-6f);
        AssertFloat(r1.Mesh.Vertices[mid].Z).IsEqualApprox(r2.Mesh.Vertices[mid].Z, 1e-6f);

        AssertFloat(r1.Mesh.Vertices[last].X).IsEqualApprox(r2.Mesh.Vertices[last].X, 1e-6f);
        AssertFloat(r1.Mesh.Vertices[last].Y).IsEqualApprox(r2.Mesh.Vertices[last].Y, 1e-6f);
        AssertFloat(r1.Mesh.Vertices[last].Z).IsEqualApprox(r2.Mesh.Vertices[last].Z, 1e-6f);

        // Skin arrays — sample first vertex (4 slots) and the midpoint vertex.
        int stride = SkinData.InfluencesPerVertex;
        int midSlot  = mid  * stride;
        int lastSlot = last * stride;

        for (int k = 0; k < stride; k++)
        {
            AssertThat(r1.Skin.BoneIndices[k]).IsEqual(r2.Skin.BoneIndices[k]);
            AssertThat(r1.Skin.BoneIndices[midSlot  + k]).IsEqual(r2.Skin.BoneIndices[midSlot  + k]);
            AssertThat(r1.Skin.BoneIndices[lastSlot + k]).IsEqual(r2.Skin.BoneIndices[lastSlot + k]);

            AssertFloat(r1.Skin.Weights[k]).IsEqualApprox(r2.Skin.Weights[k], 1e-6f);
            AssertFloat(r1.Skin.Weights[midSlot  + k]).IsEqualApprox(r2.Skin.Weights[midSlot  + k], 1e-6f);
            AssertFloat(r1.Skin.Weights[lastSlot + k]).IsEqualApprox(r2.Skin.Weights[lastSlot + k], 1e-6f);
        }
    }

    // -----------------------------------------------------------------------
    // 5. Unknown spine → empty mesh, empty skeleton, no exception.
    // -----------------------------------------------------------------------
    [TestCase]
    public void Unknown_spine_yields_empty_result()
    {
        var recipe = new Recipe { SpinePartId = "nope" };
        var result = CreatureMesher.Build(recipe, _registry, CoarseGrid);

        AssertThat(result.Skeleton.Count).IsEqual(0);
        AssertThat(result.Mesh.IsEmpty).IsTrue();
    }

    // -----------------------------------------------------------------------
    // 6. Null gridParams falls back to GridParams.Default without throwing.
    // -----------------------------------------------------------------------
    [TestCase]
    public void Null_grid_params_uses_default_without_throwing()
    {
        // Use a minimal recipe (spine only) so the test runs fast with the
        // finer default cell size.
        var recipe = new Recipe { SpinePartId = "spine_basic" };
        var result = CreatureMesher.Build(recipe, _registry, null);

        // Spine-only body still polygonises to a non-empty mesh.
        AssertThat(result.Mesh.IsEmpty).IsFalse();
        AssertThat(result.Mesh.IsWellFormed()).IsTrue();
        AssertThat(result.Skin.VertexCount).IsEqual(result.Mesh.VertexCount);
    }

    // -----------------------------------------------------------------------
    // 7. Result objects are never null regardless of input quality.
    // -----------------------------------------------------------------------
    [TestCase]
    public void Result_properties_are_never_null()
    {
        // Covers the null-recipe path (SkeletonResolver returns empty skeleton).
        var result = CreatureMesher.Build(null!, _registry, CoarseGrid);

        AssertThat(result).IsNotNull();
        AssertThat(result.Mesh).IsNotNull();
        AssertThat(result.Skin).IsNotNull();
        AssertThat(result.Skeleton).IsNotNull();
    }
}
