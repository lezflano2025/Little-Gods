using Godot;
using LittleGods.Creature;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

[TestSuite]
public class RecipeStorageTests
{
    private const string TestSlugPrefix = "test_storage_";

    [Before]
    public void CleanUp()
    {
        foreach (var slug in RecipeStorage.List())
        {
            if (slug.StartsWith(TestSlugPrefix))
            {
                RecipeStorage.Delete(slug);
            }
        }
    }

    [TestCase]
    public void Save_then_Load_round_trip()
    {
        var r = new Recipe { SpinePartId = "spine_basic" };
        var slug = $"{TestSlugPrefix}roundtrip";

        var err = RecipeStorage.Save(r, slug);
        AssertThat((int)err).IsEqual((int)Error.Ok);
        AssertThat(RecipeStorage.Exists(slug)).IsTrue();

        var loaded = RecipeStorage.Load(slug);
        AssertThat(loaded.SpinePartId).IsEqual("spine_basic");
    }

    [TestCase]
    public void List_returns_only_saved_slugs()
    {
        var r = new Recipe { SpinePartId = "spine_basic" };
        RecipeStorage.Save(r, $"{TestSlugPrefix}alpha");
        RecipeStorage.Save(r, $"{TestSlugPrefix}beta");

        var slugs = RecipeStorage.List();
        AssertThat(slugs.Contains($"{TestSlugPrefix}alpha")).IsTrue();
        AssertThat(slugs.Contains($"{TestSlugPrefix}beta")).IsTrue();
    }

    [TestCase]
    public void Delete_removes_recipe_from_disk()
    {
        var r = new Recipe { SpinePartId = "spine_basic" };
        var slug = $"{TestSlugPrefix}delete_target";
        RecipeStorage.Save(r, slug);
        AssertThat(RecipeStorage.Exists(slug)).IsTrue();

        var removed = RecipeStorage.Delete(slug);
        AssertThat(removed).IsTrue();
        AssertThat(RecipeStorage.Exists(slug)).IsFalse();
    }

    [TestCase]
    public void Delete_returns_false_for_missing_slug()
    {
        var removed = RecipeStorage.Delete($"{TestSlugPrefix}does_not_exist");
        AssertThat(removed).IsFalse();
    }

    [TestCase]
    public void Slug_sanitization_strips_unsafe_chars()
    {
        var path = RecipeStorage.PathFor("My Creature! / drop$table");
        AssertThat(path.Contains(" ")).IsFalse();
        AssertThat(path.Contains("!")).IsFalse();
        AssertThat(path.Contains("/drop")).IsFalse();
        AssertThat(path.Contains("$")).IsFalse();
        // Original alphanumerics survive
        AssertThat(path.Contains("MyCreaturedroptable")).IsTrue();
    }

    [TestCase]
    public void Empty_or_punctuation_only_slug_becomes_unnamed()
    {
        var path = RecipeStorage.PathFor("!!! / $$$");
        AssertThat(path.EndsWith("unnamed.tres")).IsTrue();
    }
}
