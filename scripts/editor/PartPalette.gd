extends Control

# scenes/editor/PartPalette.tscn controller.
# Reads PartRegistry autoload, renders one row per Part grouped by PartKind,
# and emits drag signals consumed by CreatureEditor.gd.
#
# Per docs/m1-p4-contract.md ("PartPalette → CreatureEditor").

const PartRegistry := preload("res://src/creature/PartRegistry.cs")

const KIND_ORDER: Array[int] = [0, 1, 2, 3, 4]   # Spine, Limb, Head, Mouth, Other
const KIND_NAMES: Array[String] = ["Spine", "Limb", "Head", "Mouth", "Other"]

const ROW_HEIGHT := 36
const PREVIEW_WIDTH := 50.0
const PREVIEW_HEIGHT := 28.0
const PART_BASE_COLOR := Color(0.45, 0.6, 0.85, 1.0)
const SECTION_FONT_SIZE := 12

signal part_drag_started(part_id: String)
signal part_drag_cancelled()

@onready var _list: VBoxContainer = $Scroll/List

var _builder                                  # C# RecipeBuilder (optional)
var _registry                                 # PartRegistry node or instance
var _drag_in_flight: bool = false


func _ready() -> void:
	_registry = _resolve_registry()
	refresh()


func _resolve_registry():
	var reg := get_node_or_null("/root/PartRegistry")
	if reg != null:
		return reg
	reg = PartRegistry.new()
	reg.LoadLibrary()
	return reg


# ---------- contract methods ----------

func set_recipe_builder(builder) -> void:
	_builder = builder


func refresh() -> void:
	if _list == null:
		return
	for child in _list.get_children():
		child.queue_free()
	if _registry == null:
		return

	var parts_by_kind: Dictionary = {}
	for k in KIND_ORDER:
		parts_by_kind[k] = []

	# GDScript can't access the C# `All` IReadOnlyDictionary property;
	# PartRegistry exposes a GDScript-friendly Array via GetAllParts().
	var parts: Array = _registry.GetAllParts()
	for part in parts:
		var kind_value: int = int(part.Kind)
		if not parts_by_kind.has(kind_value):
			parts_by_kind[kind_value] = []
		parts_by_kind[kind_value].append(part)

	for kind in KIND_ORDER:
		var bucket: Array = parts_by_kind[kind]
		if bucket.is_empty():
			continue
		bucket.sort_custom(func(a, b): return a.DisplayName < b.DisplayName)
		_list.add_child(_make_section_header(KIND_NAMES[kind]))
		for part in bucket:
			_list.add_child(_make_part_row(part))


func set_selection(_index: int) -> void:
	pass


func set_symmetry(_enabled: bool) -> void:
	pass


# ---------- row construction ----------

func _make_section_header(name: String) -> Label:
	var label := Label.new()
	label.text = name
	label.add_theme_font_size_override("font_size", SECTION_FONT_SIZE)
	label.add_theme_color_override("font_color", Color(0.75, 0.78, 0.82, 1.0))
	return label


func _make_part_row(part) -> Control:
	var row := _PartRow.new()
	row.part_id = part.Id
	row.display_name = part.DisplayName
	row.footprint = part.Footprint2D
	row.palette = self
	row.custom_minimum_size = Vector2(0, ROW_HEIGHT)
	row.mouse_filter = Control.MOUSE_FILTER_STOP

	var hbox := HBoxContainer.new()
	hbox.anchor_right = 1.0
	hbox.anchor_bottom = 1.0
	hbox.offset_left = 4
	hbox.offset_right = -4
	hbox.add_theme_constant_override("separation", 8)
	hbox.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_child(hbox)

	var preview := _FootprintPreview.new()
	preview.footprint = part.Footprint2D
	preview.custom_minimum_size = Vector2(PREVIEW_WIDTH, PREVIEW_HEIGHT)
	preview.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hbox.add_child(preview)

	var name_label := Label.new()
	name_label.text = part.DisplayName
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hbox.add_child(name_label)

	return row


# ---------- drag lifecycle ----------

func _notification(what: int) -> void:
	if what == NOTIFICATION_DRAG_END:
		if _drag_in_flight:
			# get_viewport().gui_get_drag_data() is non-null while a drag was
			# active; if we get here without a successful drop, surface cancel.
			var vp := get_viewport()
			if vp != null and vp.gui_get_drag_data() != null:
				part_drag_cancelled.emit()
			_drag_in_flight = false


func _notify_drag_started(part_id: String) -> void:
	_drag_in_flight = true
	part_drag_started.emit(part_id)


# ---------- inner classes ----------

class _PartRow extends Control:
	var part_id: String
	var display_name: String
	var footprint: Vector2
	var palette                                    # back-ref for signal emission

	func _get_drag_data(_at_position: Vector2):
		if palette != null:
			palette._notify_drag_started(part_id)
		var preview := Label.new()
		preview.text = display_name
		preview.add_theme_color_override("font_color", Color.WHITE)
		set_drag_preview(preview)
		return {"part_id": part_id}


class _FootprintPreview extends Control:
	var footprint: Vector2 = Vector2.ONE

	func _draw() -> void:
		var rect_size := size
		var fw: float = max(footprint.x, 0.001)
		var fh: float = max(footprint.y, 0.001)
		var scale: float = min(rect_size.x / fw, rect_size.y / fh) * 0.85
		var draw_size := Vector2(fw * scale, fh * scale)
		var origin := (rect_size - draw_size) * 0.5
		draw_rect(Rect2(origin, draw_size), PART_BASE_COLOR, true)
		draw_rect(Rect2(origin, draw_size), Color(0, 0, 0, 0.35), false, 1.0)
