using Godot;
using LittleGods.World;

namespace LittleGods.Agent;

/// One live agent's mutable simulation state — the entity the behaviour tree
/// reads and the sim tick writes. Deliberately mutable: a world holds many of
/// these and updates them in place every tick (allocating a fresh value per
/// agent per frame would not scale, and is the wrong model for a moving body).
///
/// Determinism — not immutability — is the invariant here (PRD 4): the only
/// randomness an agent uses is `Rng`, seeded per agent and threaded through its
/// whole life, and the only time source is the explicit `dt` passed to Tick.
/// Pure helpers (needs integration, steering) COMPUTE the next values; the tick
/// writes them back, so the logic stays functional even though the storage is
/// updated in place.
///
/// Agent-type-blind: there is no "creature" here, only a transform, needs, a
/// species id, and an RNG. Tribes / civs reuse this same shape later.
public sealed class AgentState
{
    /// Stable per-world identity.
    public int Id { get; }

    /// Which generated species this agent is an instance of (P5). The runtime
    /// never switches on it; perception/relations resolve behaviour from it.
    public int SpeciesId { get; }

    /// World transform (position + orientation). Mutated as the agent moves.
    public Transform3D Transform;

    /// World-space velocity (units / second). Mutated by steering.
    public Vector3 Velocity;

    /// Homeostatic state (P3 integrates it).
    public Needs Needs;

    /// Alive / dead (P3 advances it).
    public LifecycleState Lifecycle;

    /// Per-agent deterministic RNG, threaded through the agent's whole life.
    public DeterministicRng Rng { get; }

    public AgentState(int id, int speciesId, Transform3D transform, DeterministicRng rng)
    {
        Id = id;
        SpeciesId = speciesId;
        Transform = transform;
        Rng = rng;
        Velocity = Vector3.Zero;
        Needs = Needs.Newborn();
        Lifecycle = LifecycleState.Alive;
    }

    public Vector3 Position => Transform.Origin;

    /// Facing direction. Godot convention: a node looks down its local -Z.
    public Vector3 Forward => -Transform.Basis.Z;

    public bool IsAlive => Lifecycle == LifecycleState.Alive;
}
