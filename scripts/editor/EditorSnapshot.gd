extends Node

# Drives a one-shot screenshot of CreatureEditor for P4.2 visual proof.
# Loads the editor scene, waits a few frames so children populate, takes
# a viewport screenshot, exits.

const SNAPSHOT_PATH := "res://tests/snapshots/_actual/editor_p42.png"
const SETTLE_FRAMES := 12

var _frame := 0
var _shot := false


func _ready() -> void:
	# Programmatically instance the editor scene as a child of the SceneTree's root
	# so it has a full viewport to render into.
	var editor_scene := load("res://scenes/editor/CreatureEditor.tscn") as PackedScene
	var editor: Control = editor_scene.instantiate()
	add_child(editor)
	editor.set_anchors_preset(Control.PRESET_FULL_RECT)


func _process(_dt: float) -> void:
	_frame += 1
	if _frame < SETTLE_FRAMES or _shot:
		return
	_shot = true

	var img: Image = get_viewport().get_texture().get_image()
	if img == null:
		printerr("[snapshot] viewport image null")
		get_tree().quit(1)
		return

	var abs_path := ProjectSettings.globalize_path(SNAPSHOT_PATH)
	DirAccess.make_dir_recursive_absolute(abs_path.get_base_dir())
	var err := img.save_png(abs_path)
	if err == OK:
		print("[snapshot] wrote %s  %dx%d" % [abs_path, img.get_width(), img.get_height()])
		get_tree().quit(0)
	else:
		printerr("[snapshot] save_png failed: %d" % err)
		get_tree().quit(1)
