using Godot;

namespace LittleGods.Mesh;

/// Plain triangle mesh: parallel vertex/normal arrays plus a flat index buffer
/// (3 indices per triangle). Produced by MarchingCubes (M2 P1 A) and consumed
/// by GodotMeshBuilder (M2 P2), which packs it into an ArrayMesh.
///
/// Kept free of any Godot scene-tree type so it stays unit-testable as pure
/// data (PRD invariant 5: C# does math).
public sealed class MeshData
{
    public Vector3[] Vertices { get; }
    public Vector3[] Normals { get; }
    public int[] Indices { get; }

    public MeshData(Vector3[] vertices, Vector3[] normals, int[] indices)
    {
        Vertices = vertices ?? System.Array.Empty<Vector3>();
        Normals = normals ?? System.Array.Empty<Vector3>();
        Indices = indices ?? System.Array.Empty<int>();
    }

    public static MeshData Empty => new(
        System.Array.Empty<Vector3>(),
        System.Array.Empty<Vector3>(),
        System.Array.Empty<int>());

    public bool IsEmpty => Vertices.Length == 0 || Indices.Length == 0;

    public int VertexCount => Vertices.Length;

    public int TriangleCount => Indices.Length / 3;

    /// True if the index buffer is well-formed: a whole number of triangles,
    /// every index in range, and a normal per vertex. Cheap sanity gate for
    /// tests before handing the mesh to Godot.
    public bool IsWellFormed()
    {
        if (Indices.Length % 3 != 0)
        {
            return false;
        }
        if (Normals.Length != Vertices.Length)
        {
            return false;
        }
        foreach (int i in Indices)
        {
            if (i < 0 || i >= Vertices.Length)
            {
                return false;
            }
        }
        return true;
    }
}
