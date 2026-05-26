namespace LittleGods.Agent;

/// Well-known blackboard keys, declared once so the parallel task / perception /
/// steering modules share them without colliding or guessing string literals.
/// The comment names the value type each key carries.
public static class Bb
{
    // --- Movement intent (P1 wander sets a target; P2 steering consumes it) ---

    /// Vector3 (world) — where the agent currently wants to be.
    public const string MoveTarget = "move_target";

    /// bool — whether MoveTarget is set and still being pursued.
    public const string HasMoveTarget = "has_move_target";

    /// bool — set by steering once within arrive radius of MoveTarget.
    public const string Arrived = "arrived";

    // --- Steering output (P2 writes; the locomotion bridge at P4 applies) ---

    /// float [0,1] — desired forward throttle this tick.
    public const string MoveThrottle = "move_throttle";

    /// float (rad/s) — desired turn rate this tick (+ = left).
    public const string TurnRate = "turn_rate";

    // --- Perception output (P2 writes a Sighting; P2 defines the Sighting type) ---

    /// Sighting — nearest agent this one preys on.
    public const string NearestPrey = "nearest_prey";

    /// Sighting — nearest agent that preys on this one.
    public const string NearestPredator = "nearest_predator";

    /// Sighting — nearest compatible mate.
    public const string NearestMate = "nearest_mate";
}
