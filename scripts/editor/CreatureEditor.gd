extends Control

# scenes/editor/CreatureEditor.tscn controller.
# Hosts the three sub-panels (PartPalette / Workspace / Properties),
# owns the in-memory Recipe, wires signals.
#
# This GDScript controller exposes a "builder facade" via its own public
# properties (Recipe, Registry, SymmetryEnabled, Revision) so sub-panels
# can treat `self` as a duck-typed RecipeBuilder. The C# RecipeBuilder
# (src/creature/RecipeBuilder.cs) remains the source of truth for unit
# tests and C# code paths; we do not use it from GDScript because
# GDScript cannot call C# static methods.

const RecipeScript := preload("res://src/creature/Recipe.cs")
const AttachmentScript := preload("res://src/creature/Attachment.cs")
const MorphScript := preload("res://src/creature/Morph.cs")
const PartRegistryScript := preload("res://src/creature/PartRegistry.cs")

const USER_RECIPES_DIR := "user://recipes/"
const DEFAULT_SPINE := "spine_basic"
const MAX_RECIPE_BYTES := 10240
const SIZE_WARN_THRESHOLD := 9216

# ----- builder facade (sub-panels see these via duck-typed _builder = self) -----
var Recipe: Resource
var Registry: Node
var SymmetryEnabled: bool = false
var Revision: int = 0

# ----- internal -----
var _selected_index: int = -1
var _current_slug: String = ""

# ----- node refs -----
@onready var _new_button: Button = $VBox/TopBar/NewButton
@onready var _save_button: Button = $VBox/TopBar/SaveButton
@onready var _load_button: Button = $VBox/TopBar/LoadButton
@onready var _symmetry_toggle: CheckButton = $VBox/TopBar/SymmetryToggle
@onready var _size_label: Label = $VBox/TopBar/SizeLabel
@onready var _left_pane: MarginContainer = $VBox/HSplit/LeftPane
@onready var _center_pane: MarginContainer = $VBox/HSplit/CenterPane
@onready var _right_pane: MarginContainer = $VBox/HSplit/RightPane


func _ready() -> void:
	Registry = _resolve_registry()
	Recipe = RecipeScript.new()
	Recipe.SpinePartId = DEFAULT_SPINE

	_new_button.pressed.connect(_on_new_pressed)
	_save_button.pressed.connect(_on_save_pressed)
	_load_button.pressed.connect(_on_load_pressed)
	_symmetry_toggle.toggled.connect(_on_symmetry_toggled)

	_wire_panel_if_present(_left_pane)
	_wire_panel_if_present(_center_pane)
	_wire_panel_if_present(_right_pane)

	_refresh_size_label()


func _resolve_registry() -> Node:
	if has_node("/root/PartRegistry"):
		return get_node("/root/PartRegistry")
	var reg: Node = PartRegistryScript.new()
	reg.LoadLibrary()
	return reg


func _wire_panel_if_present(container: MarginContainer) -> void:
	if container.get_child_count() == 0:
		return
	var panel: Node = container.get_child(0)
	if panel.has_method("set_recipe_builder"):
		panel.set_recipe_builder(self)
	if panel.has_method("set_symmetry"):
		panel.set_symmetry(SymmetryEnabled)
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


# ---------- top-bar handlers ----------

func _on_new_pressed() -> void:
	Recipe = RecipeScript.new()
	Recipe.SpinePartId = DEFAULT_SPINE
	_selected_index = -1
	_current_slug = ""
	Revision += 1
	_broadcast_refresh()
	_broadcast_selection(-1)


func _on_save_pressed() -> void:
	var slug: String = _current_slug if _current_slug != "" else "untitled"
	var path: String = USER_RECIPES_DIR + slug + ".tres"
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(USER_RECIPES_DIR))
	var err: int = ResourceSaver.save(Recipe, path)
	if err == OK:
		_current_slug = slug
	_refresh_size_label()


func _on_load_pressed() -> void:
	if _current_slug == "":
		return
	var path: String = USER_RECIPES_DIR + _current_slug + ".tres"
	var loaded: Resource = ResourceLoader.load(path)
	if loaded != null:
		Recipe = loaded
	_selected_index = -1
	Revision += 1
	_broadcast_refresh()
	_broadcast_selection(-1)


func _on_symmetry_toggled(enabled: bool) -> void:
	SymmetryEnabled = enabled
	for c in [_left_pane, _center_pane, _right_pane]:
		if c.get_child_count() > 0:
			var p: Node = c.get_child(0)
			if p.has_method("set_symmetry"):
				p.set_symmetry(enabled)


# ---------- sub-panel signal handlers ----------

func _on_part_drag_started(_part_id: String) -> void:
	pass


func _on_part_drag_cancelled() -> void:
	pass


func _on_part_dropped(part_id: String, parent_index: int, slot_name: String, local_transform: Transform3D) -> void:
	var att: Resource = AttachmentScript.new()
	att.ParentPartIndex = parent_index
	att.ParentSlotName = slot_name
	att.ChildPartId = part_id
	att.LocalTransform = local_transform
	Recipe.Attachments.append(att)

	if SymmetryEnabled:
		var mirror_slot: String = _mirror_slot_name(slot_name)
		if mirror_slot != "" and mirror_slot != slot_name:
			var group_id: String = "mirror_%d" % Time.get_ticks_usec()
			att.MirrorGroupId = group_id
			var mirror_att: Resource = AttachmentScript.new()
			mirror_att.ParentPartIndex = parent_index
			mirror_att.ParentSlotName = mirror_slot
			mirror_att.ChildPartId = part_id
			var mirror_tx: Transform3D = local_transform
			mirror_tx.origin.x = -mirror_tx.origin.x
			mirror_att.LocalTransform = mirror_tx
			mirror_att.MirrorGroupId = group_id
			Recipe.Attachments.append(mirror_att)

	Revision += 1
	_broadcast_refresh()


func _on_attachment_clicked(attachment_index: int) -> void:
	_selected_index = attachment_index
	_broadcast_selection(attachment_index)


func _on_attachment_delete_requested(attachment_index: int) -> void:
	if attachment_index < 0 or attachment_index >= Recipe.Attachments.size():
		return
	# Clear mirror partner's group id if any.
	var group_id: String = Recipe.Attachments[attachment_index].MirrorGroupId
	if group_id != "":
		for i in range(Recipe.Attachments.size()):
			if i != attachment_index and Recipe.Attachments[i].MirrorGroupId == group_id:
				Recipe.Attachments[i].MirrorGroupId = ""
	Recipe.Attachments.remove_at(attachment_index)
	# Fix up parent indices.
	for i in range(Recipe.Attachments.size()):
		var a: Resource = Recipe.Attachments[i]
		if a.ParentPartIndex == attachment_index:
			a.ParentPartIndex = -1
		elif a.ParentPartIndex > attachment_index:
			a.ParentPartIndex -= 1
	_selected_index = -1
	Revision += 1
	_broadcast_refresh()
	_broadcast_selection(-1)


func _on_workspace_clicked_empty() -> void:
	_selected_index = -1
	_broadcast_selection(-1)


func _on_attachment_transform_changed(attachment_index: int, new_transform: Transform3D) -> void:
	if attachment_index < 0 or attachment_index >= Recipe.Attachments.size():
		return
	Recipe.Attachments[attachment_index].LocalTransform = new_transform
	Revision += 1
	_refresh_size_label()


func _on_morph_changed(attachment_index: int, stretch: Vector3, twist: float, paint_tint: Color) -> void:
	if attachment_index < 0 or attachment_index >= Recipe.Attachments.size():
		return
	var att: Resource = Recipe.Attachments[attachment_index]
	if att.MorphIndex < 0:
		var m: Resource = MorphScript.new()
		m.Stretch = stretch
		m.Twist = twist
		m.PaintTint = paint_tint
		Recipe.Morphs.append(m)
		att.MorphIndex = Recipe.Morphs.size() - 1
	else:
		var m: Resource = Recipe.Morphs[att.MorphIndex]
		m.Stretch = stretch
		m.Twist = twist
		m.PaintTint = paint_tint
	Revision += 1
	_refresh_size_label()


# ---------- broadcasting ----------

func _broadcast_refresh() -> void:
	for c in [_left_pane, _center_pane, _right_pane]:
		if c.get_child_count() > 0:
			var p: Node = c.get_child(0)
			if p.has_method("refresh"):
				p.refresh()
	_refresh_size_label()


func _broadcast_selection(index: int) -> void:
	for c in [_left_pane, _center_pane, _right_pane]:
		if c.get_child_count() > 0:
			var p: Node = c.get_child(0)
			if p.has_method("set_selection"):
				p.set_selection(index)


func _refresh_size_label() -> void:
	if _current_slug == "":
		_size_label.text = "(unsaved) / %d B" % MAX_RECIPE_BYTES
		return
	var path: String = USER_RECIPES_DIR + _current_slug + ".tres"
	if FileAccess.file_exists(path):
		var bytes: int = FileAccess.get_file_as_bytes(path).size()
		var color: Color = Color.WHITE if bytes < SIZE_WARN_THRESHOLD else Color.ORANGE_RED
		_size_label.add_theme_color_override("font_color", color)
		_size_label.text = "%d / %d B" % [bytes, MAX_RECIPE_BYTES]
	else:
		_size_label.text = "(unknown) / %d B" % MAX_RECIPE_BYTES


# ---------- mirror helper ----------

# Duplicated from RecipeBuilder.MirrorSlotName because GDScript cannot
# call C# statics. RecipeBuilder remains the source of truth for tests.
func _mirror_slot_name(slot: String) -> String:
	if slot.begins_with("left_"):
		return "right_" + slot.substr(5)
	if slot.begins_with("right_"):
		return "left_" + slot.substr(6)
	return ""
