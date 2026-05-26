using LittleGods.Creature;

namespace LittleGods.Mesh;

/// Immutable grid parameters for the marching-cubes step.
/// CellSize controls voxel resolution; IsoLevel is the field threshold
/// for surface extraction — must match the MetaballField's calibration
/// (default 0.5 keeps bones' iso-contours at their RadiusAt(p)).
public readonly struct GridParams
{
    public readonly float CellSize;
    public readonly float IsoLevel;

    public GridParams(float cellSize, float isoLevel)
    {
        CellSize  = cellSize;
        IsoLevel  = isoLevel;
    }

    /// CellSize 0.1 f, IsoLevel 0.5 f — good balance of quality vs speed
    /// for the default editor preview. Production export may override.
    public static GridParams Default { get; } = new GridParams(0.1f, 0.5f);
}

/// The complete output of one Build call: the polygonised mesh, its skinning
/// data, and the skeleton from which both were derived.
/// All three properties are non-null; MeshData / SkinData may be empty when
/// the recipe has no valid spine.
public sealed class CreatureMeshResult
{
    public MeshData        Mesh     { get; }
    public SkinData        Skin     { get; }
    public CreatureSkeleton Skeleton { get; }

    public CreatureMeshResult(MeshData mesh, SkinData skin, CreatureSkeleton skeleton)
    {
        Mesh     = mesh     ?? MeshData.Empty;
        Skin     = skin     ?? new SkinData(System.Array.Empty<int>(), System.Array.Empty<float>());
        Skeleton = skeleton ?? new CreatureSkeleton(System.Array.Empty<Bone>());
    }
}

/// Orchestrates the full Recipe → mesh pipeline:
///
///   Recipe
///     → SkeletonResolver.Resolve        (world-space bones)
///     → MetaballField                   (implicit field over skeleton)
///     → MarchingCubes.Polygonise        (indexed triangle mesh)
///     → AutoSkinner.Skin                (per-vertex bone weights)
///     → CreatureMeshResult
///
/// Pure and deterministic: no RNG, no clock, no mutable static state.
/// Same Recipe + PartRegistry + GridParams always produces identical arrays
/// (PRD invariant 4).
public static class CreatureMesher
{
    /// Build a <see cref="CreatureMeshResult"/> from a recipe.
    ///
    /// <param name="recipe">Creature description. May reference any parts in
    ///   <paramref name="registry"/>.</param>
    /// <param name="registry">Part library — the same instance passed to the
    ///   editor's RecipeBuilder.</param>
    /// <param name="gridParams">Marching-cubes grid settings. Pass null to use
    ///   <see cref="GridParams.Default"/> (CellSize 0.1, IsoLevel 0.5).</param>
    /// <returns>
    ///   A <see cref="CreatureMeshResult"/> whose <see cref="CreatureMeshResult.Mesh"/>,
    ///   <see cref="CreatureMeshResult.Skin"/>, and
    ///   <see cref="CreatureMeshResult.Skeleton"/> are all non-null.
    ///   When the recipe's spine part is unknown (or the recipe is null),
    ///   the skeleton is empty and both Mesh and Skin are empty; no field or
    ///   marching-cubes work is performed.
    /// </returns>
    public static CreatureMeshResult Build(
        Recipe       recipe,
        PartRegistry registry,
        GridParams?  gridParams = null)
    {
        GridParams gp = gridParams ?? GridParams.Default;

        // Step 1 — resolve skeleton.
        // SkeletonResolver returns an empty skeleton (Count == 0) for null
        // inputs or unknown spine parts; we honour that as the empty result.
        CreatureSkeleton skeleton = SkeletonResolver.Resolve(recipe, registry);

        if (skeleton.Count == 0)
        {
            return new CreatureMeshResult(
                MeshData.Empty,
                new SkinData(System.Array.Empty<int>(), System.Array.Empty<float>()),
                skeleton);
        }

        // Step 2 — build the implicit field.
        IScalarField field = new MetaballField(skeleton, gp.IsoLevel);

        // Step 3 — extract the iso-surface as an indexed triangle mesh.
        MeshData mesh = MarchingCubes.Polygonise(field, gp.CellSize, gp.IsoLevel);

        // Step 4 — compute per-vertex bone weights.
        // Even if Polygonise returned MeshData.Empty (degenerate field), we
        // still skin the (empty) vertex array so types are consistent.
        SkinData skin = AutoSkinner.Skin(mesh.Vertices, skeleton);

        return new CreatureMeshResult(mesh, skin, skeleton);
    }
}
