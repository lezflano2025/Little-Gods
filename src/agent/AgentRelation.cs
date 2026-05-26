namespace LittleGods.Agent;

/// How one agent regards another, from the perceiver's point of view. The
/// species layer (P5) supplies the relation between two species ids; perception
/// (P2) uses it to bucket sightings into prey / predator / mate. Agent-type
/// blind: it is just a label the generic runtime carries.
public enum AgentRelation
{
    Neutral,
    Prey,
    Predator,
    Mate,
}
