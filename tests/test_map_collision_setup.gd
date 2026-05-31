extends SceneTree

var _failed := false

func _init() -> void:
	_check_map_collision_root()
	_check_player_floor_raycast_mask()
	_check_main_scene_navigation_mesh()
	quit(1 if _failed else 0)


func _check_map_collision_root() -> void:
	var map_scene := load("res://scenes/map_remake.tscn") as PackedScene
	if map_scene == null:
		_fail("map_remake.tscn could not be loaded")
		return

	var map_root := map_scene.instantiate()
	var map_body := map_root as StaticBody3D
	if map_body == null:
		_fail("map_remake root must be StaticBody3D so direct CollisionShape3D children are active")
	elif (map_body.collision_layer & 1) == 0:
		_fail("map_remake root must be on the world collision layer (layer 1)")

	var direct_collision_shapes := 0
	for child in map_root.get_children():
		if child is CollisionShape3D:
			direct_collision_shapes += 1

	if direct_collision_shapes == 0:
		_fail("map_remake root must have direct CollisionShape3D children")

	map_root.free()


func _check_player_floor_raycast_mask() -> void:
	var player_scene := load("res://addons/JehenoThirdPersonController/PlayerCharacter/player_character_scene.tscn") as PackedScene
	if player_scene == null:
		_fail("player_character_scene.tscn could not be loaded")
		return

	var player_root := player_scene.instantiate()
	var player_body := player_root as CharacterBody3D
	if player_body == null:
		_fail("PlayerCharacter root must be CharacterBody3D")
	elif (player_body.collision_mask & 1) == 0:
		_fail("PlayerCharacter must collide with the world layer (mask 1)")

	var floor_raycast := player_root.get_node_or_null("Raycasts/FloorRaycast") as RayCast3D
	if floor_raycast == null:
		_fail("Player FloorRaycast is missing")
	elif floor_raycast.collision_mask != 1:
		_fail("Player FloorRaycast must scan the world layer (mask 1)")

	player_root.free()


func _check_main_scene_navigation_mesh() -> void:
	var main_scene := load("res://scenes/main_scene.tscn") as PackedScene
	if main_scene == null:
		_fail("main_scene.tscn could not be loaded")
		return

	var main_root := main_scene.instantiate()
	var map_navigation := main_root.get_node_or_null("MapNavigationRegion3D") as NavigationRegion3D
	if map_navigation == null:
		_fail("MapNavigationRegion3D is missing")
		main_root.free()
		return

	var navigation_mesh := map_navigation.navigation_mesh
	if navigation_mesh == null:
		_fail("MapNavigationRegion3D must have a NavigationMesh")
		main_root.free()
		return

	if navigation_mesh.get_polygon_count() < 10:
		_fail("Map navigation mesh should cover the map with optimized region polygons")

	var required_points := {
		"player_spawn": Vector3(0.0, 0.0, -15.078612),
		"knifer_spawn": Vector3(46.866783, 0.0, -117.767914),
		"suicide_spawn": Vector3(38.6056, 0.0, -116.47827),
	}

	for point_name in required_points:
		if not _navmesh_contains_point_xz(navigation_mesh, required_points[point_name]):
			_fail("Map navigation mesh does not cover %s" % point_name)

	var legacy_floor := main_root.get_node_or_null("LegacyInvisibleFloor")
	if legacy_floor == null:
		_fail("LegacyInvisibleFloor is missing")
	elif legacy_floor is NavigationRegion3D:
		_fail("LegacyInvisibleFloor must not register a stale navigation island")

	main_root.free()


func _navmesh_contains_point_xz(navigation_mesh: NavigationMesh, point: Vector3) -> bool:
	var vertices := navigation_mesh.get_vertices()
	for polygon_index in range(navigation_mesh.get_polygon_count()):
		var polygon := navigation_mesh.get_polygon(polygon_index)
		for index in range(1, polygon.size() - 1):
			var a := vertices[polygon[0]]
			var b := vertices[polygon[index]]
			var c := vertices[polygon[index + 1]]
			if _point_in_triangle_xz(point, a, b, c):
				return true
	return false


func _point_in_triangle_xz(point: Vector3, a: Vector3, b: Vector3, c: Vector3) -> bool:
	var p2 := Vector2(point.x, point.z)
	var a2 := Vector2(a.x, a.z)
	var b2 := Vector2(b.x, b.z)
	var c2 := Vector2(c.x, c.z)
	var d1 := _triangle_sign(p2, a2, b2)
	var d2 := _triangle_sign(p2, b2, c2)
	var d3 := _triangle_sign(p2, c2, a2)
	var has_negative := d1 < -0.001 or d2 < -0.001 or d3 < -0.001
	var has_positive := d1 > 0.001 or d2 > 0.001 or d3 > 0.001
	return not (has_negative and has_positive)


func _triangle_sign(p1: Vector2, p2: Vector2, p3: Vector2) -> float:
	return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y)


func _fail(message: String) -> void:
	_failed = true
	push_error(message)
