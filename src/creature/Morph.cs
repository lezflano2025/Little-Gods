using Godot;

namespace LittleGods.Creature;

/// Per-attachment shape modifier. Identity defaults: no stretch, no twist,
/// white tint. Stored in Recipe.Morphs; Attachments reference by index.
[GlobalClass]
public partial class Morph : Resource
{
    [Export] public Vector3 Stretch { get; set; } = Vector3.One;
    [Export] public float Twist { get; set; } = 0f;
    [Export] public Color PaintTint { get; set; } = Colors.White;
}
