extends Node3D

# tests/headless/cube_fall.tscn — M0 smoke test.
# Drops a RigidBody3D cube onto a static floor, asserts it fell,
# exits 0 on PASS, 1 on FAIL.

const MAX_FRAMES: int = 180   # ~3 s at 60 Hz, well past landing
const MIN_DROP: float = 1.0   # metres

var _cube: RigidBody3D
var _start_y: float = 0.0
var _frames: int = 0


func _ready() -> void:
	# Ground
	var ground := StaticBody3D.new()
	ground.position = Vector3(0, -0.5, 0)
	add_child(ground)
	var ground_col := CollisionShape3D.new()
	var ground_box := BoxShape3D.new()
	ground_box.size = Vector3(10, 1, 10)
	ground_col.shape = ground_box
	ground.add_child(ground_col)

	# Cube
	_cube = RigidBody3D.new()
	_cube.position = Vector3(0, 5, 0)
	add_child(_cube)
	var cube_col := CollisionShape3D.new()
	cube_col.shape = BoxShape3D.new()
	_cube.add_child(cube_col)
	var cube_mesh := MeshInstance3D.new()
	cube_mesh.mesh = BoxMesh.new()
	_cube.add_child(cube_mesh)

	# Camera - not strictly needed headless, but keeps the scene snapshot-ready.
	var cam := Camera3D.new()
	add_child(cam)
	cam.global_position = Vector3(0, 3, 8)
	cam.look_at(Vector3.ZERO, Vector3.UP)

	# Light
	var light := DirectionalLight3D.new()
	light.rotation_degrees = Vector3(-45, -45, 0)
	add_child(light)

	_start_y = _cube.position.y
	print("[cube_fall] start_y=%.3f" % _start_y)


func _physics_process(_delta: float) -> void:
	_frames += 1
	if _frames < MAX_FRAMES:
		return

	var drop: float = _start_y - _cube.position.y
	print("[cube_fall] frame=%d cube_y=%.3f drop=%.3f" % [_frames, _cube.position.y, drop])

	if drop >= MIN_DROP:
		print("[cube_fall] PASS")
		get_tree().quit(0)
	else:
		printerr("[cube_fall] FAIL: drop=%.3f < %.3f" % [drop, MIN_DROP])
		get_tree().quit(1)
