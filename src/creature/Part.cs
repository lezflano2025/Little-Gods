using Godot;
using Godot.Collections;

namespace LittleGods.Creature;

/// A Rigblock atom. Has a stable Id, a Kind, named attachment slots, and a
/// 2D footprint used by the M1 blueprint editor (3D mesh comes in M2).
///
/// Even though M1 only uses this for creature parts, the abstraction is
/// shared with tools, vehicles, and buildings later (PRD §6 invariant 2).
[GlobalClass]
public partial class Part : Resource
{
    /// Stable identifier used by Recipes to refer to this Part.
    /// Convention: lowercase snake_case, e.g. "spine_basic", "limb_walker".
    [Export] public string Id { get; set; } = "";

    [Export] public string DisplayName { get; set; } = "";

    [Export] public PartKind Kind { get; set; } = PartKind.Other;

    [Export] public Array<AttachmentPoint> AttachmentPoints { get; set; } = new();

    /// Named regions (e.g. "back", "belly") that paint can be applied to.
    [Export] public string[] PaintRegions { get; set; } = System.Array.Empty<string>();

    /// 2D footprint used by the blueprint editor for hit-testing and rendering.
    /// X = width, Y = height (or for circles, X = diameter).
    [Export] public Vector2 Footprint2D { get; set; } = Vector2.One;

    // --- M2 metaball skin fields (additive; FormatVersion stays 1 per
    // docs/creature-data-model.md). Define the bone this part contributes:
    // length along the part's local +Z axis, with a radius at each end so the
    // metaball field can grow tapered spheres along it. See SkeletonResolver. ---

    /// Bone length along the part's local +Z axis, in world units.
    [Export] public float BoneLength { get; set; } = 1.0f;

    /// Metaball radius at the bone's head (the slot-anchor / base end).
    [Export] public float RadiusStart { get; set; } = 0.5f;

    /// Metaball radius at the bone's tail (the far / tip end).
    [Export] public float RadiusEnd { get; set; } = 0.5f;
}
