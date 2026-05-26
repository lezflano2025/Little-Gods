namespace LittleGods.Agent;

/// An agent's homeostatic state. Immutable value; the needs system (P3) computes
/// the next Needs each tick from the current one and writes it back onto the
/// AgentState. P3 owns the thresholds and integration rules — this struct only
/// fixes the fields the rest of the sim reads.
public readonly struct Needs
{
    /// Hit points in [0, 1]. Reaching 0 kills the agent.
    public readonly float Health;

    /// Hunger in [0, 1]: 0 = sated, 1 = starving (then it drains Health).
    public readonly float Hunger;

    /// Seconds the agent has been alive.
    public readonly float Age;

    public Needs(float health, float hunger, float age)
    {
        Health = health;
        Hunger = hunger;
        Age = age;
    }

    /// A healthy, sated, newborn agent.
    public static Needs Newborn(float health = 1f) => new(health, 0f, 0f);

    /// Copy-with for the named fields (immutable update).
    public Needs With(float? health = null, float? hunger = null, float? age = null)
        => new(health ?? Health, hunger ?? Hunger, age ?? Age);
}
