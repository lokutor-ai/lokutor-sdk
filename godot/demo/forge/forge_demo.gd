extends Node3D
## Brom's Forge. Press Play and talk to Brom, the blacksmith (headphones recommended): ask what he
## sells, buy something, and watch it land in your bag.
##
## Recording a video (Godot's Movie Maker, real conversation with production):
##   python3 demo/forge/make_lines.py          # the player's lines, spoken by Lokutor TTS
##   godot --path . --write-movie /tmp/forge.avi res://demo/forge/forge_demo.tscn
## When recording, the player's lines are played and fed to Brom as the microphone.

@export var api_key: String = ""
## Plays the player's lines from demo/forge/lines/ instead of using the microphone.
## Always on while recording a movie.
@export var scripted := false

const Dwarf := preload("res://demo/forge/dwarf.gd")
const ForgeUI := preload("res://demo/forge/forge_ui.gd")

const PRICES := {"iron sword": 20, "steel shield": 25, "healing potion": 5}
const PERSONA := "You are Brom, the blacksmith of Oakvale, a gruff but warm-hearted dwarf. Speak in one or two short sentences, with a little dwarven humour. " + \
	"Your shop sells: iron sword (20 gold), steel shield (25 gold), healing potion (5 gold). Nothing else. " + \
	"When the player agrees to buy something, call give_item once for each item; it takes the gold. " + \
	"Greet the player briefly when they walk in."
const LINES := 3
const CODE := "var brom := LokutorAgent.new()\n" + \
	"brom.persona = \"You are Brom, the blacksmith of Oakvale...\"\n" + \
	"brom.voice = \"M1\"\n" + \
	"brom.add_action(\"give_item\", \"Sell an item to the player\",\n" + \
	"        _give_item, {\"item\": \"Which item\"})\n" + \
	"$Brom/Head.add_child(brom)"

var gold := 30
var brom: LokutorAgent
var dwarf: Dwarf
var ui: ForgeUI
var camera: Camera3D
var fire_light: OmniLight3D
var coal_light: OmniLight3D
var _cam_base := Vector3(0.55, 1.7, 2.3)
var _look_at := Vector3(0.15, 1.35, -2.4)
var _t := 0.0
var _spoke_at := -1.0
var _noise := FastNoiseLite.new()

# Scripted player
var _player_voice: AudioStreamPlayer
var _line_samples := PackedFloat32Array()
var _line_rate := 44100
var _line_pos := 0
var _line_playing := false


func _ready() -> void:
	var recording := OS.has_feature("movie") or "--write-movie" in OS.get_cmdline_args()
	if recording:
		scripted = true
		Engine.max_fps = 30   # keep the movie in real time: the conversation happens live
	_build_world()
	_build_forge()
	_build_props()

	dwarf = Dwarf.new()
	dwarf.position = Vector3(0, 0, -2.4)
	add_child(dwarf)
	dwarf.look_target = _cam_base

	# ---- The SDK part: a LokutorAgent, who Brom is, and what he can do ----
	brom = LokutorAgent.new()
	brom.api_key = api_key
	brom.persona = PERSONA
	brom.voice = "M1"
	brom.language = "en"
	brom.start_on_ready = false
	brom.mic_mode = LokutorAgent.MicMode.OFF if scripted else LokutorAgent.MicMode.ALWAYS
	brom.add_action("give_item", "Sell an item to the player: takes the gold and puts the item in their bag.", _give_item,
		{"item": {"type": "string", "description": "The item to sell", "enum": PRICES.keys()}})
	dwarf.voice_anchor.add_child(brom)

	ui = ForgeUI.new()
	add_child(ui)
	brom.state_changed.connect(_on_state)
	brom.character_said.connect(func(text): ui.say("Brom", text, false))
	brom.player_said.connect(func(text): ui.say("You", text, true))

	_player_voice = AudioStreamPlayer.new()
	add_child(_player_voice)

	var preview := OS.get_environment("FORGE_PREVIEW")
	if preview != "":
		# Renders the scene without a conversation and saves a screenshot (for checking the look).
		ui.show_title("Talk to your game characters", "Lokutor Voice Agents  ·  Godot & Unity", 1.0)
		await get_tree().create_timer(0.8).timeout
		get_viewport().get_texture().get_image().save_png(preview.replace(".png", "_title.png"))
		ui.say("Brom", "Welcome to my forge, traveler. What brings you in tonight?", false)
		await get_tree().create_timer(3.4).timeout
		get_viewport().get_texture().get_image().save_png(preview)
		get_tree().quit()
	elif scripted:
		_run_script()
	else:
		ui.show_title("Brom's Forge", "Walk in and talk: Brom is listening", 2.0)
		brom.start_conversation()


func _give_item(args: Dictionary) -> String:
	var item: String = args.get("item", "")
	if not PRICES.has(item):
		return "There is no %s in the shop." % item
	var price: int = PRICES[item]
	if gold < price:
		return "The player has only %d gold; the %s costs %d." % [gold, item, price]
	gold -= price
	ui.set_gold(gold)
	ui.set_card_detail("-%d gold" % price)
	ui.add_to_bag(item)
	dwarf.give(item)
	return "Gave the %s. The player paid %d gold and has %d left." % [item, price, gold]


func _on_state(state: LokutorAgent.State) -> void:
	ui.set_state(state)
	if state == LokutorAgent.State.SPEAKING:
		_spoke_at = _t


func _process(delta: float) -> void:
	_t += delta
	dwarf.mouth_open = brom.mouth_open
	ui.set_level(brom.mouth_open)
	# A slow, handheld-feeling push in.
	var drift := Vector3(sin(_t * 0.13) * 0.07, sin(_t * 0.21) * 0.025, -minf(_t * 0.006, 0.35))
	camera.position = _cam_base + drift
	camera.look_at(_look_at + Vector3(sin(_t * 0.1) * 0.03, 0, 0))
	# The fire breathes.
	var n := _noise.get_noise_1d(_t * 6.0)
	fire_light.light_energy = 2.4 + n * 0.8
	coal_light.light_energy = 1.6 + n * 0.6
	if scripted:
		_feed_player(delta)


# ---- The scripted player (video) --------------------------------------------------------------

func _run_script() -> void:
	for i in LINES:
		if not FileAccess.file_exists("res://demo/forge/lines/line%d.wav" % (i + 1)):
			push_error("Missing demo/forge/lines/line%d.wav: run python3 demo/forge/make_lines.py" % (i + 1))
			get_tree().quit(1)
			return
	ui.show_title("Talk to your game characters", "Lokutor Voice Agents  ·  Godot & Unity", 2.6)
	await get_tree().create_timer(1.6).timeout
	brom.start_conversation()
	await _reply_done(0.0, 15.0)
	for i in LINES:
		await get_tree().create_timer(0.45).timeout
		_start_line("res://demo/forge/lines/line%d.wav" % (i + 1))
		while _line_playing:
			await get_tree().process_frame
		await _reply_done(_t, 25.0)
	await get_tree().create_timer(1.2).timeout
	ui.show_end(CODE, "Give any character a voice", "lokutor.com")
	await get_tree().create_timer(6.0).timeout
	brom.stop_conversation()
	get_tree().quit()


func _reply_done(since: float, timeout: float) -> void:
	var until := _t + timeout
	while _t < until:
		await get_tree().process_frame
		if _spoke_at > since and brom.state == LokutorAgent.State.READY:
			return


func _start_line(path: String) -> void:
	var bytes := FileAccess.get_file_as_bytes(path)
	var wav := _parse_wav(bytes)
	if wav.is_empty():
		push_error("Could not read " + path)
		return
	var stream := AudioStreamWAV.new()
	stream.format = AudioStreamWAV.FORMAT_16_BITS
	stream.mix_rate = wav.rate
	stream.stereo = wav.channels == 2
	stream.data = wav.data
	_player_voice.stream = stream
	_player_voice.play()
	_line_rate = wav.rate
	_line_samples = PackedFloat32Array()
	var frames: int = wav.data.size() >> (1 if wav.channels == 1 else 2)
	_line_samples.resize(frames)
	for i in frames:
		_line_samples[i] = wav.data.decode_s16(i * 2 * wav.channels) / 32768.0
	_line_pos = 0
	_line_playing = true


## The microphone, for the scripted player: the line being played, then silence, in real time.
func _feed_player(delta: float) -> void:
	var n := int(round(delta * _line_rate))
	var chunk := PackedFloat32Array()
	if _line_playing:
		var end := mini(_line_pos + n, _line_samples.size())
		chunk = _line_samples.slice(_line_pos, end)
		_line_pos = end
		if _line_pos >= _line_samples.size():
			_line_playing = false
	if chunk.size() < n:
		var pad := PackedFloat32Array()
		pad.resize(n - chunk.size())
		chunk.append_array(pad)
	brom.feed_microphone(chunk, _line_rate)


func _parse_wav(bytes: PackedByteArray) -> Dictionary:
	if bytes.size() < 12 or bytes.slice(0, 4).get_string_from_ascii() != "RIFF":
		return {}
	var pos := 12
	var channels := 1
	var rate := 44100
	while pos + 8 <= bytes.size():
		var id := bytes.slice(pos, pos + 4).get_string_from_ascii()
		var size := bytes.decode_u32(pos + 4)
		if id == "fmt ":
			channels = bytes.decode_u16(pos + 10)
			rate = bytes.decode_u32(pos + 12)
		elif id == "data":
			return {"channels": channels, "rate": rate, "data": bytes.slice(pos + 8, pos + 8 + size)}
		pos += 8 + size + (size & 1)
	return {}


# ---- The world --------------------------------------------------------------------------------

func _build_world() -> void:
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color("0a0706")
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color("39404f")
	env.ambient_light_energy = 0.7
	env.tonemap_mode = Environment.TONE_MAPPER_AGX
	env.tonemap_exposure = 1.05
	env.glow_enabled = true
	env.glow_intensity = 0.85
	env.glow_bloom = 0.08
	env.glow_hdr_threshold = 1.0
	env.ssao_enabled = true
	env.ssao_intensity = 1.6
	env.volumetric_fog_enabled = true
	env.volumetric_fog_density = 0.012
	env.volumetric_fog_albedo = Color("f2f0ee")
	env.volumetric_fog_anisotropy = 0.35
	env.adjustment_enabled = true
	env.adjustment_contrast = 1.06
	env.adjustment_saturation = 1.1
	var world := WorldEnvironment.new()
	world.environment = env
	add_child(world)

	camera = Camera3D.new()
	camera.fov = 36
	camera.position = _cam_base
	add_child(camera)
	camera.look_at(_look_at)
	var attrs := CameraAttributesPractical.new()
	attrs.dof_blur_far_enabled = true
	attrs.dof_blur_far_distance = 5.2
	attrs.dof_blur_far_transition = 3.0
	attrs.dof_blur_amount = 0.05
	camera.attributes = attrs

	# Cold moonlight through a window on the left, for contrast with the fire.
	var moon := SpotLight3D.new()
	moon.light_color = Color("7fa6ff")
	moon.light_energy = 5.0
	moon.spot_range = 14.0
	moon.spot_angle = 22.0
	moon.shadow_enabled = true
	moon.light_volumetric_fog_energy = 2.5
	moon.position = Vector3(-4.6, 4.6, -1.4)
	add_child(moon)
	moon.look_at(Vector3(-0.6, 0.0, -2.0))
	# A warm fill from behind the camera, low.
	var fill := OmniLight3D.new()
	fill.light_color = Color("ffb27a")
	fill.light_energy = 0.35
	fill.omni_range = 7.0
	fill.position = Vector3(-1.2, 2.0, 1.8)
	add_child(fill)
	# A soft key light on Brom's face, so it reads against the fire behind him.
	var key := SpotLight3D.new()
	key.light_color = Color("ffe6cf")
	key.light_energy = 3.0
	key.spot_range = 6.0
	key.spot_angle = 24.0
	key.spot_attenuation = 0.7
	key.light_volumetric_fog_energy = 0.0
	key.position = Vector3(-0.9, 2.8, 0.2)
	add_child(key)
	key.look_at(Vector3(0, 1.6, -2.4))
	# A cool rim from behind, to separate him from the wall.
	var rim := OmniLight3D.new()
	rim.light_color = Color("8fb0ff")
	rim.light_energy = 1.4
	rim.omni_range = 3.2
	rim.position = Vector3(-0.9, 2.3, -3.6)
	add_child(rim)

	_slab(Vector3(16, 0.2, 16), Vector3(0, -0.1, -2), _stone(Color("2d2a28"), Color("57504a"), 7.0, 0.5))      # floor
	_slab(Vector3(16, 7, 0.5), Vector3(0, 3.5, -5.3), _stone(Color("34302d"), Color("6a6159"), 3.5, 1.4))     # back wall
	_slab(Vector3(0.5, 7, 12), Vector3(-5.2, 3.5, -1.5), _stone(Color("34302d"), Color("6a6159"), 3.5, 1.4))  # left wall
	var wood := _material(Color("4a2f1c"), 0.85)
	for z in [-4.6, -2.4, -0.2]:
		_slab(Vector3(12, 0.35, 0.35), Vector3(0, 4.3, z), wood)
	# The window the moonlight comes through
	_slab(Vector3(0.1, 1.4, 1.0), Vector3(-4.9, 3.6, -1.4), _material(Color("9ec2ff"), 0.2, 0.0, Color("7aa6ff"), 0.6))


func _build_forge() -> void:
	var stone := _stone(Color("332b26"), Color("54463c"), 2.0, 1.0)
	var at := Vector3(2.35, 0, -3.9)
	_slab(Vector3(1.9, 1.15, 1.5), at + Vector3(0, 0.575, 0), stone)
	_slab(Vector3(1.3, 0.55, 0.12), at + Vector3(0, 0.62, 0.72), _material(Color("120b08"), 1.0))
	var coals := _material(Color("1a0802"), 0.9, 0.0, Color("ff3c08"), 1.15)
	var embers_tex := NoiseTexture2D.new()
	var nz := FastNoiseLite.new()
	nz.frequency = 0.08
	embers_tex.noise = nz
	coals.emission_texture = embers_tex
	_slab(Vector3(1.15, 0.14, 1.0), at + Vector3(0, 1.17, 0.05), coals)
	_slab(Vector3(1.7, 1.2, 1.2), at + Vector3(0, 2.6, -0.1), _material(Color("2d2a28"), 0.45, 0.85))  # hood
	_slab(Vector3(0.6, 3.5, 0.6), at + Vector3(0, 4.9, -0.2), _material(Color("2d2a28"), 0.45, 0.85))  # chimney
	fire_light = OmniLight3D.new()
	fire_light.light_color = Color("ff7b2e")
	fire_light.omni_range = 7.0
	fire_light.shadow_enabled = true
	fire_light.light_volumetric_fog_energy = 1.6
	fire_light.position = at + Vector3(-0.2, 1.7, 0.8)
	add_child(fire_light)
	coal_light = OmniLight3D.new()
	coal_light.light_color = Color("ff5a12")
	coal_light.omni_range = 2.4
	coal_light.position = at + Vector3(0, 1.35, 0.2)
	add_child(coal_light)
	# Embers rising from the coals
	var embers := GPUParticles3D.new()
	embers.amount = 60
	embers.lifetime = 2.8
	embers.position = at + Vector3(0, 1.25, 0.1)
	var pm := ParticleProcessMaterial.new()
	pm.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_BOX
	pm.emission_box_extents = Vector3(0.5, 0.05, 0.4)
	pm.direction = Vector3(0, 1, 0)
	pm.spread = 25.0
	pm.initial_velocity_min = 0.4
	pm.initial_velocity_max = 1.2
	pm.gravity = Vector3(0, 0.25, 0)
	pm.turbulence_enabled = true
	pm.turbulence_noise_strength = 0.6
	pm.scale_min = 0.5
	pm.scale_max = 1.2
	var fade := Gradient.new()
	fade.set_color(0, Color(1, 0.75, 0.35, 1))
	fade.set_color(1, Color(1, 0.3, 0.05, 0))
	var ramp := GradientTexture1D.new()
	ramp.gradient = fade
	pm.color_ramp = ramp
	embers.process_material = pm
	var quad := QuadMesh.new()
	quad.size = Vector2(0.018, 0.018)
	var ember_mat := StandardMaterial3D.new()
	ember_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	ember_mat.billboard_mode = BaseMaterial3D.BILLBOARD_ENABLED
	ember_mat.vertex_color_use_as_albedo = true
	ember_mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	ember_mat.albedo_color = Color(4.0, 2.0, 0.6)
	quad.material = ember_mat
	embers.draw_pass_1 = quad
	add_child(embers)


func _build_props() -> void:
	var iron := _material(Color("5b5f66"), 0.32, 0.9)
	var wood := _material(Color("5a3a22"), 0.8)
	# Anvil, in the foreground on the left
	var anvil := Vector3(-1.25, 0, -1.6)
	_slab(Vector3(0.36, 0.5, 0.3), anvil + Vector3(0, 0.25, 0), iron)
	_slab(Vector3(0.8, 0.18, 0.32), anvil + Vector3(0, 0.59, 0), iron)
	var horn := MeshInstance3D.new()
	var cone := CylinderMesh.new()
	cone.top_radius = 0.0
	cone.bottom_radius = 0.12
	cone.height = 0.42
	horn.mesh = cone
	horn.material_override = iron
	horn.position = anvil + Vector3(0.58, 0.6, 0)
	horn.rotation.z = -PI / 2
	add_child(horn)
	# Weapon rack on the back wall
	_slab(Vector3(1.8, 0.12, 0.12), Vector3(-2.3, 2.6, -5.0), wood)
	var blade := _material(Color("c9ced6"), 0.18, 1.0)
	var gold_trim := _material(Color("b8902f"), 0.3, 1.0)
	for x in [-2.85, -2.45]:
		_slab(Vector3(0.07, 1.0, 0.02), Vector3(x, 1.95, -4.98), blade)
		_slab(Vector3(0.26, 0.05, 0.06), Vector3(x, 2.47, -4.98), gold_trim)
		_slab(Vector3(0.05, 0.2, 0.05), Vector3(x, 2.6, -4.98), wood)
	var shield := MeshInstance3D.new()
	var disc := CylinderMesh.new()
	disc.top_radius = 0.38
	disc.bottom_radius = 0.38
	disc.height = 0.06
	shield.mesh = disc
	shield.material_override = _material(Color("7a4b2a"), 0.7)
	shield.position = Vector3(-1.75, 2.0, -4.98)
	shield.rotation.x = PI / 2
	add_child(shield)
	var boss := MeshInstance3D.new()
	var boss_mesh := SphereMesh.new()
	boss_mesh.radius = 0.09
	boss_mesh.height = 0.18
	boss.mesh = boss_mesh
	boss.material_override = iron
	boss.position = Vector3(-1.75, 2.0, -4.93)
	add_child(boss)
	# Potion shelf
	_slab(Vector3(1.3, 0.06, 0.32), Vector3(0.9, 1.75, -4.95), wood)
	var potions := [Color(1.0, 0.15, 0.2), Color(0.2, 0.5, 1.0), Color(0.3, 1.0, 0.45)]
	for i in potions.size():
		var p := MeshInstance3D.new()
		var bottle := SphereMesh.new()
		bottle.radius = 0.1
		bottle.height = 0.2
		p.mesh = bottle
		p.material_override = _material(potions[i] * 0.4, 0.1, 0.0, potions[i], 2.2)
		p.position = Vector3(0.55 + i * 0.35, 1.88, -4.92)
		add_child(p)
	# Barrels by the forge
	for spot in [Vector3(3.9, 0, -2.7), Vector3(3.6, 0, -1.9)]:
		var barrel := MeshInstance3D.new()
		var cyl := CylinderMesh.new()
		cyl.top_radius = 0.34
		cyl.bottom_radius = 0.34
		cyl.height = 0.9
		barrel.mesh = cyl
		barrel.material_override = wood
		barrel.position = spot + Vector3(0, 0.45, 0)
		add_child(barrel)


# ---- Helpers ----------------------------------------------------------------------------------

func _slab(size: Vector3, at: Vector3, mat: Material) -> MeshInstance3D:
	var m := MeshInstance3D.new()
	var box := BoxMesh.new()
	box.size = size
	m.mesh = box
	m.material_override = mat
	m.position = at
	add_child(m)
	return m


func _material(color: Color, rough := 0.8, metal := 0.0, emission := Color.BLACK, emission_energy := 0.0) -> StandardMaterial3D:
	var mat := StandardMaterial3D.new()
	mat.albedo_color = color
	mat.roughness = rough
	mat.metallic = metal
	if emission_energy > 0.0:
		mat.emission_enabled = true
		mat.emission = emission
		mat.emission_energy_multiplier = emission_energy
	return mat


## Rough stone: cellular noise for the blocks, and the same noise as a normal map for relief.
func _stone(dark: Color, light: Color, scale: float, cell: float) -> StandardMaterial3D:
	var noise := FastNoiseLite.new()
	noise.noise_type = FastNoiseLite.TYPE_CELLULAR
	noise.frequency = 0.02 / cell
	noise.cellular_return_type = FastNoiseLite.RETURN_DISTANCE2_SUB
	var grad := Gradient.new()
	grad.set_color(0, dark)
	grad.set_color(1, light)
	var albedo := NoiseTexture2D.new()
	albedo.noise = noise
	albedo.color_ramp = grad
	albedo.seamless = true
	var normal := NoiseTexture2D.new()
	normal.noise = noise
	normal.as_normal_map = true
	normal.bump_strength = 6.0
	normal.seamless = true
	var mat := StandardMaterial3D.new()
	mat.albedo_texture = albedo
	mat.normal_enabled = true
	mat.normal_texture = normal
	mat.roughness = 0.92
	mat.uv1_triplanar = true
	mat.uv1_scale = Vector3(scale, scale, scale) * 0.1
	return mat
