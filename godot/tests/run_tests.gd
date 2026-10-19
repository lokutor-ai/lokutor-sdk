extends SceneTree
## Headless live test of the LokutorAgent node against production:
##   godot --headless --path . --script res://tests/run_tests.gd
## Needs LOKUTOR_API_KEY, and tests/lines/line1.pcm.. (16 kHz mono s16le; tests/make_lines.sh makes
## them with macOS `say`). The lines are fed as microphone audio in real time; the character's
## voice is consumed in real time without a sound card. Exit code 0 = passed.

const LINES := [
	"Hi! I'm heading into the mountains. What do you have for me?",
	"The iron sword sounds good. I'll take it.",
	"Thanks, Brom. Goodbye!",
]

var agent: LokutorAgent
var t0 := 0
var log_lines := PackedStringArray()
var heard_player := 0
var sold := false
var gold := 30
var spoke_since := -1.0
var failures := PackedStringArray()


func _initialize() -> void:
	_run()


func _log(line: String) -> void:
	var stamped := "[%6.2fs] %s" % [(Time.get_ticks_msec() - t0) / 1000.0, line]
	print(stamped)
	log_lines.append(stamped)


func _now() -> float:
	return (Time.get_ticks_msec() - t0) / 1000.0


func _run() -> void:
	t0 = Time.get_ticks_msec()
	if OS.get_environment("LOKUTOR_API_KEY") == "":
		print("set LOKUTOR_API_KEY")
		quit(2)
		return
	await process_frame

	agent = LokutorAgent.new()
	agent.start_on_ready = false
	# LOKUTOR_REAL_AUDIO=1: through a real AudioStreamGenerator (the dummy audio driver plays it).
	agent.simulate_audio_device = OS.get_environment("LOKUTOR_REAL_AUDIO") != "1"
	agent.record_output = true
	agent.mic_mode = LokutorAgent.MicMode.OFF
	agent.voice = "M1"
	agent.persona = "You are Brom, the blacksmith of Oakvale, a gruff but kind dwarf. Speak in one or two short sentences. " + \
		"Your shop sells: iron sword (20 gold), steel shield (25 gold), healing potion (5 gold). Nothing else. " + \
		"When the player agrees to buy something, call give_item; it takes the gold. Greet the player when they arrive."
	agent.add_action("give_item", "Sell an item to the player: takes the gold and puts the item in their bag.", _give_item,
		{"item": {"type": "string", "description": "The item to sell", "enum": ["iron sword", "steel shield", "healing potion"]}})
	agent.state_changed.connect(_on_state)
	agent.player_said.connect(_on_player_said)
	agent.character_said.connect(_on_character_said)
	agent.action_called.connect(_on_action)
	agent.error_occurred.connect(_on_error)
	agent.conversation_ended.connect(_on_ended)
	root.add_child(agent)

	_log("connecting")
	agent.start_conversation()
	await _wait_reply(15.0, 0.0)
	if spoke_since < 0:
		failures.append("no greeting")

	for i in LINES.size():
		var pcm := FileAccess.get_file_as_bytes("res://tests/lines/line%d.pcm" % (i + 1))
		if pcm.is_empty():
			failures.append("missing tests/lines/line%d.pcm (run tests/make_lines.sh)" % (i + 1))
			break
		_log("PLAYER (speaking): " + LINES[i])
		await _speak(pcm)
		var stopped := _now()
		var latency := await _wait_reply(20.0, stopped)
		if latency >= 0:
			_log("Brom answered %d ms after the player stopped" % int(latency * 1000))
		else:
			failures.append("no answer to line %d" % (i + 1))

	agent.stop_conversation()
	await process_frame

	if heard_player < 2:
		failures.append("only %d player lines recognised" % heard_player)
	if not sold:
		failures.append("give_item(iron sword) never ran")
	var voiced := 0
	for v in agent.recorded_output:
		if absf(v) > 0.01:
			voiced += 1
	if voiced < 44100:
		failures.append("less than a second of the character's voice")
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://tests/out"))
	_write_wav("res://tests/out/brom_godot.wav", agent.recorded_output, 44100)
	var f := FileAccess.open("res://tests/out/brom_godot_log.txt", FileAccess.WRITE)
	f.store_string("\n".join(log_lines) + "\n")
	f.close()

	if failures.is_empty():
		print("GODOT LIVE TEST: PASSED (%d player lines recognised, give_item ran, %.1f s of voice)" % [heard_player, voiced / 44100.0])
		quit(0)
	else:
		print("GODOT LIVE TEST: FAILED: " + "; ".join(failures))
		quit(1)


func _give_item(args: Dictionary) -> String:
	var item: String = args.get("item", "")
	if item != "iron sword":
		return "Gave the %s." % item
	gold -= 20
	sold = true
	return "Gave the iron sword. The player paid 20 gold and has %d left." % gold


func _on_state(s: LokutorAgent.State) -> void:
	_log("state: " + LokutorAgent.State.keys()[s])
	if s == LokutorAgent.State.SPEAKING:
		spoke_since = _now()


func _on_player_said(text: String) -> void:
	heard_player += 1
	_log("PLAYER (recognised): " + text)


func _on_character_said(text: String) -> void:
	_log("BROM: " + text)


func _on_action(action_name: String, args: Dictionary, result: String) -> void:
	_log("ACTION %s(%s) -> %s" % [action_name, JSON.stringify(args), result])


func _on_error(code: String, message: String) -> void:
	_log("ERROR %s: %s" % [code, message])


func _on_ended(reason: String) -> void:
	_log("ended: " + reason)


## Feeds 16 kHz PCM16 as microphone audio in real time, then keeps sending silence like a real mic.
func _speak(pcm: PackedByteArray) -> void:
	var samples := PackedFloat32Array()
	samples.resize(pcm.size() >> 1)
	for i in samples.size():
		samples[i] = pcm.decode_s16(i * 2) / 32768.0
	var start := Time.get_ticks_msec()
	var sent := 0
	while sent < samples.size():
		await process_frame
		var due := mini(int((Time.get_ticks_msec() - start) * 16.0), samples.size())
		if due > sent:
			agent.feed_microphone(samples.slice(sent, due), 16000)
			sent = due


## Keeps the mic stream going (silence) until the character has answered and finished.
func _wait_reply(timeout: float, since: float) -> float:
	var end := _now() + timeout
	var last := Time.get_ticks_msec()
	while _now() < end:
		await process_frame
		var now := Time.get_ticks_msec()
		var gap := int((now - last) * 16.0)
		if gap > 0:
			var silence := PackedFloat32Array()
			silence.resize(gap)
			agent.feed_microphone(silence, 16000)
			last = now
		if spoke_since > since and agent.state == LokutorAgent.State.READY:
			break
	return spoke_since - since if spoke_since > since else -1.0


func _write_wav(path: String, samples: PackedFloat32Array, rate: int) -> void:
	var data := PackedByteArray()
	data.resize(samples.size() * 2)
	for i in samples.size():
		data.encode_s16(i * 2, int(clampf(samples[i], -1.0, 1.0) * 32767.0))
	var f := FileAccess.open(path, FileAccess.WRITE)
	f.store_buffer("RIFF".to_ascii_buffer())
	f.store_32(36 + data.size())
	f.store_buffer("WAVEfmt ".to_ascii_buffer())
	f.store_32(16)
	f.store_16(1)
	f.store_16(1)
	f.store_32(rate)
	f.store_32(rate * 2)
	f.store_16(2)
	f.store_16(16)
	f.store_buffer("data".to_ascii_buffer())
	f.store_32(data.size())
	f.store_buffer(data)
	f.close()
