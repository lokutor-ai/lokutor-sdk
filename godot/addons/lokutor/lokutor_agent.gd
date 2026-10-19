@icon("res://addons/lokutor/icon.svg")
class_name LokutorAgent
extends Node3D
## Makes its parent character a voice agent: it listens through the microphone, answers in its own
## voice from where it stands (3D audio), moves its mouth, and calls your game's functions
## ("actions") when the conversation calls for it.

signal player_said(text: String)
signal player_said_partial(text: String)
signal character_said(text: String)
## The player cut the character off; `heard` is what they actually heard.
signal character_interrupted(heard: String)
signal state_changed(state: State)
## An action ran: its name, the arguments the character chose, and what it returned.
signal action_called(action_name: String, args: Dictionary, result: String)
signal error_occurred(code: String, message: String)
signal conversation_ended(reason: String)

enum State { DISCONNECTED, CONNECTING, READY, PLAYER_TALKING, THINKING, SPEAKING }
enum MicMode { ALWAYS, PUSH_TO_TALK, OFF }

@export_group("Account")
## Your Lokutor API key (lokutor.com dashboard > API keys). Fine while prototyping; a shipped game
## should set ticket_provider so the key stays on your server. Empty: the LOKUTOR_API_KEY environment variable.
@export var api_key: String = ""
## Optional: an agent built in the Lokutor dashboard. Its persona, voice and language are used unless set below.
@export var agent_id: String = ""

@export_group("Character")
## Who the character is, what it knows and how it talks.
@export_multiline var persona: String = "You are a friendly villager in a fantasy town. Answer in one or two short sentences."
## F1-F5 (female) or M1-M5 (male), or a voice id from the Lokutor dashboard.
@export var voice: String = "F1"
## en, es, fr, pt, ca, gl, eu...
@export var language: String = "en"
## Start talking as soon as the scene starts. Off: call start_conversation() (e.g. when the player walks up).
@export var start_on_ready: bool = true
## The voice comes from this node's position (3D). Off: heard the same everywhere.
@export var spatial_voice: bool = true

@export_group("Microphone")
@export var mic_mode: MicMode = MicMode.ALWAYS
## An Input Map action held to talk in PUSH_TO_TALK mode (or call set_talking()).
@export var push_to_talk_action: StringName = &""
## Mutes the microphone while the character talks, so speakers can't make it interrupt itself.
## Turn off when players use headphones and should be able to interrupt.
@export var mute_mic_while_speaking: bool = true

@export_group("Lip sync")
## A jaw (or head part) rotated around its local X axis as the character speaks.
@export var jaw: Node3D
@export var jaw_max_angle_degrees: float = 12.0
## Or a mesh with a mouth-open blend shape.
@export var face: MeshInstance3D
@export var mouth_blend_shape: StringName = &""
@export_range(0.2, 5.0) var mouth_gain: float = 1.5

@export_group("Game actions")
## Actions the character can use, wired to methods in the Inspector.
@export var actions: Array[LokutorAction] = []

## For a shipped game: returns (or awaits) a short-lived ticket from YOUR server, which calls
## POST https://api.lokutor.com/ws-ticket with the API key.
var ticket_provider: Callable
var url: String = "wss://api.lokutor.com/ws/agent"
var state: State = State.DISCONNECTED
## How open the mouth should be right now, 0..1, following the audio being heard.
var mouth_open: float = 0.0
var session_id: String = ""

const INPUT_RATE := 16000
const PLAYBACK_RATE := 44100

var _ws: WebSocketPeer
var _was_open := false
var _code_actions: Dictionary = {}   # name -> {description, handler, params}
var _generation := 0
var _talking_override := false
var _jaw_rest: Vector3
var _last_audio_ms := 0
var _speaking_since_ms := 0
var _reply_audio_arrived := false
var _player_talking_since_ms := 0
var _mic_voice_ms := 0

# Playback: decoded voice waits in _voice until the generator has room.
var _player: Node
var _generator: AudioStreamGenerator
var _gen_playback: AudioStreamGeneratorPlayback
var _voice := PackedVector2Array()
var _voice_read := 0
var _playback_rate := PLAYBACK_RATE
var _mouth_schedule: Array = []     # [due_msec, level]
var _mouth_target := 0.0
var _pushed_since_clear := false
var _gen_capacity := 0
## When the voice queued in the generator will have finished coming out of the speakers.
var _audible_until_ms := 0
## After the voice stops, the room's echo still reaches the microphone for a moment.
const ECHO_TAIL_MS := 250

# Microphone: a muted bus with a capture effect.
var _mic_player: AudioStreamPlayer
var _capture: AudioEffectCapture
var _mic_bus := -1
var _resample_pos := 0.0
var _resample_carry := PackedFloat32Array()

## For tests: when true, everything the character says is also appended here (mono).
var record_output := false
var recorded_output := PackedFloat32Array()
## No sound card (headless runs, servers): the voice is consumed in real time without being played.
var simulate_audio_device := false
var _sim_last_us := 0


func _ready() -> void:
	if jaw:
		_jaw_rest = jaw.rotation
	if start_on_ready and not Engine.is_editor_hint():
		start_conversation()


## Lets the character call a game function. `params` maps each argument to a description
## ({"item": "Which item"}) or to a JSON-schema dictionary ({"item": {"type": "string", "enum": [...]}}).
## The handler receives a Dictionary of arguments and returns a short sentence saying what happened.
func add_action(action_name: String, description: String, handler: Callable, params: Dictionary = {}) -> void:
	_code_actions[action_name] = {"description": description, "handler": handler, "params": params}
	if _is_open():
		_send_tools()


## Opens the conversation: the character greets the player and starts listening.
func start_conversation() -> void:
	if _ws != null:
		return
	var key := api_key if api_key != "" else OS.get_environment("LOKUTOR_API_KEY")
	if key == "" and not ticket_provider.is_valid():
		push_error("Lokutor: no API key. Paste one into the LokutorAgent's Api Key, or set ticket_provider for a shipped game.")
		return
	_set_state(State.CONNECTING)
	var query := PackedStringArray()
	if agent_id != "":
		query.append("agent_id=" + agent_id.uri_encode())
	var ws := WebSocketPeer.new()
	ws.inbound_buffer_size = 4 * 1024 * 1024   # a reply's audio can arrive in large frames
	ws.outbound_buffer_size = 1024 * 1024
	ws.max_queued_packets = 8192
	var headers := PackedStringArray(["User-Agent: lokutor-godot/0.1"])
	if ticket_provider.is_valid():
		var ticket = await ticket_provider.call()
		query.append("api_key=" + str(ticket).uri_encode())
	else:
		headers.append("X-API-Key: " + key)
	ws.handshake_headers = headers
	var full_url := url + ("?" + "&".join(query) if query.size() > 0 else "")
	var err := ws.connect_to_url(full_url)
	if err != OK:
		push_error("Lokutor: could not start the connection (%s)." % error_string(err))
		_set_state(State.DISCONNECTED)
		return
	_ws = ws
	_was_open = false
	_generation = 0
	_sim_last_us = 0
	_clear_voice()
	_setup_audio()


## Ends the conversation and releases the microphone.
func stop_conversation() -> void:
	if _ws != null:
		_ws.close(1000, "bye")
	_end("stopped")


## Changes who the character is, mid-conversation.
func set_persona(new_persona: String) -> void:
	persona = new_persona
	if _is_open():
		_send({"type": "prompt", "data": new_persona})


## Push-to-talk from any input: true while the player holds the button.
func set_talking(talking: bool) -> void:
	_talking_override = talking


## Sends audio you captured yourself (mono floats at `rate`), e.g. from a custom source.
func feed_microphone(samples: PackedFloat32Array, rate: int) -> void:
	if not _is_open() or samples.is_empty():
		return
	var mono16 := _resample(samples, rate, INPUT_RATE)
	if mute_mic_while_speaking and _agent_audible():
		mono16.fill(0.0)
	var energy := 0.0
	for v in mono16:
		energy += v * v
	if mono16.size() > 0 and sqrt(energy / mono16.size()) > 0.02:
		_mic_voice_ms = Time.get_ticks_msec()
	var bytes := PackedByteArray()
	bytes.resize(mono16.size() * 2)
	for i in mono16.size():
		bytes.encode_s16(i * 2, int(clampf(mono16[i], -1.0, 1.0) * 32767.0))
	_ws.put_packet(bytes)


func _process(_delta: float) -> void:
	if _ws == null:
		return
	_ws.poll()
	var ws_state := _ws.get_ready_state()
	if ws_state == WebSocketPeer.STATE_OPEN:
		if not _was_open:
			_was_open = true
			_send_config()
		while _ws.get_available_packet_count() > 0:
			var packet := _ws.get_packet()
			if _ws.was_string_packet():
				_handle_message(packet.get_string_from_utf8())
			else:
				_queue_voice(packet, _generation)
		_pump_microphone()
	elif ws_state == WebSocketPeer.STATE_CLOSED:
		var reason := _ws.get_close_reason()
		if reason == "":
			reason = "closed (%d)" % _ws.get_close_code() if _was_open else "could not connect (check the API key and network)"
		_end(reason)
		return
	_pump_playback()
	_update_state()
	_update_player_talking()
	_update_mouth()


func _exit_tree() -> void:
	stop_conversation()


# ---- Protocol -----------------------------------------------------------------------------------

func _send(msg: Dictionary) -> void:
	_ws.send_text(JSON.stringify(msg))


func _send_config() -> void:
	var dashboard := agent_id != ""
	# A dashboard agent keeps its own voice, language and prompt unless the game overrides them.
	if voice != "" or not dashboard:
		_send({"type": "voice", "data": voice if voice != "" else "F1"})
	if language != "" or not dashboard:
		_send({"type": "language", "data": language if language != "" else "en"})
	_send_tools()
	_send({"type": "visemes", "data": false})
	if persona != "" or not dashboard:
		_send({"type": "prompt", "data": persona if persona != "" else "You are a friendly character in a video game. Keep answers short."})
	# Last: the server starts the conversation once it has the whole configuration.
	_send({"type": "rates", "playback": PLAYBACK_RATE, "input": INPUT_RATE})


func _send_tools() -> void:
	var tools: Array = []
	for a in actions:
		if a != null and a.action_name.strip_edges() != "":
			tools.append(_tool_def(a.action_name.strip_edges(), a.description, {}))
	for action_name in _code_actions:
		var a: Dictionary = _code_actions[action_name]
		tools.append(_tool_def(action_name, a.description, a.params))
	if not tools.is_empty():
		_send({"type": "tools", "data": tools})


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


func _handle_message(text: String) -> void:
	var msg = JSON.parse_string(text)
	if not (msg is Dictionary):
		return
	match str(msg.get("type", "")):
		"audio":
			var b64: String = str(msg.get("data", ""))
			if b64 != "":
				_queue_voice(Marshalls.base64_to_raw(b64), int(msg.get("generation", 0)))
		"server_rates":
			var playback := int(msg.get("playback", PLAYBACK_RATE))
			if playback > 0 and playback != _playback_rate:
				_playback_rate = playback
				if not simulate_audio_device:
					_setup_playback()
		"status":
			_on_status(str(msg.get("data", "")), int(msg.get("generation", 0)), str(msg.get("session_id", "")))
		"transcript":
			var t: String = str(msg.get("data", msg.get("text", "")))
			if str(msg.get("role", "user")) == "agent":
				character_said.emit(t)
			elif msg.get("partial", false):
				player_said_partial.emit(t)
			else:
				player_said.emit(t)
		"transcript_truncated":
			character_interrupted.emit(str(msg.get("data", "")))
		"tool_call":
			_on_tool_call(str(msg.get("name", "")), str(msg.get("arguments", "{}")), str(msg.get("call_id", "")))
		"error":
			var d = msg.get("data")
			if d is Dictionary:
				error_occurred.emit(str(d.get("code", "error")), str(d.get("message", "")))
				push_warning("Lokutor: %s: %s" % [d.get("code", "error"), d.get("message", "")])
			else:
				error_occurred.emit("error", str(d))
				push_warning("Lokutor: " + str(d))


func _on_status(status: String, generation: int, sid: String) -> void:
	match status:
		"connected":
			if sid != "":
				session_id = sid
			_set_state(State.READY)
		"listening":
			# The player started talking. Not an interruption yet: it may be a cough.
			if state != State.SPEAKING:
				_set_state(State.PLAYER_TALKING)
		"interrupted":
			_clear_voice()
			_set_state(State.PLAYER_TALKING)
		"thinking":
			if generation > _generation:
				_generation = generation
				_clear_voice()
				_set_state(State.THINKING)
			elif state != State.SPEAKING and not _voice_playing():
				# The server says "thinking" again before each sentence of the same reply.
				_set_state(State.THINKING)
		"speaking":
			_set_state(State.SPEAKING)


func _on_tool_call(action_name: String, arguments: String, call_id: String) -> void:
	var args = JSON.parse_string(arguments) if arguments != "" else {}
	if not (args is Dictionary):
		args = {}
	var result := ""
	if _code_actions.has(action_name):
		var handler: Callable = _code_actions[action_name].handler
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
	if call_id != "" and _is_open():
		_send({"type": "tool_result", "data": {"call_id": call_id, "result": result}})
	action_called.emit(action_name, args, result)


# ---- Voice ---------------------------------------------------------------------------------------

func _setup_audio() -> void:
	if not simulate_audio_device:
		_setup_playback()
	if mic_mode != MicMode.OFF:
		_setup_microphone()


func _setup_playback() -> void:
	if _player != null:
		_player.queue_free()
	_generator = AudioStreamGenerator.new()
	_generator.mix_rate = _playback_rate
	_generator.buffer_length = 0.25
	if spatial_voice:
		var p3 := AudioStreamPlayer3D.new()
		p3.unit_size = 3.0
		p3.max_distance = 40.0
		_player = p3
	else:
		_player = AudioStreamPlayer.new()
	_player.name = "LokutorVoice"
	_player.stream = _generator
	add_child(_player)
	_player.play()
	_gen_playback = _player.get_stream_playback()
	_gen_capacity = _gen_playback.get_frames_available()


func _queue_voice(pcm16: PackedByteArray, generation: int) -> void:
	if generation < _generation:
		return   # a reply that has been superseded
	if generation > _generation:
		_generation = generation
		_clear_voice()
	var n := pcm16.size() >> 1
	var start := _voice.size()
	_voice.resize(start + n)
	for i in n:
		var v := pcm16.decode_s16(i * 2) / 32768.0
		_voice[start + i] = Vector2(v, v)
	_last_audio_ms = Time.get_ticks_msec()
	if state != State.SPEAKING:
		_set_state(State.SPEAKING)
	_reply_audio_arrived = true


func _pump_playback() -> void:
	var left := _voice.size() - _voice_read
	var n := 0
	var queued_ms := 0
	if simulate_audio_device:
		var now := Time.get_ticks_usec()
		if _sim_last_us == 0:
			_sim_last_us = now
		n = mini(int((now - _sim_last_us) * _playback_rate / 1000000.0), left)
		_sim_last_us = now
	else:
		if _gen_playback == null:
			return
		var room := _gen_playback.get_frames_available()
		n = mini(room, left)
		queued_ms = int((_generator.buffer_length * _playback_rate - room) * 1000.0 / _playback_rate)
	if n <= 0:
		return
	var chunk := _voice.slice(_voice_read, _voice_read + n)
	if not simulate_audio_device:
		_gen_playback.push_buffer(chunk)
		_pushed_since_clear = true
		# Everything queued in the generator, plus the sound card's own latency, is still to be heard.
		var queued_after := _gen_capacity - _gen_playback.get_frames_available()
		_audible_until_ms = Time.get_ticks_msec() + int(queued_after * 1000.0 / _playback_rate + AudioServer.get_output_latency() * 1000.0)
	else:
		_audible_until_ms = Time.get_ticks_msec()
	_voice_read += n
	var sum := 0.0
	for f in chunk:
		sum += f.x * f.x
		if record_output:
			recorded_output.append(f.x)
	# Lip sync: this chunk is heard once what is already queued in the generator has played.
	_mouth_schedule.append([Time.get_ticks_msec() + queued_ms, sqrt(sum / n)])
	if _voice_read > 1 << 20:
		_voice = _voice.slice(_voice_read)
		_voice_read = 0


func _voice_playing() -> bool:
	return _voice.size() - _voice_read > 0


func _clear_voice() -> void:
	_voice = PackedVector2Array()
	_voice_read = 0
	_mouth_schedule.clear()
	# Godot only empties a generator that is stopped (clear_buffer() refuses while it plays), so the
	# player is restarted instead: a new playback starts with an empty buffer. Only when something
	# was queued, so a reply that never started does not cost a restart.
	if _gen_playback != null and _pushed_since_clear and _player != null and _player.is_inside_tree():
		_player.stop()
		_player.play()
		_gen_playback = _player.get_stream_playback()
		_gen_capacity = _gen_playback.get_frames_available()
	_pushed_since_clear = false
	_audible_until_ms = 0


## True while the character can still be heard (and so could reach the microphone): voice waiting,
## voice in the sound card's buffers, or the room's echo just after.
func _agent_audible() -> bool:
	return _voice_playing() or Time.get_ticks_msec() < _audible_until_ms + ECHO_TAIL_MS


func _update_state() -> void:
	# Speaking ends once the voice has played out and nothing new has arrived for a moment.
	# The server says "speaking" when synthesis starts, which can be half a second before the first
	# audio. A reply is over once its audio has arrived, nothing new has come for a moment, and it can
	# no longer be heard (sound card buffers and the room's echo included): READY then means the
	# microphone is really open again. Without any audio, give up after 5 s.
	if state == State.SPEAKING and not _voice_playing():
		var now := Time.get_ticks_msec()
		var finished := (now - maxi(_last_audio_ms, _speaking_since_ms) > 400 and not _agent_audible()) if _reply_audio_arrived \
			else now - _speaking_since_ms > 5000
		if finished:
			_set_state(State.READY)


func _update_player_talking() -> void:
	# The server says when the player starts talking, but not when a start led nowhere (a noise, or
	# speech it set aside). Without a reply under way, the player is done once the microphone has
	# been quiet for 2 s.
	var now := Time.get_ticks_msec()
	if state == State.PLAYER_TALKING and now - _player_talking_since_ms > 2500 and now - _mic_voice_ms > 2000:
		_set_state(State.READY)


func _update_mouth() -> void:
	var now := Time.get_ticks_msec()
	while not _mouth_schedule.is_empty() and _mouth_schedule[0][0] <= now:
		_mouth_target = _mouth_schedule.pop_front()[1]
	if _mouth_schedule.is_empty() and not _voice_playing() and now - _last_audio_ms > 300:
		_mouth_target = 0.0
	var target := clampf(_mouth_target * 4.0 * mouth_gain, 0.0, 1.0)
	mouth_open = lerpf(mouth_open, target, 0.6 if target > mouth_open else 0.25)
	if jaw:
		jaw.rotation = _jaw_rest + Vector3(deg_to_rad(jaw_max_angle_degrees) * mouth_open, 0.0, 0.0)
	if face and mouth_blend_shape != &"":
		var idx := face.find_blend_shape_by_name(mouth_blend_shape)
		if idx >= 0:
			face.set_blend_shape_value(idx, mouth_open)


# ---- Microphone ----------------------------------------------------------------------------------

func _setup_microphone() -> void:
	if not ProjectSettings.get_setting("audio/driver/enable_input", false):
		push_warning("Lokutor: Project Settings > Audio > Driver > Enable Input is off, so the character cannot hear the microphone. Enable the Lokutor plugin, or turn it on.")
	_mic_bus = AudioServer.get_bus_index("LokutorMic")
	if _mic_bus == -1:
		_mic_bus = AudioServer.bus_count
		AudioServer.add_bus(_mic_bus)
		AudioServer.set_bus_name(_mic_bus, "LokutorMic")
		AudioServer.add_bus_effect(_mic_bus, AudioEffectCapture.new())
		AudioServer.set_bus_mute(_mic_bus, true)   # captured, not played back to the player
	_capture = AudioServer.get_bus_effect(_mic_bus, 0) as AudioEffectCapture
	_capture.clear_buffer()
	_mic_player = AudioStreamPlayer.new()
	_mic_player.name = "LokutorMicrophone"
	_mic_player.stream = AudioStreamMicrophone.new()
	_mic_player.bus = &"LokutorMic"
	add_child(_mic_player)
	_mic_player.play()


func _mic_open() -> bool:
	match mic_mode:
		MicMode.ALWAYS:
			return true
		MicMode.PUSH_TO_TALK:
			return _talking_override or (push_to_talk_action != &"" and InputMap.has_action(push_to_talk_action) and Input.is_action_pressed(push_to_talk_action))
	return false


func _pump_microphone() -> void:
	if _capture == null:
		return
	var frames := _capture.get_frames_available()
	if frames <= 0:
		return
	var stereo := _capture.get_buffer(frames)
	var mono := PackedFloat32Array()
	mono.resize(stereo.size())
	var open := _mic_open()
	for i in stereo.size():
		mono[i] = (stereo[i].x + stereo[i].y) * 0.5 if open else 0.0
	feed_microphone(mono, int(AudioServer.get_mix_rate()))


## Streaming resampler with its phase kept across calls; averages when downsampling.
func _resample(input: PackedFloat32Array, from_rate: int, to_rate: int) -> PackedFloat32Array:
	if from_rate == to_rate:
		return input
	var buf := _resample_carry + input
	var step := float(from_rate) / to_rate
	var out := PackedFloat32Array()
	out.resize(int((buf.size() - _resample_pos) / step) + 1)
	var n := 0
	var pos := _resample_pos
	if step > 1.0:
		while pos + step <= buf.size():
			var a := int(pos)
			var b := int(pos + step)
			var sum := 0.0
			for k in range(a, b):
				sum += buf[k]
			out[n] = sum / maxi(1, b - a)
			n += 1
			pos += step
	else:
		while pos + 1.0 < buf.size():
			var a := int(pos)
			var t := pos - a
			out[n] = buf[a] * (1.0 - t) + buf[a + 1] * t
			n += 1
			pos += step
	var keep := mini(int(pos), buf.size())
	_resample_carry = buf.slice(keep)
	_resample_pos = pos - keep
	out.resize(n)
	return out


# ---- Lifecycle -----------------------------------------------------------------------------------

func _is_open() -> bool:
	return _ws != null and _ws.get_ready_state() == WebSocketPeer.STATE_OPEN


func _set_state(next: State) -> void:
	if next == State.SPEAKING and state != State.SPEAKING:
		_speaking_since_ms = Time.get_ticks_msec()
		_reply_audio_arrived = false
	if next == State.PLAYER_TALKING and state != State.PLAYER_TALKING:
		_player_talking_since_ms = Time.get_ticks_msec()
	if next == state:
		return
	state = next
	state_changed.emit(next)


func _end(reason: String) -> void:
	if _ws == null:
		return
	_ws = null
	if _mic_player != null:
		_mic_player.queue_free()
		_mic_player = null
	_capture = null
	_clear_voice()
	_set_state(State.DISCONNECTED)
	conversation_ended.emit(reason)
