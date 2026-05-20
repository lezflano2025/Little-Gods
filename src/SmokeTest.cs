using Godot;

namespace LittleGods;

// Smoke test — verifies C# compiles against Godot bindings in M0.
// Should be removed once a real C# module exists.
public partial class SmokeTest : Node
{
    public override void _Ready()
    {
        GD.Print("[C#] SmokeTest._Ready — Little Gods M0 boot");
    }
}
