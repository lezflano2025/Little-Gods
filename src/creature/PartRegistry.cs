using Godot;
using System.Collections.Generic;

namespace LittleGods.Creature;

/// Autoload singleton (one of the few sanctioned by CLAUDE.md, since the part
/// library is genuinely cross-cutting state). Registered in project.godot.
///
/// Loads every `.tres` Part under LibraryPath at _Ready, then provides id
/// lookups for the editor, runtime, and tests.
public partial class PartRegistry : Node
{
    public const string LibraryPath = "res://assets/rigblock/";

    public static PartRegistry? Instance { get; private set; }

    private readonly Dictionary<string, Part> _parts = new();

    public int Count => _parts.Count;

    public IReadOnlyDictionary<string, Part> All => _parts;

    public override void _Ready()
    {
        if (Instance != null && Instance != this)
        {
            GD.PushWarning("PartRegistry: second instance instantiated; freeing.");
            QueueFree();
            return;
        }
        Instance = this;
        LoadLibrary();
    }

    /// Idempotent: clears + reloads. Safe to call from tests.
    public void LoadLibrary()
    {
        _parts.Clear();
        var dir = DirAccess.Open(LibraryPath);
        if (dir == null)
        {
            GD.PushWarning($"PartRegistry: {LibraryPath} not openable");
            return;
        }
        dir.ListDirBegin();
        var name = dir.GetNext();
        while (!string.IsNullOrEmpty(name))
        {
            if (!dir.CurrentIsDir() && name.EndsWith(".tres"))
            {
                var part = ResourceLoader.Load<Part>(LibraryPath + name);
                if (part == null)
                {
                    GD.PushWarning($"PartRegistry: failed to load {name}");
                }
                else if (string.IsNullOrEmpty(part.Id))
                {
                    GD.PushWarning($"PartRegistry: {name} has empty Id; skipping");
                }
                else if (_parts.ContainsKey(part.Id))
                {
                    GD.PushWarning($"PartRegistry: duplicate Id {part.Id} from {name}");
                }
                else
                {
                    _parts[part.Id] = part;
                }
            }
            name = dir.GetNext();
        }
        dir.ListDirEnd();
    }

    public Part? Get(string id) => _parts.TryGetValue(id, out var p) ? p : null;

    /// For tests: register a part directly without touching disk.
    public void Register(Part part)
    {
        if (string.IsNullOrEmpty(part.Id))
        {
            throw new System.ArgumentException("Part has empty Id", nameof(part));
        }
        if (_parts.ContainsKey(part.Id))
        {
            throw new System.InvalidOperationException($"Part '{part.Id}' already registered");
        }
        _parts[part.Id] = part;
    }

    public void Clear() => _parts.Clear();
}
