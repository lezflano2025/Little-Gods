namespace LittleGods.Agent;

/// The result of ticking a behaviour-tree node (ADR-0005).
public enum BtStatus
{
    /// Still working; tick me again next frame.
    Running,

    /// Completed successfully.
    Success,

    /// Completed but failed.
    Failure,
}
