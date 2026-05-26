using Godot;
using Godot.Collections;

namespace LittleGods.Mesh;

/// Converts pure C# mesh/skin/skeleton data into Godot scene-tree types.
/// Called by CreatureMesher (M2 P2) after the full pipeline has run.
///
/// ArrayMesh is ref-counted (Resource) — callers do not need to free it.
/// Skeleton3D is a Node — callers own it and must add/free it appropriately.
public static class GodotMeshBuilder
{
    // -------------------------------------------------------------------------
    // ArrayMesh
    // -------------------------------------------------------------------------

    /// Pack a MeshData + optional SkinData into a single-surface ArrayMesh.
    /// Returns an empty ArrayMesh (zero surfaces) when mesh.IsEmpty.
    /// Skinning is applied only when skin is non-null and its VertexCount
    /// matches the mesh's VertexCount (and there is at least one vertex).
    public static ArrayMesh BuildArrayMesh(MeshData mesh, SkinData? skin)
    {
        var result = new ArrayMesh();

        if (mesh.IsEmpty)
        {
            return result;
        }

        // Godot.Collections.Array sized to Mesh.ArrayType.Max.
        var arrays = new Array();
        arrays.Resize((int)Godot.Mesh.ArrayType.Max);

        // Parallel arrays — Godot marshals these to the appropriate packed types.
        arrays[(int)Godot.Mesh.ArrayType.Vertex] = mesh.Vertices;
        arrays[(int)Godot.Mesh.ArrayType.Normal] = mesh.Normals;
        arrays[(int)Godot.Mesh.ArrayType.Index]  = mesh.Indices;

        bool applySkin = skin is not null
            && mesh.VertexCount > 0
            && skin.VertexCount == mesh.VertexCount;

        if (applySkin)
        {
            // BoneIndices: int[] (4 per vertex) → PackedInt32Array
            // Weights:     float[] (4 per vertex) → PackedFloat32Array
            // The default surface layout already uses 4 influences per vertex,
            // so no extra Mesh.ArrayFormat flags are required.
            arrays[(int)Godot.Mesh.ArrayType.Bones]   = skin!.BoneIndices;
            arrays[(int)Godot.Mesh.ArrayType.Weights] = skin!.Weights;
        }

        result.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
        return result;
    }

    // -------------------------------------------------------------------------
    // Skeleton3D
    // -------------------------------------------------------------------------

    /// Build a Skeleton3D whose bone rests match the world-space bone data.
    /// Bone rests are stored LOCAL to the parent (Godot convention):
    ///   root bone  → rest == global rest
    ///   child bone → rest == parentGlobal.AffineInverse() * global
    ///
    /// Parents always precede children in CreatureSkeleton.Bones, so each
    /// parent's global transform is already computed when we reach a child.
    ///
    /// Basis convention (+Z toward Tail):
    ///   d     = (Tail - Head).Normalized()  (guarded for zero-length bones)
    ///   up    = Vector3.Up, or Vector3.Right when nearly parallel to d
    ///   right = up.Cross(d).Normalized()
    ///   up    = d.Cross(right).Normalized()
    ///   Basis = new Basis(right, up, d)  (column constructor: X=right Y=up Z=d)
    public static Skeleton3D BuildSkeleton3D(CreatureSkeleton skeleton)
    {
        var result = new Skeleton3D();

        if (skeleton.Count == 0)
        {
            return result;
        }

        var globalRests = new Transform3D[skeleton.Count];

        for (int i = 0; i < skeleton.Count; i++)
        {
            Bone bone = skeleton.Bones[i];

            // --- global rest transform ---
            Transform3D globalRest = ComputeGlobalRest(bone);
            globalRests[i] = globalRest;

            // --- add to skeleton ---
            result.AddBone($"bone_{i}");

            if (bone.ParentIndex < 0)
            {
                result.SetBoneParent(i, -1);
                result.SetBoneRest(i, globalRest);
            }
            else
            {
                result.SetBoneParent(i, bone.ParentIndex);
                Transform3D localRest = globalRests[bone.ParentIndex].AffineInverse() * globalRest;
                result.SetBoneRest(i, localRest);
            }
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Transform3D ComputeGlobalRest(Bone bone)
    {
        Vector3 origin = bone.Head;

        // Direction from Head to Tail; guard zero-length bones.
        Vector3 d = bone.Tail - bone.Head;
        d = d.LengthSquared() > 1e-12f ? d.Normalized() : Vector3.Back;

        // Build orthonormal basis with +Z along d.
        // Use Vector3.Up as the reference "up", fall back to Vector3.Right when
        // d is nearly parallel to Up (dot product close to ±1).
        Vector3 refUp = Mathf.Abs(d.Dot(Vector3.Up)) < 0.99f
            ? Vector3.Up
            : Vector3.Right;

        Vector3 right = refUp.Cross(d).Normalized();
        Vector3 up    = d.Cross(right).Normalized();

        // Basis column constructor: X=right, Y=up, Z=d.
        var basis = new Basis(right, up, d);
        return new Transform3D(basis, origin);
    }
}
