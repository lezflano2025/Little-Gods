namespace LittleGods.Creature;

/// What category of part this is. Drives which Slots can connect to which.
/// A Part has exactly one Kind; a Slot has a PartKindMask of allowed Kinds.
public enum PartKind
{
    Spine,
    Limb,
    Head,
    Mouth,
    Other,
}

/// Bitmask over PartKind for "what Kinds may attach here".
[System.Flags]
public enum PartKindMask
{
    None = 0,
    Spine = 1,
    Limb = 2,
    Head = 4,
    Mouth = 8,
    Other = 16,
    All = Spine | Limb | Head | Mouth | Other,
}

public static class PartKindExt
{
    public static PartKindMask AsMask(this PartKind kind) =>
        kind switch
        {
            PartKind.Spine => PartKindMask.Spine,
            PartKind.Limb => PartKindMask.Limb,
            PartKind.Head => PartKindMask.Head,
            PartKind.Mouth => PartKindMask.Mouth,
            PartKind.Other => PartKindMask.Other,
            _ => PartKindMask.None,
        };

    public static bool Allows(this PartKindMask mask, PartKind kind) =>
        (mask & kind.AsMask()) != 0;
}
