class_name LokutorVoicePlayer
extends Node3D
## Plays a voice as it arrives and knows, to a few milliseconds, how much of it has been heard: lip sync and the
## microphone's echo guard run on that clock, not on the network's.

## Samples handed to the sound card, as they are: for a character that another character listens to.
signal voice_played(samples: PackedFloat32Array, rate: int)
## The first sound of a reply is being heard.
signal started(generation: int)
## The last sound of a reply has been heard.
signal finished(generation: int)

## Play through your own AudioStreamPlayer, AudioStreamPlayer2D or AudioStreamPlayer3D. Empty: one is made.
@export var output: Node
## Without an `output`: play from this node's place in the world (3D).
@export var spatial: bool = false
@export var bus: StringName = &"Master"

## Rate of the audio pushed. Changing it restarts the output.
var rate: int = Lokutor.OUTPUT_RATE:
	set(value):
		if value > 0 and value != rate:
			rate = value
			if _playback != null:
				_open_output()
## The reply being played.
var generation: int = 0
## Loudness of what is being heard, 0..1.
var level: float = 0.0
## No sound card (headless runs, servers, tests): the voice is consumed in real time without being played.
var simulate_audio_device: bool = false
## Everything heard is also appended to `recorded` (mono).
var record: bool = false
var recorded := PackedFloat32Array()

const BUFFER_S := 0.25
## After the voice stops, the room's echo still reaches the microphone for a moment (on a computer).
const ECHO_TAIL_MS := 250
## Phones and browsers hear their own speaker later: their sound and their microphone are both buffered more, and
## the latency they report leaves some of it out.
const ECHO_TAIL_MOBILE_MS := 700


## How long after the voice the microphone may still hear it, on this device.
static func echo_tail_ms() -> int:
	return ECHO_TAIL_MOBILE_MS if OS.has_feature("mobile") or OS.has_feature("web") else ECHO_TAIL_MS

var _queue: Array[PackedByteArray] = []
var _queued_samples := 0
var _head := 0                  # samples already taken from _queue[0]
var _generator: AudioStreamGenerator
var _playback: AudioStreamGeneratorPlayback
var _capacity := 0
var _own_output := false
var _pushed := 0                # samples of this reply given to the sound card
var _audible_until_ms := 0
var _levels: Array = []         # [sample position, loudness]
var _sim_us := 0
var _heard := 0.0
var _is_started := false
var _is_finished := true
var _last_push_ms := 0


func _ready() -> void:
	if not simulate_audio_device:
		_open_output()


## A new reply begins: what was queued of the previous one is dropped, and time counts from 0.
func begin(new_generation: int) -> void:
	clear()
	generation = new_generation


## Queues voice (16-bit PCM, mono, at `rate`). Audio of an older reply is ignored.
func push(pcm16: PackedByteArray, for_generation: int = generation) -> void:
	if for_generation < generation or pcm16.size() < 2:
		return
	if for_generation > generation:
		begin(for_generation)
	_queue.append(pcm16)
	_queued_samples += pcm16.size() >> 1
	_last_push_ms = Time.get_ticks_msec()
	_is_finished = false


## Stops at once and forgets what was queued.
func clear() -> void:
	_queue.clear()
	_queued_samples = 0
	_head = 0
	_levels.clear()
	# Godot only empties a generator that is stopped, so the player is restarted: a new playback starts empty. Only
	# when something was given to it, so a reply that never started does not cost a restart.
	if _playback != null and _pushed > 0 and output != null and output.is_inside_tree():
		output.stop()
		output.play()
		_playback = output.get_stream_playback()
		_capacity = _playback.get_frames_available()
	_pushed = 0
	_heard = 0.0
	_audible_until_ms = 0
	_is_started = false
	_is_finished = true


## Voice is waiting or playing.
func is_playing() -> bool:
	return _queued_samples > 0 or Time.get_ticks_msec() < _audible_until_ms


## Voice waiting to be given to the sound card, in seconds.
func queued_seconds() -> float:
	return float(_queued_samples) / rate


## The voice can still reach the microphone: waiting, in the sound card's buffers, or echoing in the room.
func is_audible() -> bool:
	return _queued_samples > 0 or Time.get_ticks_msec() < _audible_until_ms + echo_tail_ms()


## How much of the current reply has been heard, in seconds: the clock viseme times are read against.
func heard_seconds() -> float:
	return _heard


func _open_output() -> void:
	if _own_output and output != null:
		output.queue_free()
		output = null
	_generator = AudioStreamGenerator.new()
	_generator.mix_rate = rate
	_generator.buffer_length = BUFFER_S
	if output == null:
		_own_output = true
		if spatial:
			var p3 := AudioStreamPlayer3D.new()
			p3.unit_size = 3.0
			p3.max_distance = 40.0
			output = p3
		else:
			output = AudioStreamPlayer.new()
		output.name = "LokutorVoice"
		output.bus = bus
		add_child(output)
	output.stream = _generator
	output.play()
	_playback = output.get_stream_playback()
	_capacity = _playback.get_frames_available()


func _process(_delta: float) -> void:
	var room := 0
	if simulate_audio_device:
		var now := Time.get_ticks_usec()
		if _sim_us != 0:
			room = int((now - _sim_us) * rate / 1000000.0)
		if room > 0 or _sim_us == 0:
			_sim_us = now
	elif _playback != null:
		room = _playback.get_frames_available()
	var n := mini(room, _queued_samples)
	if n > 0:
		_give(n)
	_update_clock()


func _give(n: int) -> void:
	var frames := PackedVector2Array()
	frames.resize(n)
	var mono := PackedFloat32Array()
	mono.resize(n)
	var i := 0
	var block_start := _pushed
	var sum := 0.0
	var in_block := 0
	var block := maxi(1, rate / 50)          # loudness every 20 ms
	while i < n:
		var pcm := _queue[0]
		var left := (pcm.size() >> 1) - _head
		var take := mini(left, n - i)
		for k in take:
			var v := pcm.decode_s16((_head + k) * 2) / 32768.0
			frames[i] = Vector2(v, v)
			mono[i] = v
			i += 1
			sum += v * v
			in_block += 1
			if in_block == block:
				_levels.append([block_start, sqrt(sum / block)])
				block_start += block
				sum = 0.0
				in_block = 0
		_head += take
		if take == left:
			_queue.pop_front()
			_head = 0
	if in_block > 0:
		_levels.append([block_start, sqrt(sum / in_block)])
	_queued_samples -= n
	_pushed += n
	var now := Time.get_ticks_msec()
	if simulate_audio_device:
		_audible_until_ms = now
	else:
		_playback.push_buffer(frames)
		var in_card := _capacity - _playback.get_frames_available()
		_audible_until_ms = now + int(in_card * 1000.0 / rate + AudioServer.get_output_latency() * 1000.0)
	if record:
		recorded.append_array(mono)
	voice_played.emit(mono, rate)


func _update_clock() -> void:
	var heard := 0.0
	if _pushed > 0:
		if simulate_audio_device:
			heard = float(_pushed) / rate
		else:
			var in_card := _capacity - _playback.get_frames_available()
			# The card's buffer empties a mix block at a time: count on from the last block.
			heard = float(_pushed - in_card) / rate - AudioServer.get_output_latency()
			if in_card > 0:
				heard += AudioServer.get_time_since_last_mix()
			heard = clampf(heard, 0.0, float(_pushed) / rate)
	_heard = maxf(_heard, heard)          # it never runs backwards within a reply
	var position := int(_heard * rate)
	var target := 0.0
	var playing := _pushed > 0 and (_heard < float(_pushed) / rate - 0.001 or _queued_samples > 0)
	while _levels.size() > 1 and _levels[1][0] <= position:
		_levels.pop_front()
	if playing and not _levels.is_empty() and _levels[0][0] <= position:
		target = _levels[0][1]
	level = target
	if not _is_started and _pushed > 0 and _heard > 0.0:
		_is_started = true
		started.emit(generation)
	if not _is_finished and _queued_samples == 0 and _pushed > 0 and not playing and Time.get_ticks_msec() - _last_push_ms > 400 \
			and Time.get_ticks_msec() >= _audible_until_ms:
		_is_finished = true
		finished.emit(generation)
