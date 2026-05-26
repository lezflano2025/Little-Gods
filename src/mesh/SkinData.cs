using Godot;

namespace LittleGods.Mesh;

/// Per-vertex skinning: exactly InfluencesPerVertex bone indices and matching
/// weights for each mesh vertex, laid out flat (vertex v occupies slots
/// [v*4 .. v*4+3]). This is the layout Godot's ARRAY_BONES / ARRAY_WEIGHTS
/// expect, so GodotMeshBuilder (M2 P2) can hand the arrays straight to an
/// ArrayMesh.
///
/// Produced by AutoSkinner (M2 P1 C). Weights for each vertex sum to 1.
public sealed class SkinData
{
    public const int InfluencesPerVertex = 4;

    /// length == VertexCount * InfluencesPerVertex.
    public int[] BoneIndices { get; }

    /// length == VertexCount * InfluencesPerVertex. Each group of 4 sums to 1.
    public float[] Weights { get; }

    public SkinData(int[] boneIndices, float[] weights)
    {
        BoneIndices = boneIndices ?? System.Array.Empty<int>();
        Weights = weights ?? System.Array.Empty<float>();
    }

    public int VertexCount => Weights.Length / InfluencesPerVertex;

    /// Validates the flat layout: array lengths agree and are a multiple of 4,
    /// every weight group sums to ~1, and no bone index is negative.
    public bool IsWellFormed(int boneCount, float weightTolerance = 1e-3f)
    {
        if (BoneIndices.Length != Weights.Length)
        {
            return false;
        }
        if (Weights.Length % InfluencesPerVertex != 0)
        {
            return false;
        }
        for (int v = 0; v < VertexCount; v++)
        {
            float sum = 0f;
            for (int k = 0; k < InfluencesPerVertex; k++)
            {
                int slot = v * InfluencesPerVertex + k;
                int bone = BoneIndices[slot];
                if (bone < 0 || bone >= boneCount)
                {
                    return false;
                }
                sum += Weights[slot];
            }
            if (Mathf.Abs(sum - 1f) > weightTolerance)
            {
                return false;
            }
        }
        return true;
    }
}
