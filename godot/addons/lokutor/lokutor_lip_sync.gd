@icon("res://addons/lokutor/icon.svg")
class_name LokutorLipSync
extends Node
## Moves a mouth with a voice. Give it a face (blend shapes), a jaw, or a 2D sprite with mouth frames; LokutorAgent and
## LokutorVoice feed it visemes and the clock of what is being heard. Without visemes it follows the loudness.
##
## Blend shapes are found by name: the 15 visemes of Oculus / Ready Player Me (viseme_PP, viseme_aa...), VRM
## (aa ih ou ee oh), VRoid and the like (Fcl_MTH_A, MTH_A...), ARKit (jawOpen, mouthFunnel...) or a single mouth-open
## shape. Or write your own in `mapping`.
##
## Several voices can share a mouth (a LokutorAgent and a LokutorVoice on one face): it follows the one speaking.

## The mouth changed shape. Use it to swap sprites or sounds yourself; weights has the blend between shapes.
signal viseme_changed(viseme: String)

enum Rig { NONE, VISEMES, VRM, ARKIT, MOUTH_OPEN, CUSTOM }

@export_group("Blend shapes")
## The meshes whose mouth moves: usually the face, plus teeth or tongue if they have the shapes too.
@export var faces: Array[MeshInstance3D] = []
## Your rig, when its names are not recognised: viseme name -> {blend shape name: weight 0..1}.
@export var mapping: Dictionary = {}

@export_group("Jaw")
## A jaw (or head part) rotated around its local X axis.
@export var jaw: Node3D
## Or a bone of a skeleton.
@export var skeleton: Skeleton3D
@export var jaw_bone: String = ""
@export var jaw_max_angle_degrees: float = 12.0

@export_group("2D")
## A Sprite2D, AnimatedSprite2D or any node with a `frame` property, whose frames are mouths.
@export var sprite: Node
## Frame for each viseme. Empty: 0 closed, 1 slightly open, 2 open, 3 round (if the sprite has them).
@export var sprite_frames: Dictionary = {}

@export_group("Feel")
## How far the mouth opens.
@export_range(0.0, 2.0) var intensity: float = 1.0
## Seconds the mouth leads the sound: people open before they speak.
@export_range(0.0, 0.2) var lead: float = 0.05
## Seconds over which one shape melts into the next. 0.05 is crisp and cartoonish, 0.12 soft.
@export_range(0.01, 0.3) var blend: float = 0.07
## How much quiet speech closes the mouth, 0..1.
@export_range(0.0, 1.0) var loudness: float = 0.35
## Steps per second of the mouth, for a hand-drawn look (12 = animation on twos). 0 = smooth.
@export var stepped_fps: float = 0.0

## How open the mouth is, 0..1.
var mouth_open: float = 0.0
## Weight of each of the 15 visemes right now (Lokutor.VISEMES order).
var weights := PackedFloat32Array()
## The strongest one.
var current: String = "sil"
## Loudness multiplier for mouths that follow the loudness (no visemes).
var gain: float = 1.5

## How open each viseme is, for jaws and mouth-open shapes.
const OPENNESS := [0.0, 0.0, 0.12, 0.28, 0.3, 0.38, 0.3, 0.16, 0.28, 0.3, 1.0, 0.62, 0.36, 0.72, 0.34]

## VRM has five mouths; the rest are approximated with them.
const VRM := [{}, {}, {"ih": 0.15}, {"aa": 0.25}, {"aa": 0.3}, {"aa": 0.35}, {"ou": 0.35, "ih": 0.2}, {"ih": 0.45}, {"aa": 0.25},
	{"ou": 0.3}, {"aa": 1.0}, {"ee": 0.9}, {"ih": 0.9}, {"oh": 1.0}, {"ou": 1.0}]

const ARKIT := [{},
	{"mouthClose": 0.3, "mouthPressLeft": 0.5, "mouthPressRight": 0.5, "mouthRollLower": 0.4, "mouthRollUpper": 0.3},
	{"mouthRollLower": 0.6, "mouthUpperUpLeft": 0.3, "mouthUpperUpRight": 0.3, "jawOpen": 0.06},
	{"jawOpen": 0.16, "tongueOut": 0.35}, {"jawOpen": 0.2, "mouthLowerDownLeft": 0.2, "mouthLowerDownRight": 0.2},
	{"jawOpen": 0.26, "mouthStretchLeft": 0.15, "mouthStretchRight": 0.15},
	{"mouthFunnel": 0.5, "jawOpen": 0.12}, {"jawOpen": 0.06, "mouthSmileLeft": 0.3, "mouthSmileRight": 0.3, "mouthStretchLeft": 0.2, "mouthStretchRight": 0.2},
	{"jawOpen": 0.18}, {"mouthFunnel": 0.3, "jawOpen": 0.16}, {"jawOpen": 0.62, "mouthLowerDownLeft": 0.2, "mouthLowerDownRight": 0.2},
	{"jawOpen": 0.36, "mouthSmileLeft": 0.25, "mouthSmileRight": 0.25, "mouthStretchLeft": 0.2, "mouthStretchRight": 0.2},
	{"jawOpen": 0.2, "mouthSmileLeft": 0.45, "mouthSmileRight": 0.45}, {"jawOpen": 0.42, "mouthFunnel": 0.6},
	{"jawOpen": 0.16, "mouthPucker": 0.8}]

const _MOUTH_OPEN_NAMES := ["mouthopen", "mouth_open", "jawopen", "jaw_open", "mouth_a", "mth_a", "open", "a"]
## Prefixes of rigs whose mouths are the vowels a i u e o (VRoid, Unity-chan and the like).
const _VOWEL_PREFIXES := ["fcl_mth_", "mth_", "mouth_"]

var _tracks: Dictionary = {}       # whose voice (instance id, 0 for calls without one) -> {events: [{v, t, d}], first}
var _speaker := -1                 # whose track the mouth follows
var _speaker_busy := false
var _targets: Array = []           # [[mesh, blend shape index, weight per viseme (15 floats)]]
var _rig: Rig = Rig.NONE
var _jaw_rest := Vector3.ZERO
var _jaw_bone := -1
var _step_s := 0.0


func _ready() -> void:
	weights.resize(15)
	bind()


## Finds the blend shapes again, after changing faces or mapping at runtime.
func bind() -> void:
	_targets.clear()
	_rig = Rig.NONE
	if jaw:
		_jaw_rest = jaw.rotation
	_jaw_bone = skeleton.find_bone(jaw_bone) if skeleton and jaw_bone != "" else -1
	for mesh in faces:
		if mesh == null or mesh.mesh == null:
			continue
		var shapes := {}          # lower-case name -> index
		for i in mesh.get_blend_shape_count():
			shapes[str(mesh.mesh.get_blend_shape_name(i)).to_lower()] = i
		for full in shapes.keys():
			# FBX exporters put the mesh or blend-shape node in front: "Face.viseme_aa", "blendShape1.MTH_A".
			var last: String = full.get_slice(".", full.get_slice_count(".") - 1)
			if last != "" and not shapes.has(last):
				shapes[last] = shapes[full]
		var table := _table_for(shapes)
		var by_shape := {}        # index -> 15 weights
		for v in table.size():
			for shape_name in table[v]:
				var index: int = shapes.get(str(shape_name).to_lower(), -1)
				if index >= 0:
					if not by_shape.has(index):
						var w := PackedFloat32Array()
						w.resize(15)
						by_shape[index] = w
					by_shape[index][v] = float(table[v][shape_name])
		for index in by_shape:
			_targets.append([mesh, index, by_shape[index]])


## Which kind of mouth was found.
func rig() -> Rig:
	return _rig


func _table_for(shapes: Dictionary) -> Array:
	if not mapping.is_empty():
		_rig = Rig.CUSTOM
		var custom: Array = []
		for v in Lokutor.VISEMES:
			custom.append(mapping.get(v, {}))
		return custom
	# The 15 visemes under any of the usual prefixes.
	for prefix in ["viseme_", "vis_", "v_", "viseme.", "vrc.v_", ""]:
		var table: Array = []
		var found := 0
		for v in Lokutor.VISEMES:
			var n: String = (prefix + v).to_lower()
			if not shapes.has(n) and v in ["ih", "oh", "ou"]:
				n = (prefix + v[0]).to_lower()              # Ready Player Me: viseme_I, viseme_O, viseme_U
				if v == "ou":
					n = (prefix + "u").to_lower()
			table.append({n: 1.0} if shapes.has(n) else {})
			found += 1 if shapes.has(n) else 0
		if found >= 8:
			_rig = Rig.VISEMES
			return table
	if shapes.has("aa") and shapes.has("oh") and (shapes.has("ee") or shapes.has("e")):
		_rig = Rig.VRM
		var vrm: Array = VRM.duplicate(true)
		if not shapes.has("ee"):
			for row in vrm:
				if row.has("ee"):
					row["e"] = row["ee"]
		return vrm
	for p in _VOWEL_PREFIXES:
		if shapes.has(p + "a") and shapes.has(p + "i") and shapes.has(p + "u") and shapes.has(p + "o"):
			_rig = Rig.VRM
			var names := {"aa": p + "a", "ih": p + "i", "ou": p + "u", "ee": p + "e", "oh": p + "o"}
			var vowels: Array = []
			for row in VRM:
				var renamed := {}
				for n in row:
					renamed[names[n]] = row[n]
				vowels.append(renamed)
			return vowels
	if shapes.has("jawopen") and shapes.has("mouthfunnel"):
		_rig = Rig.ARKIT
		return ARKIT
	for n in _MOUTH_OPEN_NAMES:
		if shapes.has(n):
			_rig = Rig.MOUTH_OPEN
			var one: Array = []
			for v in 15:
				one.append({n: OPENNESS[v]})
			return one
	return []


## The visemes of the reply being heard, as they arrive: [{v, t, d}]. `source` is whose voice it is, when several share
## the mouth.
func push_visemes(list: Array, source: Object = null) -> void:
	_track(source).events.append_array(list)


## A new reply, or silence: the mouth closes. Without a source, whoever is speaking.
func clear(source: Object = null) -> void:
	if source == null:
		_tracks.clear()
	else:
		_tracks.erase(_id(source))


func has_visemes() -> bool:
	return _tracks.values().any(func(track): return not track.events.is_empty())


## This voice is gone: the mouth closes, or follows another.
func release(source: Object = null) -> void:
	_tracks.erase(_id(source))
	if _speaker == _id(source):
		_move(0.0, 0.0, false, 1.0, {})
		_speaker = -1
		_speaker_busy = false


## One frame of the mouth. `heard` is how much of the reply has been heard (LokutorVoicePlayer.heard_seconds()),
## `level` its loudness, `speaking` whether it is playing at all. Called for you by LokutorAgent and LokutorVoice.
func advance(heard: float, level: float, speaking: bool, delta: float, source: Object = null) -> void:
	var id := _id(source)
	if _speaker == -1 or (speaking and not _speaker_busy):
		_speaker = id
	if _speaker != id:
		return                             # another voice has the mouth
	_speaker_busy = speaking
	_move(heard, level, speaking, delta, _tracks.get(id, {}))


func _id(source: Object) -> int:
	return source.get_instance_id() if source != null else 0


func _track(source: Object) -> Dictionary:
	var id := _id(source)
	if not _tracks.has(id):
		_tracks[id] = {"events": [], "first": 0}
	return _tracks[id]


func _move(heard: float, level: float, speaking: bool, delta: float, track: Dictionary) -> void:
	if stepped_fps > 0.0:
		_step_s += delta
		if _step_s < 1.0 / stepped_fps:
			return
		delta = _step_s
		_step_s = 0.0
	var target := PackedFloat32Array()
	target.resize(15)
	var events: Array = track.get("events", [])
	if speaking and not events.is_empty():
		_visemes_at(heard + lead, target, track)
		# Quiet syllables open less; the lips still close fully on p, b, m.
		var volume := lerpf(1.0, clampf(level * 9.0, 0.0, 1.0), loudness)
		for v in range(2, 15):
			target[v] *= volume
	elif speaking:
		target[10] = clampf(level * 4.0 * gain, 0.0, 1.0)
	# The shapes already cross-fade on the timeline: this only takes the edge off changes of loudness and cuts.
	var k := 1.0 - exp(-delta / (blend * 0.35 if events.size() > 0 else blend * 0.6))
	var strongest := 0
	var open := 0.0
	for v in 15:
		weights[v] = lerpf(weights[v], target[v], k)
		open += weights[v] * OPENNESS[v]
		if v > 0 and weights[v] > maxf(0.2, weights[strongest] if strongest > 0 else 0.0):
			strongest = v
	mouth_open = clampf(open * intensity, 0.0, 1.0)
	if Lokutor.VISEMES[strongest] != current:
		current = Lokutor.VISEMES[strongest]
		viseme_changed.emit(current)
	_apply()


## Each shape rises over `blend` around its start and falls over `blend` around its end, so two neighbours overlap and
## a short one between two others never reaches full weight: that is coarticulation.
func _visemes_at(t: float, out: PackedFloat32Array, track: Dictionary) -> void:
	var half := blend * 0.5
	var events: Array = track.events
	while track.first < events.size() and events[track.first].t + events[track.first].d + half < t:
		track.first += 1
	var i: int = track.first
	var total := 0.0
	while i < events.size() and events[i].t - half <= t:
		var e: Dictionary = events[i]
		var a := smoothstep(e.t - half, e.t + half, t)
		var b := 1.0 - smoothstep(e.t + e.d - half, e.t + e.d + half, t)
		var w := minf(a, b)
		out[e.v] += w
		total += w
		i += 1
	if total > 1.0:
		for v in 15:
			out[v] /= total
	out[0] = 0.0          # sil is the rest pose, not a shape


func _apply() -> void:
	for target in _targets:
		var sum := 0.0
		var per_viseme: PackedFloat32Array = target[2]
		for v in 15:
			sum += weights[v] * per_viseme[v]
		(target[0] as MeshInstance3D).set_blend_shape_value(target[1], clampf(sum * intensity, 0.0, 1.5))
	if jaw:
		jaw.rotation = _jaw_rest + Vector3(deg_to_rad(jaw_max_angle_degrees) * mouth_open, 0.0, 0.0)
	if _jaw_bone >= 0:
		var rest := skeleton.get_bone_rest(_jaw_bone).basis.get_rotation_quaternion()
		skeleton.set_bone_pose_rotation(_jaw_bone, rest * Quaternion(Vector3.RIGHT, deg_to_rad(jaw_max_angle_degrees) * mouth_open))
	if sprite:
		sprite.set("frame", _sprite_frame())


func _sprite_frame() -> int:
	if not sprite_frames.is_empty():
		return int(sprite_frames.get(current, sprite_frames.get("sil", 0)))
	var count := 4
	if sprite is Sprite2D:
		count = sprite.hframes * sprite.vframes
	elif sprite is AnimatedSprite2D and sprite.sprite_frames != null:
		count = sprite.sprite_frames.get_frame_count(sprite.animation)
	var f := 0
	if current in ["oh", "ou", "RR", "CH"]:
		f = 3 if count > 3 else 1
	elif mouth_open > 0.45:
		f = 2
	elif mouth_open > 0.08:
		f = 1
	return mini(f, count - 1)
