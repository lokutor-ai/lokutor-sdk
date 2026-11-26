class_name LokutorTTSClient
extends Node
## Text to speech over wss://api.lokutor.com/ws/tts, as TTSClient in the JavaScript and Python SDKs. Add it to the tree.
## For a character that says written lines use LokutorVoice, which plays them and moves the mouth.

## 16-bit PCM, 44.1 kHz, mono, as it is generated.
signal audio(pcm16: PackedByteArray)
## [{v, c, t, d}], t from the request's first sample.
signal visemes(list: Array)
signal ttfb(ms: int)
signal error(err: LokutorError)

var api_key: String = ""
## Called (and awaited) for the credential: a ticket from your server, in a shipped game.
var auth: Callable
var server_url: String = Lokutor.TTS_URL
## Keep the connection open this long after a request, so the next one skips the handshake. It counts as a session.
var keep_open_seconds: float = 8.0

## Seconds to wait for the first audio, and between chunks afterwards.
const FIRST_AUDIO_S := 30.0
const STALL_S := 15.0

var _ws: WebSocketPeer
var _was_open := false
var _url := ""
var _headers: PackedStringArray
var _busy := false
var _done := false
var _error: LokutorError
var _pcm: PackedByteArray
var _visemes: Array
var _first := false
var _sent_ms := 0
var _deadline_ms := 0
var _idle_ms := 0
var _pending: Dictionary = {}
var _queue: Array = []               # the requests, the one being spoken first


## Speaks `text` and returns {ok, audio, sample_rate, visemes, error} when it is complete; `audio` and `visemes` are
## also emitted as they arrive. Options: voice, language, speed, steps, visemes, temperature, dictionary_ids,
## pronunciations, and on_audio, on_visemes, on_ttfb, on_error callbacks; and ticket, any RefCounted of yours to
## cancel(ticket) this request alone. Requests run one after another.
func synthesize(text: String, options: Dictionary = {}) -> Dictionary:
	var ticket: RefCounted = options.get("ticket", RefCounted.new())
	if is_inside_tree():
		_queue.append(ticket)
	while _queue.has(ticket) and _queue[0] != ticket:
		await get_tree().process_frame
	if not _queue.has(ticket):             # cancelled while it waited, or this node is not in the tree
		var err := LokutorError.new("internal.cancelled", "Cancelled.")
		if options.has("on_error"):
			options.on_error.call(err)
		return {"ok": false, "audio": PackedByteArray(), "sample_rate": Lokutor.OUTPUT_RATE, "visemes": [], "error": err}
	_busy = true
	_done = false
	_error = null
	_first = false
	_pcm = PackedByteArray()
	_visemes = []
	_pending = {"text": text, "voice": options.get("voice", "F1"), "language": options.get("language", "en"), "visemes": options.get("visemes", false)}
	# Only when set: the server's defaults are what production is tuned for.
	for k in ["speed", "steps", "temperature", "dictionary_ids", "pronunciations"]:
		if options.has(k):
			_pending[k] = options[k]
	var links: Array = []
	for pair in [["on_audio", audio], ["on_visemes", visemes], ["on_ttfb", ttfb], ["on_error", error]]:
		if options.has(pair[0]):
			pair[1].connect(options[pair[0]])
			links.append([pair[1], options[pair[0]]])
	_deadline_ms = Time.get_ticks_msec() + int(FIRST_AUDIO_S * 1000.0)
	if _ws == null:
		var err := await _dial()
		if err != null:
			_finish(err)
	elif _ws.get_ready_state() == WebSocketPeer.STATE_OPEN:
		_send_request()
	while not _done:
		await get_tree().process_frame
	_busy = false
	_idle_ms = Time.get_ticks_msec()
	if _error != null:
		# A request that failed leaves its connection in an unknown state: what the server still had to say (an EOS after
		# an error) would be taken for the next one's answer.
		_close()
		error.emit(_error)
	for l in links:
		l[0].disconnect(l[1])
	_queue.erase(ticket)
	return {"ok": _error == null, "audio": _pcm, "sample_rate": Lokutor.OUTPUT_RATE, "visemes": _visemes, "error": _error}


## Stops the request being spoken and forgets the ones waiting. With the ticket given to synthesize(), only that one.
func cancel(ticket: RefCounted = null) -> void:
	if _queue.is_empty() or (ticket != null and not _queue.has(ticket)):
		return
	var speaking: RefCounted = _queue[0]
	for waiting in _queue.duplicate():
		if (waiting != speaking or not _busy) and (ticket == null or waiting == ticket):
			_queue.erase(waiting)
	if _busy and (ticket == null or ticket == speaking):
		_close()
		_finish(LokutorError.new("internal.cancelled", "Cancelled."))


func close() -> void:
	cancel()
	_close()


func _close() -> void:
	if _ws != null:
		_ws.close(1000)
		_ws = null
	_was_open = false


func _dial() -> LokutorError:
	var key := api_key if api_key != "" else OS.get_environment("LOKUTOR_API_KEY")
	var ticket := false
	if auth.is_valid():
		var credential = await auth.call()
		if _done:
			return _error                  # cancelled while the credential came
		if not (credential is String) or credential == "":
			return LokutorError.new("auth.missing_key", "The auth callback returned no credential.", "", true)
		key = credential
		ticket = true
	if key == "" and not Lokutor.keyless_allowed():
		return LokutorError.new("auth.missing_key", "No API key: set api_key, or auth for a shipped game.")
	var ws := WebSocketPeer.new()
	ws.inbound_buffer_size = 4 * 1024 * 1024
	ws.max_queued_packets = 8192
	var url := server_url
	var headers := PackedStringArray(["User-Agent: lokutor-godot/" + Lokutor.VERSION])
	if key == "":                            # a browser on Lokutor's own pages: the server decides, on /ws (the same
		url = url.replace("/ws/tts", "/ws")  # synthesis as /ws/tts, and the path the lokutor.com demo may use)
	elif ticket or key.begins_with("wst_") or OS.has_feature("web"):
		url += ("&" if url.contains("?") else "?") + "api_key=" + key.uri_encode()
	else:
		headers.append("X-API-Key: " + key)
	ws.handshake_headers = PackedStringArray() if OS.has_feature("web") else headers   # browsers set none of their own
	_url = url
	_headers = headers
	var started := ws.connect_to_url(url, Lokutor.tls_options)
	if started != OK:
		return LokutorError.new("ws.close", "Could not start the connection (%s)." % error_string(started), "", true)
	_ws = ws
	_was_open = false
	return null


func _on_refused(detail: String) -> void:
	var asked := _pending
	var err := await Lokutor._why_refused(_url, _headers, detail)
	if is_same(asked, _pending) and _busy and not _done:
		_finish(err)


func _send_request() -> void:
	_sent_ms = Time.get_ticks_msec()
	_ws.send_text(JSON.stringify(_pending))


func _process(_delta: float) -> void:
	if _ws == null:
		return
	_ws.poll()
	var now := Time.get_ticks_msec()
	match _ws.get_ready_state():
		WebSocketPeer.STATE_OPEN:
			if not _was_open:
				_was_open = true
				if _busy:
					_send_request()
			while _ws != null and _ws.get_available_packet_count() > 0:
				var packet := _ws.get_packet()
				if _ws.was_string_packet():
					handle_message(packet.get_string_from_utf8())
				else:
					_on_audio(packet)
			if _ws != null and not _busy and now - _idle_ms > int(keep_open_seconds * 1000.0):
				_close()
		WebSocketPeer.STATE_CLOSED:
			var code := _ws.get_close_code()
			var reason := _ws.get_close_reason()
			var opened := _was_open
			_ws = null
			_was_open = false
			if _busy and not _done:
				var demo := LokutorError.from_demo_close(reason)
				if demo != null:
					_finish(demo)
				elif _first and code == 1000:
					_finish(null)
				elif not opened:
					_on_refused(reason)
				else:
					_finish(LokutorError.new("internal.error", "The connection closed (%d%s)." % [code, ": " + reason if reason != "" else ""], "", true))
	if _busy and not _done and now > _deadline_ms:
		_close()
		_finish(LokutorError.new("internal.timeout", "The stream stalled." if _first else "No audio within %d s." % int(FIRST_AUDIO_S), "", true))


## Handles one text frame. Public so tests can feed it.
func handle_message(text: String) -> void:
	if text == "EOS":
		_finish(_error)
		return
	var msg = JSON.parse_string(text)
	if msg is Array:
		_on_visemes(msg)
	elif msg is Dictionary:
		match str(msg.get("type", "")):
			"error": _finish(LokutorError.from_server(msg.get("data")))
			"audio":
				if msg.get("data") is String:
					_on_audio(Marshalls.base64_to_raw(msg.data))
			"visemes": _on_visemes(msg.get("data"))
			"eos": _finish(_error)


func _on_audio(pcm: PackedByteArray) -> void:
	if not _busy or _done:
		return
	if not _first:
		_first = true
		ttfb.emit(Time.get_ticks_msec() - _sent_ms)
	_deadline_ms = Time.get_ticks_msec() + int(STALL_S * 1000.0)
	_pcm.append_array(pcm)
	audio.emit(pcm)


func _on_visemes(payload) -> void:
	if not _busy or _done:
		return
	var list := LokutorVoiceAgentClient.normalize_visemes(payload)
	_visemes.append_array(list)
	visemes.emit(list)


func _finish(err: LokutorError) -> void:
	if _done:
		return
	_error = err
	_done = true


func _exit_tree() -> void:
	close()
