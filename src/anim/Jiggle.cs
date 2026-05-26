using Godot;
using static Godot.Mathf;

namespace LittleGods.Anim;

/// Tunable spring-follower parameters for secondary motion (M3 P5).
/// A higher Stiffness snaps back faster; lower Damping overshoots more (livelier
/// jiggle). Defaults are underdamped-but-stable for a ~1/60 s tick.
public readonly struct JiggleParams
{
    /// Spring constant pulling the follower toward its anchor (1/s^2 scale).
    public readonly float Stiffness;

    /// Velocity damping (1/s scale). Larger ⇒ less overshoot, quicker settle.
    public readonly float Damping;

    public JiggleParams(float stiffness, float damping)
    {
        Stiffness = stiffness;
        Damping = damping;
    }

    public static JiggleParams Default { get; } = new(stiffness: 120f, damping: 14f);
}

/// Spring-damped secondary motion (M3 P5): a follower point chases a moving
/// anchor through a damped spring, so when the anchor jerks (the body
/// accelerates) the follower lags then springs back — the jiggle of a tail,
/// jowl, or belly. The lag itself is the secondary motion; no explicit
/// acceleration input is needed.
///
/// Pure and deterministic: semi-implicit (symplectic) Euler with an EXPLICIT
/// dt, so a fixed-timestep run is byte-reproducible (PRD invariant 4). The
/// caller owns the (value, velocity) state and feeds it back each tick.
public static class Jiggle
{
    /// One spring step. Pulls <paramref name="value"/> toward
    /// <paramref name="anchor"/>; returns the next (value, velocity).
    ///
    ///   accel    = Stiffness * (anchor - value) - Damping * velocity
    ///   velocity = velocity + accel * dt        (updated first — symplectic)
    ///   value    = value + velocity * dt
    ///
    /// dt is clamped to a small positive value to stay stable if a caller
    /// passes a huge or non-positive step.
    public static (Vector3 value, Vector3 velocity) Step(
        Vector3 value,
        Vector3 velocity,
        Vector3 anchor,
        JiggleParams p,
        float dt)
    {
        float h = Clamp(dt, 0f, 0.05f);
        Vector3 accel = p.Stiffness * (anchor - value) - p.Damping * velocity;
        Vector3 nextVel = velocity + accel * h;
        Vector3 nextVal = value + nextVel * h;
        return (nextVal, nextVel);
    }
}
