using System.Collections.Generic;
using Godot;
using LittleGods.Agent;

namespace LittleGods.World;

/// The generic, agent-type-blind view of the world that behaviour-tree tasks
/// query. The runtime never branches on "is this a creature"; tasks read ground
/// height, the sim clock / time-of-day, and nearby agents through this seam.
///
/// Perception (P2) and steering build ON TOP of these primitives; this
/// interface stays minimal so it is trivial to fake in unit tests.
public interface IWorldServices
{
    /// Terrain under the world (foot planting, spawning, the body ride height).
    IGroundSampler Ground { get; }

    /// Accumulated simulation time in seconds (the explicit double sim clock,
    /// PRD invariant 4 — never a wall clock).
    double ElapsedSeconds { get; }

    /// Fraction of the current day in [0, 1): 0 = midnight, 0.5 = noon.
    double TimeOfDay { get; }

    /// Live agents whose position is within `radius` of `position`. The raw
    /// spatial primitive Perception consumes; ordering is unspecified.
    IReadOnlyList<AgentState> AgentsNear(Vector3 position, float radius);
}
