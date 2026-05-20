using Godot;

namespace LittleGods.Creature;

/// A named socket on a Part where child parts can attach.
/// Local-space position + outward normal. AllowedKinds defaults to All.
[GlobalClass]
public partial class AttachmentPoint : Resource
{
    [Export] public string Name { get; set; } = "";
    [Export] public Vector3 LocalPosition { get; set; } = Vector3.Zero;
    [Export] public Vector3 LocalNormal { get; set; } = Vector3.Up;
    [Export] public PartKindMask AllowedKinds { get; set; } = PartKindMask.All;
}
