@icon("res://addons/lokutor/icon.svg")
class_name LokutorVoice
extends Node3D
## A character that says written lines (barks, narration, tutorials): text to speech played from its place in the
## world, with lip sync. Lines are kept on disk, so a line said again costs nothing and starts at once.

## Every LokutorVoice and LokutorAgent is in this group.
const GROUP := &"lokutor_voices"

signal line_started(text: String)
signal line_finished(text: String)
signal error_occurred(code: String, message: String)
## The lokutor.com demo (a browser with no key) would not say a line: "daily" (the visitor's minutes for today are
## used), "busy" (the demo is full right now) or "call", and the server's words. See LokutorAgent.demo_limit.
signal demo_limit(limit: String, message: String)

@export_group("Account")
## Empty: the LOKUTOR_API_KEY environment variable, and in a browser no key at all (Lokutor.keyless_allowed).
@export var api_key: String = ""

@export_group("Voice")
@export var voice: String = "F1"
@export var language: String = "en"
## The voice comes from this node's position (3D). Off: heard the same everywhere.
@export var spatial_voice: bool = true
@export var voice_bus: StringName = &"Master"
## Play through your own AudioStreamPlayer, AudioStreamPlayer2D or AudioStreamPlayer3D.
@export var voice_output: Node
## Agents do not listen while this voice is heard (see LokutorAgent.mute_mic_while_speaking). Off for a voice too far away
## to reach the player's microphone.
@export var mutes_microphones: bool = true
## Where lines are kept. Empty: nowhere. res://voices ships recorded lines with the game (see save_lines_to).
@export_dir var cache_dir: String = "user://lokutor_voices"

@export_group("Lip sync")
@export var lip_sync: LokutorLipSync
@export var jaw: Node3D
@export var face: MeshInstance3D

var ticket_provider: Callable
var url: String = Lokutor.TTS_URL
var simulate_audio_device := false
var mouth_open: float = 0.0
var speaking: bool:
	get: return _current != ""

var tts: LokutorTTSClient
var player: LokutorVoicePlayer

var _queue: Array = []
var _current := ""
var _generation := 0

## In a browser with no key (the lokutor.com demo, Lokutor.keyless_allowed) every voice speaks through this one connection:
## the demo has a few connections at once for all its visitors, and a game's lines are short.
static var _shared: LokutorTTSClient


func _ready() -> void:
	add_to_group(GROUP)
	if Lokutor.keyless_allowed() and api_key == "" and OS.get_environment("LOKUTOR_API_KEY") == "":
		if not is_instance_valid(_shared):
			_shared = LokutorTTSClient.new()
			_shared.name = "LokutorVoices"
			get_tree().root.add_child.call_deferred(_shared)
		tts = _shared
	else:
		tts = LokutorTTSClient.new()
		tts.name = "TTS"
		add_child(tts)
	player = LokutorVoicePlayer.new()
	player.name = "Voice"
	player.spatial = spatial_voice
	player.bus = voice_bus
	player.output = voice_output
	player.simulate_audio_device = simulate_audio_device
	add_child(player)
	if lip_sync == null:
		for node in get_children() + get_parent().get_children():
			if node is LokutorLipSync:
				lip_sync = node
	if lip_sync == null:
		lip_sync = LokutorLipSync.new()
		lip_sync.jaw = jaw
		if face:
			lip_sync.faces = [face]
		add_child(lip_sync)


## Says a line; with `await`, returns when it has been heard (true) or could not be said (false). Lines wait for each
## other. Options override the voice for this line: voice, language, interrupt (say it now, dropping what is being said).
func say(text: String, options: Dictionary = {}) -> bool:
	if options.get("interrupt", false):
		stop()
	var ticket := RefCounted.new()
	_queue.append(ticket)
	while not _queue.is_empty() and _queue[0] != ticket:
		await get_tree().process_frame
		if not _queue.has(ticket):
			return false                   # stop() dropped it while it waited
	if not is_inside_tree():
		return false
	_current = text
	_generation += 1
	player.begin(_generation)
	lip_sync.clear(self)
	line_started.emit(text)
	var ok := await _speak(text, options, ticket)
	while ok and _queue.has(ticket) and player.is_playing():
		await get_tree().process_frame
	var heard := ok and _queue.has(ticket)         # stop() takes the line out of the queue
	if _queue.has(ticket):
		_queue.erase(ticket)
		_current = ""
	line_finished.emit(text)
	return heard


## Stops at once and forgets the lines waiting.
func stop() -> void:
	for line in _queue:
		tts.cancel(line)                   # only the lines: what preload_lines() is fetching goes on
	_queue.clear()
	_current = ""
	player.clear()
	lip_sync.clear(self)


## Fetches lines before they are needed, so they start at once and, once kept, work without a network.
func preload_lines(lines: PackedStringArray, options: Dictionary = {}) -> void:
	for line in lines:
		if not has_line(line, options):
			var got := await _fetch(line, options)
			if got.ok:
				_store(line, options, got.audio, got.visemes)


## Whether a line is already on disk.
func has_line(text: String, options: Dictionary = {}) -> bool:
	return cache_dir != "" and FileAccess.file_exists(_path(text, options))


## Keeps lines in a project folder (run it from the editor or a tool script): set cache_dir to it and ship the game
## with its lines recorded.
func save_lines_to(folder: String, lines: PackedStringArray, options: Dictionary = {}) -> void:
	var before := cache_dir
	cache_dir = folder
	await preload_lines(lines, options)
	cache_dir = before


func _speak(text: String, options: Dictionary, ticket: RefCounted) -> bool:
	var kept := _load(text, options)
	if not kept.is_empty():
		player.push(kept.audio, _generation)
		lip_sync.push_visemes(kept.visemes, self)
		return true
	var mine := _generation
	var got := await _fetch(text, options, ticket, func(pcm): if _queue.has(ticket): player.push(pcm, mine),
		func(list): if _queue.has(ticket): lip_sync.push_visemes(list, self))
	if not got.ok:
		if got.error.code != "internal.cancelled":
			push_warning("Lokutor: %s" % got.error)
			if got.error.detail.begins_with("demo_"):
				Lokutor.tell_page("demo_limit", {"limit": got.error.detail.trim_prefix("demo_"), "message": got.error.message})
				demo_limit.emit(got.error.detail.trim_prefix("demo_"), got.error.message)
			error_occurred.emit(got.error.code, got.error.message)
		return false
	_store(text, options, got.audio, got.visemes)
	return true


func _fetch(text: String, options: Dictionary, ticket: RefCounted = null, on_audio := Callable(), on_visemes := Callable()) -> Dictionary:
	while not tts.is_inside_tree():                # the shared connection joins the tree a frame later
		await get_tree().process_frame
	tts.api_key = api_key
	tts.auth = ticket_provider
	tts.server_url = url
	var o := {"voice": options.get("voice", voice), "language": options.get("language", language), "visemes": true}
	if ticket != null:
		o["ticket"] = ticket
	if on_audio.is_valid():
		o["on_audio"] = on_audio
		o["on_visemes"] = on_visemes
	return await tts.synthesize(text, o)


func _path(text: String, options: Dictionary) -> String:
	var key := "%s|%s|%s" % [options.get("voice", voice), options.get("language", language), text]
	return cache_dir.path_join(key.sha1_text().left(20) + ".lokvoice")


func _store(text: String, options: Dictionary, pcm: PackedByteArray, list: Array) -> void:
	if cache_dir == "" or pcm.is_empty():
		return
	DirAccess.make_dir_recursive_absolute(cache_dir)
	var f := FileAccess.open(_path(text, options), FileAccess.WRITE)
	if f != null:
		f.store_var({"text": text, "rate": Lokutor.OUTPUT_RATE, "audio": pcm, "visemes": list})


func _load(text: String, options: Dictionary) -> Dictionary:
	if not has_line(text, options):
		return {}
	var f := FileAccess.open(_path(text, options), FileAccess.READ)
	var d = f.get_var() if f != null else null
	return d if d is Dictionary and d.has("audio") else {}


func _process(delta: float) -> void:
	if player == null:
		return
	lip_sync.advance(player.heard_seconds(), player.level, player.is_playing(), delta, self)
	mouth_open = lip_sync.mouth_open


func _exit_tree() -> void:
	if tts != null:
		stop()
	if lip_sync != null:
		lip_sync.release(self)
