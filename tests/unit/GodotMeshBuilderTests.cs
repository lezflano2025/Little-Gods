using Godot;
using LittleGods.Mesh;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// M2 P2: GodotMeshBuilder converts MeshData/SkinData/CreatureSkeleton into
/// Godot scene-tree types (ArrayMesh, Skeleton3D).
///
/// These tests run inside the Godot runtime (GdUnit4 scene runner) so that
/// Skeleton3D and ArrayMesh APIs are fully available.
///
/// Memory discipline:
///   - ArrayMesh is a Resource (ref-counted) — no AutoFree needed.
///   - Skeleton3D is a Node — every instance is wrapped in AutoFree(…) so
///     GdUnit4 can clean up orphan nodes and the test runner never complains.
[TestSuite]
public class GodotMeshBuilderTests
{
    // -------------------------------------------------------------------------
    // ArrayMesh — triangle with skin
    // -------------------------------------------------------------------------

    [TestCase]
    public void BuildArrayMesh_single_triangle_with_skin_has_one_surface_three_verts()
    {
        // Hand-built single triangle.
        var vertices = new Vector3[]
        {
            new(0f, 0f, 0f),
            new(1f, 0f, 0f),
            new(0f, 1f, 0f),
        };
        var normals = new Vector3[]
        {
            Vector3.Up,
            Vector3.Up,
            Vector3.Up,
        };
        var indices = new int[] { 0, 1, 2 };

        var mesh = new MeshData(vertices, normals, indices);

        // SkinData: 3 vertices × 4 influences = 12 slots.
        // Give each vertex 100 % weight on bone 0.
        var boneIndices = new int[]   { 0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0 };
        var weights     = new float[] { 1f,0f,0f,0f, 1f,0f,0f,0f, 1f,0f,0f,0f };
        var skin = new SkinData(boneIndices, weights);

        ArrayMesh result = GodotMeshBuilder.BuildArrayMesh(mesh, skin);

        // ArrayMesh is ref-counted — no AutoFree required.
        AssertThat(result.GetSurfaceCount()).IsEqual(1);
        AssertThat(result.SurfaceGetArrayLen(0)).IsEqual(3);
    }

    // -------------------------------------------------------------------------
    // ArrayMesh — empty mesh
    // -------------------------------------------------------------------------

    [TestCase]
    public void BuildArrayMesh_empty_mesh_returns_zero_surfaces()
    {
        ArrayMesh result = GodotMeshBuilder.BuildArrayMesh(MeshData.Empty, null);

        AssertThat(result.GetSurfaceCount()).IsEqual(0);
    }

    // -------------------------------------------------------------------------
    // Skeleton3D — bone count and parent indices
    // -------------------------------------------------------------------------

    [TestCase]
    public void BuildSkeleton3D_three_bone_skeleton_has_correct_count_and_parents()
    {
        // Root bone (ParentIndex == -1).
        var root = new Bone(
            head: new Vector3(0f, 0f, 0f),
            tail: new Vector3(0f, 1f, 0f),
            radiusHead: 0.3f,
            radiusTail: 0.3f,
            parentIndex: -1);

        // Two children both attached to bone 0.
        var childA = new Bone(
            head: new Vector3(-0.5f, 1f, 0f),
            tail: new Vector3(-0.5f, 2f, 0f),
            radiusHead: 0.2f,
            radiusTail: 0.2f,
            parentIndex: 0);

        var childB = new Bone(
            head: new Vector3(0.5f, 1f, 0f),
            tail: new Vector3(0.5f, 2f, 0f),
            radiusHead: 0.2f,
            radiusTail: 0.2f,
            parentIndex: 0);

        var skeleton = new CreatureSkeleton(new[] { root, childA, childB });

        // Skeleton3D is a Node — must be AutoFree'd to avoid orphan warnings.
        Skeleton3D skel3d = AutoFree(GodotMeshBuilder.BuildSkeleton3D(skeleton))!;

        AssertThat(skel3d.GetBoneCount()).IsEqual(3);
        AssertThat(skel3d.GetBoneParent(0)).IsEqual(-1);
        AssertThat(skel3d.GetBoneParent(1)).IsEqual(0);
        AssertThat(skel3d.GetBoneParent(2)).IsEqual(0);
    }

    // -------------------------------------------------------------------------
    // Skeleton3D — root bone rest origin matches bone.Head
    // -------------------------------------------------------------------------

    [TestCase]
    public void BuildSkeleton3D_root_bone_rest_origin_matches_head()
    {
        // Root bone with a non-trivial Head position so the origin check is meaningful.
        var rootHead = new Vector3(0f, 0f, -1f);
        var rootTail = new Vector3(0f, 0f,  1f);

        var root = new Bone(
            head: rootHead,
            tail: rootTail,
            radiusHead: 0.4f,
            radiusTail: 0.4f,
            parentIndex: -1);

        var skeleton = new CreatureSkeleton(new[] { root });

        Skeleton3D skel3d = AutoFree(GodotMeshBuilder.BuildSkeleton3D(skeleton))!;

        Transform3D rest = skel3d.GetBoneRest(0);

        // Root bone rest is its global rest, so origin == bone.Head.
        float tol = 1e-3f;
        AssertFloat(rest.Origin.X).IsEqualApprox(rootHead.X, tol);
        AssertFloat(rest.Origin.Y).IsEqualApprox(rootHead.Y, tol);
        AssertFloat(rest.Origin.Z).IsEqualApprox(rootHead.Z, tol);
    }

    // -------------------------------------------------------------------------
    // Skin — bind pose is the inverse global rest (M3 P0)
    // -------------------------------------------------------------------------

    [TestCase]
    public void BuildSkin_bind_pose_inverts_global_rest_so_rest_is_undeformed()
    {
        // A two-bone chain: the child bends up off the root's +Z axis, so the
        // parent transform is non-trivial and the round-trip is meaningful.
        var root  = new Bone(new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 1f), 0.4f, 0.4f, -1);
        var child = new Bone(new Vector3(0f, 0f, 1f),  new Vector3(0f, 1f, 1f), 0.3f, 0.2f, 0);
        var skeleton = new CreatureSkeleton(new[] { root, child });

        Skeleton3D skel3d = AutoFree(GodotMeshBuilder.BuildSkeleton3D(skeleton))!;
        Skin skin = GodotMeshBuilder.BuildSkin(skeleton);

        AssertThat(skin.GetBindCount()).IsEqual(2);
        for (int i = 0; i < skeleton.Count; i++)
        {
            AssertThat(skin.GetBindBone(i)).IsEqual(i);
            // Skinning matrix at rest = globalRest_i * bindPose_i must be identity
            // (that is exactly what leaves the mesh undeformed in the rest pose).
            Transform3D skinning = GlobalRest(skel3d, i) * skin.GetBindPose(i);
            AssertThat(skinning.IsEqualApprox(Transform3D.Identity)).IsTrue();
        }
    }

    /// Accumulate a bone's global rest from the Skeleton3D's local rests.
    private static Transform3D GlobalRest(Skeleton3D s, int bone)
    {
        Transform3D t = s.GetBoneRest(bone);
        int p = s.GetBoneParent(bone);
        while (p >= 0)
        {
            t = s.GetBoneRest(p) * t;
            p = s.GetBoneParent(p);
        }
        return t;
    }
}
