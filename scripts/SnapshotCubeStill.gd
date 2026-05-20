extends Node

# tests/snapshots/cube_still.tscn - deterministic snapshot baseline.
# Renders to an off-screen SubViewport (so it works under --headless),
# captures one frame as PNG, exits.

const RENDER_SIZE: Vector2i = Vector2i(640, 480)
const RENDER_FRAMES: int = 8
const OUTPUT_PATH: String = "res://tests/snapshots/_actual/cube_still.png"

var _vp: SubViewport
var _frame: int = 0
var _captured: bool = false


func _ready() -> void:
	seed(42)

	_vp = SubViewport.new()
	_vp.size = RENDER_SIZE
	_vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	_vp.transparent_bg = false
	add_child(_vp)

	# World root inside the SubViewport
	var world := Node3D.new()
	_vp.add_child(world)

	# Ground
	var ground := MeshInstance3D.new()
	var gmesh := BoxMesh.new()
	gmesh.size = Vector3(10, 0.2, 10)
	ground.mesh = gmesh
	ground.position = Vector3(0, -0.1, 0)
	world.add_child(ground)

	# Cube
	var cube := MeshInstance3D.new()
	cube.mesh = BoxMesh.new()
	cube.position = Vector3(0, 0.5, 0)
	world.add_child(cube)

	# Camera (current must be true for SubViewport)
	var cam := Camera3D.new()
	cam.current = true
	world.add_child(cam)
	cam.global_position = Vector3(3, 2.5, 3)
	cam.look_at(Vector3.ZERO, Vector3.UP)

	# Directional light
	var light := DirectionalLight3D.new()
	light.rotation_degrees = Vector3(-50, -40, 0)
	light.light_energy = 1.2
	world.add_child(light)

	# Environment - flat sky color for determinism
	var env := WorldEnvironment.new()
	var e := Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0.08, 0.08, 0.10)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.3, 0.3, 0.35)
	e.ambient_light_energy = 0.5
	env.environment = e
	world.add_child(env)

	print("[snapshot] scene ready (%dx%d, waiting %d frames)" % [RENDER_SIZE.x, RENDER_SIZE.y, RENDER_FRAMES])


func _process(_delta: float) -> void:
	_frame += 1
	if _frame < RENDER_FRAMES or _captured:
		return
	_captured = true

	var tex := _vp.get_texture()
	if tex == null:
		printerr("[snapshot] SubViewport texture null")
		get_tree().quit(2)
		return

	var img: Image = tex.get_image()
	if img == null:
		printerr("[snapshot] SubViewport image null (renderer unavailable?)")
		get_tree().quit(2)
		return

	var abs_path := ProjectSettings.globalize_path(OUTPUT_PATH)
	DirAccess.make_dir_recursive_absolute(abs_path.get_base_dir())
	var err := img.save_png(abs_path)
	if err == OK:
		print("[snapshot] wrote %s  %dx%d  %d bytes" % [abs_path, img.get_width(), img.get_height(), FileAccess.get_file_as_bytes(abs_path).size()])
		get_tree().quit(0)
	else:
		printerr("[snapshot] save_png failed (err=%d)" % err)
		get_tree().quit(1)
