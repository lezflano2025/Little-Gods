extends Control

# scenes/editor/CreatureEditor.tscn controller.
# Hosts the three sub-panels (PartPalette / Workspace / Properties),
# owns the in-memory RecipeBuilder, wires signals.
#
# Per docs/m1-p4-contract.md.

const RecipeBuilder := preload("res://src/creature/RecipeBuilder.cs")
const Recipe := preload("res://src/creature/Recipe.cs")
const RecipeStorage := preload("res://src/creature/RecipeStorage.cs")
const PartRegistry := preload("res://src/creature/PartRegistry.cs")

const DEFAULT_SPINE := "spine_basic"

@onready var _new_button: Button = $VBox/TopBar/NewButton
@onready var _save_button: Button = $VBox/TopBar/SaveButton
@onready var _load_button: Button = $VBox/TopBar/LoadButton
@onready var _symmetry_toggle: CheckButton = $VBox/TopBar/SymmetryToggle
@onready var _size_label: Label = $VBox/TopBar/SizeLabel

@onready var _left_pane: MarginContainer = $VBox/HSplit/LeftPane
@onready var _center_pane: MarginContainer = $VBox/HSplit/CenterPane
@onready var _right_pane: MarginContainer = $VBox/HSplit/RightPane

var _builder                     # C# RecipeBuilder instance
var _selected_index: int = -1
var _current_slug: String = ""


func _ready() -> void:
	var registry := _resolve_registry()
	if registry == null:
		push_error("CreatureEditor: PartRegistry autoload not found")
		return

	_builder = RecipeBuilder.ForNewCreature(DEFAULT_SPINE, registry)

	_new_button.pressed.connect(_on_new_pressed)
	_save_button.pressed.connect(_on_save_pressed)
	_load_button.pressed.connect(_on_load_pressed)
	_symmetry_toggle.toggled.connect(_on_symmetry_toggled)

	# Hand the builder to whichever sub-panels are already present.
	# (P4.1 placeholders are static Labels; real panels in P4.2 will
	# expose set_recipe_builder().)
	_wire_panel_if_present(_left_pane)
	_wire_panel_if_present(_center_pane)
	_wire_panel_if_present(_right_pane)

	_refresh_size_label()


func _resolve_registry():
	# PartRegistry is an autoload; look it up on the scene tree root.
	if has_node("/root/PartRegistry"):
		return get_node("/root/PartRegistry")
	# Fall back to building one ad-hoc for headless test scenarios.
	var reg = PartRegistry.new()
	reg.LoadLibrary()
	return reg


func _wire_panel_if_present(container: MarginContainer) -> void:
	if container.get_child_count() == 0:
		return
	var panel := container.get_child(0)
	# Sub-panels (P4.2) will implement these methods; placeholders won't.
	if panel.has_method("set_recipe_builder"):
		panel.set_recipe_builder(_builder)
	if panel.has_method("set_symmetry"):
		panel.set_symmetry(_symmetry_toggle.button_pressed)

	# Wire any signals the contract says these panels emit.
	_connect_if_has_signal(panel, "part_dropped", _on_part_dropped)
	_connect_if_has_signal(panel, "attachment_clicked", _on_attachment_clicked)
	_connect_if_has_signal(panel, "attachment_delete_requested", _on_attachment_delete_requested)
	_connect_if_has_signal(panel, "workspace_clicked_empty", _on_workspace_clicked_empty)
	_connect_if_has_signal(panel, "attachment_transform_changed", _on_attachment_transform_changed)
	_connect_if_has_signal(panel, "morph_changed", _on_morph_changed)
	_connect_if_has_signal(panel, "part_drag_started", _on_part_drag_started)
	_connect_if_has_signal(panel, "part_drag_cancelled", _on_part_drag_cancelled)


func _connect_if_has_signal(target: Object, signal_name: String, callable: Callable) -> void:
	if target.has_signal(signal_name):
		target.connect(signal_name, callable)


# ---------- top bar handlers ----------

func _on_new_pressed() -> void:
	var registry := _resolve_registry()
	_builder = RecipeBuilder.ForNewCreature(DEFAULT_SPINE, registry)
	_selected_index = -1
	_current_slug = ""
	_broadcast_refresh()
	_broadcast_selection(-1)


func _on_save_pressed() -> void:
	var slug := _current_slug if _current_slug != "" else "untitled"
	RecipeStorage.Save(_builder.Recipe, slug)
	_current_slug = slug
	_refresh_size_label()


func _on_load_pressed() -> void:
	# Load the most recent recipe slug we know about (TODO P4.3: open a real
	# load dialog). For P4.1 this is a stub that simply re-loads the current
	# slug if one has been saved.
	if _current_slug == "":
		return
	var registry := _resolve_registry()
	var recipe = RecipeStorage.Load(_current_slug)
	_builder = RecipeBuilder.new(recipe, registry)
	_selected_index = -1
	_broadcast_refresh()
	_broadcast_selection(-1)


func _on_symmetry_toggled(enabled: bool) -> void:
	_builder.SymmetryEnabled = enabled
	for c in [_left_pane, _center_pane, _right_pane]:
		if c.get_child_count() > 0:
			var p := c.get_child(0)
			if p.has_method("set_symmetry"):
				p.set_symmetry(enabled)


# ---------- sub-panel signal handlers ----------

func _on_part_drag_started(_part_id: String) -> void:
	pass   # Hook for future "drag preview" UX


func _on_part_drag_cancelled() -> void:
	pass


func _on_part_dropped(part_id: String, parent_index: int, slot_name: String, local_transform: Transform3D) -> void:
	_builder.AddAttachmentMaybeMirrored(parent_index, slot_name, part_id, local_transform)
	_broadcast_refresh()


func _on_attachment_clicked(attachment_index: int) -> void:
	_selected_index = attachment_index
	_broadcast_selection(attachment_index)


func _on_attachment_delete_requested(attachment_index: int) -> void:
	_builder.RemoveAttachment(attachment_index)
	_selected_index = -1
	_broadcast_refresh()
	_broadcast_selection(-1)


func _on_workspace_clicked_empty() -> void:
	_selected_index = -1
	_broadcast_selection(-1)


func _on_attachment_transform_changed(attachment_index: int, new_transform: Transform3D) -> void:
	_builder.SetTransform(attachment_index, new_transform)
	_refresh_size_label()


func _on_morph_changed(attachment_index: int, stretch: Vector3, twist: float, paint_tint: Color) -> void:
	# Morph is referenced from the Attachment by index. Create one if needed.
	# (Full implementation arrives in P4.3 integration.)
	pass


# ---------- broadcasting ----------

func _broadcast_refresh() -> void:
	for c in [_left_pane, _center_pane, _right_pane]:
		if c.get_child_count() > 0:
			var p := c.get_child(0)
			if p.has_method("refresh"):
				p.refresh()
	_refresh_size_label()


func _broadcast_selection(index: int) -> void:
	for c in [_left_pane, _center_pane, _right_pane]:
		if c.get_child_count() > 0:
			var p := c.get_child(0)
			if p.has_method("set_selection"):
				p.set_selection(index)


func _refresh_size_label() -> void:
	# We measure size on the LAST SAVED bytes, not the in-memory Recipe,
	# because Godot serialization is needed to get a real size. P4.3 may
	# refine this to a live estimate.
	if _current_slug == "":
		_size_label.text = "(unsaved) / 10240 B"
		return
	var path := RecipeStorage.PathFor(_current_slug)
	if FileAccess.file_exists(path):
		var bytes := FileAccess.get_file_as_bytes(path).size()
		var color := Color.WHITE if bytes < 9216 else Color.ORANGE_RED
		_size_label.add_theme_color_override("font_color", color)
		_size_label.text = "%d / 10240 B" % bytes
	else:
		_size_label.text = "(unknown) / 10240 B"
