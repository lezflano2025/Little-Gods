using Godot;
using Godot.Collections;

namespace LittleGods.Mesh;

/// Converts pure C# mesh/skin/skeleton data into Godot scene-tree types.
/// Called by CreatureMesher (M2 P2) after the full pipeline has run.
///
/// ArrayMesh / Skin are ref-counted (Resource) — callers do not need to free
/// them. Skeleton3D is a Node — callers own it and must add/free it.
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

    /// Build a fresh Skeleton3D whose bone rests match the world-space bones.
    /// See PopulateSkeleton3D for the rest/parent convention.
    public static Skeleton3D BuildSkeleton3D(CreatureSkeleton skeleton)
        => PopulateSkeleton3D(new Skeleton3D(), skeleton);

    /// (Re)populate an existing Skeleton3D from a CreatureSkeleton, in place.
    /// Clears any current bones first, so the same node can be reused across
    /// rebuilds (CreaturePreview does this to avoid node churn).
    ///
    /// Bone rests are stored LOCAL to the parent (Godot convention):
    ///   root bone  → rest == global rest
    ///   child bone → rest == parentGlobal.AffineInverse() * global
    ///
    /// Parents always precede children in CreatureSkeleton.Bones, so each
    /// parent's global transform is already computed when we reach a child.
    public static Skeleton3D PopulateSkeleton3D(Skeleton3D target, CreatureSkeleton skeleton)
    {
        target.ClearBones();

        if (skeleton.Count == 0)
        {
            return target;
        }

        Transform3D[] globalRests = ComputeGlobalRests(skeleton);

        for (int i = 0; i < skeleton.Count; i++)
        {
            Bone bone = skeleton.Bones[i];
            target.AddBone($"bone_{i}");

            if (bone.ParentIndex < 0)
            {
                target.SetBoneParent(i, -1);
                target.SetBoneRest(i, globalRests[i]);
            }
            else
            {
                target.SetBoneParent(i, bone.ParentIndex);
                Transform3D localRest = globalRests[bone.ParentIndex].AffineInverse() * globalRests[i];
                target.SetBoneRest(i, localRest);
            }
        }

        return target;
    }

    // -------------------------------------------------------------------------
    // Skin (bind poses) — M3 P0
    // -------------------------------------------------------------------------

    /// Build a Skin whose bind pose for bone i is the INVERSE of that bone's
    /// global rest. The skinning matrix is `globalPose_i * bindPose_i`, so at
    /// rest (globalPose == globalRest) it is the identity and the mesh is
    /// undeformed; posing the Skeleton3D then deforms the skin.
    ///
    /// Bind index i maps to bone i (the mesh's ARRAY_BONES are bone indices).
    /// Returns an empty Skin for an empty skeleton.
    public static Skin BuildSkin(CreatureSkeleton skeleton)
    {
        var skin = new Skin();

        if (skeleton.Count == 0)
        {
            return skin;
        }

        Transform3D[] globalRests = ComputeGlobalRests(skeleton);

        skin.SetBindCount(skeleton.Count);
        for (int i = 0; i < skeleton.Count; i++)
        {
            skin.SetBindBone(i, i);
            skin.SetBindPose(i, globalRests[i].AffineInverse());
        }

        return skin;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// Global rest transform of every bone. Bones are already world-space, so a
    /// bone's global rest is built directly from its Head/Tail — no parent-chain
    /// accumulation needed. BuildSkeleton3D and BuildSkin share this so the
    /// Skeleton3D rests and the Skin bind poses are guaranteed consistent.
    private static Transform3D[] ComputeGlobalRests(CreatureSkeleton skeleton)
    {
        var globals = new Transform3D[skeleton.Count];
        for (int i = 0; i < skeleton.Count; i++)
        {
            globals[i] = ComputeGlobalRest(skeleton.Bones[i]);
        }
        return globals;
    }

    /// Build a bone's global rest transform from its world-space Head/Tail.
    /// Basis convention (+Z toward Tail):
    ///   d     = (Tail - Head).Normalized()  (guarded for zero-length bones)
    ///   up    = Vector3.Up, or Vector3.Right when nearly parallel to d
    ///   right = up.Cross(d).Normalized()
    ///   up    = d.Cross(right).Normalized()
    ///   Basis = new Basis(right, up, d)  (column constructor: X=right Y=up Z=d)
    private static Transform3D ComputeGlobalRest(Bone bone)
    {
        Vector3 origin = bone.Head;

        Vector3 d = bone.Tail - bone.Head;
        d = d.LengthSquared() > 1e-12f ? d.Normalized() : Vector3.Back;

        Vector3 refUp = Mathf.Abs(d.Dot(Vector3.Up)) < 0.99f
            ? Vector3.Up
            : Vector3.Right;

        Vector3 right = refUp.Cross(d).Normalized();
        Vector3 up    = d.Cross(right).Normalized();

        var basis = new Basis(right, up, d);
        return new Transform3D(basis, origin);
    }
}
