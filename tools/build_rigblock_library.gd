#!/usr/bin/env -S godot --headless -s
extends SceneTree

# tools/build_rigblock_library.gd
# Generates the 9-part M1 Rigblock library under res://assets/rigblock/.
# Run with:  godot --headless --path . -s res://tools/build_rigblock_library.gd
#
# Hand-authoring .tres files is error-prone because Godot regenerates
# ext_resource ids on every save anyway. We programmatically build the
# Part resources and let Godot serialise them.

const PART_SCRIPT := "res://src/creature/Part.cs"
const AP_SCRIPT := "res://src/creature/AttachmentPoint.cs"
const OUT_DIR := "res://assets/rigblock/"

# Mirror of the C# PartKind enum.
const KIND_SPINE := 0
const KIND_LIMB := 1
const KIND_HEAD := 2
const KIND_MOUTH := 3
const KIND_OTHER := 4

# Mirror of the C# PartKindMask flags enum.
const MASK_NONE := 0
const MASK_SPINE := 1
const MASK_LIMB := 2
const MASK_HEAD := 4
const MASK_MOUTH := 8
const MASK_OTHER := 16
const MASK_ALL := 31

var _part_script: Script
var _ap_script: Script


func _init() -> void:
	_part_script = load(PART_SCRIPT)
	_ap_script = load(AP_SCRIPT)
	if _part_script == null or _ap_script == null:
		printerr("Cannot load Part/AttachmentPoint scripts")
		quit(1)
		return

	var out_abs := ProjectSettings.globalize_path(OUT_DIR)
	DirAccess.make_dir_recursive_absolute(out_abs)

	# Spine - 6 named slots: head, tail, two shoulders, two hips.
	# Editor can add more dynamic slots; these are the canonical anchors.
	save_part(build_spine_basic(), "spine_basic.tres")

	# Limbs - leaves for M1 (no chained tips). M2+ may add a "tip" slot.
	# Args: id, display, footprint, bone_len, radius_start, radius_end.
	save_part(build_limb("limb_walker", "Walking Limb", Vector2(0.4, 1.2), 1.2, 0.22, 0.12), "limb_walker.tres")
	save_part(build_limb("limb_runner", "Running Limb", Vector2(0.4, 1.6), 1.6, 0.18, 0.08), "limb_runner.tres")
	save_part(build_limb("limb_wing", "Wing", Vector2(1.5, 0.8), 0.8, 0.30, 0.12), "limb_wing.tres")
	save_part(build_limb("limb_tail", "Tail", Vector2(0.3, 1.4), 1.4, 0.25, 0.05), "limb_tail.tres")

	# Heads - one outward-facing "jaw" slot that accepts mouths.
	save_part(build_head("head_predator", "Predator Head", 0.7, 0.40, 0.34), "head_predator.tres")
	save_part(build_head("head_herbivore", "Herbivore Head", 0.7, 0.40, 0.38), "head_herbivore.tres")

	# Mouths - leaves.
	save_part(build_mouth("mouth_beak", "Beak", Vector2(0.5, 0.3), 0.3, 0.18, 0.04), "mouth_beak.tres")
	save_part(build_mouth("mouth_fang", "Fangs", Vector2(0.6, 0.4), 0.4, 0.25, 0.10), "mouth_fang.tres")

	print("Built 9 parts in %s" % OUT_DIR)
	quit(0)


func build_spine_basic() -> Resource:
	var part: Resource = _part_script.new()
	part.Id = "spine_basic"
	part.DisplayName = "Basic Spine"
	part.Kind = KIND_SPINE
	part.Footprint2D = Vector2(2.0, 0.6)
	# Centred body bone running z=[-1, 1], slightly tapered toward the tail.
	part.BoneLength = 2.0
	part.RadiusStart = 0.45
	part.RadiusEnd = 0.35
	part.PaintRegions = PackedStringArray(["back", "belly"])
	part.AttachmentPoints = [
		make_ap("head",           Vector3(0, 0, 1),     Vector3(0, 0, 1),  MASK_HEAD),
		make_ap("tail",           Vector3(0, 0, -1),    Vector3(0, 0, -1), MASK_LIMB),
		make_ap("left_shoulder",  Vector3(-0.3, 0, 0.6),  Vector3(-1, 0, 0), MASK_LIMB),
		make_ap("right_shoulder", Vector3(0.3, 0, 0.6),   Vector3(1, 0, 0),  MASK_LIMB),
		make_ap("left_hip",       Vector3(-0.3, 0, -0.4), Vector3(-1, 0, 0), MASK_LIMB),
		make_ap("right_hip",      Vector3(0.3, 0, -0.4),  Vector3(1, 0, 0),  MASK_LIMB),
	]
	return part


func build_limb(id: String, display: String, footprint: Vector2,
		bone_len: float, r_start: float, r_end: float) -> Resource:
	var part: Resource = _part_script.new()
	part.Id = id
	part.DisplayName = display
	part.Kind = KIND_LIMB
	part.Footprint2D = footprint
	part.BoneLength = bone_len
	part.RadiusStart = r_start
	part.RadiusEnd = r_end
	part.AttachmentPoints = []
	return part


func build_head(id: String, display: String,
		bone_len: float, r_start: float, r_end: float) -> Resource:
	var part: Resource = _part_script.new()
	part.Id = id
	part.DisplayName = display
	part.Kind = KIND_HEAD
	part.Footprint2D = Vector2(0.8, 0.7)
	part.BoneLength = bone_len
	part.RadiusStart = r_start
	part.RadiusEnd = r_end
	part.AttachmentPoints = [
		make_ap("jaw", Vector3(0, -0.1, 0.4), Vector3(0, 0, 1), MASK_MOUTH),
	]
	return part


func build_mouth(id: String, display: String, footprint: Vector2,
		bone_len: float, r_start: float, r_end: float) -> Resource:
	var part: Resource = _part_script.new()
	part.Id = id
	part.DisplayName = display
	part.Kind = KIND_MOUTH
	part.Footprint2D = footprint
	part.BoneLength = bone_len
	part.RadiusStart = r_start
	part.RadiusEnd = r_end
	part.AttachmentPoints = []
	return part


func make_ap(name: String, pos: Vector3, normal: Vector3, allowed: int) -> Resource:
	var ap: Resource = _ap_script.new()
	ap.Name = name
	ap.LocalPosition = pos
	ap.LocalNormal = normal
	ap.AllowedKinds = allowed
	return ap


func save_part(part: Resource, filename: String) -> void:
	var path: String = OUT_DIR + filename
	var err: int = ResourceSaver.save(part, path)
	if err == OK:
		print("  wrote %s" % path)
	else:
		printerr("  FAIL %s: %d" % [path, err])
