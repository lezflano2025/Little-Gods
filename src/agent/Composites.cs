namespace LittleGods.Agent;

/// <summary>Policy for how a Parallel node determines its own status.</summary>
public enum ParallelPolicy
{
    /// <summary>All children must succeed; any failure causes failure.</summary>
    RequireAll,

    /// <summary>Any child succeeding causes success; all must fail to fail.</summary>
    RequireOne,
}

/// <summary>
/// Ticks children left to right, stopping at the first Failure or Running.
/// Returns Success only when all children succeed.
/// Memoryless: re-evaluates from the first child each Tick.
/// 0 children => Success.
/// </summary>
internal sealed class Sequence : IBtTask
{
    private readonly IBtTask[] _children;

    internal Sequence(IBtTask[] children) => _children = children;

    public BtStatus Tick(in BtContext ctx)
    {
        foreach (IBtTask child in _children)
        {
            BtStatus s = child.Tick(ctx);
            if (s != BtStatus.Success)
                return s; // Failure or Running — stop immediately
        }
        return BtStatus.Success;
    }
}

/// <summary>
/// Ticks children left to right, stopping at the first Success or Running.
/// Returns Failure only when all children fail.
/// Memoryless: re-evaluates from the first child each Tick.
/// 0 children => Failure.
/// </summary>
internal sealed class Selector : IBtTask
{
    private readonly IBtTask[] _children;

    internal Selector(IBtTask[] children) => _children = children;

    public BtStatus Tick(in BtContext ctx)
    {
        foreach (IBtTask child in _children)
        {
            BtStatus s = child.Tick(ctx);
            if (s != BtStatus.Failure)
                return s; // Success or Running — stop immediately
        }
        return BtStatus.Failure;
    }
}

/// <summary>
/// Ticks ALL children every Tick regardless of individual results.
/// RequireAll: any Failure => Failure; else all Success => Success; else Running.
/// RequireOne: any Success => Success; else all Failure => Failure; else Running.
/// 0 children: RequireAll => Success, RequireOne => Failure.
/// </summary>
internal sealed class Parallel : IBtTask
{
    private readonly ParallelPolicy _policy;
    private readonly IBtTask[] _children;

    internal Parallel(ParallelPolicy policy, IBtTask[] children)
    {
        _policy = policy;
        _children = children;
    }

    public BtStatus Tick(in BtContext ctx)
    {
        if (_children.Length == 0)
            return _policy == ParallelPolicy.RequireAll ? BtStatus.Success : BtStatus.Failure;

        bool anyFailure = false;
        bool anySuccess = false;
        bool anyRunning = false;

        foreach (IBtTask child in _children)
        {
            switch (child.Tick(ctx))
            {
                case BtStatus.Failure: anyFailure = true; break;
                case BtStatus.Success: anySuccess = true; break;
                case BtStatus.Running: anyRunning = true; break;
            }
        }

        if (_policy == ParallelPolicy.RequireAll)
        {
            if (anyFailure)  return BtStatus.Failure;
            if (anyRunning)  return BtStatus.Running;
            return BtStatus.Success; // all succeeded
        }
        else // RequireOne
        {
            if (anySuccess)  return BtStatus.Success;
            if (anyRunning)  return BtStatus.Running;
            return BtStatus.Failure; // all failed
        }
    }
}
