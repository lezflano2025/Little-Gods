using System;

namespace LittleGods.Agent;

/// <summary>
/// Leaf node that delegates its result to an arbitrary function.
/// The function receives the current BtContext and returns BtStatus directly.
/// </summary>
internal sealed class ActionTask : IBtTask
{
    private readonly Func<BtContext, BtStatus> _fn;

    internal ActionTask(Func<BtContext, BtStatus> fn) => _fn = fn;

    public BtStatus Tick(in BtContext ctx) => _fn(ctx);
}

/// <summary>
/// Leaf node backed by a boolean predicate.
/// Returns Success when the predicate is true, Failure when false. Never returns Running.
/// </summary>
internal sealed class ConditionTask : IBtTask
{
    private readonly Func<BtContext, bool> _predicate;

    internal ConditionTask(Func<BtContext, bool> predicate) => _predicate = predicate;

    public BtStatus Tick(in BtContext ctx)
        => _predicate(ctx) ? BtStatus.Success : BtStatus.Failure;
}

/// <summary>
/// Leaf node that runs a side-effecting action and always returns Success.
/// </summary>
internal sealed class DoTask : IBtTask
{
    private readonly Action<BtContext> _effect;

    internal DoTask(Action<BtContext> effect) => _effect = effect;

    public BtStatus Tick(in BtContext ctx)
    {
        _effect(ctx);
        return BtStatus.Success;
    }
}
