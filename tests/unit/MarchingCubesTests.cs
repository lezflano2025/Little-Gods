using System.Collections.Generic;
using Godot;
using LittleGods.Mesh;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// M2 P1 (agent A): MarchingCubes polygonises an IScalarField into an indexed,
/// watertight triangle mesh with outward field-gradient normals.
[TestSuite]
public class MarchingCubesTests
{
    private const float R = 1.0f;
    private const float CellSize = 0.1f;

    /// Analytic sphere field: f(p) = 1.5 - |p|/R, so the iso=0.5 surface is
    /// exactly |p| = R. High at the centre (1.5), low far away — matching the
    /// "high inside" convention. Bounds pad the sphere by 20% per side.
    private sealed class SphereField : IScalarField
    {
        public float Sample(Vector3 p) => 1.5f - p.Length() / R;

        public Aabb Bounds => new(
            new Vector3(-1.2f * R, -1.2f * R, -1.2f * R),
            new Vector3(2.4f * R, 2.4f * R, 2.4f * R));
    }

    /// Constant field below the iso level everywhere: never crosses 0.5.
    private sealed class EmptyField : IScalarField
    {
        public float Sample(Vector3 p) => 0f;

        public Aabb Bounds => new(
            new Vector3(-1f, -1f, -1f),
            new Vector3(2f, 2f, 2f));
    }

    [TestCase]
    public void Triangle_table_entries_reference_only_crossed_edges()
    {
        for (int cubeindex = 0; cubeindex < 256; cubeindex++)
        {
            int edgeMask = MarchingCubesTables.EdgeTable[cubeindex];
            int[] tris = MarchingCubesTables.TriTable[cubeindex];

            int count = 0;
            for (int t = 0; t < tris.Length; t++)
            {
                int e = tris[t];
                if (e == -1)
                {
                    break;
                }
                count++;
                // Edge index in [0,11].
                AssertThat(e >= 0 && e <= 11).IsTrue();
                // That edge's crossing bit must be set in the edge table.
                AssertThat((edgeMask & (1 << e)) != 0).IsTrue();
            }
            // Vertices come in whole triangles (multiples of 3).
            AssertThat(count % 3).IsEqual(0);
        }
    }

    [TestCase]
    public void Below_iso_field_yields_empty_mesh()
    {
        var result = MarchingCubes.Polygonise(new EmptyField(), CellSize);
        AssertThat(result.IsEmpty).IsTrue();
    }

    [TestCase]
    public void Sphere_field_is_well_formed_and_non_empty()
    {
        var result = MarchingCubes.Polygonise(new SphereField(), CellSize);

        AssertThat(result.IsEmpty).IsFalse();
        AssertThat(result.IsWellFormed()).IsTrue();
        AssertThat(result.TriangleCount > 0).IsTrue();
    }

    [TestCase]
    public void Sphere_vertices_lie_in_a_thin_shell_at_radius_R()
    {
        var result = MarchingCubes.Polygonise(new SphereField(), CellSize);

        float tol = 1.5f * CellSize;
        foreach (Vector3 v in result.Vertices)
        {
            float dist = v.Length();
            AssertFloat(dist).IsEqualApprox(R, tol);
        }
    }

    [TestCase]
    public void Sphere_normals_point_outward()
    {
        var result = MarchingCubes.Polygonise(new SphereField(), CellSize);

        for (int i = 0; i < result.Vertices.Length; i++)
        {
            Vector3 v = result.Vertices[i];
            Vector3 n = result.Normals[i];
            // For a sphere the outward direction is the radial direction.
            Vector3 radial = v.Normalized();
            float dot = n.Dot(radial);
            AssertThat(dot > 0.5f).IsTrue();
        }
    }

    [TestCase]
    public void Sphere_mesh_is_watertight()
    {
        var result = MarchingCubes.Polygonise(new SphereField(), CellSize);

        // Count how many triangles touch each undirected edge.
        var edgeCounts = new Dictionary<long, int>();
        int[] idx = result.Indices;
        for (int t = 0; t < idx.Length; t += 3)
        {
            int a = idx[t];
            int b = idx[t + 1];
            int c = idx[t + 2];
            AddEdge(edgeCounts, a, b);
            AddEdge(edgeCounts, b, c);
            AddEdge(edgeCounts, c, a);
        }

        AssertThat(edgeCounts.Count > 0).IsTrue();
        foreach (KeyValuePair<long, int> kv in edgeCounts)
        {
            // Every edge is shared by exactly two triangles -> closed surface.
            AssertThat(kv.Value).IsEqual(2);
        }
    }

    [TestCase]
    public void Polygonise_is_deterministic()
    {
        var field = new SphereField();
        var a = MarchingCubes.Polygonise(field, CellSize);
        var b = MarchingCubes.Polygonise(field, CellSize);

        AssertThat(a.Vertices.Length).IsEqual(b.Vertices.Length);
        AssertThat(a.Indices.Length).IsEqual(b.Indices.Length);

        // Sample a few vertex components for exact (approx) agreement.
        int step = a.Vertices.Length / 5;
        if (step < 1)
        {
            step = 1;
        }
        for (int i = 0; i < a.Vertices.Length; i += step)
        {
            AssertFloat(a.Vertices[i].X).IsEqualApprox(b.Vertices[i].X, 1e-6f);
            AssertFloat(a.Vertices[i].Y).IsEqualApprox(b.Vertices[i].Y, 1e-6f);
            AssertFloat(a.Vertices[i].Z).IsEqualApprox(b.Vertices[i].Z, 1e-6f);
        }

        // Index buffers must match element-for-element.
        for (int i = 0; i < a.Indices.Length; i += step * 3 + 1)
        {
            AssertThat(a.Indices[i]).IsEqual(b.Indices[i]);
        }
    }

    private static void AddEdge(Dictionary<long, int> counts, int a, int b)
    {
        int lo = a < b ? a : b;
        int hi = a < b ? b : a;
        long key = ((long)lo << 32) | (uint)hi;
        counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
    }
}
