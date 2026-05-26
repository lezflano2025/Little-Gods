namespace LittleGods.Agent;

/// <summary>
/// Inverts Success to Failure and Failure to Success. Running passes through unchanged.
/// </summary>
internal sealed class Inverter : IBtTask
{
    private readonly IBtTask _child;

    internal Inverter(IBtTask child) => _child = child;

    public BtStatus Tick(in BtContext ctx)
    {
        BtStatus s = _child.Tick(ctx);
        return s switch
        {
            BtStatus.Success => BtStatus.Failure,
            BtStatus.Failure => BtStatus.Success,
            _                => BtStatus.Running,
        };
    }
}

/// <summary>
/// Always returns Success for Success or Failure; Running passes through unchanged.
/// </summary>
internal sealed class Succeeder : IBtTask
{
    private readonly IBtTask _child;

    internal Succeeder(IBtTask child) => _child = child;

    public BtStatus Tick(in BtContext ctx)
    {
        BtStatus s = _child.Tick(ctx);
        return s == BtStatus.Running ? BtStatus.Running : BtStatus.Success;
    }
}

/// <summary>
/// Always returns Failure for Success or Failure; Running passes through unchanged.
/// </summary>
internal sealed class Failer : IBtTask
{
    private readonly IBtTask _child;

    internal Failer(IBtTask child) => _child = child;

    public BtStatus Tick(in BtContext ctx)
    {
        BtStatus s = _child.Tick(ctx);
        return s == BtStatus.Running ? BtStatus.Running : BtStatus.Failure;
    }
}
