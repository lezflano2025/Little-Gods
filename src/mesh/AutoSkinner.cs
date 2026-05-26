using Godot;

namespace LittleGods.Mesh;

/// Computes GPU-ready skinning weights for a set of mesh vertices against a
/// creature skeleton. Each vertex is influenced by up to 4 bones (the nearest
/// by segment distance), weighted by inverse distance and normalised to sum 1.
///
/// Output layout matches Godot's ARRAY_BONES / ARRAY_WEIGHTS flat format:
/// vertex v occupies slots [v*4 .. v*4+3] in both BoneIndices and Weights.
///
/// Edge cases
/// ----------
/// * Fewer than 4 bones — the skeleton has k < 4 bones. All k are used; the
///   remaining (4-k) slots are padded with bone index 0 and weight 0.
/// * Vertex lies exactly on a bone segment — distance is 0. epsilon = 1e-5f
///   prevents division by zero; that bone receives weight ≈ 1/epsilon >> all
///   others and ends up with weight ≈ 1 after normalisation.
/// * 0-bone skeleton — every slot receives bone index 0 and weight 0. This
///   cannot be normalised (no bones to contribute). IsWellFormed will reject it
///   (boneCount 0 makes every bone index out of range), but the method will not
///   throw, NaN, or Inf.
/// * Determinism — ties in distance are broken by ascending bone index, so two
///   calls with identical inputs always produce identical arrays.
public static class AutoSkinner
{
    private const float Epsilon = 1e-5f;

    /// <summary>
    /// Assign skinning weights to every vertex in <paramref name="vertices"/>
    /// against the supplied <paramref name="skeleton"/>.
    /// </summary>
    /// <param name="vertices">World-space mesh vertices to skin.</param>
    /// <param name="skeleton">Creature skeleton providing bone segments.</param>
    /// <returns>
    /// A <see cref="SkinData"/> with flat arrays of length
    /// <c>vertices.Length * SkinData.InfluencesPerVertex</c>.
    /// </returns>
    public static SkinData Skin(Vector3[] vertices, CreatureSkeleton skeleton)
    {
        if (vertices == null)
        {
            vertices = System.Array.Empty<Vector3>();
        }

        int vertexCount = vertices.Length;
        int stride = SkinData.InfluencesPerVertex; // 4

        int[] boneIndices = new int[vertexCount * stride];
        float[] weights = new float[vertexCount * stride];

        int boneCount = skeleton.Count;

        // Fast path: no bones — arrays already zero-initialised.
        if (boneCount == 0)
        {
            return new SkinData(boneIndices, weights);
        }

        // How many influences are actually available.
        int influences = System.Math.Min(boneCount, stride);

        // Working buffer: (distance, boneIndex) pairs, reused per vertex to
        // avoid per-vertex heap allocation.
        (float dist, int idx)[] candidates = new (float, int)[boneCount];

        for (int v = 0; v < vertexCount; v++)
        {
            Vector3 p = vertices[v];

            // --- Step 1: compute distance to every bone ---
            for (int b = 0; b < boneCount; b++)
            {
                candidates[b] = (skeleton.Bones[b].DistanceTo(p), b);
            }

            // --- Step 2: partial sort — bring the `influences` nearest to the
            //     front. Insertion sort is O(n*influences) and fine for the
            //     small boneCount values expected in creature skeletons. Ties
            //     are broken by ascending bone index (determinism guarantee). ---
            for (int i = 0; i < influences; i++)
            {
                int minPos = i;
                for (int j = i + 1; j < boneCount; j++)
                {
                    if (IsBefore(candidates[j], candidates[minPos]))
                    {
                        minPos = j;
                    }
                }

                if (minPos != i)
                {
                    var tmp = candidates[i];
                    candidates[i] = candidates[minPos];
                    candidates[minPos] = tmp;
                }
            }

            // --- Step 3: compute raw inverse-distance weights for chosen slots ---
            int baseSlot = v * stride;
            float weightSum = 0f;

            for (int k = 0; k < influences; k++)
            {
                float d = candidates[k].dist;
                float w = 1f / System.Math.Max(d, Epsilon);
                boneIndices[baseSlot + k] = candidates[k].idx;
                weights[baseSlot + k] = w;
                weightSum += w;
            }

            // Padding slots: bone index 0, weight 0 (already zero; just set index).
            for (int k = influences; k < stride; k++)
            {
                boneIndices[baseSlot + k] = 0;
                weights[baseSlot + k] = 0f;
            }

            // --- Step 4: normalise the kept weights ---
            // weightSum > 0 guaranteed (boneCount >= 1 and epsilon > 0).
            float invSum = 1f / weightSum;
            for (int k = 0; k < influences; k++)
            {
                weights[baseSlot + k] *= invSum;
            }
        }

        return new SkinData(boneIndices, weights);
    }

    /// <summary>
    /// Returns true if <paramref name="a"/> should be sorted before
    /// <paramref name="b"/>: smaller distance wins; ties break by smaller
    /// bone index (ascending) for determinism.
    /// </summary>
    private static bool IsBefore((float dist, int idx) a, (float dist, int idx) b)
    {
        if (a.dist < b.dist)
        {
            return true;
        }

        if (a.dist > b.dist)
        {
            return false;
        }

        // Exact distance tie — prefer the lower bone index.
        return a.idx < b.idx;
    }
}
