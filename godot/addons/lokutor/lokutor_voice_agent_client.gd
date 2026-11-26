class_name LokutorVoiceAgentClient
extends Node
## A voice conversation over wss://api.lokutor.com/ws/agent, with the same options, methods and events as
## VoiceAgentClient in the JavaScript and Python SDKs. Add it to the tree and call open().
##
## For a character in a scene use LokutorAgent, which adds the microphone, the 3D voice, lip sync and actions on
## top of this. Use this one when you want the conversation and nothing else.
##
## Two names differ from JavaScript because Godot's Object already has them: connect() is open() and
## disconnect() is close().

## A final line of player speech.
signal transcription(text: String)
## The player's words while they are still talking. Not stored in get_transcript().
signal transcription_partial(text: String)
## What the agent is about to say.
signal response(text: String)
## The agent's voice: 16-bit PCM, mono, at playback_rate. Audio of replies already superseded never gets here.
signal audio(pcm16: PackedByteArray, generation: int)
## Mouth shapes of the reply: [{v, c, t, d}], t and d in seconds from the reply's first sample.
signal visemes(list: Array, generation: int)
## connected, listening, thinking, speaking, interrupted, acknowledgements, and the client's own reconnecting and
## disconnected.
signal status(value: String)
signal error(err: LokutorError)
## The agent wants a function called: {name, arguments, call_id}; arguments is a JSON string. Answer with
## submit_tool_result(call_id, result).
signal tool_call(call: Dictionary)
## A new reply has begun: whatever was queued of the previous one must not be played.
signal generation_started(generation: int)
## The player cut the agent off. `heard` is what they heard of `full`.
signal transcript_truncated(heard: String, full: String)
## The player's paused sentence and its continuation became one line: `text` replaces `previous`.
signal transcript_revised(text: String, previous: String)
## How many seconds this session may still run, when the server sets a limit.
signal session_limit(seconds: float, reason: String)
## A dropped connection was opened again. It is a new server session: the agent does not remember the old one.
signal reconnected()
## The server ended the session on purpose; no reconnection follows.
signal ended(code: int, reason: String)

## An API key, or a wst_ ticket. Fine while prototyping; a shipped game should set `auth`.
var api_key: String = ""
## Called (and awaited) before every connect and reconnect for the credential to use: have it ask your server for a
## ticket (POST https://api.lokutor.com/ws-ticket), so the key never ships with the game.
var auth: Callable
## An agent from the dashboard. Then voice, language and prompt are sent only if you set them.
var agent_id: String = ""
var server_url: String = Lokutor.VOICE_AGENT_URL
var tools: Array = []
## Ask for visemes.
var want_visemes: bool = false
## Your own OpenAI-compatible endpoint: {base_url, api_key, model, headers, tools, speculative_llm}.
var llm: Dictionary = {}
## Your model runs in the game: func(messages: Array, tools: Array, reply: LokutorVoiceAgentClient.LlmReply).
## Return the reply as a String, or call reply.chunk(text) as it is produced.
var on_llm_request: Callable
## "none": the agent's tools are not sent to on_llm_request.
var llm_tools: String = "auto"
## Open the connection again when it drops (2 s, 4 s, 8 s, 10 s, 10 s).
var auto_reconnect: bool = true
var max_reconnect_attempts: int = 5
var connect_timeout: float = 15.0
var user_agent: String = "lokutor-godot/" + Lokutor.VERSION

var prompt: String = ""
var voice: String = "F1"
## "" = the agent follows the player's language ("auto").
var language: String = "en"
var pitch: float = 0.0

## The reply being spoken. Grows by one with each reply of a session.
var generation: int = 0
var session_id: String = ""
## Rate of the agent's audio, announced by the server.
var playback_rate: int = Lokutor.OUTPUT_RATE
var input_rate: int = Lokutor.INPUT_RATE
var connected: bool:
	get: return _ws != null and _ws.get_ready_state() == WebSocketPeer.STATE_OPEN

## Set by start_managed(): the microphone and the speaker this client runs.
var microphone: LokutorMicrophone
var player: LokutorVoicePlayer

var _ws: WebSocketPeer
var _opened := false
var _url := ""                          # of the last attempt, and its headers
var _headers: PackedStringArray
var _asking := false                    # why that was refused
var _user_close := false
var _voice_explicit := false
var _language_explicit := false
var _messages: Array = []
var _attempts := 0
var _retry_at_ms := 0
var _deadline_ms := 0
var _opening := false
var _reopening := false
var _epoch := 0                     # grows when a connection is asked for or given up: an older one still waiting is stale
var _closing: WebSocketPeer
var _closing_until_ms := 0
var _replies: Dictionary = {}      # request_id -> LlmReply


## A client from a dictionary of options, as in JavaScript:
##   LokutorVoiceAgentClient.create({"api_key": key, "prompt": "You are Mira.", "voice": "F2", "visemes": true})
static func create(config: Dictionary) -> LokutorVoiceAgentClient:
	var c := LokutorVoiceAgentClient.new()
	c.configure(config)
	return c


## Sets options by name. on_transcription, on_response, on_audio, on_visemes, on_status and on_error connect a callback.
func configure(config: Dictionary) -> LokutorVoiceAgentClient:
	for key in config:
		var value = config[key]
		match str(key):
			"voice":
				voice = str(value)
				_voice_explicit = true
			"language":
				language = Lokutor.language_to_wire(str(value))
				_language_explicit = true
			"visemes": want_visemes = value
			"on_transcription": transcription.connect(value)
			"on_response": response.connect(value)
			"on_audio": audio.connect(value)
			"on_visemes": visemes.connect(value)
			"on_status": status.connect(value)
			"on_error": error.connect(value)
			_:
				if key in self:
					set(key, value)
				else:
					push_warning("Lokutor: VoiceAgentClient has no option '%s'." % key)
	return self


## Opens the session and sends the configuration. Returns true once connected, false if it could not (the reason is
## emitted as `error`).
func open() -> bool:
	if _ws != null or _opening:
		return connected
	_user_close = false
	_attempts = 0
	_retry_at_ms = 0
	_reopening = false
	_opening = true
	_asking = false
	_epoch += 1
	var mine := _epoch
	var err := await _dial()
	if mine != _epoch:
		return false                       # closed while it waited for the credential
	if err != null:
		_opening = false
		error.emit(err)
		status.emit("disconnected")
		return false
	while mine == _epoch and (_asking or (_ws != null and not _opened)):
		await get_tree().process_frame
	if mine != _epoch:
		return false
	_opening = false
	return connected


## Ends the session and releases the microphone.
func close() -> void:
	var was := _ws != null or _retry_at_ms != 0 or _opening or _reopening
	_user_close = true
	_retry_at_ms = 0
	_opening = false
	_reopening = false
	_asking = false
	_epoch += 1
	if _ws != null:
		_ws.close(1000, "bye")
		_closing = _ws          # polled until the goodbye has been sent
		_closing_until_ms = Time.get_ticks_msec() + 1000
		_ws = null
	_opened = false
	_stop_managed()
	_cancel_replies()
	if was:
		status.emit("disconnected")


## Runs the microphone and the speaker too: a whole conversation in one call.
func start_managed() -> bool:
	if player == null:
		player = LokutorVoicePlayer.new()
		add_child(player)
		audio.connect(func(pcm, gen): player.push(pcm, gen))
		generation_started.connect(func(gen): player.begin(gen))
		status.connect(func(s): if s == "interrupted": player.clear())
	if microphone == null:
		microphone = LokutorMicrophone.new()
		add_child(microphone)
		microphone.captured.connect(send_audio)
	if not await open():
		return false
	player.rate = playback_rate
	if not microphone.start():
		error.emit(LokutorError.new("audio.microphone_unavailable", "The microphone could not be started.",
			"Project Settings > Audio > Driver > Enable Input"))
		close()
		return false
	return true


## Sends microphone audio: 16-bit PCM, 16 kHz, mono. Keep sending it (silence too): the server measures pauses in it.
func send_audio(pcm16: PackedByteArray) -> void:
	if connected and not pcm16.is_empty():
		_ws.put_packet(pcm16)


## The same from float samples at any rate.
func send_audio_float(samples: PackedFloat32Array, rate: int) -> void:
	if not connected or samples.is_empty():
		return
	if _resampler == null or _resampler.from_rate != rate or _resampler.to_rate != input_rate:
		_resampler = LokutorAudio.StreamResampler.new(rate, input_rate)
	send_audio(LokutorAudio.float32_to_pcm16(_resampler.process(samples)))

var _resampler: LokutorAudio.StreamResampler


func update_prompt(new_prompt: String) -> void:
	prompt = new_prompt
	_send_if_open({"type": "prompt", "data": new_prompt})


func update_voice(new_voice: String) -> void:
	voice = new_voice
	_voice_explicit = true
	_send_if_open({"type": "voice", "data": new_voice})


func update_language(new_language: String) -> void:
	language = Lokutor.language_to_wire(new_language)
	_language_explicit = true
	_send_if_open({"type": "language", "data": language})


func update_tools(new_tools: Array) -> void:
	tools = new_tools
	_send_if_open({"type": "tools", "data": new_tools})


## Shifts the voice's pitch for the rest of the session.
func update_pitch(new_pitch: float) -> void:
	pitch = new_pitch
	_send_if_open({"type": "pitch", "data": new_pitch})


## Asks for visemes, or stops asking, from the next reply on.
func update_visemes(enabled: bool) -> void:
	want_visemes = enabled
	_send_if_open({"type": "visemes", "data": enabled})


## Clears the conversation: the agent starts fresh.
func reset() -> void:
	_messages.clear()
	_send_if_open({"type": "reset"})


## Answers a tool_call. `result` is a String, or anything JSON can carry.
func submit_tool_result(call_id: String, result) -> void:
	_send_if_open({"type": "tool_result", "data": {"call_id": call_id,
		"result": result if result is String else JSON.stringify(result)}})


## The conversation so far: [{role, text, timestamp}], timestamp in milliseconds (Time.get_ticks_msec).
func get_transcript() -> Array:
	return _messages.duplicate(true)


func get_transcript_text() -> String:
	var out := PackedStringArray()
	for m in _messages:
		out.append("%s: %s" % ["You" if m.role == "user" else "Agent", m.text])
	return "\n".join(out)


## Mutes or unmutes the managed microphone; returns whether it is now muted.
func toggle_mute() -> bool:
	if microphone == null:
		return false
	microphone.muted = not microphone.muted
	return microphone.muted


## The managed microphone's loudness, 0..1.
func get_amplitude() -> float:
	return microphone.level if microphone != null else 0.0


static func fetch_voices(base_url: String = Lokutor.VOICE_AGENT_URL) -> Array:
	return await Lokutor.fetch_voices(base_url)

static func fetch_languages(base_url: String = Lokutor.VOICE_AGENT_URL) -> Array:
	return await Lokutor.fetch_languages(base_url)

static func fetch_models(base_url: String = Lokutor.VOICE_AGENT_URL) -> Array:
	return await Lokutor.fetch_models(base_url)

static func fetch_config(base_url: String = Lokutor.VOICE_AGENT_URL) -> Dictionary:
	return await Lokutor.fetch_config(base_url)

static func fetch_status(base_url: String = Lokutor.VOICE_AGENT_URL) -> Dictionary:
	return await Lokutor.fetch_status(base_url)

static func fetch_health(base_url: String = Lokutor.VOICE_AGENT_URL) -> Dictionary:
	return await Lokutor.fetch_health(base_url)


# ---- Connection ----------------------------------------------------------------------------------

func _dial() -> LokutorError:
	var key := api_key
	var ticket := false
	var mine := _epoch
	if auth.is_valid():
		var credential = await auth.call()
		if mine != _epoch:
			return LokutorError.new("internal.cancelled", "Closed while connecting.")
		if not (credential is String) or credential == "":
			return LokutorError.new("auth.missing_key", "The auth callback returned no credential.", "", true)
		key = credential
		ticket = true
	if key == "" and not Lokutor.keyless_allowed():
		return LokutorError.new("auth.missing_key", "No API key: set api_key, or auth for a shipped game.")
	var query := PackedStringArray()
	if agent_id != "":
		query.append("agent_id=" + agent_id.uri_encode())
	var ws := WebSocketPeer.new()
	ws.inbound_buffer_size = 4 * 1024 * 1024    # a reply's audio can arrive in large frames
	ws.outbound_buffer_size = 1024 * 1024
	ws.max_queued_packets = 8192
	var headers := PackedStringArray(["User-Agent: " + user_agent])
	# Browsers cannot set WebSocket headers; tickets are made for the query.
	if key == "":
		pass                                     # a browser on Lokutor's own pages: the server decides
	elif ticket or key.begins_with("wst_") or OS.has_feature("web"):
		query.append("api_key=" + key.uri_encode())
	else:
		headers.append("X-API-Key: " + key)
	ws.handshake_headers = PackedStringArray() if OS.has_feature("web") else headers   # browsers set none of their own
	_headers = headers
	var url := server_url
	if query.size() > 0:
		url += ("&" if url.contains("?") else "?") + "&".join(query)
	_url = url
	var started := ws.connect_to_url(url, Lokutor.tls_options)
	if started != OK:
		return LokutorError.new("ws.close", "Could not start the connection (%s)." % error_string(started), "", true)
	_ws = ws
	_opened = false
	# Every socket is a new server session, and a new session numbers its replies from 1.
	generation = 0
	_deadline_ms = Time.get_ticks_msec() + int(connect_timeout * 1000.0)
	return null


func _process(_delta: float) -> void:
	if _closing != null:
		_closing.poll()
		if _closing.get_ready_state() == WebSocketPeer.STATE_CLOSED or Time.get_ticks_msec() > _closing_until_ms:
			_closing = null
	if _ws == null:
		if _retry_at_ms != 0 and Time.get_ticks_msec() >= _retry_at_ms:
			_retry_at_ms = 0
			_reopen()
		return
	_ws.poll()
	match _ws.get_ready_state():
		WebSocketPeer.STATE_OPEN:
			if not _opened:
				_opened = true
				_send_config()
				if _reopening:
					_reopening = false
					_attempts = 0
					reconnected.emit()
			# A handler may close the session, or open another, while messages are being read.
			var ws := _ws
			while ws == _ws and ws.get_available_packet_count() > 0:
				var packet := ws.get_packet()
				if ws.was_string_packet():
					handle_message(packet.get_string_from_utf8())
				else:
					audio.emit(packet, generation)
		WebSocketPeer.STATE_CONNECTING:
			if Time.get_ticks_msec() > _deadline_ms:
				_ws.close()
				_ws = null
				_failed(LokutorError.new("internal.timeout", "Could not reach Lokutor in %d s." % int(connect_timeout)))
		WebSocketPeer.STATE_CLOSED:
			var code := _ws.get_close_code()
			var reason := _ws.get_close_reason()
			_ws = null
			_on_closed(1006 if code == -1 else code, reason)


func _reopen() -> void:
	_reopening = true
	var mine := _epoch
	var err := await _dial()
	if err == null or mine != _epoch:
		return
	if err.retryable:
		_schedule_retry()
	else:
		_reopening = false
		_stop_managed()
		error.emit(err)
		status.emit("disconnected")


func _on_closed(code: int, reason: String) -> void:
	var was_open := _opened
	_opened = false
	_cancel_replies()
	var demo := LokutorError.from_demo_close(reason)
	if demo != null:                         # the lokutor.com demo's limits: nothing to retry or ask
		_reopening = false
		ended.emit(code, reason)
		error.emit(demo)
		_stop_managed()
		status.emit("disconnected")
		return
	if not was_open:
		_on_refused(reason if reason != "" else "close code %d" % code)
		return
	if not RECONNECTABLE.has(code):
		_reopening = false
		ended.emit(code, reason)
		error.emit(LokutorError.new("session.ended", reason if reason != "" else "The session was ended by the server.", "close code %d" % code))
		_stop_managed()
		status.emit("disconnected")
		return
	_schedule_retry()

const RECONNECTABLE := Lokutor.RECONNECTABLE_CLOSE_CODES


func _on_refused(detail: String) -> void:
	var mine := _epoch
	_asking = true
	var err := await Lokutor._why_refused(_url, _headers, detail)
	if mine != _epoch:
		return
	_asking = false
	if _reopening and not err.retryable:
		_reopening = false
		_stop_managed()
	_failed(err)


func _failed(err: LokutorError) -> void:
	if _reopening:
		_schedule_retry()
		return
	error.emit(err)
	status.emit("disconnected")


func _schedule_retry() -> void:
	if not auto_reconnect or _attempts >= max_reconnect_attempts:
		_reopening = false
		error.emit(LokutorError.new("ws.close", "The connection was lost.", "%d reconnection attempts" % _attempts, true))
		_stop_managed()
		status.emit("disconnected")
		return
	_attempts += 1
	_retry_at_ms = Time.get_ticks_msec() + int(minf(1000.0 * pow(2.0, _attempts), 10000.0))
	status.emit("reconnecting")


func _stop_managed() -> void:
	if microphone != null:
		microphone.stop()
	if player != null:
		player.clear()


func _exit_tree() -> void:
	close()


# ---- Protocol ------------------------------------------------------------------------------------

func _send(msg: Dictionary) -> void:
	_ws.send_text(JSON.stringify(msg))


func _send_if_open(msg: Dictionary) -> void:
	if connected:
		_send(msg)


func _send_config() -> void:
	for msg in config_messages():
		_send(msg)


## The configuration sent on connect, in order. `rates` is last: it starts the conversation.
func config_messages() -> Array:
	var out: Array = []
	var dashboard := agent_id != ""
	if not dashboard or _voice_explicit:
		out.append({"type": "voice", "data": voice})
	if not dashboard or _language_explicit:
		out.append({"type": "language", "data": Lokutor.language_to_wire(language)})
	if not tools.is_empty():
		out.append({"type": "tools", "data": tools})
	out.append({"type": "visemes", "data": want_visemes})
	if pitch != 0.0:
		out.append({"type": "pitch", "data": pitch})
	if not llm.is_empty():
		var d := {}
		for k in ["base_url", "api_key", "model", "headers", "tools", "speculative_llm"]:
			if llm.has(k):
				d[k] = llm[k]
		out.append({"type": "llm", "data": d})
	elif on_llm_request.is_valid():
		out.append({"type": "llm", "data": {"mode": "client", "tools": llm_tools}})
	if not dashboard or prompt != "":
		out.append({"type": "prompt", "data": prompt})
	out.append({"type": "rates", "playback": Lokutor.OUTPUT_RATE, "input": Lokutor.INPUT_RATE})
	return out


## Handles one message from the server. Public so tests can feed it.
func handle_message(text: String) -> void:
	var msg = JSON.parse_string(text)
	if not (msg is Dictionary):
		return
	var data = msg.get("data")
	match str(msg.get("type", "")):
		"audio":
			var gen := int(msg.get("generation", generation))
			if data is String and data != "" and gen >= generation:
				_advance(gen)
				audio.emit(Marshalls.base64_to_raw(data), gen)
		"visemes", "viseme":
			var gen := int(msg.get("generation", generation))
			if gen >= generation:
				var list := normalize_visemes(data if data != null else msg)
				if not list.is_empty() or data is Array:
					_advance(gen)
					visemes.emit(list, gen)
		"server_rates":
			playback_rate = int(msg.get("playback", playback_rate)) if int(msg.get("playback", 0)) > 0 else playback_rate
			input_rate = int(msg.get("input", input_rate)) if int(msg.get("input", 0)) > 0 else input_rate
		"status":
			var value := str(data)
			if value == "connected" and msg.get("session_id") is String:
				session_id = msg.session_id
			if value == "thinking":
				_advance(int(msg.get("generation", 0)))
			status.emit(value)
		"transcript":
			var line := str(data if data != null else msg.get("text", ""))
			if msg.get("partial", false):
				transcription_partial.emit(line)
			elif str(msg.get("role", "user")) == "agent":
				_remember("agent", line)
				response.emit(line)
			else:
				_remember("user", line)
				transcription.emit(line)
		"transcript_truncated":
			var heard := str(data if data != null else "")
			_truncate(heard, str(msg.get("full", "")), bool(msg.get("emitted", false)))
			transcript_truncated.emit(heard, str(msg.get("full", "")))
		"transcript_revised":
			_revise(str(data), str(msg.get("previous", "")))
			transcript_revised.emit(str(data), str(msg.get("previous", "")))
		"tool_call":
			tool_call.emit({"name": str(msg.get("name", "")), "arguments": str(msg.get("arguments", "{}")), "call_id": str(msg.get("call_id", ""))})
		"session_limit":
			var d: Dictionary = data if data is Dictionary else msg
			session_limit.emit(float(d.get("seconds", 0.0)), str(d.get("reason", "")))
		"error":
			error.emit(LokutorError.from_server(data))
		"llm_request":
			_serve_llm(msg)
		"llm_cancel":
			var r: LlmReply = _replies.get(str(msg.get("request_id", "")))
			if r != null:
				r.cancelled = true


## [{v, c, t, d}] from what the server sends. A missing duration lasts until the next one starts.
static func normalize_visemes(payload) -> Array:
	if payload is Dictionary:
		payload = payload["visemes"] if payload.get("visemes") is Array else [payload]
	if not (payload is Array):
		return []
	var out: Array = []
	for item in payload:
		if not (item is Dictionary) or not (item.has("t") or item.has("timestamp")):
			continue
		var c := str(item.get("c", item.get("char", "sil")))
		var id = item.get("v", item.get("id"))
		var v := int(id) if id is float or id is int else -1
		if v < 0 or v >= Lokutor.VISEMES.size():
			v = Lokutor.viseme_id(c)           # an id this version does not know: its name may say
		out.append({"v": v, "c": c,
			"t": float(item.get("t", item.get("timestamp", 0.0))), "d": float(item.get("d", 0.0))})
	for i in out.size():
		if out[i].d <= 0.0:
			out[i].d = maxf(0.02, out[i + 1].t - out[i].t) if i + 1 < out.size() else 0.12
	return out


func _advance(gen: int) -> void:
	if gen > generation:
		generation = gen
		generation_started.emit(gen)


func _remember(role: String, text: String) -> void:
	_messages.append({"role": role, "text": text, "timestamp": Time.get_ticks_msec()})


## The player heard only part of an agent line: the history keeps what was heard.
func _truncate(heard: String, full: String, emitted: bool) -> void:
	if emitted:
		for i in range(_messages.size() - 1, -1, -1):
			if _messages[i].role == "agent" and _messages[i].text == full:
				if heard == "":
					_messages.remove_at(i)
				else:
					_messages[i].text = heard
				return
	elif heard != "":
		_remember("agent", heard)


func _revise(text: String, previous: String) -> void:
	for i in range(_messages.size() - 1, -1, -1):
		if _messages[i].role == "user" and (_messages[i].text == previous or previous == ""):
			_messages = _messages.slice(0, i + 1)
			_messages[i].text = text
			return
	_remember("user", text)


# ---- Your own model ------------------------------------------------------------------------------

func _serve_llm(req: Dictionary) -> void:
	var reply := LlmReply.new()
	reply._client = self
	reply._id = str(req.get("request_id", ""))
	if not on_llm_request.is_valid():
		reply.fail("no on_llm_request handler")
		return
	_replies[reply._id] = reply
	var out = await on_llm_request.call(req.get("messages", []), req.get("tools", []), reply)
	_replies.erase(reply._id)
	if out is String:
		reply.chunk(out)
	elif out is Array:
		for part in out:
			if part is String:
				reply.chunk(part)
			elif part is Dictionary:
				reply.tool_call(str(part.get("name", "")), part.get("arguments", {}), str(part.get("id", "")))
	elif out is Dictionary and out.has("name"):
		reply.tool_call(str(out.name), out.get("arguments", {}), str(out.get("id", "")))
	reply.done()


func _cancel_replies() -> void:
	for id in _replies:
		_replies[id].cancelled = true
	_replies.clear()


## The reply of your own model to one llm_request. Stop when `cancelled` turns true: the player spoke over it.
class LlmReply extends RefCounted:
	var cancelled := false
	var _client: LokutorVoiceAgentClient
	var _id := ""
	var _finished := false

	func chunk(text: String) -> void:
		if text != "" and not cancelled and not _finished:
			_client._send_if_open({"type": "llm_chunk", "data": text, "request_id": _id})

	func tool_call(tool_name: String, arguments, id := "") -> void:
		if not cancelled and not _finished:
			_client._send_if_open({"type": "llm_tool_call", "id": id if id != "" else "call_%s_%s" % [_id, tool_name], "name": tool_name,
				"arguments": arguments if arguments is String else JSON.stringify(arguments), "request_id": _id})

	func done() -> void:
		if not cancelled and not _finished:
			_client._send_if_open({"type": "llm_done", "request_id": _id})
		_finished = true

	func fail(message: String) -> void:
		if not _finished:
			_client._send_if_open({"type": "llm_error", "data": message.left(300), "request_id": _id})
		_finished = true
