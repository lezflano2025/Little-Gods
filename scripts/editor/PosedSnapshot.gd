extends Node

# One-shot screenshot proving M3 P0 skin deformation. Builds a creature, grabs
# the REST render, bends the first limb's knee via CreaturePreview.BendFirstKnee
# (which runs through ApplyPose on the Skeleton3D), grabs the BENT render, and
# saves them side by side (rest | bent) so the skin deformation is obvious.
# Self-contained viewport/camera/light — no dependency on the editor scene.

const CreaturePreviewScript := preload("res://src/mesh/CreaturePreview.cs")
const RecipeScript := preload("res://src/creature/Recipe.cs")
const AttachmentScript := preload("res://src/creature/Attachment.cs")
const PartRegistryScript := preload("res://src/creature/PartRegistry.cs")

const SNAPSHOT_PATH := "res://tests/snapshots/_actual/preview3d_posed.png"
const REST_FRAME := 14
const BEND_FRAME := 16
const SHOT_FRAME := 34
const BEND_RADIANS := 0.95  # ~54 degrees

var _viewport: SubViewport
var _camera: Camera3D
var _preview
var _frame := 0
var _rest_img: Image = null
var _bent := false
var _shot := false


func _ready() -> void:
	_viewport = SubViewport.new()
	_viewport.own_world_3d = true
	_viewport.transparent_bg = false
	_viewport.size = Vector2i(560, 540)
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

	_camera = Camera3D.new()
	_camera.current = true
	_viewport.add_child(_camera)

	_preview = CreaturePreviewScript.new()
	_viewport.add_child(_preview)

	_build_creature()
	_frame_camera()


func _process(_dt: float) -> void:
	_frame += 1

	if _frame == REST_FRAME and _rest_img == null:
		_rest_img = _grab()
	if _frame == BEND_FRAME and not _bent:
		_preview.BendFirstKnee(BEND_RADIANS)
		_bent = true
	if _frame < SHOT_FRAME or _shot:
		return
	_shot = true

	var bent_img := _grab()
	if _rest_img == null or bent_img == null:
		printerr("[posed] viewport image null")
		get_tree().quit(1)
		return

	# Composite rest | bent side by side.
	var w := bent_img.get_width()
	var h := bent_img.get_height()
	var combo := Image.create(w * 2, h, false, bent_img.get_format())
	combo.blit_rect(_rest_img, Rect2i(0, 0, w, h), Vector2i(0, 0))
	combo.blit_rect(bent_img, Rect2i(0, 0, w, h), Vector2i(w, 0))

	var abs_path := ProjectSettings.globalize_path(SNAPSHOT_PATH)
	DirAccess.make_dir_recursive_absolute(abs_path.get_base_dir())
	var err := combo.save_png(abs_path)
	if err == OK:
		print("[posed] wrote %s  %dx%d (rest | bent)" % [abs_path, combo.get_width(), combo.get_height()])
		get_tree().quit(0)
	else:
		printerr("[posed] save_png failed: %d" % err)
		get_tree().quit(1)


func _grab() -> Image:
	return _viewport.get_texture().get_image()


func _build_creature() -> void:
	var registry = PartRegistryScript.new()
	registry.LoadLibrary()
	var recipe = RecipeScript.new()
	recipe.SpinePartId = "spine_basic"
	# One prominent limb so the knee bend dominates the silhouette.
	_add(recipe, -1, "left_hip", "limb_runner")
	_preview.SetCellSize(0.08)
	_preview.Rebuild(recipe, registry)


func _frame_camera() -> void:
	var aabb: AABB = _preview.GetMeshAabb()
	var target := Vector3.ZERO
	var distance := 5.0
	if aabb.size.length() > 0.01:
		target = aabb.get_center()
		distance = clampf(aabb.size.length() * 1.7, 2.5, 25.0)
	# The first limb's knee bends about world +Z, so the bend lives in the X-Y
	# plane. Look down +Z (slightly raised) so the knee reads as a clean L.
	var yaw := 0.0
	var pitch := 0.22
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
