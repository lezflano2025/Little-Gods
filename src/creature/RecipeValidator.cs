using System.Collections.Generic;

namespace LittleGods.Creature;

/// Cross-references a Recipe against a PartRegistry: checks every Part Id
/// referenced exists, parent indices are well-formed (no cycles, no
/// forward refs), slot names exist on the parent Part, and the child Kind
/// is allowed by the slot's mask.
public static class RecipeValidator
{
    public readonly record struct Issue(int AttachmentIndex, string Code, string Message);

    // Stable issue codes (tests assert on these).
    public const string NoSpine = "no_spine";
    public const string UnknownSpine = "unknown_spine";
    public const string ForwardRef = "forward_ref";
    public const string BadParentIndex = "bad_parent_index";
    public const string NoChildId = "no_child_id";
    public const string UnknownChild = "unknown_child";
    public const string BadMorphIndex = "bad_morph_index";
    public const string UnknownSlot = "unknown_slot";
    public const string KindNotAllowed = "kind_not_allowed";

    public static List<Issue> Validate(Recipe recipe, PartRegistry registry)
    {
        var issues = new List<Issue>();

        if (string.IsNullOrEmpty(recipe.SpinePartId))
        {
            issues.Add(new Issue(-1, NoSpine, "Recipe has empty SpinePartId"));
        }
        else if (registry.Get(recipe.SpinePartId) == null)
        {
            issues.Add(new Issue(-1, UnknownSpine,
                $"Spine '{recipe.SpinePartId}' not in registry"));
        }

        var spine = string.IsNullOrEmpty(recipe.SpinePartId)
            ? null
            : registry.Get(recipe.SpinePartId);

        for (int i = 0; i < recipe.Attachments.Count; i++)
        {
            var att = recipe.Attachments[i];

            // ---- parent index ----
            Part? parentPart = null;
            if (att.ParentPartIndex == -1)
            {
                parentPart = spine;
            }
            else if (att.ParentPartIndex < -1 || att.ParentPartIndex >= recipe.Attachments.Count)
            {
                issues.Add(new Issue(i, BadParentIndex,
                    $"Attachment {i}: parent index {att.ParentPartIndex} out of range"));
            }
            else if (att.ParentPartIndex >= i)
            {
                issues.Add(new Issue(i, ForwardRef,
                    $"Attachment {i}: parent {att.ParentPartIndex} >= self (forward reference / cycle)"));
            }
            else
            {
                parentPart = registry.Get(recipe.Attachments[att.ParentPartIndex].ChildPartId);
            }

            // ---- child part ----
            Part? childPart = null;
            if (string.IsNullOrEmpty(att.ChildPartId))
            {
                issues.Add(new Issue(i, NoChildId, $"Attachment {i}: empty ChildPartId"));
            }
            else
            {
                childPart = registry.Get(att.ChildPartId);
                if (childPart == null)
                {
                    issues.Add(new Issue(i, UnknownChild,
                        $"Attachment {i}: unknown ChildPartId '{att.ChildPartId}'"));
                }
            }

            // ---- morph index ----
            if (att.MorphIndex >= recipe.Morphs.Count)
            {
                issues.Add(new Issue(i, BadMorphIndex,
                    $"Attachment {i}: MorphIndex {att.MorphIndex} >= Morphs.Count {recipe.Morphs.Count}"));
            }

            // ---- slot lookup on parent ----
            if (parentPart != null)
            {
                AttachmentPoint? slot = null;
                foreach (var ap in parentPart.AttachmentPoints)
                {
                    if (ap.Name == att.ParentSlotName) { slot = ap; break; }
                }
                if (slot == null)
                {
                    issues.Add(new Issue(i, UnknownSlot,
                        $"Attachment {i}: slot '{att.ParentSlotName}' not on parent Part '{parentPart.Id}'"));
                }
                else if (childPart != null && !slot.AllowedKinds.Allows(childPart.Kind))
                {
                    issues.Add(new Issue(i, KindNotAllowed,
                        $"Attachment {i}: kind {childPart.Kind} not allowed by slot '{att.ParentSlotName}' on '{parentPart.Id}'"));
                }
            }
        }

        return issues;
    }

    public static bool IsValid(Recipe recipe, PartRegistry registry) =>
        Validate(recipe, registry).Count == 0;
}
