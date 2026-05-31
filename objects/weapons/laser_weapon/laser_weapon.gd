extends Node3D

@export var beam_scene: PackedScene = preload("res://addons/BinbunVFX/beam_vfx/effects/base/base_beam_vfx.tscn")
@export var fire_action: StringName = &"play_char_fire_laser_action"
@export var damage_per_second: float = 10.0
@export var max_range: float = 30.0
@export_flags_3d_physics var collision_mask: int = 5
@export var use_input: bool = true
@export var aim_from_camera: bool = true
@export var camera_path: NodePath = ^"../CameraHolder/SpringArm3D/Camera3D"
@export var use_external_aim: bool = false

var _beam: Node3D
var _beam_end: Node3D
var _camera: Camera3D
var _is_firing := false
var _external_firing := false
var _external_aim_direction := Vector3.ZERO
var _external_exclusion_root: Node

func _ready() -> void:
	_ensure_fire_action_registered()
	_camera = get_node_or_null(camera_path) as Camera3D
	_setup_beam()
	_set_beam_active(false)

func _physics_process(delta: float) -> void:
	_is_firing = _external_firing
	if use_input:
		_is_firing = _is_firing or Input.is_action_pressed(fire_action)

	_update_aim()
	_set_beam_active(_is_firing)

	if _is_firing:
		_fire_laser(delta)

func set_external_firing(active: bool) -> void:
	_external_firing = active
	if not active:
		_set_beam_active(false)

func set_external_aim_direction(direction: Vector3) -> void:
	_external_aim_direction = direction.normalized() if direction.length_squared() > 0.0001 else Vector3.ZERO

func set_external_exclusion_root(root: Node) -> void:
	_external_exclusion_root = root

func _setup_beam() -> void:
	_beam_end = Node3D.new()
	_beam_end.name = "LaserBeamEnd"
	_beam_end.top_level = true
	add_child(_beam_end)

	_beam = beam_scene.instantiate() as Node3D
	_beam.name = "LaserBeamVFX"
	_beam.top_level = true
	add_child(_beam)

	if _beam.has_method("_setup_effect"):
		_beam.call("_setup_effect")

	_beam.global_position = global_position
	_beam_end.global_position = global_position + _get_aim_direction() * max_range

	_beam.set("end_point", _beam_end)
	_beam.set("beam_length", max_range)
	_beam.set("primary_color", Color(0.2, 0.9, 1.0, 1.0))
	_beam.set("secondary_color", Color(0.05, 0.45, 1.0, 1.0))
	_beam.set("tertiary_color", Color(0.0, 0.08, 0.18, 1.0))
	_beam.set("beam_radius", 0.08)
	_beam.set("start_radius", 0.14)
	_beam.set("emission", 5.0)
	_beam.set("pulse_strength", 0.12)

func _fire_laser(delta: float) -> void:
	var origin := global_position
	var direction := _get_aim_direction()
	var target_position := origin + direction * max_range
	var query := PhysicsRayQueryParameters3D.create(origin, target_position)
	query.collision_mask = collision_mask
	query.collide_with_areas = true
	query.collide_with_bodies = true
	query.exclude = _build_exclusion_rids()

	var result := get_world_3d().direct_space_state.intersect_ray(query)
	if not result.is_empty():
		target_position = result["position"]
		_apply_damage(result["collider"], damage_per_second * delta)

	_beam.global_position = origin
	_beam_end.global_position = target_position

func _update_aim() -> void:
	var direction := _get_aim_direction()
	var target := global_position + direction
	var up := Vector3.UP

	if abs(direction.dot(up)) > 0.98:
		up = Vector3.RIGHT

	look_at(target, up)

func _get_aim_direction() -> Vector3:
	if use_external_aim and _external_aim_direction.length_squared() > 0.0001:
		return _external_aim_direction.normalized()

	if aim_from_camera:
		if not is_instance_valid(_camera):
			_camera = get_viewport().get_camera_3d()

		if is_instance_valid(_camera):
			return -_camera.global_transform.basis.z.normalized()

	return -global_transform.basis.z.normalized()

func _apply_damage(collider: Object, amount: float) -> void:
	var receiver := _find_damage_receiver(collider)
	if receiver == null:
		return

	if receiver.has_method("ApplyDamage"):
		receiver.call("ApplyDamage", amount, self)
	elif receiver.has_method("apply_damage"):
		receiver.call("apply_damage", amount, self)

func _find_damage_receiver(node: Object) -> Node:
	var current := node as Node

	while current != null:
		if current.has_method("ApplyDamage") or current.has_method("apply_damage"):
			return current

		current = current.get_parent()

	return null

func _set_beam_active(active: bool) -> void:
	if _beam == null:
		return

	_beam.visible = active
	_beam.set("open_amount", 1.0 if active else 0.0)
	_beam.set("start_emitting", active)
	_beam.set("end_emitting", active)
	_beam.set("audio_playing", active)

func _build_exclusion_rids() -> Array[RID]:
	var exclusions: Array[RID] = []
	var current := _external_exclusion_root if is_instance_valid(_external_exclusion_root) else get_parent()

	while current != null:
		if current is CollisionObject3D:
			exclusions.append((current as CollisionObject3D).get_rid())

		current = current.get_parent()

	return exclusions

func _ensure_fire_action_registered() -> void:
	if InputMap.has_action(fire_action):
		return

	InputMap.add_action(fire_action)
	var mouse_event := InputEventMouseButton.new()
	mouse_event.button_index = MouseButton.MOUSE_BUTTON_LEFT
	InputMap.action_add_event(fire_action, mouse_event)
