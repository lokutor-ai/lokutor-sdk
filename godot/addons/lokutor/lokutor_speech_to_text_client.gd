class_name LokutorSpeechToTextClient
extends Node
## Speech to text as the player talks (wss://api.lokutor.com/ws/stt): voice commands, dictation, captions. No language
## model and no voice. As SpeechToTextClient in the JavaScript SDK. Add it to the tree and call open().

signal partial_transcript(text: String)
signal final_transcript(text: String)
signal error(err: LokutorError)
## connecting, connected, disconnected.
signal status_changed(value: String)

var api_key: String = ""
var auth: Callable
var server_url: String = Lokutor.STT_URL
var language: String = "en"
## "silero" (default) or "rms".
var vad: String = "silero"
## Names and terms to write your way when what is heard is close.
var keyterms: Array = []
## Capture the microphone. Off: send your own audio with send_audio().
var use_microphone: bool = true
var microphone: LokutorMicrophone
var connected: bool:
	get: return _ws != null and _ws.get_ready_state() == WebSocketPeer.STATE_OPEN

var _ws: WebSocketPeer
var _was_open := false
var _url := ""
var _headers: PackedStringArray
var _asking := false                    # why a connection was refused


## Connects and starts listening. True once connected.
func open() -> bool:
	if _ws != null or _asking:
		return connected
	status_changed.emit("connecting")
	var key := api_key if api_key != "" else OS.get_environment("LOKUTOR_API_KEY")
	var ticket := auth.is_valid()
	if ticket:
		var credential = await auth.call()
		key = credential if credential is String else ""
	if key == "" and (ticket or not Lokutor.keyless_allowed()):
		error.emit(LokutorError.new("auth.missing_key",
			"The auth callback returned no credential." if ticket else "No API key: set api_key, or auth for a shipped game."))
		status_changed.emit("disconnected")
		return false
	var ws := WebSocketPeer.new()
	var url := server_url
	var headers := PackedStringArray(["User-Agent: lokutor-godot/" + Lokutor.VERSION])
	if key == "":
		pass                                     # a browser on Lokutor's own pages: the server decides
	elif ticket or key.begins_with("wst_") or OS.has_feature("web"):
		url += ("&" if url.contains("?") else "?") + "api_key=" + key.uri_encode()
	else:
		headers.append("X-API-Key: " + key)
	ws.handshake_headers = PackedStringArray() if OS.has_feature("web") else headers   # browsers set none of their own
	_url = url
	_headers = headers
	if ws.connect_to_url(url, Lokutor.tls_options) != OK:
		error.emit(LokutorError.new("ws.close", "Could not start the connection.", "", true))
		status_changed.emit("disconnected")
		return false
	_ws = ws
	_was_open = false
	while _asking or (_ws == ws and ws.get_ready_state() == WebSocketPeer.STATE_CONNECTING):
		await get_tree().process_frame
	if not connected:
		return false
	if use_microphone:
		if microphone == null:
			microphone = LokutorMicrophone.new()
			add_child(microphone)
			microphone.captured.connect(send_audio)
		if not microphone.start():
			error.emit(LokutorError.new("audio.microphone_unavailable", "The microphone could not be started."))
			close()
			return false
	return true


func close() -> void:
	if microphone != null:
		microphone.stop()
	if _ws != null or _asking:
		if _ws != null:
			_ws.close(1000)
		_ws = null
		_asking = false
		status_changed.emit("disconnected")


## 16-bit PCM, 16 kHz, mono. Keep sending silence: the end of an utterance is measured in it.
func send_audio(pcm16: PackedByteArray) -> void:
	if connected and not pcm16.is_empty():
		_ws.put_packet(pcm16)


## Finishes whatever is being said now.
func end_utterance() -> void:
	if connected:
		_ws.send_text('{"type":"end"}')


func _process(_delta: float) -> void:
	if _ws == null:
		return
	_ws.poll()
	match _ws.get_ready_state():
		WebSocketPeer.STATE_OPEN:
			if not _was_open:
				_was_open = true
				var first := {"language": language, "vad": vad}
				if not keyterms.is_empty():
					first["keyterms"] = keyterms
				_ws.send_text(JSON.stringify(first))
				status_changed.emit("connected")
			while _ws != null and _ws.get_available_packet_count() > 0:
				var packet := _ws.get_packet()
				if _ws.was_string_packet():
					handle_message(packet.get_string_from_utf8())
		WebSocketPeer.STATE_CLOSED:
			var opened := _was_open
			var reason := _ws.get_close_reason()
			_ws = null
			if microphone != null:
				microphone.stop()
			var demo := LokutorError.from_demo_close(reason)
			if demo != null:
				error.emit(demo)
			elif not opened:
				_asking = true
				var err := await Lokutor._why_refused(_url, _headers, reason)
				if not _asking:
					return                     # closed meanwhile
				_asking = false
				error.emit(err)
			status_changed.emit("disconnected")


## Handles one message. Public so tests can feed it.
func handle_message(text: String) -> void:
	var msg = JSON.parse_string(text)
	if not (msg is Dictionary):
		return
	match str(msg.get("type", "")):
		"transcript":
			if msg.get("isFinal", false):
				final_transcript.emit(str(msg.get("data", "")))
			else:
				partial_transcript.emit(str(msg.get("data", "")))
		"error":
			error.emit(LokutorError.from_server(msg.get("data")))


func _exit_tree() -> void:
	close()
