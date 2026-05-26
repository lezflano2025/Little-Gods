namespace LittleGods.Anim;

/// One resolved two-bone limb (upper + lower joined at a knee), recorded by
/// SkeletonResolver per ADR-0003. Pure topology: indices into
/// CreatureSkeleton.Bones plus the rest segment lengths and the parent slot it
/// grew from. Consumed by the IK solver (M3 P1), limb classifier (P2), gait
/// controller (P3) and foot planner (P4).
///
/// Classification into a <see cref="LimbType"/> is deliberately NOT stored here
/// — it is derived later by LimbClassifier so resolution stays pure topology.
///
/// Immutable value type (PRD coding-style: no mutation).
public readonly struct LimbChain
{
    /// Recipe.Attachments index that produced this chain, for symmetry / side
    /// pairing and cross-referencing the recipe. -1 when not from an attachment.
    public readonly int AttachmentIndex;

    /// Upper bone index (hip → knee). Its Head is the hip / root joint.
    public readonly int RootBone;

    /// Lower bone index (knee → foot). Its Head is the knee joint.
    public readonly int KneeBone;

    /// Bone whose Tail is the foot (the chain tip). Equals <see cref="KneeBone"/>
    /// for a two-bone chain; named distinctly so a future 3-bone limb can differ.
    public readonly int FootBone;

    /// Rest length hip → knee (already morph-scaled).
    public readonly float UpperLength;

    /// Rest length knee → foot (already morph-scaled).
    public readonly float LowerLength;

    /// Parent slot this limb attached at, e.g. "left_hip" / "right_shoulder" /
    /// "tail". The classifier reads it to infer side and role.
    public readonly string SlotName;

    public LimbChain(
        int attachmentIndex,
        int rootBone,
        int kneeBone,
        int footBone,
        float upperLength,
        float lowerLength,
        string slotName)
    {
        AttachmentIndex = attachmentIndex;
        RootBone = rootBone;
        KneeBone = kneeBone;
        FootBone = footBone;
        UpperLength = upperLength;
        LowerLength = lowerLength;
        SlotName = slotName ?? "";
    }

    /// Total rest length hip → foot.
    public float TotalLength => UpperLength + LowerLength;
}
