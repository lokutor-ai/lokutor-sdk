class_name LokutorMicrophone
extends Node
## The player's microphone as 16 kHz PCM, ready to send. Needs Project Settings > Audio > Driver > Enable Input
## (enabling the plugin turns it on).

## 16-bit PCM, 16 kHz, mono.
signal captured(pcm16: PackedByteArray)
## The same before conversion: mono floats at `rate`.
signal captured_float(samples: PackedFloat32Array, rate: int)

const ANDROID_PERMISSION := "android.permission.RECORD_AUDIO"

## Sends silence instead of the microphone (the server still needs to hear pauses).
var muted: bool = false
## Called each frame, if set: return false to send silence (push to talk, or the character is talking).
var gate: Callable
## Loudness, 0..1.
var level: float = 0.0
var running: bool:
	get: return _player != null

var _player: AudioStreamPlayer
var _capture: AudioEffectCapture
var _resampler: LokutorAudio.StreamResampler
var _bus: StringName          # one per microphone: two characters listening must not take turns reading one capture
static var _open := 0


## Starts capturing. False when the project has no audio input.
func start() -> bool:
	if _player != null:
		return true
	if not ProjectSettings.get_setting("audio/driver/enable_input", false):
		push_warning("Lokutor: Project Settings > Audio > Driver > Enable Input is off, so the microphone cannot be heard. Enable the Lokutor plugin, or turn it on.")
		return false
	_bus = StringName("LokutorMic%d" % get_instance_id())
	var bus := AudioServer.bus_count
	AudioServer.add_bus(bus)
	AudioServer.set_bus_name(bus, _bus)
	AudioServer.add_bus_effect(bus, AudioEffectCapture.new())
	AudioServer.set_bus_mute(bus, true)          # captured, not played back to the player
	_capture = AudioServer.get_bus_effect(bus, 0) as AudioEffectCapture
	_capture.clear_buffer()
	_player = AudioStreamPlayer.new()
	_player.name = "LokutorMicrophone"
	_player.stream = AudioStreamMicrophone.new()
	_player.bus = _bus
	add_child(_player)
	_open += 1
	if _may_record():
		_player.play()
	_resampler = LokutorAudio.StreamResampler.new(int(AudioServer.get_mix_rate()), Lokutor.INPUT_RATE)
	return true


## On Android the player has to say yes first: a microphone that plays before that stays silent.
func _may_record() -> bool:
	if OS.get_name() != "Android" or OS.request_permission(ANDROID_PERMISSION):
		return true
	var answered := get_tree().on_request_permissions_result
	if not answered.is_connected(_on_permission):
		answered.connect(_on_permission)
	return false


func _on_permission(permission: String, granted: bool) -> void:
	if permission != ANDROID_PERMISSION:
		return
	get_tree().on_request_permissions_result.disconnect(_on_permission)
	if not granted:
		push_warning("Lokutor: the player did not let the game use the microphone.")
	elif _player != null and not _player.playing:
		_player.play()


func stop() -> void:
	if _player != null:
		_player.queue_free()
		_player = null
		_open -= 1
	_capture = null
	level = 0.0
	if _bus != &"":
		var bus := AudioServer.get_bus_index(_bus)
		if bus > 0:
			AudioServer.remove_bus(bus)
		_bus = &""


## The microphones of this computer. Choose one with AudioServer.input_device.
static func devices() -> PackedStringArray:
	return AudioServer.get_input_device_list()


func _process(_delta: float) -> void:
	if _capture == null:
		return
	var frames := _capture.get_frames_available()
	if frames <= 0:
		return
	var stereo := _capture.get_buffer(frames)
	var mono := PackedFloat32Array()
	mono.resize(stereo.size())
	var sum := 0.0
	for i in stereo.size():
		var v := (stereo[i].x + stereo[i].y) * 0.5
		mono[i] = v
		sum += v * v
	level = clampf(sqrt(sum / stereo.size()) * 4.0, 0.0, 1.0)
	if muted or (gate.is_valid() and not gate.call()):
		mono.fill(0.0)
	captured_float.emit(mono, int(AudioServer.get_mix_rate()))
	if captured.get_connections().size() > 0:
		captured.emit(LokutorAudio.float32_to_pcm16(_resampler.process(mono)))


func _exit_tree() -> void:
	var capturing := _player != null
	stop()
	# Godot on Android closes its audio before it lets go of the microphone, and crashes if the game quits while one is open.
	if capturing and _open == 0 and OS.get_name() == "Android" and AudioServer.has_method(&"set_input_device_active"):
		AudioServer.call(&"set_input_device_active", false)
