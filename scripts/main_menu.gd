extends Control

const MAIN_SCENE_PATH := "res://scenes/main_scene.tscn"

@onready var start_button: Button = $buttons/start_button
@onready var settings_button: Button = $buttons/settings_button
@onready var exit_button: Button = $buttons/exit_button

var settings_dialog: AcceptDialog
var fullscreen_check_box: CheckBox
var master_volume_slider: HSlider


func _ready() -> void:
	start_button.pressed.connect(_on_start_button_pressed)
	settings_button.pressed.connect(_on_settings_button_pressed)
	exit_button.pressed.connect(_on_exit_button_pressed)

	_create_settings_dialog()


func _on_start_button_pressed() -> void:
	var error := get_tree().change_scene_to_file(MAIN_SCENE_PATH)
	if error != OK:
		push_error("Failed to load main scene: %s" % MAIN_SCENE_PATH)


func _on_settings_button_pressed() -> void:
	settings_dialog.popup_centered()


func _on_exit_button_pressed() -> void:
	get_tree().quit()


func _create_settings_dialog() -> void:
	settings_dialog = AcceptDialog.new()
	settings_dialog.title = "Настройки"
	settings_dialog.exclusive = true
	settings_dialog.min_size = Vector2i(360, 180)
	settings_dialog.ok_button_text = "Закрыть"
	add_child(settings_dialog)

	var content := VBoxContainer.new()
	content.add_theme_constant_override("separation", 12)
	settings_dialog.add_child(content)

	fullscreen_check_box = CheckBox.new()
	fullscreen_check_box.text = "Полноэкранный режим"
	fullscreen_check_box.button_pressed = DisplayServer.window_get_mode() == DisplayServer.WINDOW_MODE_FULLSCREEN
	fullscreen_check_box.toggled.connect(_on_fullscreen_toggled)
	content.add_child(fullscreen_check_box)

	var volume_label := Label.new()
	volume_label.text = "Громкость"
	content.add_child(volume_label)

	master_volume_slider = HSlider.new()
	master_volume_slider.min_value = 0.0
	master_volume_slider.max_value = 1.0
	master_volume_slider.step = 0.01
	master_volume_slider.value = db_to_linear(AudioServer.get_bus_volume_db(AudioServer.get_bus_index("Master")))
	master_volume_slider.value_changed.connect(_on_master_volume_changed)
	content.add_child(master_volume_slider)


func _on_fullscreen_toggled(enabled: bool) -> void:
	var mode := DisplayServer.WINDOW_MODE_FULLSCREEN if enabled else DisplayServer.WINDOW_MODE_WINDOWED
	DisplayServer.window_set_mode(mode)


func _on_master_volume_changed(value: float) -> void:
	var master_bus := AudioServer.get_bus_index("Master")
	var volume_db := -80.0 if value <= 0.0 else linear_to_db(value)
	AudioServer.set_bus_volume_db(master_bus, volume_db)
