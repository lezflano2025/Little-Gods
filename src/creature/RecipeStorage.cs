using Godot;
using System.Collections.Generic;

namespace LittleGods.Creature;

/// File-system convention for user recipes. Recipes saved via the editor
/// land in `user://recipes/<slug>.tres`.
public static class RecipeStorage
{
    public const string UserRecipesDir = "user://recipes/";

    public static string PathFor(string slug) => $"{UserRecipesDir}{Sanitize(slug)}.tres";

    public static Error Save(Recipe recipe, string slug)
    {
        EnsureDir();
        return recipe.Save(PathFor(slug));
    }

    public static Recipe Load(string slug) => Recipe.Load(PathFor(slug));

    public static bool Exists(string slug) => Godot.FileAccess.FileExists(PathFor(slug));

    public static bool Delete(string slug)
    {
        if (!Exists(slug)) return false;
        var abs = ProjectSettings.GlobalizePath(PathFor(slug));
        var err = DirAccess.RemoveAbsolute(abs);
        return err == Error.Ok;
    }

    public static List<string> List()
    {
        var slugs = new List<string>();
        if (!DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(UserRecipesDir)))
        {
            return slugs;
        }
        var dir = DirAccess.Open(UserRecipesDir);
        if (dir == null) return slugs;
        dir.ListDirBegin();
        var name = dir.GetNext();
        while (!string.IsNullOrEmpty(name))
        {
            if (!dir.CurrentIsDir() && name.EndsWith(".tres"))
            {
                slugs.Add(name.Substring(0, name.Length - 5));
            }
            name = dir.GetNext();
        }
        dir.ListDirEnd();
        return slugs;
    }

    private static void EnsureDir()
    {
        var abs = ProjectSettings.GlobalizePath(UserRecipesDir);
        if (!DirAccess.DirExistsAbsolute(abs))
        {
            DirAccess.MakeDirRecursiveAbsolute(abs);
        }
    }

    /// Strip slug down to alphanumeric / dash / underscore. Empty input
    /// becomes "unnamed".
    private static string Sanitize(string slug)
    {
        var sb = new System.Text.StringBuilder(slug.Length);
        foreach (var c in slug)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
            {
                sb.Append(c);
            }
        }
        return sb.Length > 0 ? sb.ToString() : "unnamed";
    }
}
