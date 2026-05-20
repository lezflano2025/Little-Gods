extends MarginContainer

# Properties inspector sub-panel for CreatureEditor. Mutates the selected
# Attachment's Morph (Stretch / Twist / PaintTint) via debounced signals.
# Contract: docs/m1-p4-contract.md "Properties → CreatureEditor".

signal morph_changed(attachment_index: int, stretch: Vector3, twist: float, paint_tint: Color)
signal attachment_delete_requested(attachment_index: int)
signal attachment_inspector_close_requested

const STRETCH_MIN: float = 0.1
const STRETCH_MAX: float = 3.0
const TWIST_DEG_MIN: float = -180.0
const TWIST_DEG_MAX: float = 180.0
const DEBOUNCE_SECONDS: float = 0.2

@onready var _empty_label: Label = $EmptyLabel
@onready var _content: VBoxContainer = $Content
@onready var _header: Label = $Content/Header
@onready var _stretch_x_slider: HSlider = $Content/StretchSection/StretchXRow/StretchXSlider
@onready var _stretch_y_slider: HSlider = $Content/StretchSection/StretchYRow/StretchYSlider
@onready var _stretch_z_slider: HSlider = $Content/StretchSection/StretchZRow/StretchZSlider
@onready var _stretch_x_value: Label = $Content/StretchSection/StretchXRow/StretchXValue
@onready var _stretch_y_value: Label = $Content/StretchSection/StretchYRow/StretchYValue
@onready var _stretch_z_value: Label = $Content/StretchSection/StretchZRow/StretchZValue
@onready var _twist_slider: HSlider = $Content/TwistSection/TwistRow/TwistSlider
@onready var _twist_value: Label = $Content/TwistSection/TwistRow/TwistValue
@onready var _paint_picker: ColorPickerButton = $Content/PaintSection/PaintPicker
@onready var _delete_button: Button = $Content/DeleteButton
@onready var _debounce_timer: Timer = $DebounceTimer

var _builder                       # C# RecipeBuilder instance
var _selected_index: int = -1
var _suppress_emit: bool = false   # true while populating from data


func _ready() -> void:
	_stretch_x_slider.value_changed.connect(_on_stretch_x_changed)
	_stretch_y_slider.value_changed.connect(_on_stretch_y_changed)
	_stretch_z_slider.value_changed.connect(_on_stretch_z_changed)
	_twist_slider.value_changed.connect(_on_twist_changed)

	_stretch_x_slider.drag_ended.connect(_on_slider_drag_ended)
	_stretch_y_slider.drag_ended.connect(_on_slider_drag_ended)
	_stretch_z_slider.drag_ended.connect(_on_slider_drag_ended)
	_twist_slider.drag_ended.connect(_on_slider_drag_ended)

	_paint_picker.color_changed.connect(_on_paint_changed)
	_delete_button.pressed.connect(_on_delete_pressed)
	_debounce_timer.timeout.connect(_emit_morph_changed)

	_show_empty_state()


# ---------- contract methods ----------

func set_recipe_builder(builder) -> void:
	_builder = builder


func refresh() -> void:
	# Recipe changed externally; re-populate from current selection.
	if _selected_index < 0:
		_show_empty_state()
		return
	if _builder == null:
		_show_empty_state()
		return
	var attachments = _builder.Recipe.Attachments
	if _selected_index >= attachments.size():
		# Selection got pruned by a deletion; defer to controller for reselect.
		_selected_index = -1
		_show_empty_state()
		return
	_populate_from_attachment(_selected_index)


func set_selection(index: int) -> void:
	_selected_index = index
	if index < 0 or _builder == null:
		_show_empty_state()
		return
	var attachments = _builder.Recipe.Attachments
	if index >= attachments.size():
		_show_empty_state()
		return
	_populate_from_attachment(index)


func set_symmetry(_enabled: bool) -> void:
	# Properties panel does not care about symmetry; accept the call silently.
	pass


# ---------- view state ----------

func _show_empty_state() -> void:
	_empty_label.visible = true
	_content.visible = false


func _populate_from_attachment(index: int) -> void:
	var att = _builder.Recipe.Attachments[index]

	_header.text = _format_header(att)

	var stretch: Vector3 = Vector3.ONE
	var twist_rad: float = 0.0
	var tint: Color = Color.WHITE

	if att.MorphIndex >= 0 and att.MorphIndex < _builder.Recipe.Morphs.size():
		var morph = _builder.Recipe.Morphs[att.MorphIndex]
		stretch = morph.Stretch
		twist_rad = morph.Twist
		tint = morph.PaintTint

	_suppress_emit = true
	_stretch_x_slider.value = stretch.x
	_stretch_y_slider.value = stretch.y
	_stretch_z_slider.value = stretch.z
	_twist_slider.value = rad_to_deg(twist_rad)
	_paint_picker.color = tint
	_suppress_emit = false

	_update_stretch_readouts()
	_update_twist_readout()

	_empty_label.visible = false
	_content.visible = true


func _format_header(att) -> String:
	var part_name: String = att.ChildPartId
	if has_node("/root/PartRegistry"):
		var registry = get_node("/root/PartRegistry")
		var part = registry.Get(att.ChildPartId)
		if part != null and part.DisplayName != "":
			part_name = part.DisplayName
	var slot: String = att.ParentSlotName if att.ParentSlotName != "" else "(spine)"
	return "%s on %s" % [part_name, slot]


# ---------- slider handlers ----------

func _on_stretch_x_changed(_v: float) -> void:
	_update_stretch_readouts()


func _on_stretch_y_changed(_v: float) -> void:
	_update_stretch_readouts()


func _on_stretch_z_changed(_v: float) -> void:
	_update_stretch_readouts()


func _on_twist_changed(_v: float) -> void:
	_update_twist_readout()


func _update_stretch_readouts() -> void:
	_stretch_x_value.text = "%.2f" % _stretch_x_slider.value
	_stretch_y_value.text = "%.2f" % _stretch_y_slider.value
	_stretch_z_value.text = "%.2f" % _stretch_z_slider.value
	_schedule_emit()


func _update_twist_readout() -> void:
	_twist_value.text = "%d°" % int(round(_twist_slider.value))
	_schedule_emit()


func _on_slider_drag_ended(value_changed: bool) -> void:
	# Drag released — flush immediately if there's a pending edit.
	if value_changed and not _suppress_emit:
		_debounce_timer.stop()
		_emit_morph_changed()


func _on_paint_changed(_color: Color) -> void:
	_schedule_emit()


func _schedule_emit() -> void:
	if _suppress_emit:
		return
	if _selected_index < 0:
		return
	_debounce_timer.stop()
	_debounce_timer.start(DEBOUNCE_SECONDS)


func _emit_morph_changed() -> void:
	if _selected_index < 0:
		return
	var stretch := Vector3(
		_stretch_x_slider.value,
		_stretch_y_slider.value,
		_stretch_z_slider.value
	)
	var twist_rad: float = deg_to_rad(_twist_slider.value)
	var tint: Color = _paint_picker.color
	morph_changed.emit(_selected_index, stretch, twist_rad, tint)


# ---------- delete ----------

func _on_delete_pressed() -> void:
	if _selected_index < 0:
		return
	# Per contract: Workspace also emits this signal. The controller is
	# responsible for handling it idempotently regardless of source.
	attachment_delete_requested.emit(_selected_index)
