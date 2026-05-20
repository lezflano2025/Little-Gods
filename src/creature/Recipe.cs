using Godot;
using Godot.Collections;

namespace LittleGods.Creature;

/// A creature recipe: spine + ordered attachments + morphs + paint + metadata.
/// Serialises to `.tres` text. Per PRD §6, recipe must stay under 10 KB.
[GlobalClass]
public partial class Recipe : Resource
{
    /// Bumped whenever the on-disk format changes incompatibly.
    public const int CurrentFormatVersion = 1;

    /// Hard upper bound on recipe file size in bytes (PRD §6 invariant 1).
    public const int MaxRecipeBytes = 10 * 1024;

    [Export] public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// Root part of the creature. Must be set; resolves via PartRegistry.
    [Export] public string SpinePartId { get; set; } = "";

    [Export] public Array<Attachment> Attachments { get; set; } = new();

    [Export] public Array<Morph> Morphs { get; set; } = new();

    /// Arbitrary key->Color paint overrides keyed by region name.
    [Export] public Dictionary Paint { get; set; } = new();

    /// Arbitrary metadata (author, created_at as iso8601 string, etc).
    /// Do NOT put DateTime.Now or anything non-deterministic here from code;
    /// metadata is set by the editor UI on explicit user save.
    [Export] public Dictionary Metadata { get; set; } = new();

    /// Save this Recipe to `path` (res://, user://, or absolute). Returns the
    /// Godot Error code from ResourceSaver.
    public Error Save(string path)
    {
        return ResourceSaver.Save(this, path);
    }

    /// Load a Recipe from `path`. Throws RecipeVersionException if the on-disk
    /// FormatVersion isn't CurrentFormatVersion. Throws FileNotFoundException
    /// if the resource can't be loaded at all.
    public static Recipe Load(string path)
    {
        var loaded = ResourceLoader.Load<Recipe>(path, nameof(Recipe), ResourceLoader.CacheMode.Ignore);
        if (loaded == null)
        {
            throw new System.IO.FileNotFoundException($"Recipe not loadable from {path}");
        }
        if (loaded.FormatVersion != CurrentFormatVersion)
        {
            throw new RecipeVersionException(loaded.FormatVersion);
        }
        return loaded;
    }

    /// Read the on-disk size in bytes for an already-saved recipe.
    /// Use after Save() to verify the 10 KB invariant.
    public static long FileSize(string path)
    {
        var bytes = Godot.FileAccess.GetFileAsBytes(path);
        return bytes.Length;
    }
}
