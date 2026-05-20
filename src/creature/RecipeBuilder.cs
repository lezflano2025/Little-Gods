using Godot;
using System;
using System.Collections.Generic;

namespace LittleGods.Creature;

/// Editor-facing wrapper around a mutable Recipe. The blueprint editor
/// (GDScript) drives a RecipeBuilder; CreatureEditor.gd holds the
/// instance.
///
/// All edit operations go through this class so that:
/// - symmetry pairing is handled in one place
/// - parent-index fixups after deletion are correct
/// - the validator can be invoked uniformly
public class RecipeBuilder
{
    public Recipe Recipe { get; }
    public PartRegistry Registry { get; }
    public bool SymmetryEnabled { get; set; }

    /// Bumped on every successful mutation (used by the editor to invalidate
    /// the rendered view).
    public ulong Revision { get; private set; }

    public RecipeBuilder(Recipe recipe, PartRegistry registry)
    {
        Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public static RecipeBuilder ForNewCreature(string spinePartId, PartRegistry registry)
    {
        return new RecipeBuilder(new Recipe { SpinePartId = spinePartId }, registry);
    }

    /// Add an attachment. Returns the new attachment's index.
    public int AddAttachment(int parentIndex, string slotName, string childPartId,
                              Transform3D? localTransform = null)
    {
        var att = new Attachment
        {
            ParentPartIndex = parentIndex,
            ParentSlotName = slotName,
            ChildPartId = childPartId,
            LocalTransform = localTransform ?? Transform3D.Identity,
        };
        Recipe.Attachments.Add(att);
        Revision++;
        return Recipe.Attachments.Count - 1;
    }

    /// Add an attachment, and if SymmetryEnabled with a mirrorable slot,
    /// also add its mirror partner. Returns the indices of every
    /// attachment added (1 or 2 entries).
    public int[] AddAttachmentMaybeMirrored(int parentIndex, string slotName,
                                             string childPartId, Transform3D localTransform)
    {
        int primary = AddAttachment(parentIndex, slotName, childPartId, localTransform);
        if (!SymmetryEnabled)
        {
            return new[] { primary };
        }
        var mirrorSlot = MirrorSlotName(slotName);
        if (mirrorSlot == null || mirrorSlot == slotName)
        {
            return new[] { primary };
        }
        var groupId = NewMirrorGroupId();
        Recipe.Attachments[primary].MirrorGroupId = groupId;

        int mirror = AddAttachment(parentIndex, mirrorSlot, childPartId, MirrorTransform(localTransform));
        Recipe.Attachments[mirror].MirrorGroupId = groupId;
        return new[] { primary, mirror };
    }

    /// Remove an attachment and re-parent / re-index any orphans.
    public void RemoveAttachment(int index)
    {
        if (index < 0 || index >= Recipe.Attachments.Count) return;

        // If this attachment had a mirror partner, clear the partner's group id.
        var groupId = Recipe.Attachments[index].MirrorGroupId;
        if (!string.IsNullOrEmpty(groupId))
        {
            for (int i = 0; i < Recipe.Attachments.Count; i++)
            {
                if (i == index) continue;
                if (Recipe.Attachments[i].MirrorGroupId == groupId)
                {
                    Recipe.Attachments[i].MirrorGroupId = "";
                }
            }
        }

        Recipe.Attachments.RemoveAt(index);

        // Fix parent indices in any remaining attachment.
        for (int i = 0; i < Recipe.Attachments.Count; i++)
        {
            var a = Recipe.Attachments[i];
            if (a.ParentPartIndex == index)
            {
                a.ParentPartIndex = -1;   // orphan -> reparent to spine
            }
            else if (a.ParentPartIndex > index)
            {
                a.ParentPartIndex--;
            }
        }
        Revision++;
    }

    /// Update an existing attachment's transform.
    public void SetTransform(int index, Transform3D localTransform)
    {
        if (index < 0 || index >= Recipe.Attachments.Count) return;
        Recipe.Attachments[index].LocalTransform = localTransform;
        Revision++;
    }

    /// Returns indices of every other attachment that shares this one's MirrorGroupId.
    public List<int> SiblingsInMirrorGroup(int index)
    {
        var result = new List<int>();
        if (index < 0 || index >= Recipe.Attachments.Count) return result;
        var groupId = Recipe.Attachments[index].MirrorGroupId;
        if (string.IsNullOrEmpty(groupId)) return result;
        for (int i = 0; i < Recipe.Attachments.Count; i++)
        {
            if (i == index) continue;
            if (Recipe.Attachments[i].MirrorGroupId == groupId)
            {
                result.Add(i);
            }
        }
        return result;
    }

    /// Slot-name mirror convention: "left_X" <-> "right_X". Returns null if not mirrorable.
    public static string? MirrorSlotName(string slotName)
    {
        if (slotName.StartsWith("left_", StringComparison.Ordinal))
            return "right_" + slotName.Substring(5);
        if (slotName.StartsWith("right_", StringComparison.Ordinal))
            return "left_" + slotName.Substring(6);
        return null;
    }

    /// Mirror a transform across the local X axis. Position is flipped; basis
    /// is left as-is (the basis encodes orientation relative to the slot's
    /// own normal, which is already mirrored by virtue of using a mirrored
    /// slot). M3 may revisit when IK retargeting lands.
    public static Transform3D MirrorTransform(Transform3D t)
    {
        var origin = t.Origin;
        origin.X = -origin.X;
        return new Transform3D(t.Basis, origin);
    }

    private static string NewMirrorGroupId() =>
        "mirror_" + Guid.NewGuid().ToString("N").Substring(0, 8);
}
