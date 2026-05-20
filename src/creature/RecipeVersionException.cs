namespace LittleGods.Creature;

/// Thrown by Recipe.Load when the on-disk FormatVersion is not current.
public class RecipeVersionException : System.Exception
{
    public int Version { get; }

    public RecipeVersionException(int version)
        : base($"Unsupported recipe FormatVersion: {version} (current: {Recipe.CurrentFormatVersion})")
    {
        Version = version;
    }
}
