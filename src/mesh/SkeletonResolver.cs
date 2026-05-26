using System.Collections.Generic;
using Godot;
using LittleGods.Anim;
using LittleGods.Creature;

namespace LittleGods.Mesh;

/// Resolves a Recipe (+ PartRegistry) into a world-space CreatureSkeleton.
///
/// Bone model (ADR-0002 for the base, ADR-0003 for limbs):
///   - Bone 0 is the ROOT spine, placed at the origin and centred on its local
///     +Z axis: Head = -Z * L/2, Tail = +Z * L/2.
///   - Every ATTACHMENT grows from its slot anchor along the slot's outward
///     LocalNormal (so shoulders splay, the tail extends back, etc.).
///   - A NON-LIMB part contributes ONE bone (Head = anchor, Tail = anchor +
///     axis * BoneLength).
///   - A PartKind.Limb part contributes a TWO-BONE CHAIN: an upper bone from
///     the anchor to a mid knee, and a lower bone from the knee to the foot
///     (the tip). The two sub-bones are colinear at rest; IK bends the knee at
///     runtime. Radii taper continuously through the knee. A LimbChain record
///     is emitted per limb for the animation layer.
///   - Morph.Stretch.Z scales bone/chain length; Stretch.X/Y scale radii;
///     Twist rotates the part frame about +Z (a no-op on round metaball skin,
///     but composed so a child part's slots rotate with it).
///
/// Because bones are no longer 1:1 with attachments, the resolver maintains an
/// attachment -> tip-bone map: a child's parent bone is its parent attachment's
/// LAST emitted bone (the foot for a limb, the single bone otherwise), or bone
/// 0 (the spine) when ParentPartIndex < 0. Never assume "bone = attachment + 1".
///
/// Pure and deterministic: no RNG, no clock (PRD invariant 4). Reads only
/// generic Rigblock data, so the same path works for non-creature recipes
/// (PRD invariant 2).
public static class SkeletonResolver
{
    private const float DefaultBoneLength = 1.0f;
    private const float DefaultRadius = 0.5f;

    /// Fraction of a limb's length assigned to the upper (hip→knee) bone.
    /// 0.5 places the knee at the midpoint (ADR-0003).
    private const float LimbKneeSplit = 0.5f;

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
        var limbChains = new List<LimbChain>();

        // The bone a child of attachment i should parent to (its tip bone).
        var attachmentTipBone = new int[recipe.Attachments.Count];

        // Bone 0: the root spine, centred on its +Z axis at the world origin.
        float spineLen = BoneLengthOf(spinePart);
        var spineAxis = Vector3.Back; // +Z, the body axis convention
        bones.Add(new Bone(
            head: -spineAxis * (spineLen * 0.5f),
            tail: spineAxis * (spineLen * 0.5f),
            radiusHead: RadiusStartOf(spinePart),
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

            AttachmentPoint? slot = FindSlot(parentPart, att.ParentSlotName);
            Vector3 slotPos = slot?.LocalPosition ?? Vector3.Zero;
            Vector3 slotNormal = slot?.LocalNormal ?? Vector3.Back;
            // Orient the slot frame so +Z faces the slot's outward normal, so a
            // part grows along that normal (e.g. shoulders splay sideways, the
            // tail extends back) rather than all parts collapsing onto world +Z.
            var slotXform = new Transform3D(BasisLookingAlong(slotNormal), slotPos);

            // Anchor frame: where this part sits, before its own morph.
            Transform3D anchorFrame = parentFrame * slotXform * att.LocalTransform;

            var morph = MorphFor(recipe, att.MorphIndex);
            var childPart = registry.Get(att.ChildPartId);

            float length = BoneLengthOf(childPart) * morph.Stretch.Z;
            float radiusScale = (morph.Stretch.X + morph.Stretch.Y) * 0.5f;

            Vector3 axis = anchorFrame.Basis.Z;
            axis = axis.LengthSquared() > 1e-12f ? axis.Normalized() : spineAxis;

            Vector3 head = anchorFrame.Origin;
            float rStart = RadiusStartOf(childPart) * radiusScale;
            float rEnd = RadiusEndOf(childPart) * radiusScale;

            int parentBone = att.ParentPartIndex < 0 ? 0 : attachmentTipBone[att.ParentPartIndex];

            if (childPart != null && childPart.Kind == PartKind.Limb)
            {
                // Two-bone chain: upper (hip→knee) + lower (knee→foot).
                float upperLen = length * LimbKneeSplit;
                float lowerLen = length - upperLen;
                Vector3 knee = head + axis * upperLen;
                Vector3 foot = head + axis * length;
                float rKnee = Mathf.Lerp(rStart, rEnd, LimbKneeSplit);

                int upperIdx = bones.Count;
                bones.Add(new Bone(head, knee, rStart, rKnee, parentBone));
                int lowerIdx = bones.Count;
                bones.Add(new Bone(knee, foot, rKnee, rEnd, upperIdx));

                limbChains.Add(new LimbChain(
                    attachmentIndex: i,
                    rootBone: upperIdx,
                    kneeBone: lowerIdx,
                    footBone: lowerIdx,
                    upperLength: upperLen,
                    lowerLength: lowerLen,
                    slotName: att.ParentSlotName));

                attachmentTipBone[i] = lowerIdx;
            }
            else
            {
                Vector3 tail = head + axis * length;
                int idx = bones.Count;
                bones.Add(new Bone(head, tail, rStart, rEnd, parentBone));
                attachmentTipBone[i] = idx;
            }

            // This part's children attach in its morphed/twisted frame.
            var twist = new Transform3D(new Basis(Vector3.Back, morph.Twist), Vector3.Zero);
            var scale = new Transform3D(Basis.Identity.Scaled(morph.Stretch), Vector3.Zero);
            childFrames[i] = anchorFrame * twist * scale;
        }

        return new CreatureSkeleton(bones.ToArray(), limbChains.ToArray());
    }

    private static AttachmentPoint? FindSlot(Part? part, string slotName)
    {
        if (part == null)
        {
            return null;
        }
        foreach (var ap in part.AttachmentPoints)
        {
            if (ap != null && ap.Name == slotName)
            {
                return ap;
            }
        }
        return null;
    }

    /// Orthonormal basis whose +Z column points along n (the slot normal).
    /// Picks a stable reference up-vector to avoid degeneracy when n is
    /// near-vertical. Used to orient a part's bone along its slot normal.
    private static Basis BasisLookingAlong(Vector3 n)
    {
        n = n.LengthSquared() > 1e-12f ? n.Normalized() : Vector3.Back;
        Vector3 up = Mathf.Abs(n.Dot(Vector3.Up)) < 0.99f ? Vector3.Up : Vector3.Right;
        Vector3 x = up.Cross(n).Normalized();
        Vector3 y = n.Cross(x).Normalized();
        return new Basis(x, y, n);
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
