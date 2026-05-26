namespace LittleGods.Agent;

/// <summary>
/// Root wrapper for a behaviour tree. Holds the root IBtTask and exposes
/// a Tick method that delegates to it. Caches the last tick result in LastStatus.
///
/// BehaviorTree itself is NOT stateless — it owns one per-tree LastStatus so
/// the caller can inspect what happened after each tick. The tree tasks underneath
/// it ARE stateless (shared across agents). Only one BehaviorTree is held per
/// agent (or per-species tree invocation), not shared.
/// </summary>
public sealed class BehaviorTree
{
    private readonly IBtTask _root;

    /// <summary>The result of the most recent call to Tick. Running until the first tick.</summary>
    public BtStatus LastStatus { get; private set; } = BtStatus.Running;

    /// <param name="root">The root node of the behaviour tree.</param>
    public BehaviorTree(IBtTask root) => _root = root;

    /// <summary>
    /// Advance the tree by one tick. Delegates to the root task and caches the result.
    /// </summary>
    public BtStatus Tick(in BtContext ctx)
    {
        LastStatus = _root.Tick(ctx);
        return LastStatus;
    }
}
