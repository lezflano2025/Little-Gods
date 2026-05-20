extends Control

# scenes/editor/Workspace.tscn controller.
# Top-down 2D blueprint canvas. Renders the spine + placed Attachments
# from the in-memory Recipe (via RecipeBuilder), accepts drag-drop from
# PartPalette, surfaces selection / delete / mirror-ghost interactions.
#
# Per docs/m1-p4-contract.md ("Workspace → CreatureEditor" + "What each
# panel owns → Workspace").

const WORKSPACE_SCALE := 100.0           # 1 m = 100 px
const SNAP_RADIUS_PX := 24.0
const ATTACHMENT_POINT_RADIUS := 4.0
const ATTACHMENT_POINT_HIGHLIGHT_RADIUS := 7.0

const PART_BASE_COLOR := Color(0.45, 0.6, 0.85, 1.0)
const PART_OUTLINE_COLOR := Color(0.08, 0.1, 0.14, 0.7)
const SPINE_COLOR := Color(0.55, 0.7, 0.95, 1.0)
const ATTACHMENT_POINT_COLOR := Color(0.35, 0.85, 0.45, 1.0)
const ATTACHMENT_POINT_HIGHLIGHT_COLOR := Color(0.95, 1.0, 0.55, 1.0)
const SELECTED_OUTLINE_COLOR := Color(1.0, 0.85, 0.2, 1.0)
const MIRROR_GHOST_COLOR := Color(0.45, 0.6, 0.85, 0.4)
const BACKGROUND_COLOR := Color(0.12, 0.13, 0.16, 1.0)
const GRID_COLOR := Color(1.0, 1.0, 1.0, 0.05)
const GRID_STEP_PX := 50.0

signal part_dropped(part_id: String, parent_index: int, slot_name: String, local_transform: Transform3D)
signal attachment_clicked(attachment_index: int)
signal attachment_delete_requested(attachment_index: int)
signal workspace_clicked_empty()
signal attachment_transform_changed(attachment_index: int, new_transform: Transform3D)

var _builder                                       # C# RecipeBuilder
var _selected_index: int = -1
var _symmetry_enabled: bool = false

# Hover state during drag (refreshed in _process while a drag is in flight).
var _hover_parent_index: int = -2                  # -2 = none, -1 = spine, >=0 = attachment
var _hover_slot_name: String = ""
var _hover_slot_canvas_pos: Vector2 = Vector2.ZERO

# Cached layout from the last _draw() pass — populated so hit-testing in
# _gui_input can find which placed Attachment the user clicked on.
var _attachment_rects: Array = []                  # Array of {index: int, center: Vector2, size: Vector2, kind: int}


func _ready() -> void:
	focus_mode = Control.FOCUS_ALL
	mouse_filter = Control.MOUSE_FILTER_STOP
	resized.connect(queue_redraw)


# ---------- contract methods ----------

func set_recipe_builder(builder) -> void:
	_builder = builder
	queue_redraw()


func refresh() -> void:
	queue_redraw()


func set_selection(index: int) -> void:
	_selected_index = index
	queue_redraw()


func set_symmetry(enabled: bool) -> void:
	_symmetry_enabled = enabled
	queue_redraw()


# ---------- hover update during drag ----------

func _process(_delta: float) -> void:
	var vp := get_viewport()
	if vp == null:
		return
	if not vp.gui_is_dragging():
		if _hover_parent_index != -2:
			_hover_parent_index = -2
			_hover_slot_name = ""
			queue_redraw()
		return
	var local := get_local_mouse_position()
	var slot = _find_nearest_free_slot(local, SNAP_RADIUS_PX)
	if slot == null:
		if _hover_parent_index != -2:
			_hover_parent_index = -2
			_hover_slot_name = ""
			queue_redraw()
		return
	if slot.parent_index != _hover_parent_index or slot.slot_name != _hover_slot_name:
		_hover_parent_index = slot.parent_index
		_hover_slot_name = slot.slot_name
		_hover_slot_canvas_pos = slot.canvas_pos
		queue_redraw()


# ---------- drop target ----------

func _can_drop_data(at_position: Vector2, data: Variant) -> bool:
	if typeof(data) != TYPE_DICTIONARY:
		return false
	if not data.has("part_id"):
		return false
	if _builder == null:
		return false
	return _find_nearest_free_slot(at_position, SNAP_RADIUS_PX) != null


func _drop_data(at_position: Vector2, data: Variant) -> void:
	if typeof(data) != TYPE_DICTIONARY or not data.has("part_id"):
		return
	var slot = _find_nearest_free_slot(at_position, SNAP_RADIUS_PX)
	if slot == null:
		return
	# M1: identity transform — the slot position itself encodes the placement.
	# A future P4.3 pass may add fine-positioning on drop.
	var xform := Transform3D.IDENTITY
	_hover_parent_index = -2
	_hover_slot_name = ""
	part_dropped.emit(data.part_id, slot.parent_index, slot.slot_name, xform)


# ---------- input ----------

func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		grab_focus()
		var hit := _hit_test_attachment(event.position)
		if hit >= 0:
			attachment_clicked.emit(hit)
		else:
			workspace_clicked_empty.emit()
		# TODO P4.3: begin drag of placed attachment here; on release emit
		# attachment_transform_changed(idx, new_local_transform).


func _unhandled_key_input(event: InputEvent) -> void:
	if not has_focus():
		return
	if _selected_index < 0:
		return
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_DELETE or event.keycode == KEY_BACKSPACE:
			attachment_delete_requested.emit(_selected_index)
			accept_event()


# ---------- drawing ----------

func _draw() -> void:
	_attachment_rects.clear()
	var rect_size := size
	draw_rect(Rect2(Vector2.ZERO, rect_size), BACKGROUND_COLOR, true)
	_draw_grid(rect_size)

	if _builder == null:
		return

	var origin := rect_size * 0.5
	var spine_id: String = _builder.Recipe.SpinePartId
	var spine = null
	if spine_id != "":
		spine = _builder.Registry.Get(spine_id)
	if spine == null:
		return

	# Spine body.
	_draw_part_shape(origin, spine, PartKindEnum.SPINE, SPINE_COLOR, _selected_index == -1 and false)

	# Cache slot canvas positions (parent_index -> {slot_name: Vector2}) for hit-testing and drawing.
	var slot_positions: Dictionary = {}
	slot_positions[-1] = _slot_world_positions_for_part(spine, origin)

	# Each placed Attachment.
	var attachments = _builder.Recipe.Attachments
	for i in range(attachments.size()):
		var a = attachments[i]
		var parent_slots: Dictionary = slot_positions.get(a.ParentPartIndex, {})
		var parent_origin: Vector2 = parent_slots.get(a.ParentSlotName, origin)
		var local_off: Vector3 = a.LocalTransform.origin
		var pos: Vector2 = parent_origin + Vector2(local_off.x, local_off.z) * WORKSPACE_SCALE
		var part = _builder.Registry.Get(a.ChildPartId)
		if part == null:
			continue
		var kind_value := int(part.Kind)
		var selected := i == _selected_index
		_draw_part_shape(pos, part, kind_value, PART_BASE_COLOR, selected)
		_attachment_rects.append({
			"index": i,
			"center": pos,
			"size": part.Footprint2D * WORKSPACE_SCALE,
			"kind": kind_value,
		})
		slot_positions[i] = _slot_world_positions_for_part(part, pos)

	# Attachment-point dots (drawn after shapes so they sit on top).
	for parent_index in slot_positions.keys():
		var slots: Dictionary = slot_positions[parent_index]
		for slot_name in slots.keys():
			var pos: Vector2 = slots[slot_name]
			var is_hover: bool = parent_index == _hover_parent_index and slot_name == _hover_slot_name
			var color: Color = ATTACHMENT_POINT_HIGHLIGHT_COLOR if is_hover else ATTACHMENT_POINT_COLOR
			var radius: float = ATTACHMENT_POINT_HIGHLIGHT_RADIUS if is_hover else ATTACHMENT_POINT_RADIUS
			draw_circle(pos, radius, color)
			if is_hover:
				draw_arc(pos, SNAP_RADIUS_PX, 0.0, TAU, 48, ATTACHMENT_POINT_HIGHLIGHT_COLOR, 1.0)

	# Mirror ghost preview when hovering during a drag.
	if _symmetry_enabled and _hover_parent_index != -2 and _hover_slot_name != "":
		var mirror_name: String = _mirror_slot_name(_hover_slot_name)
		if mirror_name != "" and mirror_name != _hover_slot_name:
			var parent_slots: Dictionary = slot_positions.get(_hover_parent_index, {})
			if parent_slots.has(mirror_name):
				var mirror_pos: Vector2 = parent_slots[mirror_name]
				draw_circle(mirror_pos, ATTACHMENT_POINT_HIGHLIGHT_RADIUS, MIRROR_GHOST_COLOR)
				draw_arc(mirror_pos, SNAP_RADIUS_PX, 0.0, TAU, 48, MIRROR_GHOST_COLOR, 1.0)


# Local mirror-slot helper (matches RecipeBuilder.MirrorSlotName).
# Duplicated in GDScript because GDScript cannot call C# static methods.
# RecipeBuilder remains the source of truth for unit tests.
func _mirror_slot_name(slot: String) -> String:
	if slot.begins_with("left_"):
		return "right_" + slot.substr(5)
	if slot.begins_with("right_"):
		return "left_" + slot.substr(6)
	return ""


func _draw_grid(rect_size: Vector2) -> void:
	var x := 0.0
	while x < rect_size.x:
		draw_line(Vector2(x, 0.0), Vector2(x, rect_size.y), GRID_COLOR, 1.0)
		x += GRID_STEP_PX
	var y := 0.0
	while y < rect_size.y:
		draw_line(Vector2(0.0, y), Vector2(rect_size.x, y), GRID_COLOR, 1.0)
		y += GRID_STEP_PX


# kind_value: 0=Spine, 1=Limb, 2=Head, 3=Mouth, 4=Other
func _draw_part_shape(center: Vector2, part, kind_value: int, color: Color, selected: bool) -> void:
	var fp: Vector2 = part.Footprint2D * WORKSPACE_SCALE
	# Map footprint X = width, Y = depth (along Z, top-down).
	var w: float = max(fp.x, 4.0)
	var h: float = max(fp.y, 4.0)
	match kind_value:
		PartKindEnum.HEAD, PartKindEnum.MOUTH:
			var r: float = max(w, h) * 0.5
			draw_circle(center, r, color)
			draw_arc(center, r, 0.0, TAU, 32, PART_OUTLINE_COLOR, 1.5)
			if selected:
				draw_arc(center, r + 1.5, 0.0, TAU, 32, SELECTED_OUTLINE_COLOR, 2.0)
		PartKindEnum.SPINE, PartKindEnum.LIMB:
			_draw_capsule(center, w, h, color)
			if selected:
				_draw_capsule_outline(center, w + 3.0, h + 3.0, SELECTED_OUTLINE_COLOR, 2.0)
		_:
			var rect := Rect2(center - Vector2(w, h) * 0.5, Vector2(w, h))
			draw_rect(rect, color, true)
			draw_rect(rect, PART_OUTLINE_COLOR, false, 1.5)
			if selected:
				var sel := rect.grow(2.0)
				draw_rect(sel, SELECTED_OUTLINE_COLOR, false, 2.0)


func _draw_capsule(center: Vector2, w: float, h: float, color: Color) -> void:
	# Capsule oriented along the longer axis.
	var horizontal := w >= h
	var long_axis: float = w if horizontal else h
	var short_axis: float = h if horizontal else w
	var r: float = short_axis * 0.5
	var rect_len: float = max(long_axis - short_axis, 0.0)
	if horizontal:
		var rect := Rect2(center - Vector2(rect_len * 0.5, r), Vector2(rect_len, short_axis))
		if rect_len > 0.0:
			draw_rect(rect, color, true)
		draw_circle(center + Vector2(-rect_len * 0.5, 0.0), r, color)
		draw_circle(center + Vector2(rect_len * 0.5, 0.0), r, color)
	else:
		var rect := Rect2(center - Vector2(r, rect_len * 0.5), Vector2(short_axis, rect_len))
		if rect_len > 0.0:
			draw_rect(rect, color, true)
		draw_circle(center + Vector2(0.0, -rect_len * 0.5), r, color)
		draw_circle(center + Vector2(0.0, rect_len * 0.5), r, color)
	_draw_capsule_outline(center, w, h, PART_OUTLINE_COLOR, 1.5)


func _draw_capsule_outline(center: Vector2, w: float, h: float, color: Color, thickness: float) -> void:
	var horizontal := w >= h
	var long_axis: float = w if horizontal else h
	var short_axis: float = h if horizontal else w
	var r: float = short_axis * 0.5
	var rect_len: float = max(long_axis - short_axis, 0.0)
	if horizontal:
		# Top + bottom segments of the rectangle.
		draw_line(center + Vector2(-rect_len * 0.5, -r), center + Vector2(rect_len * 0.5, -r), color, thickness)
		draw_line(center + Vector2(-rect_len * 0.5, r), center + Vector2(rect_len * 0.5, r), color, thickness)
		draw_arc(center + Vector2(-rect_len * 0.5, 0.0), r, PI * 0.5, PI * 1.5, 24, color, thickness)
		draw_arc(center + Vector2(rect_len * 0.5, 0.0), r, -PI * 0.5, PI * 0.5, 24, color, thickness)
	else:
		draw_line(center + Vector2(-r, -rect_len * 0.5), center + Vector2(-r, rect_len * 0.5), color, thickness)
		draw_line(center + Vector2(r, -rect_len * 0.5), center + Vector2(r, rect_len * 0.5), color, thickness)
		draw_arc(center + Vector2(0.0, -rect_len * 0.5), r, PI, TAU, 24, color, thickness)
		draw_arc(center + Vector2(0.0, rect_len * 0.5), r, 0.0, PI, 24, color, thickness)


# ---------- geometry helpers ----------

func _slot_world_positions_for_part(part, part_canvas_pos: Vector2) -> Dictionary:
	# Returns slot_name -> Vector2 (canvas position) for every AttachmentPoint
	# on `part`. Top-down: use the X and Z components of LocalPosition.
	var out: Dictionary = {}
	if part == null:
		return out
	for ap in part.AttachmentPoints:
		var local: Vector3 = ap.LocalPosition
		out[ap.Name] = part_canvas_pos + Vector2(local.x, local.z) * WORKSPACE_SCALE
	return out


func _find_nearest_free_slot(canvas_pos: Vector2, max_dist_px: float):
	# Returns {parent_index: int, slot_name: String, canvas_pos: Vector2}
	# for the closest unoccupied AttachmentPoint, or null.
	if _builder == null:
		return null
	var origin := size * 0.5
	var spine = null
	if _builder.Recipe.SpinePartId != "":
		spine = _builder.Registry.Get(_builder.Recipe.SpinePartId)
	if spine == null:
		return null

	# Build occupancy set: which (parent_index, slot_name) pairs are taken.
	var occupied: Dictionary = {}
	var attachments = _builder.Recipe.Attachments
	for i in range(attachments.size()):
		var a = attachments[i]
		occupied[_occupancy_key(a.ParentPartIndex, a.ParentSlotName)] = true

	var best_dist_sq: float = max_dist_px * max_dist_px
	var best = null

	# Walk the spine's slots.
	for ap in spine.AttachmentPoints:
		if occupied.has(_occupancy_key(-1, ap.Name)):
			continue
		var pos: Vector2 = origin + Vector2(ap.LocalPosition.x, ap.LocalPosition.z) * WORKSPACE_SCALE
		var d_sq: float = canvas_pos.distance_squared_to(pos)
		if d_sq < best_dist_sq:
			best_dist_sq = d_sq
			best = {"parent_index": -1, "slot_name": ap.Name, "canvas_pos": pos}

	# Walk slots on placed (non-spine) attachments too. In M1 the bundled
	# limbs/heads have no AttachmentPoints, so this loop is usually a no-op,
	# but the data model supports chained attachments and so do we.
	for i in range(attachments.size()):
		var a = attachments[i]
		var parent_pos: Vector2
		if a.ParentPartIndex == -1:
			parent_pos = origin
			var spine_slots := _slot_world_positions_for_part(spine, origin)
			if spine_slots.has(a.ParentSlotName):
				parent_pos = spine_slots[a.ParentSlotName]
		else:
			# Skip chained-on-chained for the snap target search to keep this
			# bounded; non-spine slots are still drawn but rarely used in M1.
			continue
		var local_off: Vector3 = a.LocalTransform.origin
		var att_pos: Vector2 = parent_pos + Vector2(local_off.x, local_off.z) * WORKSPACE_SCALE
		var child_part = _builder.Registry.Get(a.ChildPartId)
		if child_part == null:
			continue
		for ap in child_part.AttachmentPoints:
			if occupied.has(_occupancy_key(i, ap.Name)):
				continue
			var pos: Vector2 = att_pos + Vector2(ap.LocalPosition.x, ap.LocalPosition.z) * WORKSPACE_SCALE
			var d_sq: float = canvas_pos.distance_squared_to(pos)
			if d_sq < best_dist_sq:
				best_dist_sq = d_sq
				best = {"parent_index": i, "slot_name": ap.Name, "canvas_pos": pos}

	return best


func _occupancy_key(parent_index: int, slot_name: String) -> String:
	return "%d:%s" % [parent_index, slot_name]


func _hit_test_attachment(canvas_pos: Vector2) -> int:
	# Last-drawn first so the topmost shape wins.
	for i in range(_attachment_rects.size() - 1, -1, -1):
		var entry: Dictionary = _attachment_rects[i]
		var c: Vector2 = entry.center
		var s: Vector2 = entry.size
		var kind: int = entry.kind
		var hit := false
		match kind:
			PartKindEnum.HEAD, PartKindEnum.MOUTH:
				var r: float = max(s.x, s.y) * 0.5
				hit = canvas_pos.distance_to(c) <= r
			_:
				var rect := Rect2(c - s * 0.5, s)
				hit = rect.has_point(canvas_pos)
		if hit:
			return entry.index
	return -1


# Mirror of PartKind (C# enum) so we can avoid hard-coded magic numbers
# scattered through the file. Order must match PartKind.cs.
class PartKindEnum:
	const SPINE := 0
	const LIMB := 1
	const HEAD := 2
	const MOUTH := 3
	const OTHER := 4
