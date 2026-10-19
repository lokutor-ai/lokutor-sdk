extends Node3D
## Brom, a stylised dwarf built from primitives: he breathes, blinks, looks at the camera, opens his
## jaw (and beard) with his voice, nods while he talks, and hands things over.

## How open his mouth is, 0..1 (set every frame from LokutorAgent.mouth_open).
var mouth_open := 0.0
## Where to look (usually the camera).
var look_target := Vector3(0, 1.6, 2)
## The node his voice comes from: put the LokutorAgent here.
var voice_anchor: Node3D

const SKIN := Color("e3a985")
const GINGER := Color("c25a26")
const GINGER_DARK := Color("8e3c17")
const TUNIC := Color("2f5a4f")

var _body: Node3D
var _head: Node3D
var _jaw: Node3D
var _mouth: MeshInstance3D
var _eyes: Node3D
var _arm_l: Node3D
var _arm_r: Node3D
var _hand_l: Node3D
var _held: Node3D
var _t := 0.0
var _next_blink := 2.5
var _blink := 0.0
var _gesture := 0.0
var _talk := 0.0


func _ready() -> void:
	_build()


## Holds out `item_name` for a moment, as if handing it over.
func give(item_name: String) -> void:
	_gesture = 1.0
	if _held:
		_held.queue_free()
	_held = _make_item(item_name)
	_hand_l.add_child(_held)


func _process(delta: float) -> void:
	_t += delta
	_talk = lerpf(_talk, mouth_open, 1.0 - exp(-delta * 18.0))
	# Breathing and a slow sway.
	_body.scale = Vector3(1.0, 1.0 + sin(_t * 1.6) * 0.012, 1.0)
	_body.rotation.y = sin(_t * 0.45) * 0.03
	# Jaw and beard follow the voice; the head nods a little while he talks.
	_jaw.rotation.x = _talk * 0.32
	_mouth.scale = Vector3(1.0, 0.25 + _talk * 1.5, 1.0)
	var to_target := (look_target - _head.global_position)
	var yaw := clampf(atan2(to_target.x, to_target.z), -0.5, 0.5)
	var pitch := clampf(-atan2(to_target.y, Vector2(to_target.x, to_target.z).length()), -0.3, 0.3)
	_head.rotation = _head.rotation.lerp(Vector3(pitch * 0.6 + sin(_t * 7.0) * 0.035 * _talk, yaw * 0.6, sin(_t * 0.9) * 0.025), 1.0 - exp(-delta * 4.0))
	# Blink every few seconds.
	_next_blink -= delta
	if _next_blink <= 0.0:
		_blink = 1.0
		_next_blink = randf_range(2.5, 5.0)
	_blink = maxf(0.0, _blink - delta * 8.0)
	_eyes.scale = Vector3(1.0, 1.0 - _blink * 0.9, 1.0)
	# Arms: the hammer arm rests and sways; the free arm reaches out when he hands something over.
	_gesture = maxf(0.0, _gesture - delta * 0.35)
	var reach := sin(clampf(1.0 - _gesture, 0.0, 1.0) * PI) if _gesture > 0.0 else 0.0
	_arm_l.rotation = Vector3(-1.25 * reach + sin(_t * 1.3) * 0.03, 0.0, 0.18 - 0.1 * reach)
	_arm_r.rotation = Vector3(sin(_t * 1.1) * 0.04 + _talk * 0.08, 0.0, -0.22)
	if _held and _gesture <= 0.0:
		_held.queue_free()
		_held = null


func _build() -> void:
	_body = Node3D.new()
	add_child(_body)
	# Legs and boots
	for side in [-1.0, 1.0]:
		_add(_body, _capsule(0.13, 0.55), Color("3b2a20"), Vector3(0.17 * side, 0.32, 0))
		_add(_body, _box(Vector3(0.24, 0.14, 0.34)), Color("1f1712"), Vector3(0.17 * side, 0.07, 0.05), 0.6)
	# Torso: stocky, with an apron and a belt
	var torso := _add(_body, _capsule(0.42, 1.05), TUNIC, Vector3(0, 0.98, 0))
	torso.scale = Vector3(1.18, 1.0, 0.95)
	var apron := _add(_body, _box(Vector3(0.62, 0.62, 0.06)), Color("4a2e1c"), Vector3(0, 0.78, 0.36), 0.7)
	apron.rotation.x = -0.08
	var belt := _add(_body, _cylinder(0.47, 0.47, 0.1), Color("2a1d14"), Vector3(0, 0.95, 0), 0.5)
	belt.scale = Vector3(1.18, 1.0, 0.98)
	_add(_body, _box(Vector3(0.14, 0.11, 0.04)), Color("d8a632"), Vector3(0, 0.95, 0.46), 0.3, 0.9)
	# Arms
	_arm_l = _shoulder(_body, Vector3(-0.5, 1.32, 0))
	_arm_r = _shoulder(_body, Vector3(0.5, 1.32, 0))
	_hand_l = Node3D.new()
	_hand_l.position = Vector3(0, -0.62, 0.08)
	_arm_l.add_child(_hand_l)
	_hammer(_arm_r)
	# Head
	_head = Node3D.new()
	_head.position = Vector3(0, 1.68, 0.02)
	_body.add_child(_head)
	_add(_head, _sphere(0.27), SKIN, Vector3.ZERO, 0.7)
	_add(_head, _sphere(0.075), Color("d4836a"), Vector3(0, -0.03, 0.265), 0.6)            # nose
	for side in [-1.0, 1.0]:
		_add(_head, _sphere(0.06), SKIN.darkened(0.08), Vector3(0.27 * side, 0, 0), 0.7)  # ears
	_eyes = Node3D.new()
	_eyes.position = Vector3(0, 0.03, 0.225)
	_head.add_child(_eyes)
	for side in [-1.0, 1.0]:
		_add(_eyes, _sphere(0.048), Color("f4efe6"), Vector3(0.095 * side, 0, 0), 0.3)
		_add(_eyes, _sphere(0.024), Color("1b1310"), Vector3(0.095 * side, 0, 0.034), 0.2)
		var brow := _add(_head, _box(Vector3(0.12, 0.035, 0.05)), GINGER_DARK, Vector3(0.1 * side, 0.1, 0.235), 0.9)
		brow.rotation.z = -0.22 * side
	# Helmet with horns
	var helmet := SphereMesh.new()
	helmet.radius = 0.3
	helmet.height = 0.3
	helmet.is_hemisphere = true
	_add(_head, helmet, Color("8d939b"), Vector3(0, 0.13, -0.02), 0.32, 0.9)
	_add(_head, _cylinder(0.31, 0.31, 0.05), Color("6d7178"), Vector3(0, 0.14, -0.02), 0.4, 0.9)
	for side in [-1.0, 1.0]:
		var horn := _add(_head, _cylinder(0.0, 0.06, 0.3), Color("e8dcc2"), Vector3(0.3 * side, 0.27, -0.02), 0.5)
		horn.rotation.z = -0.9 * side
	# Moustache (stays) and beard (upper part stays, lower part moves with the jaw)
	for side in [-1.0, 1.0]:
		var stache := _add(_head, _capsule(0.045, 0.22), GINGER, Vector3(0.085 * side, -0.09, 0.245), 0.95)
		stache.rotation = Vector3(0, 0, 1.2 * side)
	var upper := _add(_head, _sphere(0.25), GINGER, Vector3(0, -0.15, 0.06), 0.95)
	upper.scale = Vector3(1.12, 0.5, 0.85)
	_jaw = Node3D.new()
	_jaw.position = Vector3(0, -0.13, 0.12)
	_head.add_child(_jaw)
	_mouth = _add(_jaw, _sphere(0.06), Color("3a1209"), Vector3(0, -0.005, 0.12), 0.5)
	_mouth.scale = Vector3(1.0, 0.3, 1.0)
	var beard := _add(_jaw, _sphere(0.3), GINGER, Vector3(0, -0.2, 0.0), 0.95)
	beard.scale = Vector3(1.05, 1.15, 0.75)
	var tip := _add(_jaw, _sphere(0.18), GINGER_DARK, Vector3(0, -0.46, 0.03), 0.95)
	tip.scale = Vector3(0.9, 1.1, 0.7)
	# Where his voice comes from
	voice_anchor = Node3D.new()
	voice_anchor.position = Vector3(0, -0.05, 0.2)
	_head.add_child(voice_anchor)


func _shoulder(parent: Node3D, at: Vector3) -> Node3D:
	var pivot := Node3D.new()
	pivot.position = at
	parent.add_child(pivot)
	_add(pivot, _sphere(0.17), TUNIC, Vector3.ZERO)
	_add(pivot, _capsule(0.12, 0.55), TUNIC, Vector3(0, -0.28, 0))
	_add(pivot, _sphere(0.11), SKIN, Vector3(0, -0.58, 0.02), 0.7)
	return pivot


func _hammer(arm: Node3D) -> void:
	var grip := Node3D.new()
	grip.position = Vector3(0, -0.6, 0.06)
	grip.rotation = Vector3(1.3, 0, 0)
	arm.add_child(grip)
	_add(grip, _cylinder(0.025, 0.025, 0.55), Color("5a3a22"), Vector3(0, 0.15, 0), 0.8)
	_add(grip, _box(Vector3(0.24, 0.12, 0.12)), Color("4a4d52"), Vector3(0, 0.42, 0), 0.35, 0.95)


func _make_item(item_name: String) -> Node3D:
	var item := Node3D.new()
	if item_name.contains("sword"):
		_add(item, _box(Vector3(0.05, 0.75, 0.012)), Color("c9ced6"), Vector3(0, 0.48, 0), 0.18, 1.0)
		_add(item, _box(Vector3(0.22, 0.04, 0.05)), Color("b8902f"), Vector3(0, 0.1, 0), 0.3, 1.0)
		_add(item, _cylinder(0.02, 0.02, 0.16), Color("4a2e1c"), Vector3(0, 0.0, 0), 0.8)
		item.rotation = Vector3(-0.9, 0, 0)
	elif item_name.contains("shield"):
		var s := _add(item, _cylinder(0.3, 0.3, 0.05), Color("7a4b2a"), Vector3(0, 0.1, 0.1), 0.7)
		s.rotation.x = PI / 2
		var rim := _add(item, _cylinder(0.32, 0.32, 0.03), Color("8b9097"), Vector3(0, 0.1, 0.08), 0.3, 0.9)
		rim.rotation.x = PI / 2
	else:
		_add(item, _sphere(0.09), Color(0.9, 0.12, 0.15), Vector3(0, 0.12, 0), 0.1, 0.0, Color(0.9, 0.1, 0.1), 1.5)
		_add(item, _cylinder(0.03, 0.03, 0.08), Color("d9e2e8"), Vector3(0, 0.24, 0), 0.1)
	return item


# ---- Mesh helpers -----------------------------------------------------------------------------

func _add(parent: Node3D, mesh: Mesh, color: Color, at: Vector3, rough := 0.8, metal := 0.0, emission := Color.BLACK, emission_energy := 0.0) -> MeshInstance3D:
	var m := MeshInstance3D.new()
	m.mesh = mesh
	var mat := StandardMaterial3D.new()
	mat.albedo_color = color
	mat.roughness = rough
	mat.metallic = metal
	if emission_energy > 0.0:
		mat.emission_enabled = true
		mat.emission = emission
		mat.emission_energy_multiplier = emission_energy
	m.material_override = mat
	m.position = at
	parent.add_child(m)
	return m


func _sphere(r: float) -> SphereMesh:
	var s := SphereMesh.new()
	s.radius = r
	s.height = r * 2.0
	return s


func _capsule(r: float, h: float) -> CapsuleMesh:
	var c := CapsuleMesh.new()
	c.radius = r
	c.height = h
	return c


func _box(size: Vector3) -> BoxMesh:
	var b := BoxMesh.new()
	b.size = size
	return b


func _cylinder(top: float, bottom: float, h: float) -> CylinderMesh:
	var c := CylinderMesh.new()
	c.top_radius = top
	c.bottom_radius = bottom
	c.height = h
	return c
