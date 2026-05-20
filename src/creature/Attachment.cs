using Godot;

namespace LittleGods.Creature;

/// One placed Part instance in a Recipe.
///
/// ParentPartIndex semantics:
///   -1     -> attached to the spine (Recipe.SpinePartId)
///   0..N-1 -> attached to Recipe.Attachments[i]
/// Attachments must reference only attachments that come earlier in the list
/// (no cycles, no forward refs).
[GlobalClass]
public partial class Attachment : Resource
{
    [Export] public int ParentPartIndex { get; set; } = -1;

    /// Name of the AttachmentPoint on the parent Part this attaches to.
    [Export] public string ParentSlotName { get; set; } = "";

    /// Id of the Part being attached. Resolved against PartRegistry at runtime.
    [Export] public string ChildPartId { get; set; } = "";

    [Export] public Transform3D LocalTransform { get; set; } = Transform3D.Identity;

    /// Index into Recipe.Morphs. -1 = use identity morph.
    [Export] public int MorphIndex { get; set; } = -1;

    /// Empty string = not mirrored. Non-empty = symmetry-paired with the
    /// other Attachment(s) sharing this MirrorGroupId.
    [Export] public string MirrorGroupId { get; set; } = "";
}
