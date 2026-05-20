# Claude Code setup

This project ships a `.mcp.json` at the repo root that tells Claude Code which MCP servers to load when the project is opened. Below is the per-server install + verification.

## Per-server install

### godot-mcp (Coding-Solo's fork)

PRD §5.2 lists `tomyud1/godot-mcp` first with Coding-Solo's as fallback. We default to Coding-Solo's because it is the actively-maintained fork on npm.

**Install (one-time, system-wide):**

```powershell
npx -y @coding-solo/godot-mcp --help   # downloads + caches the package
```

The project's `.mcp.json` already wires `npx -y @coding-solo/godot-mcp` with `GODOT_PATH` pointing at our pinned Godot 4.6.3 install (`C:\tools\Godot\...`). If you cloned outside the default path, update `.mcp.json` accordingly (or move to an ADR).

**Verify:**

```powershell
# In Claude Code, after opening the repo:
#   /mcp           lists registered servers; "godot" should appear
#   Ask Claude:    "list the scenes in this Godot project"
#                  Expected: cube_fall.tscn, cube_still.tscn (via godot-mcp)
```

### blender-mcp (ahujasid)

Requires the Blender add-on from the same repo. Two halves:

**1. Server (Python via uv):**

```powershell
uvx blender-mcp --help    # downloads + caches the server package
```

The project's `.mcp.json` wires `uvx blender-mcp`. Telemetry is off by default; set `DISABLE_TELEMETRY=true` in env if you want belt-and-braces.

**2. Blender add-on:**

```
1. Download addon.py from https://github.com/ahujasid/blender-mcp/blob/main/addon.py
2. Open Blender 4.x -> Edit -> Preferences -> Add-ons -> Install...
3. Pick the downloaded addon.py
4. Tick "Interface: Blender MCP" to enable
5. In any 3D View, open the sidebar (N), find "BlenderMCP" tab, click "Connect to Claude"
```

The add-on listens on `localhost:9876`. The MCP server connects to it.

**Verify:**

```
# In Claude Code:
#   /mcp                  shows "blender"
#   Ask Claude:           "list the meshes in the current Blender scene"
#                         (Blender must be open with the add-on connected)
```

### Supabase MCP

Already available globally on this machine via the Supabase Claude plugin (see `mcp__plugin_supabase_supabase__*` tools in Claude's tool list). No project-level config required. Gallery wiring lands in M5.

## What `.mcp.json` controls

Claude Code reads `.mcp.json` at the repo root and registers the listed servers automatically when the project is opened. Each entry maps to one process:

```json
{
  "mcpServers": {
    "<short-name>": {
      "command": "<exe>",
      "args": [ ... ],
      "env": { ... }
    }
  }
}
```

Project-level entries override anything with the same name in the user's global config; everything else from the global config still loads.

## Troubleshooting

| Symptom | Fix |
|-|-|
| `/mcp` shows "godot: failed to start" | run `npx -y @coding-solo/godot-mcp` once manually to prime the npm cache |
| godot-mcp can't find Godot | check `GODOT_PATH` in `.mcp.json` resolves to a real `.exe` |
| blender-mcp can't connect | open Blender and click "Connect to Claude" in the sidebar — the add-on must be the one accepting the connection |
| `uvx` not found | `winget install astral-sh.uv` (or follow https://docs.astral.sh/uv/) |
