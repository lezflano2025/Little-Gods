using System.Collections.Generic;
using Godot;
using LittleGods.Creature;

namespace LittleGods.Mesh;

/// Resolves a Recipe (+ PartRegistry) into a world-space CreatureSkeleton.
///
/// The bone model is the one locked in docs/m2-plan.md:
///   - One bone per placed part.
///   - The ROOT (spine, placed at origin) is centred on its local +Z axis:
///     Head = -Z * L/2, Tail = +Z * L/2.
///   - Every ATTACHMENT grows from its slot anchor: Head = the anchor point,
///     Tail = Head + (anchor's world +Z) * BoneLength.
///   - Morph.Stretch.Z scales bone length; Stretch.X/Y scale the radii;
///     Twist rotates the part frame about +Z (a no-op on round metaball skin,
///     but composed in so a child part's slots rotate with it).
///
/// Bone 0 is always the root. Recipe.Attachments[i] maps to bone i + 1, so a
/// child's parent bone index is ParentPartIndex + 1 (or 0 when -1 = spine).
///
/// Pure and deterministic: no RNG, no clock (PRD invariant 4). Reads only
/// generic Rigblock data, so the same path works for non-creature recipes
/// (PRD invariant 2).
public static class SkeletonResolver
{
    private const float DefaultBoneLength = 1.0f;
    private const float DefaultRadius = 0.5f;

    public static CreatureSkeleton Resolve(Recipe recipe, PartRegistry registry)
    {
        if (recipe == null || registry == null)
        {
            return new CreatureSkeleton(System.Array.Empty<Bone>());
        }

        var spinePart = registry.Get(recipe.SpinePartId);
        if (spinePart == null)
        {
            return new CreatureSkeleton(System.Array.Empty<Bone>());
        }

        var bones = new List<Bone>(recipe.Attachments.Count + 1);

        // Bone 0: the root spine, centred on its +Z axis at the world origin.
        float spineLen = BoneLengthOf(spinePart);
        float spineRadius = RadiusStartOf(spinePart);
        var spineAxis = Vector3.Back; // +Z, the body axis convention
        bones.Add(new Bone(
            head: -spineAxis * (spineLen * 0.5f),
            tail: spineAxis * (spineLen * 0.5f),
            radiusHead: spineRadius,
            radiusTail: RadiusEndOf(spinePart),
            parentIndex: -1));

        // The frame used to locate the spine's direct children (unmorphed).
        var spineChildFrame = Transform3D.Identity;

        // Per-attachment frame used to locate that part's own children.
        var childFrames = new Transform3D[recipe.Attachments.Count];

        for (int i = 0; i < recipe.Attachments.Count; i++)
        {
            var att = recipe.Attachments[i];

            Transform3D parentFrame;
            Part? parentPart;
            if (att.ParentPartIndex < 0)
            {
                parentFrame = spineChildFrame;
                parentPart = spinePart;
            }
            else
            {
                parentFrame = childFrames[att.ParentPartIndex];
                parentPart = registry.Get(recipe.Attachments[att.ParentPartIndex].ChildPartId);
            }

            Vector3 slotPos = SlotPosition(parentPart, att.ParentSlotName);
            var slotXform = new Transform3D(Basis.Identity, slotPos);

            // Anchor frame: where this part sits, before its own morph.
            Transform3D anchorFrame = parentFrame * slotXform * att.LocalTransform;

            var morph = MorphFor(recipe, att.MorphIndex);
            var childPart = registry.Get(att.ChildPartId);

            float length = BoneLengthOf(childPart) * morph.Stretch.Z;
            float radiusScale = (morph.Stretch.X + morph.Stretch.Y) * 0.5f;

            Vector3 axis = anchorFrame.Basis.Z;
            axis = axis.LengthSquared() > 1e-12f ? axis.Normalized() : spineAxis;

            Vector3 head = anchorFrame.Origin;
            Vector3 tail = head + axis * length;

            int parentBone = att.ParentPartIndex < 0 ? 0 : att.ParentPartIndex + 1;
            bones.Add(new Bone(
                head: head,
                tail: tail,
                radiusHead: RadiusStartOf(childPart) * radiusScale,
                radiusTail: RadiusEndOf(childPart) * radiusScale,
                parentIndex: parentBone));

            // This part's children attach in its morphed/twisted frame.
            var twist = new Transform3D(new Basis(Vector3.Back, morph.Twist), Vector3.Zero);
            var scale = new Transform3D(Basis.Identity.Scaled(morph.Stretch), Vector3.Zero);
            childFrames[i] = anchorFrame * twist * scale;
        }

        return new CreatureSkeleton(bones.ToArray());
    }

    private static Vector3 SlotPosition(Part? part, string slotName)
    {
        if (part == null)
        {
            return Vector3.Zero;
        }
        foreach (var ap in part.AttachmentPoints)
        {
            if (ap != null && ap.Name == slotName)
            {
                return ap.LocalPosition;
            }
        }
        return Vector3.Zero;
    }

    private static Morph MorphFor(Recipe recipe, int morphIndex)
    {
        if (morphIndex >= 0 && morphIndex < recipe.Morphs.Count)
        {
            return recipe.Morphs[morphIndex] ?? new Morph();
        }
        return new Morph();
    }

    private static float BoneLengthOf(Part? p) => p != null ? p.BoneLength : DefaultBoneLength;
    private static float RadiusStartOf(Part? p) => p != null ? p.RadiusStart : DefaultRadius;
    private static float RadiusEndOf(Part? p) => p != null ? p.RadiusEnd : DefaultRadius;
}
