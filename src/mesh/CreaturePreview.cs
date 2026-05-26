using System.Collections.Generic;
using Godot;
using LittleGods.Anim;
using LittleGods.Creature;

namespace LittleGods.Mesh;

/// Bridge node that lets GDScript trigger a full mesh rebuild and display the
/// result in the editor viewport. GDScript cannot call C# static methods, so
/// this node exposes instance methods instead.
///
/// Node layout (built lazily so unit tests can call Rebuild() off-tree):
///   CreaturePreview (this)
///     └─ Skeleton3D "CreatureSkeleton"
///          └─ MeshInstance3D "CreatureMesh"  (Skeleton = "..", Skin = bind pose)
///
/// Placing the mesh under the skeleton with a Skin (M3 P0) makes posing the
/// Skeleton3D deform the skinned mesh. Call Rebuild() when the recipe changes;
/// call ApplyPose() each tick to animate.
[GlobalClass]
public partial class CreaturePreview : Node3D
{
    private Skeleton3D? _skeleton;
    private MeshInstance3D? _meshInstance;
    private CreatureSkeleton? _lastSkeleton;
    private Recipe? _lastRecipe;
    private PartRegistry? _lastRegistry;
    private GridParams _gridParams = GridParams.Default;

    /// Vertex count from the most recent Rebuild call.
    /// Zero when the last recipe produced no geometry.
    public int LastVertexCount { get; private set; }

    /// Triangle count from the most recent Rebuild call.
    /// Zero when the last recipe produced no geometry.
    public int LastTriangleCount { get; private set; }

    /// The Skeleton3D driving the skin. Null before the first Rebuild. Exposed
    /// so the animation layer (and tests) can read/drive bone poses directly.
    public Skeleton3D? Skeleton => _skeleton;

    public override void _Ready() => EnsureNodes();

    /// Called from GDScript each time the recipe changes.
    /// Safe to call when the node is not in the scene tree (unit tests).
    /// Safe to call with an unknown/null spine — produces zero geometry, no exception.
    public void Rebuild(Recipe recipe, PartRegistry registry)
    {
        EnsureNodes();

        var result = CreatureMesher.Build(recipe, registry, _gridParams);
        _lastSkeleton = result.Skeleton;
        _lastRecipe = recipe;
        _lastRegistry = registry;

        // Rebuild the bone hierarchy in place (no node churn across rebuilds).
        GodotMeshBuilder.PopulateSkeleton3D(_skeleton!, result.Skeleton);

        _meshInstance!.Mesh = GodotMeshBuilder.BuildArrayMesh(result.Mesh, result.Skin);
        // A Skin is meaningful only when there are bones to bind to.
        _meshInstance.Skin = result.Skeleton.Count > 0
            ? GodotMeshBuilder.BuildSkin(result.Skeleton)
            : null;

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

    /// Apply a pose to the skeleton: each bone's local pose = restLocal * delta
    /// (see Anim.Pose). Identity deltas restore the rest pose (undeformed mesh).
    /// No-op before the first Rebuild.
    public void ApplyPose(Pose pose)
    {
        if (_skeleton is null)
        {
            return;
        }

        int n = _skeleton.GetBoneCount();
        for (int i = 0; i < n; i++)
        {
            Transform3D local = _skeleton.GetBoneRest(i) * pose.Delta(i);
            _skeleton.SetBonePosePosition(i, local.Origin);
            _skeleton.SetBonePoseRotation(i, local.Basis.GetRotationQuaternion());
            _skeleton.SetBonePoseScale(i, local.Basis.Scale);
        }
    }

    /// Editor / snapshot helper (GDScript-callable, since Anim.Pose is C#-only):
    /// bend the first limb chain's knee by `radians` about the limb's local bend
    /// axis, via ApplyPose. Returns false when there is no limb to bend. Used by
    /// the posed-knee snapshot and handy for eyeballing deformation in-editor.
    public bool BendFirstKnee(float radians)
    {
        if (_skeleton is null || _lastSkeleton is null || _lastSkeleton.LimbChains.Length == 0)
        {
            return false;
        }

        LimbChain chain = _lastSkeleton.LimbChains[0];
        // Rotate about local X (perpendicular to the bone's +Z length axis),
        // which swings the lower segment + foot around the knee.
        var delta = new Transform3D(new Basis(Vector3.Right, radians), Vector3.Zero);
        ApplyPose(Pose.Rest(_skeleton.GetBoneCount()).With(chain.KneeBone, delta));
        return true;
    }

    /// Editor / snapshot helper (GDScript-callable): run one deterministic
    /// locomotion tick at `seconds`, apply the resulting walk pose, and return
    /// the body world position (the caller positions THIS node so the body
    /// advances + bobs). Returns Vector3.Zero with no pose change when there is
    /// no walkable creature. Legs are the chains the classifier marks as Leg,
    /// falling back to every limb chain.
    public Vector3 WalkTick(double seconds)
    {
        if (_skeleton is null || _lastSkeleton is null || _lastSkeleton.LimbChains.Length == 0)
        {
            return Vector3.Zero;
        }

        int[] legs = LegChainIndices();
        if (legs.Length == 0)
        {
            return Vector3.Zero;
        }

        Gait gait = GaitController.ForLegCount(legs.Length, LocomotionParams.Default.CadenceHz);
        LocomotionResult result = Locomotion.Tick(_lastSkeleton, legs, gait, LocomotionParams.Default, seconds);
        ApplyPose(result.Pose);
        return result.BodyPosition;
    }

    /// Indices into _lastSkeleton.LimbChains that the classifier marks as legs;
    /// every chain when classification finds none (keeps a lone-limb test walking).
    private int[] LegChainIndices()
    {
        int n = _lastSkeleton!.LimbChains.Length;
        LimbType[] types = _lastRecipe != null && _lastRegistry != null
            ? LimbClassifier.Classify(_lastRecipe, _lastRegistry, _lastSkeleton)
            : System.Array.Empty<LimbType>();

        var legs = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (i < types.Length && types[i] == LimbType.Leg)
            {
                legs.Add(i);
            }
        }
        if (legs.Count == 0)
        {
            for (int i = 0; i < n; i++)
            {
                legs.Add(i);
            }
        }
        return legs.ToArray();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// Lazily create the Skeleton3D + child MeshInstance3D (with the cartoon
    /// material). Guarded so it is safe to call from both _Ready and Rebuild.
    private void EnsureNodes()
    {
        if (_skeleton is not null)
        {
            return;
        }

        _skeleton = new Skeleton3D { Name = "CreatureSkeleton" };
        AddChild(_skeleton);

        _meshInstance = new MeshInstance3D
        {
            Name = "CreatureMesh",
            // Spore-style stylized cartoon look (ADR-0004): saturated albedo,
            // smooth-ish, zero metallic, soft rim light for the rounded read.
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

        // The mesh is a child of the skeleton; "../" resolves to it so Godot
        // skins this MeshInstance3D against the skeleton's bone poses.
        _skeleton.AddChild(_meshInstance);
        _meshInstance.Skeleton = new NodePath("..");
    }
}
