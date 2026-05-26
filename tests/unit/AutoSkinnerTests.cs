using Godot;
using LittleGods.Mesh;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// M2 P1 C: AutoSkinner — inverse-distance skinning weights.
///
/// Skeleton construction mirrors SkeletonResolverTests: in-code, no registry.
/// Each Bone(head, tail, radiusHead, radiusTail, parentIndex).
[TestSuite]
public class AutoSkinnerTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static CreatureSkeleton OneBone(Vector3 head, Vector3 tail) =>
        new(new[] { new Bone(head, tail, 0.2f, 0.2f, -1) });

    private static CreatureSkeleton TwoBones() =>
        new(new[]
        {
            new Bone(new Vector3(0, 0, 0), new Vector3(2, 0, 0), 0.2f, 0.2f, -1),
            new Bone(new Vector3(0, 3, 0), new Vector3(2, 3, 0), 0.2f, 0.2f, -1),
        });

    // Returns the 4 bone indices assigned to vertex v.
    private static int[] SlotIndices(SkinData skin, int v)
    {
        int b = v * SkinData.InfluencesPerVertex;
        return new[] { skin.BoneIndices[b], skin.BoneIndices[b + 1],
                       skin.BoneIndices[b + 2], skin.BoneIndices[b + 3] };
    }

    // Returns the 4 weights for vertex v.
    private static float[] SlotWeights(SkinData skin, int v)
    {
        int b = v * SkinData.InfluencesPerVertex;
        return new[] { skin.Weights[b], skin.Weights[b + 1],
                       skin.Weights[b + 2], skin.Weights[b + 3] };
    }

    // -----------------------------------------------------------------------
    // Vertex ON a bone segment => that bone weight ≈ 1
    // -----------------------------------------------------------------------

    [TestCase]
    public void Vertex_on_bone_segment_gets_weight_one()
    {
        var skel = TwoBones();
        // Midpoint of bone 0 segment (y=0)
        var vertices = new[] { new Vector3(1, 0, 0) };

        var skin = AutoSkinner.Skin(vertices, skel);

        AssertThat(skin.IsWellFormed(skel.Count)).IsTrue();
        // Bone 0 must occupy slot 0 (nearest).
        AssertThat(skin.BoneIndices[0]).IsEqual(0);
        // Its weight must be ≈ 1.
        AssertFloat(skin.Weights[0]).IsEqualApprox(1f, 1e-3f);
    }

    // -----------------------------------------------------------------------
    // Vertex equidistant between two bones => ≈ 0.5 / 0.5
    // -----------------------------------------------------------------------

    [TestCase]
    public void Vertex_equidistant_between_two_bones_gives_half_half()
    {
        var skel = TwoBones();
        // Bone 0 runs along y=0; bone 1 runs along y=3. Midpoint y=1.5, same x
        // distance to each. Use x=1 to sit on the x-midpoint of both segments
        // so the closest segment point on each bone is directly below/above.
        var vertices = new[] { new Vector3(1, 1.5f, 0) };

        var skin = AutoSkinner.Skin(vertices, skel);

        AssertThat(skin.IsWellFormed(skel.Count)).IsTrue();

        float[] w = SlotWeights(skin, 0);
        // Both used weights should be ≈ 0.5; padding weights are 0.
        float usedA = w[0];
        float usedB = w[1];
        AssertFloat(usedA).IsEqualApprox(0.5f, 1e-3f);
        AssertFloat(usedB).IsEqualApprox(0.5f, 1e-3f);
        AssertFloat(w[2]).IsEqualApprox(0f, 1e-6f);
        AssertFloat(w[3]).IsEqualApprox(0f, 1e-6f);
    }

    // -----------------------------------------------------------------------
    // IsWellFormed: every vertex's 4 weights sum to 1
    // -----------------------------------------------------------------------

    [TestCase]
    public void Weights_sum_to_one_for_all_vertices()
    {
        var skel = TwoBones();
        var vertices = new[]
        {
            new Vector3(0, 0, 0),
            new Vector3(1, 1.5f, 0),
            new Vector3(-5, 10, 3),
            new Vector3(1, 0, 0),
        };

        var skin = AutoSkinner.Skin(vertices, skel);

        AssertThat(skin.IsWellFormed(skel.Count)).IsTrue();
    }

    // -----------------------------------------------------------------------
    // Array lengths == vertexCount * 4
    // -----------------------------------------------------------------------

    [TestCase]
    public void Output_array_lengths_are_vertex_count_times_four()
    {
        var skel = TwoBones();
        var vertices = new[]
        {
            new Vector3(0, 0, 0),
            new Vector3(1, 1, 1),
            new Vector3(2, 2, 2),
        };

        var skin = AutoSkinner.Skin(vertices, skel);

        AssertThat(skin.BoneIndices.Length).IsEqual(vertices.Length * SkinData.InfluencesPerVertex);
        AssertThat(skin.Weights.Length).IsEqual(vertices.Length * SkinData.InfluencesPerVertex);
        AssertThat(skin.VertexCount).IsEqual(vertices.Length);
    }

    // -----------------------------------------------------------------------
    // Nearest-4 selection with >4 bones
    // -----------------------------------------------------------------------

    [TestCase]
    public void Nearest_four_selected_from_six_bone_skeleton()
    {
        // Six bones arranged along the x-axis at x = 0, 10, 20, 30, 40, 50.
        // Each is a point bone (head == tail, degenerate segment length 0).
        var bones = new Bone[]
        {
            new(new Vector3( 0, 0, 0), new Vector3( 0, 0, 0), 0.1f, 0.1f, -1), // idx 0
            new(new Vector3(10, 0, 0), new Vector3(10, 0, 0), 0.1f, 0.1f, -1), // idx 1
            new(new Vector3(20, 0, 0), new Vector3(20, 0, 0), 0.1f, 0.1f, -1), // idx 2
            new(new Vector3(30, 0, 0), new Vector3(30, 0, 0), 0.1f, 0.1f, -1), // idx 3
            new(new Vector3(40, 0, 0), new Vector3(40, 0, 0), 0.1f, 0.1f, -1), // idx 4
            new(new Vector3(50, 0, 0), new Vector3(50, 0, 0), 0.1f, 0.1f, -1), // idx 5
        };
        var skel = new CreatureSkeleton(bones);

        // Vertex near x=0 — 4 nearest are bones 0,1,2,3; farthest are 4 and 5.
        var vertices = new[] { new Vector3(1f, 0, 0) };

        var skin = AutoSkinner.Skin(vertices, skel);

        AssertThat(skin.IsWellFormed(skel.Count)).IsTrue();

        int[] indices = SlotIndices(skin, 0);
        // The 4 chosen indices must be exactly {0, 1, 2, 3} (in any order).
        var indexSet = new System.Collections.Generic.HashSet<int>(indices);
        AssertThat(indexSet.Contains(0)).IsTrue();
        AssertThat(indexSet.Contains(1)).IsTrue();
        AssertThat(indexSet.Contains(2)).IsTrue();
        AssertThat(indexSet.Contains(3)).IsTrue();
        AssertThat(indexSet.Contains(4)).IsFalse();
        AssertThat(indexSet.Contains(5)).IsFalse();
    }

    // -----------------------------------------------------------------------
    // Vertex coincident with a bone endpoint => no NaN/Inf, weights sum to 1
    // -----------------------------------------------------------------------

    [TestCase]
    public void Vertex_coincident_with_bone_endpoint_no_nan_inf()
    {
        var skel = TwoBones();
        // Exactly on bone 0's head (0,0,0).
        var vertices = new[] { new Vector3(0, 0, 0) };

        var skin = AutoSkinner.Skin(vertices, skel);

        AssertThat(skin.IsWellFormed(skel.Count)).IsTrue();

        float[] w = SlotWeights(skin, 0);
        foreach (var wt in w)
        {
            AssertThat(float.IsNaN(wt)).IsFalse();
            AssertThat(float.IsInfinity(wt)).IsFalse();
        }
        // IsWellFormed already checks sum ≈ 1, but also verify slot 0 ≈ 1.
        AssertFloat(w[0]).IsEqualApprox(1f, 1e-3f);
    }

    // -----------------------------------------------------------------------
    // Determinism: two calls, identical inputs => identical outputs
    // -----------------------------------------------------------------------

    [TestCase]
    public void Determinism_same_input_same_output()
    {
        var bones = new Bone[]
        {
            new(new Vector3(0, 0, 0), new Vector3(2, 0, 0), 0.2f, 0.2f, -1),
            new(new Vector3(0, 3, 0), new Vector3(2, 3, 0), 0.2f, 0.2f, -1),
            new(new Vector3(5, 0, 0), new Vector3(5, 3, 0), 0.3f, 0.3f, -1),
            new(new Vector3(0, 0, 5), new Vector3(2, 0, 5), 0.2f, 0.2f, -1),
            new(new Vector3(0, 3, 5), new Vector3(2, 3, 5), 0.2f, 0.2f, -1),
        };
        var skel = new CreatureSkeleton(bones);
        var vertices = new[]
        {
            new Vector3(1, 1.5f, 0),
            new Vector3(0, 0, 0),
            new Vector3(2.5f, 1.5f, 2.5f),
            new Vector3(-10, -10, -10),
        };

        var a = AutoSkinner.Skin(vertices, skel);
        var b = AutoSkinner.Skin(vertices, skel);

        AssertThat(a.BoneIndices.Length).IsEqual(b.BoneIndices.Length);
        AssertThat(a.Weights.Length).IsEqual(b.Weights.Length);

        for (int i = 0; i < a.BoneIndices.Length; i++)
        {
            AssertThat(a.BoneIndices[i]).IsEqual(b.BoneIndices[i]);
        }

        for (int i = 0; i < a.Weights.Length; i++)
        {
            AssertFloat(a.Weights[i]).IsEqualApprox(b.Weights[i], 1e-6f);
        }
    }

    // -----------------------------------------------------------------------
    // Single-bone skeleton: only bone 0, rest padded
    // -----------------------------------------------------------------------

    [TestCase]
    public void Single_bone_skeleton_pads_remaining_slots()
    {
        var skel = OneBone(new Vector3(0, 0, 0), new Vector3(2, 0, 0));
        var vertices = new[] { new Vector3(5, 5, 5) };

        var skin = AutoSkinner.Skin(vertices, skel);

        AssertThat(skin.IsWellFormed(skel.Count)).IsTrue();

        float[] w = SlotWeights(skin, 0);
        int[] idx = SlotIndices(skin, 0);

        // Only one bone; its weight must be 1.
        AssertThat(idx[0]).IsEqual(0);
        AssertFloat(w[0]).IsEqualApprox(1f, 1e-3f);
        // Padding slots: weight 0, index 0.
        AssertFloat(w[1]).IsEqualApprox(0f, 1e-6f);
        AssertFloat(w[2]).IsEqualApprox(0f, 1e-6f);
        AssertFloat(w[3]).IsEqualApprox(0f, 1e-6f);
        AssertThat(idx[1]).IsEqual(0);
        AssertThat(idx[2]).IsEqual(0);
        AssertThat(idx[3]).IsEqual(0);
    }

    // -----------------------------------------------------------------------
    // Three-bone skeleton: exactly 3 influences, one padding slot per vertex
    // -----------------------------------------------------------------------

    [TestCase]
    public void Three_bone_skeleton_one_padding_slot_per_vertex()
    {
        var skel = new CreatureSkeleton(new[]
        {
            new Bone(new Vector3(0, 0, 0), new Vector3(1, 0, 0), 0.2f, 0.2f, -1),
            new Bone(new Vector3(0, 5, 0), new Vector3(1, 5, 0), 0.2f, 0.2f, -1),
            new Bone(new Vector3(0, 0, 5), new Vector3(1, 0, 5), 0.2f, 0.2f, -1),
        });
        var vertices = new[] { new Vector3(0.5f, 2, 2) };

        var skin = AutoSkinner.Skin(vertices, skel);

        AssertThat(skin.IsWellFormed(skel.Count)).IsTrue();

        float[] w = SlotWeights(skin, 0);
        // Last slot must be a zero padding weight.
        AssertFloat(w[3]).IsEqualApprox(0f, 1e-6f);
        // First three must sum to 1; IsWellFormed already checks total sum.
        float partialSum = w[0] + w[1] + w[2];
        AssertFloat(partialSum).IsEqualApprox(1f, 1e-3f);
    }

    // -----------------------------------------------------------------------
    // Empty vertex list: returns zero-length arrays without throwing
    // -----------------------------------------------------------------------

    [TestCase]
    public void Empty_vertex_array_returns_empty_skin_data()
    {
        var skel = TwoBones();
        var skin = AutoSkinner.Skin(System.Array.Empty<Vector3>(), skel);

        AssertThat(skin.BoneIndices.Length).IsEqual(0);
        AssertThat(skin.Weights.Length).IsEqual(0);
        AssertThat(skin.VertexCount).IsEqual(0);
    }
}
