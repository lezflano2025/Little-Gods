using Godot;
using LittleGods.Creature;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

[TestSuite]
public class CreatureDataModelTests
{
    // ---------- Part ----------

    [TestCase]
    public void Part_default_construction_is_safe()
    {
        var p = new Part();
        AssertThat(p.Id).IsEqual("");
        AssertThat(p.DisplayName).IsEqual("");
        AssertThat(p.Kind).IsEqual(PartKind.Other);
        AssertThat(p.AttachmentPoints.Count).IsEqual(0);
        AssertThat(p.PaintRegions.Length).IsEqual(0);
        AssertThat(p.Footprint2D).IsEqual(Vector2.One);
    }

    [TestCase]
    public void Part_roundtrip_preserves_all_fields()
    {
        var p = new Part
        {
            Id = "spine_basic",
            DisplayName = "Basic Spine",
            Kind = PartKind.Spine,
            Footprint2D = new Vector2(1.5f, 0.5f),
            PaintRegions = new[] { "back", "belly" },
        };
        p.AttachmentPoints.Add(new AttachmentPoint
        {
            Name = "head",
            LocalPosition = new Vector3(0, 0, 1),
            LocalNormal = Vector3.Forward,
            AllowedKinds = PartKindMask.Head,
        });

        var tmp = TmpPath("part_roundtrip.tres");
        var err = ResourceSaver.Save(p, tmp);
        AssertThat((int)err).IsEqual((int)Error.Ok);

        var loaded = ResourceLoader.Load<Part>(tmp, "", ResourceLoader.CacheMode.Ignore);
        AssertThat(loaded).IsNotNull();
        AssertThat(loaded!.Id).IsEqual("spine_basic");
        AssertThat(loaded.DisplayName).IsEqual("Basic Spine");
        AssertThat(loaded.Kind).IsEqual(PartKind.Spine);
        AssertThat(loaded.Footprint2D).IsEqual(new Vector2(1.5f, 0.5f));
        AssertThat(loaded.PaintRegions.Length).IsEqual(2);
        AssertThat(loaded.AttachmentPoints.Count).IsEqual(1);
        AssertThat(loaded.AttachmentPoints[0].Name).IsEqual("head");
        AssertThat(loaded.AttachmentPoints[0].AllowedKinds).IsEqual(PartKindMask.Head);
    }

    // ---------- AttachmentPoint ----------

    [TestCase]
    public void AttachmentPoint_default_allows_every_kind()
    {
        var ap = new AttachmentPoint();
        AssertThat(ap.AllowedKinds.Allows(PartKind.Spine)).IsTrue();
        AssertThat(ap.AllowedKinds.Allows(PartKind.Limb)).IsTrue();
        AssertThat(ap.AllowedKinds.Allows(PartKind.Head)).IsTrue();
        AssertThat(ap.AllowedKinds.Allows(PartKind.Mouth)).IsTrue();
        AssertThat(ap.AllowedKinds.Allows(PartKind.Other)).IsTrue();
    }

    [TestCase]
    public void AttachmentPoint_restrictive_mask_rejects_others()
    {
        var ap = new AttachmentPoint { AllowedKinds = PartKindMask.Head | PartKindMask.Mouth };
        AssertThat(ap.AllowedKinds.Allows(PartKind.Head)).IsTrue();
        AssertThat(ap.AllowedKinds.Allows(PartKind.Mouth)).IsTrue();
        AssertThat(ap.AllowedKinds.Allows(PartKind.Limb)).IsFalse();
        AssertThat(ap.AllowedKinds.Allows(PartKind.Spine)).IsFalse();
    }

    // ---------- Morph ----------

    [TestCase]
    public void Morph_identity_defaults()
    {
        var m = new Morph();
        AssertThat(m.Stretch).IsEqual(Vector3.One);
        AssertThat(m.Twist).IsEqual(0f);
        AssertThat(m.PaintTint).IsEqual(Colors.White);
    }

    // ---------- Recipe ----------

    [TestCase]
    public void Recipe_defaults_are_empty_and_versioned()
    {
        var r = new Recipe();
        AssertThat(r.FormatVersion).IsEqual(Recipe.CurrentFormatVersion);
        AssertThat(r.SpinePartId).IsEqual("");
        AssertThat(r.Attachments.Count).IsEqual(0);
        AssertThat(r.Morphs.Count).IsEqual(0);
    }

    [TestCase]
    public void Recipe_roundtrip_preserves_attachments_and_morphs()
    {
        var r = MakeFixtureCreature();
        var tmp = TmpPath("recipe_roundtrip.tres");
        var err = r.Save(tmp);
        AssertThat((int)err).IsEqual((int)Error.Ok);

        var loaded = Recipe.Load(tmp);
        AssertThat(loaded.SpinePartId).IsEqual(r.SpinePartId);
        AssertThat(loaded.Attachments.Count).IsEqual(r.Attachments.Count);
        AssertThat(loaded.Morphs.Count).IsEqual(r.Morphs.Count);
        AssertThat(loaded.FormatVersion).IsEqual(Recipe.CurrentFormatVersion);

        for (int i = 0; i < loaded.Attachments.Count; i++)
        {
            AssertThat(loaded.Attachments[i].ChildPartId).IsEqual(r.Attachments[i].ChildPartId);
            AssertThat(loaded.Attachments[i].ParentSlotName).IsEqual(r.Attachments[i].ParentSlotName);
        }
    }

    [TestCase]
    public void Recipe_roundtrip_preserves_every_attachment_field()
    {
        // PRD §7 M1: "Save -> close -> reopen -> identical creature".
        // We don't require byte-identical on-disk representation - Godot
        // regenerates ext_resource IDs on every save, which is fine. What
        // we require is that every field of every attachment survives the
        // round-trip exactly.
        var original = MakeFixtureCreature();
        original.Morphs.Add(new Morph
        {
            Stretch = new Vector3(1.5f, 0.8f, 1.0f),
            Twist = 0.3f,
            PaintTint = new Color(0.7f, 0.4f, 0.2f, 1.0f),
        });
        original.Attachments[0].MorphIndex = 0;
        original.Attachments[0].MirrorGroupId = "leg_pair_a";
        original.Attachments[1].MirrorGroupId = "leg_pair_a";

        var path = TmpPath("recipe_structural.tres");
        original.Save(path);
        var loaded = Recipe.Load(path);

        AssertThat(loaded.SpinePartId).IsEqual(original.SpinePartId);
        AssertThat(loaded.FormatVersion).IsEqual(original.FormatVersion);
        AssertThat(loaded.Attachments.Count).IsEqual(original.Attachments.Count);
        AssertThat(loaded.Morphs.Count).IsEqual(original.Morphs.Count);

        for (int i = 0; i < loaded.Attachments.Count; i++)
        {
            var l = loaded.Attachments[i];
            var o = original.Attachments[i];
            AssertThat(l.ParentPartIndex).IsEqual(o.ParentPartIndex);
            AssertThat(l.ParentSlotName).IsEqual(o.ParentSlotName);
            AssertThat(l.ChildPartId).IsEqual(o.ChildPartId);
            AssertThat(l.LocalTransform).IsEqual(o.LocalTransform);
            AssertThat(l.MorphIndex).IsEqual(o.MorphIndex);
            AssertThat(l.MirrorGroupId).IsEqual(o.MirrorGroupId);
        }

        for (int i = 0; i < loaded.Morphs.Count; i++)
        {
            AssertThat(loaded.Morphs[i].Stretch).IsEqual(original.Morphs[i].Stretch);
            AssertThat(loaded.Morphs[i].Twist).IsEqual(original.Morphs[i].Twist);
            AssertThat(loaded.Morphs[i].PaintTint).IsEqual(original.Morphs[i].PaintTint);
        }
    }

    [TestCase]
    public void Recipe_fixture_creature_is_under_10kb()
    {
        var r = MakeFixtureCreature();   // 6-leg + 2-arm + tail = 9 attachments
        var tmp = TmpPath("recipe_size.tres");
        r.Save(tmp);
        var size = Recipe.FileSize(tmp);
        AssertThat(size).IsLess(Recipe.MaxRecipeBytes);
    }

    [TestCase]
    public void Recipe_load_throws_on_version_mismatch()
    {
        var r = new Recipe
        {
            SpinePartId = "spine_basic",
            FormatVersion = 999,
        };
        var tmp = TmpPath("recipe_badversion.tres");
        r.Save(tmp);

        var threw = false;
        try { Recipe.Load(tmp); }
        catch (RecipeVersionException) { threw = true; }
        AssertThat(threw).IsTrue();
    }

    // ---------- helpers ----------

    private static string TmpPath(string name) =>
        $"{OS.GetUserDataDir()}/test_{name}";

    private static Recipe MakeFixtureCreature()
    {
        var r = new Recipe { SpinePartId = "spine_basic" };

        for (int i = 0; i < 6; i++)
        {
            r.Attachments.Add(new Attachment
            {
                ParentPartIndex = -1,
                ParentSlotName = $"leg_{i}",
                ChildPartId = "limb_walker",
                LocalTransform = new Transform3D(Basis.Identity, new Vector3(0.3f * i, 0, 0)),
            });
        }
        for (int i = 0; i < 2; i++)
        {
            r.Attachments.Add(new Attachment
            {
                ParentPartIndex = -1,
                ParentSlotName = $"arm_{i}",
                ChildPartId = "limb_runner",
                LocalTransform = Transform3D.Identity,
            });
        }
        r.Attachments.Add(new Attachment
        {
            ParentPartIndex = -1,
            ParentSlotName = "tail",
            ChildPartId = "limb_tail",
            LocalTransform = Transform3D.Identity,
        });
        return r;
    }
}
