extends SceneTree

var _failed := false

func _init() -> void:
	_check_boss_scene_contract()
	_check_boss_attack_assets_load()
	quit(1 if _failed else 0)


func _check_boss_scene_contract() -> void:
	var boss_scene := load("res://objects/characters/enemies/vacuum_cleaner/boss/boss.tscn") as PackedScene
	if boss_scene == null:
		_fail("boss.tscn could not be loaded")
		return

	var boss := boss_scene.instantiate()
	if boss == null:
		_fail("boss.tscn could not be instantiated")
		return

	if not boss.is_in_group("enemies"):
		_fail("Boss must stay in the enemies group")

	if boss.get_node_or_null("NavigationAgent3D") == null:
		_fail("Boss must have NavigationAgent3D for movement")

	if boss.get_node_or_null("ProjectileOrigin") == null:
		_fail("Boss must have ProjectileOrigin for laser attacks")

	if boss.get_node_or_null("FlameOrigin") == null:
		_fail("Boss must have FlameOrigin for flamethrower attacks")

	if boss.get_node_or_null("LightningOrigin") == null:
		_fail("Boss must have LightningOrigin for electric attacks")

	if boss.get("LaserWeaponScene") == null:
		_fail("Boss must export the player laser weapon scene")

	if boss.get("FlameScene") == null:
		_fail("Boss must export a loaded BinbunVFX flame scene")

	if boss.get("ZapScene") == null:
		_fail("Boss must export a loaded BinbunVFX electric zap scene")

	if float(boss.get("MaxHealth")) < 100.0:
		_fail("Boss must have boss-grade health")

	if float(boss.get("LaserDamagePerSecond")) <= 0.0:
		_fail("Boss laser attack must deal positive damage over time")

	if float(boss.get("FlameDamagePerSecond")) <= 0.0:
		_fail("Boss flame attack must deal positive damage over time")

	if float(boss.get("ShockDamage")) <= 0.0:
		_fail("Boss electric attack must deal positive damage")

	if float(boss.get("LaserRange")) <= float(boss.get("PreferredAttackDistance")):
		_fail("Boss laser range must be larger than preferred movement distance")

	if float(boss.get("ShockRange")) <= float(boss.get("PreferredAttackDistance")):
		_fail("Boss electric range must be larger than preferred movement distance")

	if float(boss.get("FlameRange")) <= 0.0:
		_fail("Boss flame range must be positive")

	boss.free()


func _check_boss_attack_assets_load() -> void:
	var laser_weapon_scene := load("res://objects/weapons/laser_weapon/laser_weapon.tscn") as PackedScene
	if laser_weapon_scene == null:
		_fail("Player laser weapon scene could not be loaded")
	else:
		var laser_weapon := laser_weapon_scene.instantiate()
		if laser_weapon == null:
			_fail("Player laser weapon scene could not be instantiated")
		else:
			if not laser_weapon.has_method("set_external_firing"):
				_fail("LaserWeapon must expose set_external_firing() for enemy AI control")
			if not laser_weapon.has_method("set_external_aim_direction"):
				_fail("LaserWeapon must expose set_external_aim_direction() for enemy AI control")
			laser_weapon.free()

	var flame_scene := load("res://addons/ExplosionFXFree/assets/FlameFXFree/assets/BinbunVFX_Vol2/FlameFX/effects/fire/vfx_basic_fire_01.tscn") as PackedScene
	if flame_scene == null:
		_fail("BinbunVFX flame scene could not be loaded")

	var zap_scene := load("res://addons/BinbunVFX/ElectricFXFree/assets/BinbunVFX_Vol2/ElectricFX/effects/zap/vfx_zap_lightning_01.tscn") as PackedScene
	if zap_scene == null:
		_fail("BinbunVFX electric zap scene could not be loaded")


func _fail(message: String) -> void:
	_failed = true
	push_error(message)
