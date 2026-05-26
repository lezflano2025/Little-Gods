using Godot;

namespace LittleGods.Anim;

/// Output of the two-bone analytic IK solver (M3 P1): the resolved world-space
/// knee and foot positions for one limb, plus whether the target was reachable.
///
/// When the target is out of reach the solver clamps to full extension toward
/// it and sets <see cref="Reachable"/> = false — it never returns NaN/Inf
/// (PRD M3 acceptance: "no joint exceeds its reach (no popping to full-stretch)"
/// is handled by the caller; the solver guarantees finite output).
///
/// Immutable value type.
public readonly struct IkResult
{
    /// World-space knee (mid joint) position.
    public readonly Vector3 Knee;

    /// World-space foot (end effector) position.
    public readonly Vector3 End;

    /// True when the target was within [|upper - lower|, upper + lower] and the
    /// solved foot reaches it; false when clamped to full extension.
    public readonly bool Reachable;

    public IkResult(Vector3 knee, Vector3 end, bool reachable)
    {
        Knee = knee;
        End = end;
        Reachable = reachable;
    }
}
