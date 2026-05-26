using System.Collections.Generic;
using Godot;
using LittleGods.Agent;

namespace LittleGods.World;

/// <summary>
/// Concrete implementation of <see cref="IWorldServices"/> for simulation use.
/// Holds the ground sampler, the sim clock, a configurable day length, and the
/// current live-agent set.
/// </summary>
/// <remarks>
/// <para>
/// <b>AgentsNear boundary</b>: distance comparison is inclusive
/// (<c>distance &lt;= radius</c>), so an agent sitting exactly on the radius
/// boundary is returned.
/// </para>
/// <para>
/// <b>Spatial complexity</b>: O(n) linear scan over the registered agent list.
/// P8 will replace this with a spatial partition without changing the contract.
/// </para>
/// <para>
/// <b>Determinism</b>: no wall clock, no System.Random; time advances only via
/// <see cref="Advance"/> or the <see cref="ElapsedSeconds"/> setter.
/// </para>
/// </remarks>
public sealed class WorldServices : IWorldServices
{
    private readonly double _dayLengthSeconds;
    private IReadOnlyList<AgentState> _agents;

    // ──────────────────────────────────────────────────────────────────────────
    // Construction
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Primary constructor — suitable for production wiring and unit tests.
    /// </summary>
    /// <param name="ground">The ground sampler to expose via <see cref="Ground"/>.</param>
    /// <param name="dayLengthSeconds">Length of one in-game day in simulation seconds.</param>
    public WorldServices(IGroundSampler ground, double dayLengthSeconds = 300.0)
    {
        Ground            = ground;
        _dayLengthSeconds = dayLengthSeconds;
        ElapsedSeconds    = 0.0;
        _agents           = System.Array.Empty<AgentState>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IWorldServices
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IGroundSampler Ground { get; }

    /// <inheritdoc/>
    /// <remarks>Settable directly or advanced via <see cref="Advance"/>.</remarks>
    public double ElapsedSeconds { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// frac(ElapsedSeconds / dayLengthSeconds) in [0, 1).
    /// Uses the positive modulo so negative elapsed time (not expected in normal
    /// use) still yields a value in [0, 1).
    /// </remarks>
    public double TimeOfDay
    {
        get
        {
            double frac = ElapsedSeconds % _dayLengthSeconds;
            // Ensure positive: C# % preserves sign of dividend.
            if (frac < 0.0) frac += _dayLengthSeconds;
            return frac / _dayLengthSeconds;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// O(n) distance filter. Boundary is <b>inclusive</b>:
    /// an agent at exactly <c>radius</c> from <c>position</c> is included.
    /// Ordering of results is unspecified (iteration order of the registered list).
    /// </remarks>
    public IReadOnlyList<AgentState> AgentsNear(Vector3 position, float radius)
    {
        var result = new List<AgentState>();
        foreach (AgentState agent in _agents)
        {
            if (position.DistanceTo(agent.Position) <= radius)
                result.Add(agent);
        }
        return result;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Mutation helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Advance the simulation clock by <paramref name="dt"/> seconds.
    /// Equivalent to <c>ElapsedSeconds += dt</c>.
    /// </summary>
    public void Advance(double dt) => ElapsedSeconds += dt;

    /// <summary>
    /// Replace the registered agent list used by <see cref="AgentsNear"/>.
    /// The list is stored by reference — callers must not mutate it after passing.
    /// </summary>
    public void SetAgents(IReadOnlyList<AgentState> agents)
        => _agents = agents;
}
