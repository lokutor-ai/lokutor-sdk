@icon("res://addons/lokutor/icon.svg")
class_name LokutorAgent
extends Node3D
## Makes its parent character a voice agent: it listens through the microphone, answers in its own
## voice from where it stands (3D audio), moves its mouth, and calls your game's functions
## ("actions") when the conversation calls for it.

signal player_said(text: String)
signal player_said_partial(text: String)
## What the character is about to say. Lokutor.strip_tags(text) takes out [laugh] and the like for subtitles.
signal character_said(text: String)
## The player cut the character off; `heard` is what they actually heard.
signal character_interrupted(heard: String)
## The microphone heard the character's own words (speakers, no headphones). The line is not passed on as
## player_said, and mute_mic_while_speaking is turned on.
signal echo_heard(text: String)
signal state_changed(state: State)
## An action ran: its name, the arguments the character chose, and what it returned.
signal action_called(action_name: String, args: Dictionary, result: String)
signal error_occurred(code: String, message: String)
signal conversation_ended(reason: String)
## The connection dropped and is being opened again; then reconnected, in a fresh conversation.
signal reconnecting()
signal reconnected()
## Mouth shapes of the reply, as they arrive: [{v, c, t, d}]. LokutorLipSync already uses them.
signal visemes_received(list: Array)
## The character's voice, as it is played: for another character, or a recorder, that listens to it.
signal voice_played(samples: PackedFloat32Array, rate: int)
signal session_limit(seconds: float, reason: String)
## The lokutor.com demo (a browser with no key) stopped or refused this conversation: "call" (it reached its length),
## "daily" (the visitor's minutes for today are used) or "busy" (the demo is full right now), and the server's words.
signal demo_limit(limit: String, message: String)

enum State { DISCONNECTED, CONNECTING, READY, PLAYER_TALKING, THINKING, SPEAKING }
enum MicMode { ALWAYS, PUSH_TO_TALK, OFF }

@export_group("Account")
## Your Lokutor API key (lokutor.com dashboard > API keys). Fine while prototyping; a shipped game
## should set ticket_provider so the key stays on your server. Empty: the LOKUTOR_API_KEY environment variable, and in a
## browser no key at all (Lokutor's own pages get its time-limited demo; see Lokutor.keyless_allowed).
@export var api_key: String = ""
## Optional: an agent built in the Lokutor dashboard. Its persona, voice and language are used unless set below.
@export var agent_id: String = ""

@export_group("Character")
## Who the character is, what it knows and how it talks.
@export_multiline var persona: String = "You are a friendly villager in a fantasy town. Answer in one or two short sentences."
## The line above what set_context tells the character. Write it in the language of the persona.
@export var context_heading: String = "Right now in the game:"
## F1-F5 (female) or M1-M5 (male), or a voice id from the Lokutor dashboard.
@export var voice: String = "F1"
## en, es, fr, pt, ca, gl, eu... or "auto": the character follows the language the player speaks.
## Empty = the default ("en"; a dashboard agent keeps its own).
@export var language: String = "en"
## Shifts the voice up or down: small creatures, giants.
@export_range(-12.0, 12.0, 0.1) var pitch: float = 0.0
## Start talking as soon as the scene starts. Off: call start_conversation() (e.g. when the player walks up).
@export var start_on_ready: bool = true
## Open the connection again when it drops; the character then starts a fresh conversation.
@export var auto_reconnect: bool = true

@export_group("Voice")
## The voice comes from this node's position (3D). Off: heard the same everywhere.
@export var spatial_voice: bool = true
## The audio bus the voice plays on: put a radio, a cave or a giant's chest on it.
@export var voice_bus: StringName = &"Master"
## Play through your own AudioStreamPlayer, AudioStreamPlayer2D (2D games) or AudioStreamPlayer3D.
@export var voice_output: Node

@export_group("Microphone")
@export var mic_mode: MicMode = MicMode.ALWAYS
## An Input Map action held to talk in PUSH_TO_TALK mode (or call set_talking()).
@export var push_to_talk_action: StringName = &""
## Mutes the microphone while the character talks, so speakers can't make it interrupt itself, and while any other
## LokutorAgent or LokutorVoice does, so that it does not answer them. Turn off when players use headphones and should be
## able to interrupt.
@export var mute_mic_while_speaking: bool = true
## When the microphone is open while the character talks and what the player "said" is the character's own last words,
## it is its echo: ignore it and mute the microphone while it talks from then on.
@export var guard_against_echo: bool = true

@export_group("Lip sync")
## A LokutorLipSync: blend shapes of any rig, jaw bones, 2D sprites. Or use the simple options below.
@export var lip_sync: LokutorLipSync
## A jaw (or head part) rotated around its local X axis as the character speaks.
@export var jaw: Node3D
@export var jaw_max_angle_degrees: float = 12.0
## Or a face mesh: the 15 viseme shapes are found by name.
@export var face: MeshInstance3D
## A mesh with only a mouth-open blend shape: its name.
@export var mouth_blend_shape: StringName = &""
@export_range(0.2, 5.0) var mouth_gain: float = 1.5

@export_group("Game actions")
## Actions the character can use, wired to methods in the Inspector.
@export var actions: Array[LokutorAction] = []
## The server has the character say "One moment" or the like while an action runs. Off: it says nothing until its answer.
@export var action_filler: bool = false
## What it begins to say before it calls an action is heard. Turn it off with an action that it must call before it answers
## ("how funny that was"): the model can write its plan there.
@export var speak_before_actions: bool = true

## For a shipped game: returns (or awaits) a short-lived ticket from YOUR server, which calls
## POST https://api.lokutor.com/ws-ticket with the API key.
var ticket_provider: Callable
var url: String = Lokutor.VOICE_AGENT_URL
## Your own language model: an endpoint {base_url, api_key, model...}, or a handler in the game (see LokutorVoiceAgentClient).
var llm: Dictionary = {}
var on_llm_request: Callable

var state: State = State.DISCONNECTED
## How open the mouth should be right now, 0..1, following the audio being heard.
var mouth_open: float = 0.0
## The player's microphone is off: the character hears silence.
var muted: bool = false:
	set(value):
		muted = value
		if microphone:
			microphone.muted = value
var is_speaking: bool:
	get: return state == State.SPEAKING
## What the microphone hears reaches the character right now. False while `muted`, and with mute_mic_while_speaking
## while a voice can still be heard (its own or another character's, the echo that follows it included): show this, not
## is_speaking, as the sign that the player may talk, or the start of what they say can be lost.
var hears_microphone: bool:
	get: return not muted and not (mute_mic_while_speaking and (_agent_audible() or _others_audible()))
var session_id: String:
	get: return client.session_id if client else ""
## Rate of the character's voice.
var playback_rate: int:
	get: return player.rate if player else Lokutor.OUTPUT_RATE

## The layers underneath, for what this node does not cover.
var client: LokutorVoiceAgentClient
var player: LokutorVoicePlayer
var microphone: LokutorMicrophone

## For tests: when true, everything the character says is also appended to recorded_output (mono).
var record_output := false
var recorded_output: PackedFloat32Array:
	get: return player.recorded if player else _recorded
## No sound card (headless runs, servers): the voice is consumed in real time without being played.
var simulate_audio_device := false

var _recorded := PackedFloat32Array()
var _code_actions: Dictionary = {}   # name -> {description, handler, params}
var _context: Dictionary = {}
var _context_due_ms := 0
var _talking_override := false
var _new_reply := false
var _last_audio_ms := 0
var _speaking_since_ms := 0
var _reply_audio_arrived := false
var _turn_has_voice := false         # something has been said since the player's last line
var _action_ms := -100000            # the last action was asked for; the server's filler begins right after
var _filler := 0                     # the generation being held back, and what came of it
var _held: Array = []
var _held_s := 0.0
var _held_since_ms := 0
var _dropped := 0                     # the generation that will not be heard: see speak_before_actions
var _turn_actions := 0                # since the player's last line
var _held_text_ms := 0
var _reply_open := false             # a reply is being made or spoken (audio outside one is the agent's background sound)
var _voice_end_s := 0.0              # when the reply's last mouth shape ends
var _last_viseme_ms := 0
var _echo_until_ms := 0
var _said: PackedStringArray = []        # the character's last replies, to know its echo
var _player_talking_since_ms := 0
## After an action the character still owes the reply that uses its result: until that reply's text
## arrives (or 8 s), it is thinking, not done.
var _reply_owed_until_ms := 0
var _mic_voice_ms := 0
var _resampler: LokutorAudio.StreamResampler


func _ready() -> void:
	add_to_group(LokutorVoice.GROUP)
	if Engine.is_editor_hint():
		return
	_find_lip_sync()
	if start_on_ready:
		start_conversation()


## Lets the character call a game function. `params` maps each argument to a description
## ({"item": "Which item"}) or to a JSON-schema dictionary ({"item": {"type": "string", "enum": [...]}}).
## The handler receives a Dictionary of arguments and returns a short sentence saying what happened.
func add_action(action_name: String, description: String, handler: Callable, params: Dictionary = {}) -> void:
	_code_actions[action_name] = {"description": description, "handler": handler, "params": params}
	_refresh_actions()


func remove_action(action_name: String) -> void:
	_code_actions.erase(action_name)
	_refresh_actions()


## Opens the conversation: the character greets the player and starts listening.
func start_conversation() -> void:
	if client != null:
		return
	var key := api_key if api_key != "" else OS.get_environment("LOKUTOR_API_KEY")
	if key == "" and not ticket_provider.is_valid() and not Lokutor.keyless_allowed():
		push_error("Lokutor: no API key. Paste one into the LokutorAgent's Api Key, or set ticket_provider for a shipped game.")
		error_occurred.emit("auth.missing_key", "No API key.")
		return
	_set_state(State.CONNECTING)
	var dashboard := agent_id != ""
	var config := {"api_key": key, "agent_id": agent_id, "server_url": url, "visemes": true, "llm": llm, "pitch": pitch,
		"tools": _tool_defs(), "prompt": _full_prompt()}
	# A dashboard agent keeps its own voice, language and prompt unless the game overrides them.
	if voice != "" or not dashboard:
		config["voice"] = voice if voice != "" else "F1"
	if language != "" or not dashboard:
		config["language"] = language if language != "" else "en"
	client = LokutorVoiceAgentClient.create(config)
	client.name = "Client"
	client.auto_reconnect = auto_reconnect
	client.auth = ticket_provider
	client.on_llm_request = on_llm_request
	player = LokutorVoicePlayer.new()
	player.name = "Voice"
	player.spatial = spatial_voice
	player.bus = voice_bus
	player.output = voice_output
	player.simulate_audio_device = simulate_audio_device
	player.record = record_output
	_new_reply = false
	_reply_open = false
	_reply_owed_until_ms = 0
	add_child(player)
	add_child(client)
	client.status.connect(_on_status)
	client.generation_started.connect(_on_generation)
	client.audio.connect(_on_audio)
	client.visemes.connect(_on_visemes)
	client.transcription.connect(func(t):
		_turn_actions = 0                # a line that the server does not announce with "listening"
		if guard_against_echo and is_echo(t):
			mute_mic_while_speaking = true
			echo_heard.emit(t)
			return
		player_said.emit(t))
	client.transcription_partial.connect(func(t): player_said_partial.emit(t))
	client.response.connect(_on_response)
	client.transcript_truncated.connect(func(heard, _full): character_interrupted.emit(heard))
	client.tool_call.connect(_on_tool_call)
	client.session_limit.connect(func(s, r):
		Lokutor.tell_page("session_limit", {"seconds": s, "reason": r})
		session_limit.emit(s, r))
	client.reconnected.connect(func(): reconnected.emit())
	client.error.connect(_on_error)
	client.ended.connect(func(_code, reason): _end_reason = reason)
	player.voice_played.connect(func(s, r): voice_played.emit(s, r))
	if mic_mode != MicMode.OFF:
		microphone = LokutorMicrophone.new()
		microphone.name = "Microphone"
		microphone.gate = _mic_open
		microphone.muted = muted
		add_child(microphone)
		microphone.captured_float.connect(feed_microphone)
		microphone.start()
	_end_reason = ""
	client.open()

var _end_reason := ""


## Ends the conversation and releases the microphone.
func stop_conversation() -> void:
	if client != null:
		_end("stopped")


## Changes who the character is, mid-conversation.
func set_persona(new_persona: String) -> void:
	persona = new_persona
	_push_prompt()


## Tells the character something about the game right now ("Time left": "20 seconds"), without rewriting its persona.
## A value of null takes it out. Changes in the same quarter second are sent together. A dashboard agent needs a
## persona for this: its own prompt cannot be extended from the game.
func set_context(key: String, value) -> void:
	if value == null:
		if not _context.erase(key):
			return
	elif _context.get(key) == value:
		return
	else:
		_context[key] = value
	if client != null and _context_due_ms == 0:
		_context_due_ms = Time.get_ticks_msec() + 250


func set_voice(new_voice: String) -> void:
	voice = new_voice
	if client != null:
		client.update_voice(new_voice)


func set_language(new_language: String) -> void:
	language = new_language
	if client != null:
		client.update_language(new_language)


func set_pitch(new_pitch: float) -> void:
	pitch = new_pitch
	if client != null:
		client.update_pitch(new_pitch)


## Forgets the conversation so far: the character starts fresh.
func reset_conversation() -> void:
	if client != null:
		client.reset()


func get_transcript() -> Array:
	return client.get_transcript() if client != null else []


## Push-to-talk from any input: true while the player holds the button.
func set_talking(talking: bool) -> void:
	_talking_override = talking


## Sends audio you captured yourself (mono floats at `rate`), e.g. from a custom source.
func feed_microphone(samples: PackedFloat32Array, rate: int) -> void:
	if client == null or not client.connected or samples.is_empty():
		return
	if _resampler == null or _resampler.from_rate != rate:
		_resampler = LokutorAudio.StreamResampler.new(rate, Lokutor.INPUT_RATE)
	var mono16 := _resampler.process(samples)
	if muted or (mute_mic_while_speaking and (_agent_audible() or _others_audible())):
		mono16 = mono16.duplicate()
		mono16.fill(0.0)
	if LokutorAudio.calculate_rms(mono16) > 0.02:
		_mic_voice_ms = Time.get_ticks_msec()
	client.send_audio(LokutorAudio.float32_to_pcm16(mono16))


## How much of the current reply has been heard, in seconds.
func heard_seconds() -> float:
	return player.heard_seconds() if player else 0.0


## Loudness of the character's voice right now, 0..1.
func voice_level() -> float:
	return player.level if player else 0.0


func _process(delta: float) -> void:
	if client == null:
		return
	if player.rate != client.playback_rate:
		player.rate = client.playback_rate
	if _context_due_ms != 0 and Time.get_ticks_msec() >= _context_due_ms:
		_context_due_ms = 0
		_push_prompt()
	# The reply to the action comes when what was said meanwhile has had time to be heard.
	if _filler != 0 and _held_text_ms != 0 and Time.get_ticks_msec() > maxi(_held_text_ms + 3000, _held_since_ms + int(_held_s * 1000.0) + 2000):
		_play_filler()
	_update_state()
	_update_player_talking()
	_update_mouth(delta)


func _exit_tree() -> void:
	stop_conversation()


# ---- Prompt and actions --------------------------------------------------------------------------

func _full_prompt() -> String:
	var p := persona
	if p == "":
		if agent_id != "":
			return ""
		p = "You are a friendly character in a video game. Keep answers short."
	if _context.is_empty():
		return p
	var lines := PackedStringArray()
	for k in _context:
		lines.append("- %s: %s" % [k, _context[k]])
	return p + "\n\n" + context_heading + "\n" + "\n".join(lines)


func _push_prompt() -> void:
	# A dashboard agent without a persona keeps its own prompt: sending one would replace it.
	if client != null and (persona != "" or agent_id == ""):
		client.update_prompt(_full_prompt())


func _refresh_actions() -> void:
	if client != null:
		client.update_tools(_tool_defs())


func _tool_defs() -> Array:
	var tools: Array = []
	for a in actions:
		if a != null and a.action_name.strip_edges() != "":
			tools.append(_tool_def(a.action_name.strip_edges(), a.description, {}))
	for action_name in _code_actions:
		var a: Dictionary = _code_actions[action_name]
		tools.append(_tool_def(action_name, a.description, a.params))
	return tools


func _tool_def(action_name: String, description: String, params: Dictionary) -> Dictionary:
	var props := {}
	var required: Array = []
	for p in params:
		var spec = params[p]
		if spec is String:
			props[p] = {"type": "string", "description": spec}
			required.append(p)
		elif spec is Dictionary:
			var schema: Dictionary = spec.duplicate()
			var optional: bool = schema.get("optional", false)
			schema.erase("optional")
			if not schema.has("type"):
				schema["type"] = "string"
			props[p] = schema
			if not optional:
				required.append(p)
	return {"type": "function", "function": {"name": action_name, "description": description,
		"parameters": {"type": "object", "properties": props, "required": required}}}


func _on_tool_call(tool: Dictionary) -> void:
	_reply_owed_until_ms = Time.get_ticks_msec() + 8000
	_turn_actions += 1
	if not action_filler and _filler == 0 and not _turn_has_voice:
		_action_ms = Time.get_ticks_msec()
	elif not speak_before_actions and _turn_actions == 1 and _filler == 0 and _reply_open and _reply_audio_arrived and player.heard_seconds() < 1.5:
		_dropped = player.generation
		player.clear()
		_reply_open = false
		if lip_sync:
			lip_sync.clear(self)
		_set_state(State.THINKING)
	if state != State.SPEAKING:
		_set_state(State.THINKING)
	var action_name: String = tool.name
	var args = JSON.parse_string(tool.arguments) if tool.arguments != "" else {}
	if not (args is Dictionary):
		args = {}
	var result := ""
	if _code_actions.has(action_name):
		var handler: Callable = _code_actions[action_name].handler
		if not handler.is_valid():
			result = JSON.stringify({"error": "The action %s is no longer available." % action_name})
		else:
			var r = await handler.call(args)
			result = "done" if r == null else str(r)
	else:
		var bound: LokutorAction = null
		for a in actions:
			if a != null and a.action_name.strip_edges() == action_name:
				bound = a
		if bound == null:
			result = JSON.stringify({"error": "This game has no action called %s." % action_name})
		else:
			var node := get_node_or_null(bound.target)
			if node != null and bound.method != &"" and node.has_method(bound.method):
				node.call(bound.method)
				result = bound.reply if bound.reply != "" else "Done."
			else:
				result = JSON.stringify({"error": "The action %s is not wired to a method." % action_name})
				push_warning("Lokutor: action '%s' has no valid target/method." % action_name)
	if tool.call_id != "" and client != null:
		client.submit_tool_result(tool.call_id, result)
	action_called.emit(action_name, args, result)


# ---- The conversation ----------------------------------------------------------------------------

func _on_generation(generation: int) -> void:
	_forget_filler()
	_dropped = 0
	if Time.get_ticks_msec() - _action_ms < 400:
		_filler = generation
		_held_since_ms = Time.get_ticks_msec()
		return
	_turn_has_voice = true
	player.begin(generation)
	if lip_sync:
		lip_sync.clear(self)
	_voice_end_s = 0.0
	_new_reply = true
	_reply_open = true
	_reply_audio_arrived = false


## What the server has it say while an action runs comes right after the action, before any text, and the reply to the
## action is another generation. It is held until that one comes: a "let me check", or what the model wrote before the
## action, which can be its plan ("so I call react first"). Should it turn out to be the reply (its text comes and nothing
## follows), it is played.
func _forget_filler() -> void:
	_filler = 0
	_held.clear()
	_held_s = 0.0
	_held_text_ms = 0


func _play_filler() -> void:
	var held := _held.duplicate()
	var generation := _filler
	_forget_filler()
	_action_ms = -100000
	_on_generation(generation)
	for h in held:
		if h is Array:
			_on_visemes(h, generation)
		else:
			_on_audio(h, generation)


func _on_status(value: String) -> void:
	if value == "listening" or value == "interrupted":
		_turn_has_voice = false
		_turn_actions = 0
		_action_ms = -100000
		if _filler != 0:                   # the player goes on talking: the reply may never come, and this is not one
			_dropped = _filler
			_forget_filler()
	if value == "interrupted":
		_forget_filler()
		_dropped = 0
	if (_filler != 0 or _dropped != 0) and (value == "thinking" or value == "speaking"):
		_set_state(State.THINKING)
		return
	match value:
		"connected":
			_set_state(State.READY)
		"listening":
			# The player started talking. Not an interruption yet: it may be a cough.
			if state != State.SPEAKING:
				_set_state(State.PLAYER_TALKING)
		"interrupted":
			player.clear()
			if lip_sync:
				lip_sync.clear(self)
			_reply_open = false
			_set_state(State.PLAYER_TALKING)
		"thinking":
			if _new_reply or (state != State.SPEAKING and not player.is_playing()):
				# The server says "thinking" again before each sentence of the same reply.
				_set_state(State.THINKING)
			_new_reply = false
		"speaking":
			_reply_open = true
			_set_state(State.SPEAKING)
		"reconnecting":
			player.clear()
			if lip_sync:
				lip_sync.clear(self)
			_reply_open = false
			_reply_owed_until_ms = 0
			_set_state(State.CONNECTING)
			reconnecting.emit()
		"disconnected":
			_end(_end_reason if _end_reason != "" else "connection lost")


func _on_audio(pcm: PackedByteArray, generation: int) -> void:
	if _dropped != 0 and generation == _dropped:
		return
	if _filler != 0 and generation == _filler:
		_held.append(pcm)
		_held_s += pcm.size() / 2.0 / player.rate
		if _held_s > 15.0:
			_play_filler()
		return
	player.push(pcm, generation)
	if not _reply_open:
		return
	_last_audio_ms = Time.get_ticks_msec()
	_new_reply = false                    # its "thinking" may come late: it is already talking
	if state != State.SPEAKING:
		_set_state(State.SPEAKING)
	_reply_audio_arrived = true


func _on_visemes(list: Array, generation: int) -> void:
	if _dropped != 0 and generation == _dropped:
		return
	if _filler != 0 and generation == _filler:
		_held.append(list)
		return
	if not list.is_empty():
		_last_viseme_ms = Time.get_ticks_msec()
		_voice_end_s = maxf(_voice_end_s, list[-1].t + list[-1].d)
	if lip_sync:
		lip_sync.push_visemes(list, self)
	visemes_received.emit(list)


func _on_response(text: String) -> void:
	_reply_owed_until_ms = 0             # the reply after an action has come
	_action_ms = -100000
	if _filler != 0 and _held_text_ms == 0:
		_held_text_ms = Time.get_ticks_msec()
	_said.append(text)
	if _said.size() > 3:
		_said.remove_at(0)
	character_said.emit(text)


## Whether `heard` is the character's own recent words coming back through the microphone: three words or more, and
## three in four of them among what it said in its last replies.
func is_echo(heard: String) -> bool:
	var these := _words(heard)
	if these.size() < 3 or _said.is_empty():
		return false
	var his := {}
	for w in _words(" ".join(_said)):
		his[w] = true
	var same := 0
	for w in these:
		if his.has(w):
			same += 1
	return same >= these.size() * 0.75


static var _word_re := RegEx.create_from_string("[\\p{L}\\p{N}]+")


static func _words(text: String) -> PackedStringArray:
	var out := PackedStringArray()
	for m in _word_re.search_all(text.to_lower()):
		out.append(m.get_string())
	return out


func _on_error(err: LokutorError) -> void:
	if err.code == "session.ended":
		return                             # conversation_ended carries it
	push_warning("Lokutor: %s" % err)
	if client != null and not client.connected and err.code in ["auth.missing_key", "ws.close", "internal.timeout"]:
		_end_reason = err.message
	if err.detail.begins_with("demo_"):
		_end_reason = err.message
		Lokutor.tell_page("demo_limit", {"limit": err.detail.trim_prefix("demo_"), "message": err.message})
		demo_limit.emit(err.detail.trim_prefix("demo_"), err.message)
	error_occurred.emit(err.code, err.message)


## True while the character can still be heard (and so could reach the microphone): voice waiting,
## voice in the sound card's buffers, or the room's echo just after.
func _agent_audible() -> bool:
	if player == null:
		return false
	if _reply_open and player.is_audible():
		return true
	return Time.get_ticks_msec() < _echo_until_ms


## Another character is being heard, so it could reach the microphone too.
func _others_audible() -> bool:
	for other in get_tree().get_nodes_in_group(LokutorVoice.GROUP):
		if other is LokutorVoice and other.mutes_microphones and other.player != null and other.player.is_audible():
			return true
		if other is LokutorAgent and other != self and other._agent_audible():
			return true
	return false


## A reply is over once its audio has arrived, nothing new has come for a moment, and it can no longer be heard. An
## agent with a background sound keeps sending audio: there, its voice is over when its last mouth shape has played.
func _reply_finished() -> bool:
	var now := Time.get_ticks_msec()
	if not _reply_audio_arrived:
		# The server says "speaking" when synthesis starts, which can be half a second before the first audio.
		return now - _speaking_since_ms > 5000
	if now - maxi(_last_audio_ms, _speaking_since_ms) > 400 and not player.is_audible():
		return true
	return _voice_end_s > 0.0 and player.heard_seconds() > _voice_end_s + 0.6 and now - _last_viseme_ms > 1500


func _update_state() -> void:
	if state == State.SPEAKING and _reply_finished():
		_reply_open = false
		_echo_until_ms = Time.get_ticks_msec() + (LokutorVoicePlayer.echo_tail_ms() if player.is_playing() else 0)
		_set_state(State.THINKING if _reply_owed() else State.READY)


func _update_player_talking() -> void:
	# The server says when the player starts talking, but not when a start led nowhere (a noise, or
	# speech it set aside). Without a reply under way, the player is done once the microphone has
	# been quiet for 4 s: the answer to a real line can take more than two to start.
	var now := Time.get_ticks_msec()
	if state == State.PLAYER_TALKING and now - _player_talking_since_ms > 4500 and now - _mic_voice_ms > 4000 and not _reply_owed():
		_set_state(State.READY)
	if state == State.THINKING and _reply_owed_until_ms != 0 and not _reply_owed():
		# The reply owed after an action never came (8 s): stop waiting for it.
		_reply_owed_until_ms = 0
		_set_state(State.READY)


func _reply_owed() -> bool:
	return Time.get_ticks_msec() < _reply_owed_until_ms


func _set_state(next: State) -> void:
	if next == State.SPEAKING and state != State.SPEAKING:
		_speaking_since_ms = Time.get_ticks_msec()
	if next == State.PLAYER_TALKING and state != State.PLAYER_TALKING:
		_player_talking_since_ms = Time.get_ticks_msec()
	if next == state:
		return
	state = next
	state_changed.emit(next)


func _end(reason: String) -> void:
	if client == null:
		return
	var c := client
	var p := player
	client = null
	for node in [microphone, c, p]:
		if node != null:
			node.queue_free()
	c.close()
	p.clear()
	microphone = null
	if p != null:
		_recorded = p.recorded
	player = null
	if lip_sync:
		lip_sync.release(self)
	mouth_open = 0.0
	_set_state(State.DISCONNECTED)
	conversation_ended.emit(reason)


# ---- Mouth ---------------------------------------------------------------------------------------

func _find_lip_sync() -> void:
	if lip_sync == null:
		for node in get_children() + (get_parent().get_children() if get_parent() else []):
			if node is LokutorLipSync:
				lip_sync = node
				break
	if lip_sync == null:
		# Also without a mouth of its own: mouth_open then follows the visemes for sprites and animation trees.
		lip_sync = LokutorLipSync.new()
		lip_sync.name = "LipSync"
		lip_sync.jaw = jaw
		lip_sync.jaw_max_angle_degrees = jaw_max_angle_degrees
		lip_sync.gain = mouth_gain
		if face:
			lip_sync.faces = [face]
			if mouth_blend_shape != &"":
				var one := {}
				for v in 15:
					one[Lokutor.VISEMES[v]] = {str(mouth_blend_shape): LokutorLipSync.OPENNESS[v]}
				lip_sync.mapping = one
		add_child(lip_sync)


func _update_mouth(delta: float) -> void:
	if lip_sync == null:
		return
	lip_sync.advance(player.heard_seconds(), player.level, state == State.SPEAKING or (_reply_open and player.is_playing()), delta, self)
	mouth_open = lip_sync.mouth_open


# ---- Microphone ----------------------------------------------------------------------------------

func _mic_open() -> bool:
	match mic_mode:
		MicMode.ALWAYS:
			return true
		MicMode.PUSH_TO_TALK:
			return _talking_override or (push_to_talk_action != &"" and InputMap.has_action(push_to_talk_action) and Input.is_action_pressed(push_to_talk_action))
	return false
