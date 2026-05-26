using System;
using System.Collections.Generic;
using Godot;

namespace LittleGods.Mesh;

/// <summary>
/// Marching Cubes polygoniser. Voxelises an <see cref="IScalarField"/> over its
/// <see cref="IScalarField.Bounds"/> and extracts the iso-surface as an indexed
/// triangle mesh.
///
/// Algorithm: canonical Lorensen &amp; Cline / Paul Bourke marching cubes,
/// "Polygonising a scalar field" (http://paulbourke.net/geometry/polygonise/).
/// The 256-entry edge and triangle tables live in
/// <see cref="MarchingCubesTables"/>.
///
/// Conventions (per the M2 P1 contract):
///  - The field is HIGH inside the solid and falls to ~0 far away.
///  - cubeindex bit i is set when cornerValue[i] &lt; isoLevel (Bourke bit
///    convention as written in the reference; here a "set" bit therefore marks
///    a corner OUTSIDE the solid). Paired with the outward normal = (-gradient),
///    this produces consistently wound CCW front faces.
///  - Edge crossings use linear interpolation (VertexInterp), never midpoint
///    snapping.
///  - Vertices are deduplicated by a canonical GLOBAL edge key (the unordered
///    pair of integer grid-corner coordinates the edge spans), so triangles
///    that share a grid edge share a vertex index. The result is watertight by
///    index.
///  - Per-vertex normals come from the field gradient (central differences),
///    normalised and negated so they point outward (toward decreasing field).
///
/// Pure and deterministic: cells are marched in a fixed (z, y, x) order, with no
/// RNG, no clock and no parallelism. Identical field + params yield byte-for-byte
/// identical arrays.
/// </summary>
public static class MarchingCubes
{
    public static MeshData Polygonise(IScalarField field, float cellSize, float isoLevel = 0.5f)
    {
        if (field == null)
        {
            throw new ArgumentNullException(nameof(field));
        }
        if (!(cellSize > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), "cellSize must be > 0.");
        }

        Aabb bounds = field.Bounds;
        Vector3 origin = bounds.Position;
        Vector3 size = bounds.Size;

        // Sample points per axis = ceil(size/cellSize) + 1, clamped to >= 2 so
        // there is always at least one cell. cells = samples - 1.
        int sx = SamplesAlong(size.X, cellSize);
        int sy = SamplesAlong(size.Y, cellSize);
        int sz = SamplesAlong(size.Z, cellSize);

        int cellsX = sx - 1;
        int cellsY = sy - 1;
        int cellsZ = sz - 1;

        // Cache the scalar value at every grid corner so each corner's field is
        // sampled exactly once. Index = (z * sy + y) * sx + x.
        float[] values = SampleGrid(field, origin, cellSize, sx, sy, sz);

        var vertices = new List<Vector3>();
        // Maps a canonical global edge key -> emitted vertex index.
        var edgeToVertex = new Dictionary<long, int>();
        var indices = new List<int>();

        // Reusable per-cell scratch (avoids per-cell allocation; values are
        // overwritten before use, so this introduces no shared mutable state
        // across the deterministic ordering).
        Span<int> cornerIndex = stackalloc int[8];
        Span<float> cornerValue = stackalloc float[8];

        // Fixed (z, y, x) iteration order for determinism.
        for (int cz = 0; cz < cellsZ; cz++)
        {
            for (int cy = 0; cy < cellsY; cy++)
            {
                for (int cx = 0; cx < cellsX; cx++)
                {
                    int cubeindex = 0;
                    for (int c = 0; c < 8; c++)
                    {
                        int[] off = MarchingCubesTables.CornerOffset[c];
                        int gx = cx + off[0];
                        int gy = cy + off[1];
                        int gz = cz + off[2];
                        int flat = (gz * sy + gy) * sx + gx;
                        cornerIndex[c] = flat;
                        float v = values[flat];
                        cornerValue[c] = v;
                        // Bit set when this corner is below the iso level.
                        if (v < isoLevel)
                        {
                            cubeindex |= 1 << c;
                        }
                    }

                    int edgeMask = MarchingCubesTables.EdgeTable[cubeindex];
                    if (edgeMask == 0)
                    {
                        continue; // Cube wholly inside or outside: no crossing.
                    }

                    int[] tris = MarchingCubesTables.TriTable[cubeindex];
                    for (int t = 0; tris[t] != -1; t += 3)
                    {
                        int ea = tris[t];
                        int eb = tris[t + 1];
                        int ec = tris[t + 2];

                        int ia = EmitEdgeVertex(
                            ea, cx, cy, cz, isoLevel,
                            field, origin, cellSize,
                            cornerIndex, cornerValue,
                            sx, sy, vertices, edgeToVertex);
                        int ib = EmitEdgeVertex(
                            eb, cx, cy, cz, isoLevel,
                            field, origin, cellSize,
                            cornerIndex, cornerValue,
                            sx, sy, vertices, edgeToVertex);
                        int icv = EmitEdgeVertex(
                            ec, cx, cy, cz, isoLevel,
                            field, origin, cellSize,
                            cornerIndex, cornerValue,
                            sx, sy, vertices, edgeToVertex);

                        indices.Add(ia);
                        indices.Add(ib);
                        indices.Add(icv);
                    }
                }
            }
        }

        if (vertices.Count == 0 || indices.Count == 0)
        {
            return MeshData.Empty;
        }

        Vector3[] vertArray = vertices.ToArray();
        Vector3[] normals = ComputeNormals(field, vertArray, cellSize);
        return new MeshData(vertArray, normals, indices.ToArray());
    }

    /// <summary>Sample count along one axis: ceil(extent/cellSize) + 1, &gt;= 2.</summary>
    private static int SamplesAlong(float extent, float cellSize)
    {
        if (extent <= 0f)
        {
            return 2;
        }
        int cells = (int)Math.Ceiling(extent / cellSize);
        if (cells < 1)
        {
            cells = 1;
        }
        return cells + 1;
    }

    /// <summary>Sample the field at every grid corner exactly once.</summary>
    private static float[] SampleGrid(
        IScalarField field, Vector3 origin, float cellSize, int sx, int sy, int sz)
    {
        var values = new float[sx * sy * sz];
        int i = 0;
        for (int z = 0; z < sz; z++)
        {
            float wz = origin.Z + z * cellSize;
            for (int y = 0; y < sy; y++)
            {
                float wy = origin.Y + y * cellSize;
                for (int x = 0; x < sx; x++)
                {
                    float wx = origin.X + x * cellSize;
                    values[i++] = field.Sample(new Vector3(wx, wy, wz));
                }
            }
        }
        return values;
    }

    /// <summary>
    /// Resolve (or create) the deduplicated vertex for cube-local edge
    /// <paramref name="edge"/> and return its global index.
    /// </summary>
    private static int EmitEdgeVertex(
        int edge, int cx, int cy, int cz, float isoLevel,
        IScalarField field, Vector3 origin, float cellSize,
        ReadOnlySpan<int> cornerIndex, ReadOnlySpan<float> cornerValue,
        int sx, int sy,
        List<Vector3> vertices, Dictionary<long, int> edgeToVertex)
    {
        int[] ends = MarchingCubesTables.EdgeCorners[edge];
        int ca = ends[0];
        int cb = ends[1];

        int flatA = cornerIndex[ca];
        int flatB = cornerIndex[cb];

        // Canonical global key: order-independent over the two grid corners.
        long key = EdgeKey(flatA, flatB);
        if (edgeToVertex.TryGetValue(key, out int existing))
        {
            return existing;
        }

        // World positions of the two grid corners this edge spans.
        Vector3 pA = CornerWorld(flatA, origin, cellSize, sx, sy);
        Vector3 pB = CornerWorld(flatB, origin, cellSize, sx, sy);
        Vector3 p = VertexInterp(isoLevel, pA, pB, cornerValue[ca], cornerValue[cb]);

        int index = vertices.Count;
        vertices.Add(p);
        edgeToVertex[key] = index;
        return index;
    }

    /// <summary>
    /// Undirected edge key from two flat grid-corner indices. Independent of the
    /// owning cube, so an edge shared by neighbouring cubes maps to one vertex.
    /// </summary>
    private static long EdgeKey(int flatA, int flatB)
    {
        int lo = flatA < flatB ? flatA : flatB;
        int hi = flatA < flatB ? flatB : flatA;
        return ((long)lo << 32) | (uint)hi;
    }

    /// <summary>World-space position of a flat grid-corner index.</summary>
    private static Vector3 CornerWorld(int flat, Vector3 origin, float cellSize, int sx, int sy)
    {
        int x = flat % sx;
        int rem = flat / sx;
        int y = rem % sy;
        int z = rem / sy;
        return new Vector3(
            origin.X + x * cellSize,
            origin.Y + y * cellSize,
            origin.Z + z * cellSize);
    }

    /// <summary>
    /// Linear interpolation of the point where the field crosses
    /// <paramref name="iso"/> along the segment pA-pB (Bourke VertexInterp).
    /// </summary>
    private static Vector3 VertexInterp(float iso, Vector3 pA, Vector3 pB, float valA, float valB)
    {
        // Degenerate / coincident-value guards mirror Bourke's reference.
        const float eps = 1e-7f;
        if (Math.Abs(iso - valA) < eps)
        {
            return pA;
        }
        if (Math.Abs(iso - valB) < eps)
        {
            return pB;
        }
        if (Math.Abs(valA - valB) < eps)
        {
            return pA;
        }
        float mu = (iso - valA) / (valB - valA);
        return new Vector3(
            pA.X + mu * (pB.X - pA.X),
            pA.Y + mu * (pB.Y - pA.Y),
            pA.Z + mu * (pB.Z - pA.Z));
    }

    /// <summary>
    /// Per-vertex outward normals from the field gradient via central
    /// differences. The field rises toward the interior, so the outward normal
    /// is (-gradient).Normalized(); zero-length gradients fall back to +Y.
    /// </summary>
    private static Vector3[] ComputeNormals(IScalarField field, Vector3[] verts, float cellSize)
    {
        float h = cellSize * 0.5f;
        float inv2h = 1f / (2f * h);
        var normals = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 p = verts[i];
            float dx = field.Sample(new Vector3(p.X + h, p.Y, p.Z))
                       - field.Sample(new Vector3(p.X - h, p.Y, p.Z));
            float dy = field.Sample(new Vector3(p.X, p.Y + h, p.Z))
                       - field.Sample(new Vector3(p.X, p.Y - h, p.Z));
            float dz = field.Sample(new Vector3(p.X, p.Y, p.Z + h))
                       - field.Sample(new Vector3(p.X, p.Y, p.Z - h));

            // Outward = toward decreasing field = -gradient.
            var n = new Vector3(-dx * inv2h, -dy * inv2h, -dz * inv2h);
            float len = n.Length();
            normals[i] = len > 1e-12f ? n / len : Vector3.Up;
        }
        return normals;
    }
}
