using Godot;
using LittleGods.Creature;

namespace LittleGods.Mesh;

/// Bridge node that lets GDScript trigger a full mesh rebuild and display the
/// result in the editor viewport. GDScript cannot call C# static methods, so
/// this node exposes instance methods instead.
///
/// Place one CreaturePreview in the editor scene; call Rebuild() each time the
/// recipe changes. The child MeshInstance3D ("CreatureMesh") is created lazily
/// so unit tests can call Rebuild() without a live scene tree.
[GlobalClass]
public partial class CreaturePreview : Node3D
{
    private MeshInstance3D? _meshInstance;
    private GridParams _gridParams = GridParams.Default;

    /// Vertex count from the most recent Rebuild call.
    /// Zero when the last recipe produced no geometry.
    public int LastVertexCount { get; private set; }

    /// Triangle count from the most recent Rebuild call.
    /// Zero when the last recipe produced no geometry.
    public int LastTriangleCount { get; private set; }

    public override void _Ready() => EnsureMeshInstance();

    /// Called from GDScript each time the recipe changes.
    /// Safe to call when the node is not in the scene tree (unit tests).
    /// Safe to call with an unknown/null spine — produces zero geometry, no exception.
    public void Rebuild(Recipe recipe, PartRegistry registry)
    {
        EnsureMeshInstance();

        var result = CreatureMesher.Build(recipe, registry, _gridParams);

        _meshInstance!.Mesh = GodotMeshBuilder.BuildArrayMesh(result.Mesh, result.Skin);

        LastVertexCount   = result.Mesh.VertexCount;
        LastTriangleCount = result.Mesh.TriangleCount;
    }

    /// Override the marching-cubes cell size; IsoLevel is kept from the
    /// previous GridParams (default 0.5). Smaller values increase detail but
    /// slow the build significantly — 0.15 f is a good test trade-off.
    public void SetCellSize(float cellSize)
    {
        _gridParams = new GridParams(cellSize, _gridParams.IsoLevel);
    }

    /// Local-space AABB of the current mesh, for the editor's camera framing.
    /// Returns an empty box when there is no geometry yet.
    public Aabb GetMeshAabb() => _meshInstance?.GetAabb() ?? new Aabb();

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// Lazily create the child MeshInstance3D and its clay material.
    /// Guarded against double-creation so it is safe to call from both
    /// _Ready and Rebuild.
    private void EnsureMeshInstance()
    {
        if (_meshInstance is not null)
        {
            return;
        }

        _meshInstance = new MeshInstance3D
        {
            Name = "CreatureMesh",
            // Spore-style stylized cartoon look (ADR-0004): saturated albedo,
            // smooth-ish, zero metallic, soft rim light for the rounded read.
            // First pass via StandardMaterial3D; a cel shader + outline is a
            // later refinement. Per-creature paint will drive AlbedoColor once
            // the paint pipeline lands.
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.46f, 0.74f, 0.55f), // friendly Spore green
                Roughness   = 0.5f,
                Metallic    = 0.0f,
                RimEnabled  = true,
                Rim         = 0.6f,
                RimTint     = 0.25f,
            },
        };

        AddChild(_meshInstance);
    }
}
