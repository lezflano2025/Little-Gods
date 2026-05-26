extends Node

# Drives a one-shot screenshot of the M2 3D preview for visual proof.
# Instances the real CreatureEditor, injects a 7-attachment creature into its
# recipe, broadcasts a refresh so the Preview3D pane meshes it, waits for the
# SubViewport to render, screenshots the whole editor, and exits.

const SNAPSHOT_PATH := "res://tests/snapshots/_actual/preview3d_creature.png"
const RecipeScript := preload("res://src/creature/Recipe.cs")
const AttachmentScript := preload("res://src/creature/Attachment.cs")
const SETTLE_FRAMES := 30

var _editor: Control
var _frame := 0
var _injected := false
var _shot := false


func _ready() -> void:
	var scene := load("res://scenes/editor/CreatureEditor.tscn") as PackedScene
	_editor = scene.instantiate()
	add_child(_editor)
	_editor.set_anchors_preset(Control.PRESET_FULL_RECT)


func _process(_dt: float) -> void:
	_frame += 1
	# Let the editor _ready + panel wiring settle, then inject a full creature.
	if _frame == 5 and not _injected:
		_inject_creature()
		_injected = true
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


func _inject_creature() -> void:
	var recipe: Resource = RecipeScript.new()
	recipe.SpinePartId = "spine_basic"
	_add(recipe, -1, "left_shoulder", "limb_walker")
	_add(recipe, -1, "right_shoulder", "limb_walker")
	_add(recipe, -1, "left_hip", "limb_walker")
	_add(recipe, -1, "right_hip", "limb_walker")
	_add(recipe, -1, "tail", "limb_tail")
	var head_idx := _add(recipe, -1, "head", "head_predator")
	_add(recipe, head_idx, "jaw", "mouth_fang")
	_editor.Recipe = recipe
	_editor._broadcast_refresh()


func _add(recipe: Resource, parent: int, slot: String, child_id: String) -> int:
	var a: Resource = AttachmentScript.new()
	a.ParentPartIndex = parent
	a.ParentSlotName = slot
	a.ChildPartId = child_id
	a.LocalTransform = Transform3D.IDENTITY
	recipe.Attachments.append(a)
	return recipe.Attachments.size() - 1
