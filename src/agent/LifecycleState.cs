namespace LittleGods.Agent;

/// Where an agent is in its life. The lifecycle system (P3) advances it; the
/// spawner (P5) and the death→respawn loop (P6) react to it. Kept minimal for
/// M4 — extend (e.g. Juvenile, Mating) only when a phase needs the distinction.
public enum LifecycleState
{
    Alive,
    Dead,
}
