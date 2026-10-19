extends Node3D
## Talking blacksmith. Press Play (F5) and talk to Brom, who can sell you a sword.
## Put your API key in this node's Inspector (Api Key) or in the LOKUTOR_API_KEY environment
## variable. Headphones recommended.

@export var api_key: String = ""

const PRICES := {"iron sword": 20, "steel shield": 25, "healing potion": 5}

var gold := 30
var bag: Array[String] = []
var brom: LokutorAgent
var status_label: Label
var subtitles: Label


func _ready() -> void:
	_build_world()

	# A placeholder blacksmith: a capsule, a box head and a jaw that moves when he talks.
	var body := MeshInstance3D.new()
	body.mesh = CapsuleMesh.new()
	body.position = Vector3(0, 1, -3)
	add_child(body)
	var head := MeshInstance3D.new()
	var head_mesh := BoxMesh.new()
	head_mesh.size = Vector3(0.6, 0.45, 0.6)
	head.mesh = head_mesh
	head.position = Vector3(0, 0.75, 0)
	body.add_child(head)
	var jaw := MeshInstance3D.new()
	var jaw_mesh := BoxMesh.new()
	jaw_mesh.size = Vector3(0.48, 0.09, 0.3)
	jaw.mesh = jaw_mesh
	jaw.position = Vector3(0, -0.25, 0.15)
	head.add_child(jaw)

	# The whole integration: a LokutorAgent, who the character is, and what it can do.
	brom = LokutorAgent.new()
	brom.api_key = api_key
	brom.persona = "You are Brom, the blacksmith of Oakvale, a gruff but kind dwarf. Speak in one or two short sentences. " + \
		"Your shop sells: iron sword (20 gold), steel shield (25 gold), healing potion (5 gold). Nothing else. " + \
		"When the player agrees to buy something, call give_item; it takes the gold. Greet the player when they arrive."
	brom.voice = "M1"
	brom.language = "en"
	brom.jaw = jaw
	brom.jaw_max_angle_degrees = 25.0
	brom.add_action("give_item", "Sell an item to the player: takes the gold and puts the item in their bag.", _give_item,
		{"item": {"type": "string", "description": "The item to sell", "enum": PRICES.keys()}})
	brom.player_said.connect(func(text): subtitles.text = "You: " + text)
	brom.character_said.connect(func(text): subtitles.text += "\nBrom: " + text)
	brom.state_changed.connect(func(_state): _refresh())
	head.add_child(brom)   # his voice comes from his head; adding him starts the conversation
	_refresh()


func _give_item(args: Dictionary) -> String:
	var item: String = args.get("item", "")
	if not PRICES.has(item):
		return "There is no %s in the shop." % item
	if gold < PRICES[item]:
		return "The player has only %d gold; the %s costs %d." % [gold, item, PRICES[item]]
	gold -= PRICES[item]
	bag.append(item)
	_refresh()
	return "Gave the %s. The player paid %d gold and has %d left." % [item, PRICES[item], gold]


func _refresh() -> void:
	var says: String = ["away", "arriving...", "listening (just talk)", "hearing you", "thinking", "talking"][brom.state]
	status_label.text = "Brom is %s   |   Gold: %d   |   Bag: %s" % [says, gold, ", ".join(bag) if not bag.is_empty() else "empty"]


func _build_world() -> void:
	var camera := Camera3D.new()
	camera.position = Vector3(0, 1.6, 0)
	add_child(camera)
	camera.look_at(Vector3(0, 1.5, -3))
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-45, 30, 0)
	add_child(sun)
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.12, 0.12, 0.14)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.55, 0.55, 0.6)
	var world := WorldEnvironment.new()
	world.environment = env
	add_child(world)
	var ui := CanvasLayer.new()
	add_child(ui)
	status_label = Label.new()
	status_label.position = Vector2(20, 16)
	status_label.add_theme_font_size_override("font_size", 20)
	ui.add_child(status_label)
	subtitles = Label.new()
	subtitles.anchor_top = 1.0
	subtitles.anchor_bottom = 1.0
	subtitles.anchor_right = 1.0
	subtitles.offset_left = 20
	subtitles.offset_top = -110
	subtitles.offset_right = -20
	subtitles.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	subtitles.add_theme_font_size_override("font_size", 20)
	ui.add_child(subtitles)
