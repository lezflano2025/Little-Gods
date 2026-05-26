using System;

namespace LittleGods.Agent;

/// <summary>
/// Factory class for assembling behaviour trees ergonomically.
/// All methods return IBtTask except Tree() which returns BehaviorTree.
/// </summary>
public static class Bt
{
    /// <summary>Creates a Sequence node: succeeds when all children succeed in order.</summary>
    public static IBtTask Sequence(params IBtTask[] children)
        => new LittleGods.Agent.Sequence(children);

    /// <summary>Creates a Selector node: succeeds when any child succeeds in order.</summary>
    public static IBtTask Selector(params IBtTask[] children)
        => new LittleGods.Agent.Selector(children);

    /// <summary>Creates a Parallel node with the given policy.</summary>
    public static IBtTask Parallel(ParallelPolicy policy, params IBtTask[] children)
        => new LittleGods.Agent.Parallel(policy, children);

    /// <summary>Creates an Inverter decorator: flips Success/Failure; Running passes through.</summary>
    public static IBtTask Invert(IBtTask child)
        => new Inverter(child);

    /// <summary>Creates a Succeeder decorator: converts Failure to Success; Running passes through.</summary>
    public static IBtTask Succeed(IBtTask child)
        => new Succeeder(child);

    /// <summary>Creates a Failer decorator: converts Success to Failure; Running passes through.</summary>
    public static IBtTask Fail(IBtTask child)
        => new Failer(child);

    /// <summary>Creates an ActionTask leaf backed by a function returning BtStatus.</summary>
    public static IBtTask Action(Func<BtContext, BtStatus> fn)
        => new ActionTask(fn);

    /// <summary>
    /// Creates a DoTask leaf: runs a side-effecting action, always returns Success.
    /// </summary>
    public static IBtTask Do(Action<BtContext> effect)
        => new DoTask(effect);

    /// <summary>Creates a ConditionTask leaf: returns Success if predicate is true, else Failure.</summary>
    public static IBtTask Condition(Func<BtContext, bool> predicate)
        => new ConditionTask(predicate);

    /// <summary>Wraps a root IBtTask in a BehaviorTree that tracks LastStatus.</summary>
    public static BehaviorTree Tree(IBtTask root)
        => new BehaviorTree(root);
}
