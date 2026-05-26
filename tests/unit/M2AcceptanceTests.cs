using Godot;
using LittleGods.Creature;
using LittleGods.Mesh;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// M2 acceptance test (PRD §7 M2):
/// "saved creature reloads with identical mesh (within float tolerance)."
///
/// The mesh is never serialised - it is regenerated from the recipe. So this
/// reduces to: meshing is a pure function of the recipe, and the recipe
/// round-trips losslessly (proven in M1). We build a creature, mesh it, save +
/// reload the recipe, mesh the reloaded copy, and assert the two meshes match
/// within a tight epsilon (they are in fact bit-identical; the epsilon guards
/// against any incidental FP variance, per the PRD wording).
[TestSuite]
public class M2AcceptanceTests
{
    private const string Slug = "test_m2_acceptance";
    private const float Eps = 1e-4f;

    private PartRegistry _registry = null!;

    [Before]
    public void Setup()
    {
        _registry = new PartRegistry();
        _registry.LoadLibrary();
        if (RecipeStorage.Exists(Slug))
        {
            RecipeStorage.Delete(Slug);
        }
    }

    [After]
    public void Cleanup()
    {
        if (RecipeStorage.Exists(Slug))
        {
            RecipeStorage.Delete(Slug);
        }
    }

    private Recipe BuildCreature()
    {
        var b = RecipeBuilder.ForNewCreature("spine_basic", _registry);
        b.SymmetryEnabled = true;
        b.AddAttachmentMaybeMirrored(-1, "left_shoulder", "limb_walker", Transform3D.Identity);
        b.AddAttachmentMaybeMirrored(-1, "left_hip", "limb_runner", Transform3D.Identity);
        b.AddAttachment(-1, "tail", "limb_tail");
        int head = b.AddAttachment(-1, "head", "head_predator");
        b.AddAttachment(head, "jaw", "mouth_fang");
        return b.Recipe;
    }

    [TestCase]
    public void Saved_creature_reloads_with_identical_mesh()
    {
        var gp = new GridParams(0.15f, 0.5f);

        var original = BuildCreature();
        var meshA = CreatureMesher.Build(original, _registry, gp);

        // The creature must actually have geometry, or this proves nothing.
        AssertThat(meshA.Mesh.VertexCount).IsGreater(0);
        AssertThat(meshA.Mesh.TriangleCount).IsGreater(0);

        // Save -> close -> reopen (the editor lifecycle), then re-mesh.
        var saveErr = RecipeStorage.Save(original, Slug);
        AssertThat((int)saveErr).IsEqual((int)Error.Ok);
        var reloaded = RecipeStorage.Load(Slug);
        AssertThat(reloaded).IsNotNull();
        var meshB = CreatureMesher.Build(reloaded, _registry, gp);

        // Same topology.
        AssertThat(meshB.Mesh.VertexCount).IsEqual(meshA.Mesh.VertexCount);
        AssertThat(meshB.Mesh.Indices.Length).IsEqual(meshA.Mesh.Indices.Length);
        AssertThat(meshB.Skin.Weights.Length).IsEqual(meshA.Skin.Weights.Length);

        // Indices identical (aggregate to one assertion with a clear message).
        bool indicesEqual = true;
        for (int i = 0; i < meshA.Mesh.Indices.Length && indicesEqual; i++)
        {
            indicesEqual = meshA.Mesh.Indices[i] == meshB.Mesh.Indices[i];
        }
        AssertThat(indicesEqual)
            .OverrideFailureMessage("index buffers differ after reload")
            .IsTrue();

        // Vertices + normals within epsilon.
        float maxVertDelta = 0f;
        for (int i = 0; i < meshA.Mesh.Vertices.Length; i++)
        {
            maxVertDelta = Mathf.Max(maxVertDelta,
                meshA.Mesh.Vertices[i].DistanceTo(meshB.Mesh.Vertices[i]));
        }
        AssertThat(maxVertDelta < Eps)
            .OverrideFailureMessage($"max vertex delta {maxVertDelta} exceeds {Eps}")
            .IsTrue();

        float maxNormalDelta = 0f;
        for (int i = 0; i < meshA.Mesh.Normals.Length; i++)
        {
            maxNormalDelta = Mathf.Max(maxNormalDelta,
                meshA.Mesh.Normals[i].DistanceTo(meshB.Mesh.Normals[i]));
        }
        AssertThat(maxNormalDelta < Eps)
            .OverrideFailureMessage($"max normal delta {maxNormalDelta} exceeds {Eps}")
            .IsTrue();

        // Skin weights + bone indices identical.
        float maxWeightDelta = 0f;
        bool boneIndicesEqual = true;
        for (int i = 0; i < meshA.Skin.Weights.Length; i++)
        {
            maxWeightDelta = Mathf.Max(maxWeightDelta,
                Mathf.Abs(meshA.Skin.Weights[i] - meshB.Skin.Weights[i]));
            if (meshA.Skin.BoneIndices[i] != meshB.Skin.BoneIndices[i])
            {
                boneIndicesEqual = false;
            }
        }
        AssertThat(maxWeightDelta < Eps)
            .OverrideFailureMessage($"max skin weight delta {maxWeightDelta} exceeds {Eps}")
            .IsTrue();
        AssertThat(boneIndicesEqual)
            .OverrideFailureMessage("skin bone indices differ after reload")
            .IsTrue();
    }
}
