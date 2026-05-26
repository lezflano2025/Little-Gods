extends Node

# Renders the M3 P4 vertical slice: the bundled quadruped walking. Captures a
# horizontal strip of frames across one gait cycle (walking in place so each
# frame stays framed) via CreaturePreview.WalkTick, which runs the deterministic
# Locomotion solver (gait -> foot IK -> Pose). The strip is the human-review
# "plausible walk" artifact for the early go/no-go.

const CreaturePreviewScript := preload("res://src/mesh/CreaturePreview.cs")
const RecipeScript := preload("res://src/creature/Recipe.cs")
const AttachmentScript := preload("res://src/creature/Attachment.cs")
const PartRegistryScript := preload("res://src/creature/PartRegistry.cs")

const SNAPSHOT_PATH := "res://tests/snapshots/_actual/preview3d_walk.png"
const TIMES := [0.0, 0.2, 0.4, 0.6, 0.8]  # one cadence-1 cycle
const FRAMES_PER := 5

var _viewport: SubViewport
var _camera: Camera3D
var _preview
var _imgs: Array = []
var _idx := 0
var _wait := 0
var _done := false


func _ready() -> void:
	_viewport = SubViewport.new()
	_viewport.own_world_3d = true
	_viewport.transparent_bg = false
	_viewport.size = Vector2i(420, 460)
	_viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	_viewport.msaa_3d = Viewport.MSAA_2X
	add_child(_viewport)

	var env_holder := WorldEnvironment.new()
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.10, 0.11, 0.14)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.62, 0.62, 0.68)
	env.ambient_light_energy = 0.6
	env_holder.environment = env
	_viewport.add_child(env_holder)

	var key := DirectionalLight3D.new()
	key.rotation_degrees = Vector3(-50.0, -35.0, 0.0)
	key.light_energy = 1.2
	_viewport.add_child(key)

	# A ground plane so foot planting reads.
	var ground := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(8, 8)
	ground.mesh = plane
	var gmat := StandardMaterial3D.new()
	gmat.albedo_color = Color(0.16, 0.17, 0.20)
	ground.material_override = gmat
	_viewport.add_child(ground)

	_camera = Camera3D.new()
	_camera.current = true
	_viewport.add_child(_camera)

	_preview = CreaturePreviewScript.new()
	_viewport.add_child(_preview)

	_build_creature()
	_frame_camera()
	_apply(0)


func _process(_dt: float) -> void:
	if _done:
		return
	_wait += 1
	if _wait < FRAMES_PER:
		return
	_wait = 0

	_imgs.append(_viewport.get_texture().get_image())
	_idx += 1
	if _idx < TIMES.size():
		_apply(_idx)
		return

	_done = true
	var w: int = _imgs[0].get_width()
	var h: int = _imgs[0].get_height()
	var combo := Image.create(w * _imgs.size(), h, false, _imgs[0].get_format())
	for j in _imgs.size():
		combo.blit_rect(_imgs[j], Rect2i(0, 0, w, h), Vector2i(w * j, 0))

	var abs_path := ProjectSettings.globalize_path(SNAPSHOT_PATH)
	DirAccess.make_dir_recursive_absolute(abs_path.get_base_dir())
	var err := combo.save_png(abs_path)
	if err == OK:
		print("[walk] wrote %s  %dx%d (%d frames)" % [abs_path, combo.get_width(), combo.get_height(), _imgs.size()])
		get_tree().quit(0)
	else:
		printerr("[walk] save_png failed: %d" % err)
		get_tree().quit(1)


# Run one locomotion tick and walk in place (keep X/Z centred, let Y bob).
func _apply(i: int) -> void:
	var bp: Vector3 = _preview.WalkTick(TIMES[i])
	_preview.position = Vector3(0.0, bp.y, 0.0)


func _build_creature() -> void:
	var registry = PartRegistryScript.new()
	registry.LoadLibrary()
	var recipe = RecipeScript.new()
	recipe.SpinePartId = "spine_basic"
	_add(recipe, -1, "left_shoulder", "limb_walker")
	_add(recipe, -1, "right_shoulder", "limb_walker")
	_add(recipe, -1, "left_hip", "limb_walker")
	_add(recipe, -1, "right_hip", "limb_walker")
	_preview.SetCellSize(0.1)
	_preview.Rebuild(recipe, registry)


func _frame_camera() -> void:
	# Side-ish 3/4 view: the gait steps along Z (read as horizontal), body at ~y=1.
	var target := Vector3(0.0, 0.55, 0.0)
	var distance := 3.6
	var yaw := 1.32
	var pitch := 0.16
	var cp := cos(pitch)
	var dir := Vector3(cp * sin(yaw), sin(pitch), cp * cos(yaw))
	_camera.position = target + dir * distance
	_camera.look_at(target, Vector3.UP)


func _add(recipe: Resource, parent: int, slot: String, child_id: String) -> int:
	var a: Resource = AttachmentScript.new()
	a.ParentPartIndex = parent
	a.ParentSlotName = slot
	a.ChildPartId = child_id
	a.LocalTransform = Transform3D.IDENTITY
	recipe.Attachments.append(a)
	return recipe.Attachments.size() - 1
