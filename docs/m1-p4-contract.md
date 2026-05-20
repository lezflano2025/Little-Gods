# M1 P4 — Editor signal contract

The 2D blueprint editor is a top-level `CreatureEditor.tscn` that hosts three sub-panels as scene instances. Each sub-panel is independently built in P4.2; this document is the contract they conform to.

## Architecture

```
CreatureEditor.tscn (Control, full-screen)
├── VBoxContainer
│   ├── TopBar (HBoxContainer)
│   │   ├── NewButton  SaveButton  LoadButton
│   │   ├── (spacer)
│   │   ├── SymmetryToggle (CheckButton)
│   │   └── SizeLabel ("X / 10240 B", red over 9216)
│   └── HSplitContainer (resizable)
│       ├── LeftPane    -> PartPalette.tscn instance
│       ├── CenterPane  -> Workspace.tscn instance
│       └── RightPane   -> Properties.tscn instance
```

`CreatureEditor.gd` (GDScript) is the controller. It owns:
- `_builder: RecipeBuilder` (C#, from `LittleGods.Creature`)
- `_selected_index: int` (the attachment currently selected; -1 for none)
- `_current_slug: String` (last saved/loaded slug, or "" if unsaved)

It does NOT own any rendering state — sub-panels manage their own visuals.

## Signal contract

### PartPalette → CreatureEditor

| Signal | Args | Meaning |
|-|-|-|
| `part_drag_started` | `part_id: String` | User picked up a Part from the palette |
| `part_drag_cancelled` | — | Drag ended outside any valid drop target |

### Workspace → CreatureEditor

| Signal | Args | Meaning |
|-|-|-|
| `part_dropped` | `part_id: String`, `parent_index: int`, `slot_name: String`, `local_transform: Transform3D` | User dropped a Part on a slot (parent_index = -1 means the spine root) |
| `attachment_clicked` | `attachment_index: int` | User clicked an existing placed attachment |
| `attachment_delete_requested` | `attachment_index: int` | User pressed Delete on a selected attachment |
| `workspace_clicked_empty` | — | User clicked empty workspace (clears selection) |
| `attachment_transform_changed` | `attachment_index: int`, `new_transform: Transform3D` | User dragged a placed attachment to a new position |

### Properties → CreatureEditor

| Signal | Args | Meaning |
|-|-|-|
| `morph_changed` | `attachment_index: int`, `stretch: Vector3`, `twist: float`, `paint_tint: Color` | User adjusted morph sliders |
| `attachment_inspector_close_requested` | — | User clicked away from the inspector |

### CreatureEditor → all sub-panels (method calls, not signals)

Each sub-panel exposes these public methods. `CreatureEditor` calls them when state changes:

| Method | Args | Meaning |
|-|-|-|
| `set_recipe_builder(builder)` | `RecipeBuilder` (C#) | Initial wire-up; called once on scene ready |
| `refresh()` | — | Recipe contents changed (any mutation); re-render |
| `set_selection(index)` | `int` | Selection moved (also use index = -1 to deselect) |
| `set_symmetry(enabled)` | `bool` | Top-bar toggle changed |

## What each panel owns

### PartPalette (Agent A)
- Reads `PartRegistry.Instance` (autoload)
- Renders one row per Part grouped by `PartKind` (Spine first, then Limb, Head, Mouth)
- Each row shows DisplayName + a small footprint preview
- On mouse-down on a row: emits `part_drag_started` and begins a Godot drag-and-drop with `set_drag_preview()` and a `{part_id: String}` data payload

### Workspace (Agent B)
- Reads the in-memory Recipe via the RecipeBuilder
- Renders the spine as a horizontal capsule at canvas centre, oriented along Z; renders each placed Attachment as its Part's `Footprint2D` shape at the slot position + LocalTransform offset
- Renders each AttachmentPoint on every placed Part as a small green dot (with a slot label on hover)
- Drop target: when a drag with `{part_id}` enters, find the nearest free attachment point within `SNAP_RADIUS_PX`. If found, highlight it. On drop, emit `part_dropped` with the parent_index + slot_name
- Click on placed Part: emit `attachment_clicked`. Click on empty space: emit `workspace_clicked_empty`.
- Delete key: if anything selected, emit `attachment_delete_requested`
- Drag a placed Part: emit `attachment_transform_changed` on release

### Properties (Agent C)
- When `set_selection(index >= 0)` is called: read the Attachment at index, populate sliders
  - Stretch (Vector3) — three sliders, range 0.1 to 3.0
  - Twist (float) — slider, range -180° to 180°
  - PaintTint (Color) — Godot ColorPickerButton
- Emit `morph_changed` on any slider release (NOT on every drag event — debounce)
- When `set_selection(-1)`: show "Nothing selected" placeholder

## Constants the agents may need

```gdscript
const SNAP_RADIUS_PX := 24.0       # distance for snap-to-attachment
const WORKSPACE_SCALE := 100.0     # 1 metre in local-space = 100 pixels
const PART_BASE_COLOR := Color(0.45, 0.6, 0.85, 1.0)
const ATTACHMENT_POINT_COLOR := Color(0.35, 0.85, 0.45, 1.0)
const SELECTED_OUTLINE_COLOR := Color(1.0, 0.85, 0.2, 1.0)
const MIRROR_GHOST_COLOR := Color(0.45, 0.6, 0.85, 0.4)
```

These are suggestions; agents may adjust within reason.

## Out of scope for P4

- Undo / redo stack (post-P5 stretch)
- Multi-select
- Copy/paste
- Workspace zoom (M1 lands at fixed scale; M2 may add)
- Custom paint masking beyond a single tint (M1)
