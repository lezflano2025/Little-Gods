using System.Collections.Generic;

namespace LittleGods.Agent;

/// Per-agent working memory for the behaviour tree (ADR-0005). Tasks are
/// stateless and shared across all agents of a species, so ALL transient
/// per-agent state (the current move target, the latest sighting, a decorator's
/// timer) lives here, never in task fields.
///
/// Mutable by design — it is scratch memory, written and read within a tick.
/// Determinism is preserved because what gets written is itself a deterministic
/// function of the seeded agent state and the explicit dt.
public sealed class Blackboard
{
    private readonly Dictionary<string, object?> _values = new();

    public void Set<T>(string key, T value) => _values[key] = value;

    public bool Has(string key) => _values.ContainsKey(key);

    public void Remove(string key) => _values.Remove(key);

    public void Clear() => _values.Clear();

    /// Typed read; false (and default) if absent or the stored type mismatches.
    public bool TryGet<T>(string key, out T value)
    {
        if (_values.TryGetValue(key, out object? raw) && raw is T typed)
        {
            value = typed;
            return true;
        }
        value = default!;
        return false;
    }

    /// Typed read; throws if absent. Use when the key is a precondition.
    public T Get<T>(string key)
    {
        if (TryGet(key, out T value)) return value;
        throw new KeyNotFoundException(
            $"Blackboard has no entry '{key}' of type {typeof(T).Name}.");
    }

    /// Typed read with a fallback when absent.
    public T GetOr<T>(string key, T fallback)
        => TryGet(key, out T value) ? value : fallback;
}
