extends SubViewportContainer

# scenes/editor/Preview3D.tscn controller — the M2 real-time 3D preview that
# replaces the M1 2D blueprint Workspace as the editor's centre pane.
#
# Renders the live metaball creature through the C# CreaturePreview bridge
# (GDScript cannot call C# statics, so we drive an instance method). Orbits
# with middle-mouse drag + wheel zoom (Blender/Godot convention), and accepts
# part drops from PartPalette by auto-attaching to the first free compatible
# slot (full 3D slot-picking is a later refinement).
#
# Implements the same panel contract CreatureEditor.gd expects:
# set_recipe_builder / refresh / set_selection / set_symmetry, and emits
# part_dropped. The 3D viewport tree (SubViewport + camera + light + preview)
# is built in code to keep the .tscn trivial and avoid fragile scene authoring.

const CreaturePreviewScript := preload("res://src/mesh/CreaturePreview.cs")

signal part_dropped(part_id: String, parent_index: int, slot_name: String, local_transform: Transform3D)

var _builder                                  # the CreatureEditor builder facade
var _selected_index: int = -1
var _symmetry_enabled: bool = false

var _viewport: SubViewport
var _camera: Camera3D
var _preview                                  # CreaturePreview (C# Node3D)

# Turntable orbit state.
var _orbit_yaw: float = 0.7
var _orbit_pitch: float = 0.45
var _target: Vector3 = Vector3.ZERO
var _distance: float = 5.0


func _ready() -> void:
	stretch = true
	mouse_filter = Control.MOUSE_FILTER_STOP

	_viewport = SubViewport.new()
	_viewport.own_world_3d = true
	_viewport.transparent_bg = false
	_viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	_viewport.msaa_3d = Viewport.MSAA_2X
	add_child(_viewport)

	# Ambient + solid background so the clay creature is always visibly lit.
	var env_holder := WorldEnvironment.new()
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.10, 0.11, 0.14)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.62, 0.62, 0.68)
	env.ambient_light_energy = 0.6
	env_holder.environment = env
	_viewport.add_child(env_holder)

	var key_light := DirectionalLight3D.new()
	key_light.rotation_degrees = Vector3(-50.0, -35.0, 0.0)
	key_light.light_energy = 1.2
	_viewport.add_child(key_light)

	_camera = Camera3D.new()
	_camera.current = true
	_viewport.add_child(_camera)

	_preview = CreaturePreviewScript.new()
	_viewport.add_child(_preview)

	_update_camera()
	refresh()


# ---------- panel contract ----------

func set_recipe_builder(builder) -> void:
	_builder = builder
	refresh()


func set_symmetry(enabled: bool) -> void:
	_symmetry_enabled = enabled


func set_selection(index: int) -> void:
	_selected_index = index


func refresh() -> void:
	if _builder == null or _preview == null:
		return
	_preview.Rebuild(_builder.Recipe, _builder.Registry)
	_frame_camera()


# ---------- camera ----------

func _frame_camera() -> void:
	if _preview == null:
		return
	var aabb: AABB = _preview.GetMeshAabb()
	if aabb.size.length() > 0.01:
		_target = aabb.get_center()
		_distance = clampf(aabb.size.length() * 1.1, 2.0, 25.0)
	_update_camera()


func _update_camera() -> void:
	if _camera == null:
		return
	var cp := cos(_orbit_pitch)
	var dir := Vector3(cp * sin(_orbit_yaw), sin(_orbit_pitch), cp * cos(_orbit_yaw))
	_camera.position = _target + dir * _distance
	_camera.look_at(_target, Vector3.UP)


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion:
		if event.button_mask & MOUSE_BUTTON_MASK_MIDDLE:
			_orbit_yaw -= event.relative.x * 0.01
			_orbit_pitch = clampf(_orbit_pitch - event.relative.y * 0.01, -1.4, 1.4)
			_update_camera()
			accept_event()
	elif event is InputEventMouseButton and event.pressed:
		if event.button_index == MOUSE_BUTTON_WHEEL_UP:
			_distance = clampf(_distance - 0.5, 1.0, 40.0)
			_update_camera()
			accept_event()
		elif event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			_distance = clampf(_distance + 0.5, 1.0, 40.0)
			_update_camera()
			accept_event()


# ---------- drag-drop from PartPalette ----------

func _can_drop_data(_at_position: Vector2, data: Variant) -> bool:
	if typeof(data) != TYPE_DICTIONARY or not data.has("part_id"):
		return false
	if _builder == null:
		return false
	return _find_free_compatible_slot(String(data["part_id"])) != null


func _drop_data(_at_position: Vector2, data: Variant) -> void:
	if typeof(data) != TYPE_DICTIONARY or not data.has("part_id"):
		return
	var slot = _find_free_compatible_slot(String(data["part_id"]))
	if slot == null:
		return
	# Identity transform: the chosen slot's anchor encodes the placement.
	part_dropped.emit(String(data["part_id"]), slot.parent_index, slot.slot_name, Transform3D.IDENTITY)


# Returns {parent_index:int, slot_name:String} for the first free slot whose
# AllowedKinds accepts the dropped part's Kind (spine slots first, then placed
# attachments' own slots), or null if there is none.
func _find_free_compatible_slot(part_id: String):
	if _builder == null:
		return null
	var part = _builder.Registry.Get(part_id)
	if part == null:
		return null
	var kind_bit: int = 1 << int(part.Kind)

	var spine_id: String = _builder.Recipe.SpinePartId
	var spine = _builder.Registry.Get(spine_id) if spine_id != "" else null
	if spine == null:
		return null

	var occupied: Dictionary = {}
	var attachments = _builder.Recipe.Attachments
	for i in range(attachments.size()):
		var a = attachments[i]
		occupied["%d:%s" % [a.ParentPartIndex, a.ParentSlotName]] = true

	for ap in spine.AttachmentPoints:
		if occupied.has("%d:%s" % [-1, ap.Name]):
			continue
		if (int(ap.AllowedKinds) & kind_bit) != 0:
			return {"parent_index": -1, "slot_name": ap.Name}

	for i in range(attachments.size()):
		var child_part = _builder.Registry.Get(attachments[i].ChildPartId)
		if child_part == null:
			continue
		for ap in child_part.AttachmentPoints:
			if occupied.has("%d:%s" % [i, ap.Name]):
				continue
			if (int(ap.AllowedKinds) & kind_bit) != 0:
				return {"parent_index": i, "slot_name": ap.Name}

	return null
