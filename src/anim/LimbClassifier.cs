using LittleGods.Creature;
using LittleGods.Mesh;

namespace LittleGods.Anim;

/// M3 P2 — see docs/m3-contract.md §"Agent B — Limb-type classifier".
///
/// Assigns a <see cref="LimbType"/> to every <see cref="LimbChain"/> in a
/// resolved <see cref="CreatureSkeleton"/> by inspecting the chain's slot name
/// and, where available, the child part id from the originating
/// <see cref="Recipe"/> attachment.
///
/// Pure / deterministic / order-stable: the result array is parallel to
/// <c>skeleton.LimbChains</c> and never null.
public static class LimbClassifier
{
    /// <summary>
    /// Classify each limb chain in <paramref name="skeleton"/>.
    /// </summary>
    /// <param name="recipe">The recipe that produced the skeleton.</param>
    /// <param name="registry">
    /// Part registry used to resolve part ids (may be null; classification
    /// degrades gracefully).
    /// </param>
    /// <param name="skeleton">The resolved creature skeleton.</param>
    /// <returns>
    /// Array of length <c>skeleton.LimbChains.Length</c>, element <c>i</c>
    /// corresponding to <c>skeleton.LimbChains[i]</c>. Empty when
    /// <c>LimbChains</c> is empty.
    /// </returns>
    public static LimbType[] Classify(
        Recipe recipe,
        PartRegistry? registry,
        CreatureSkeleton skeleton)
    {
        if (skeleton is null)
        {
            return System.Array.Empty<LimbType>();
        }

        var chains = skeleton.LimbChains;
        if (chains.Length == 0)
        {
            return System.Array.Empty<LimbType>();
        }

        var result = new LimbType[chains.Length];
        for (int i = 0; i < chains.Length; i++)
        {
            result[i] = ClassifyChain(chains[i], recipe, registry);
        }
        return result;
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private static LimbType ClassifyChain(
        LimbChain chain,
        Recipe? recipe,
        PartRegistry? registry)
    {
        var slot = chain.SlotName ?? "";

        // Rule 1: slot contains "tail" -> Tail.
        if (slot.Contains("tail", System.StringComparison.OrdinalIgnoreCase))
        {
            return LimbType.Tail;
        }

        // Rule 2: part id contains "wing" -> Wing.
        if (PartIdContains(chain, recipe, registry, "wing"))
        {
            return LimbType.Wing;
        }

        // Rule 3: slot is load-bearing (hip / shoulder / leg) -> Leg.
        if (slot.Contains("hip",      System.StringComparison.OrdinalIgnoreCase) ||
            slot.Contains("shoulder", System.StringComparison.OrdinalIgnoreCase) ||
            slot.Contains("leg",      System.StringComparison.OrdinalIgnoreCase))
        {
            return LimbType.Leg;
        }

        // Rule 4: default -> Arm.
        return LimbType.Arm;
    }

    /// <summary>
    /// Returns true when the part id associated with <paramref name="chain"/>
    /// contains <paramref name="substring"/> (case-insensitive).  Guards all
    /// null / out-of-range conditions so the classifier never throws.
    /// </summary>
    private static bool PartIdContains(
        LimbChain chain,
        Recipe? recipe,
        PartRegistry? registry,
        string substring)
    {
        int idx = chain.AttachmentIndex;
        if (recipe is null || idx < 0 || idx >= recipe.Attachments.Count)
        {
            return false;
        }

        var attachment = recipe.Attachments[idx];
        if (attachment is null)
        {
            return false;
        }

        var childId = attachment.ChildPartId ?? "";

        // Prefer the canonical id from the registry when available.
        if (registry is not null)
        {
            var part = registry.Get(childId);
            if (part is not null)
            {
                childId = part.Id ?? childId;
            }
        }

        return childId.Contains(substring, System.StringComparison.OrdinalIgnoreCase);
    }
}
