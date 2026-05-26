using LittleGods.World;

namespace LittleGods.Agent;

/// Everything a behaviour-tree task may touch for one tick, gathered into one
/// readonly value (ADR-0005). It is generic — an agent, its blackboard, the
/// world services, and the frame dt — so tasks never reach for anything
/// type-specific and the same tree drives any agent.
public readonly struct BtContext
{
    /// The agent being ticked (its transform/needs/rng are read and written).
    public AgentState Agent { get; }

    /// The agent's working memory (move target, sightings, timers).
    public Blackboard Blackboard { get; }

    /// The generic world view (ground, clock, nearby agents).
    public IWorldServices World { get; }

    /// Elapsed simulation time for this tick, in seconds (the explicit sim
    /// clock — never a wall clock; PRD invariant 4).
    public double Dt { get; }

    public BtContext(AgentState agent, Blackboard blackboard, IWorldServices world, double dt)
    {
        Agent = agent;
        Blackboard = blackboard;
        World = world;
        Dt = dt;
    }
}

/// A node in a behaviour tree. ONE method, agent-type-blind. Implementations are
/// stateless and shared across every agent of a species; all per-agent state
/// lives in ctx.Agent / ctx.Blackboard, so the same task instance ticks many
/// agents safely (and the tree is built once per species, not per agent).
public interface IBtTask
{
    BtStatus Tick(in BtContext ctx);
}
