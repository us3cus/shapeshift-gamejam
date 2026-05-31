extends SceneTree

var _failed := false

func _init() -> void:
	_check_electric_shared_scripts_do_not_hide_global_classes()
	_check_microwave_scene_contract()
	_check_electric_zap_asset_loads()
	quit(1 if _failed else 0)


func _check_electric_shared_scripts_do_not_hide_global_classes() -> void:
	var shared_script_paths := [
		"res://addons/BinbunVFX/ElectricFXFree/assets/BinbunVFX_Vol2/shared/script/VFXOmniLightBB.gd",
		"res://addons/BinbunVFX/ElectricFXFree/assets/BinbunVFX_Vol2/shared/script/VFXControllerBB.gd",
		"res://addons/BinbunVFX/ElectricFXFree/assets/BinbunVFX_Vol2/shared/script/VFXEmitterBB.gd",
	]

	for script_path in shared_script_paths:
		var script_text := FileAccess.get_file_as_string(script_path)
		if script_text.contains("class_name "):
			_fail("%s must not define class_name because ExplosionFXFree already owns those global VFX classes" % script_path)


func _check_microwave_scene_contract() -> void:
	var microwave_scene := load("res://objects/characters/enemies/vacuum_cleaner/microwave/microwave.tscn") as PackedScene
	if microwave_scene == null:
		_fail("microwave.tscn could not be loaded")
		return

	var microwave := microwave_scene.instantiate()
	if microwave == null:
		_fail("microwave.tscn could not be instantiated")
		return

	if not microwave.is_in_group("enemies"):
		_fail("Microwave must stay in the enemies group")

	if microwave.get_node_or_null("NavigationAgent3D") == null:
		_fail("Microwave must have NavigationAgent3D for movement")

	if microwave.get_node_or_null("LightningOrigin") == null:
		_fail("Microwave must have LightningOrigin for electric attacks")

	if microwave.get("ZapScene") == null:
		_fail("Microwave must export a loaded ElectricFXFree zap scene")

	if float(microwave.get("ShockDamage")) <= 0.0:
		_fail("Microwave electric attack must deal positive damage")

	if float(microwave.get("AttackRange")) <= float(microwave.get("PreferredAttackDistance")):
		_fail("Microwave attack range must be larger than preferred movement distance")

	microwave.free()


func _check_electric_zap_asset_loads() -> void:
	var zap_scene := load("res://addons/BinbunVFX/ElectricFXFree/assets/BinbunVFX_Vol2/ElectricFX/effects/zap/vfx_zap_lightning_01.tscn") as PackedScene
	if zap_scene == null:
		_fail("ElectricFXFree zap scene could not be loaded")
		return

	var zap := zap_scene.instantiate()
	if zap == null:
		_fail("ElectricFXFree zap scene could not be instantiated")
		return

	if not zap.has_method("play"):
		_fail("ElectricFXFree zap scene must expose play()")

	if not zap.has_signal("finished"):
		_fail("ElectricFXFree zap scene must emit finished for cleanup")

	zap.free()


func _fail(message: String) -> void:
	_failed = true
	push_error(message)
