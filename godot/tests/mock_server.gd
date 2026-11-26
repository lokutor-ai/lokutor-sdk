class_name LokutorMockServer
extends Node
## A Lokutor API small enough to run inside a test: WebSockets (agent, tts, stt) on one port and an HTTP route on another,
## on localhost. What each client sends is kept in `sessions`; tests answer through Session.send().

class Session extends RefCounted:
	var url := ""                      # "ws://127.0.0.1:18765/ws/agent?api_key=..."
	var ws: WebSocketPeer
	var text: Array = []               # parsed JSON messages received, in order
	var audio := PackedByteArray()
	var opened := false
	var closed := false

	func send(msg) -> void:
		if ws.get_ready_state() == WebSocketPeer.STATE_OPEN:
			ws.send_text(msg if msg is String else JSON.stringify(msg))

	func send_audio(pcm: PackedByteArray, generation := 1) -> void:
		send({"type": "audio", "data": Marshalls.raw_to_base64(pcm), "generation": generation})

	func types() -> Array:
		return text.map(func(m): return m.get("type", "") if m is Dictionary else "")

	func last(type: String):
		for i in range(text.size() - 1, -1, -1):
			if text[i] is Dictionary and text[i].get("type", "") == type:
				return text[i]
		return null

	## Cuts the connection without a goodbye, as a network failure does.
	func drop() -> void:
		closed = true
		ws.close(-1)

signal opened(session: Session)
signal message(session: Session, msg)

var port := 0
var http_port := 0
var sessions: Array[Session] = []
var http_requests: Array = []
var http_status := 200
var http_body = {}
## Refuse new connections (a server that is down).
var refuse := false
## ... or say why, as the API does: {code, message, retryable}.
var refuse_with := {}

var _tcp := TCPServer.new()
var _http := TCPServer.new()
var _http_peers: Array = []            # [StreamPeerTCP, bytes so far]


func start() -> bool:
	for p in range(18765, 18865):
		if port == 0 and _tcp.listen(p, "127.0.0.1") == OK:
			port = p
		elif port != 0 and _http.listen(p, "127.0.0.1") == OK:
			http_port = p
			return true
	return false


func url(path: String) -> String:
	return "ws://127.0.0.1:%d%s" % [port, path]


func http_url(path: String) -> String:
	return "ws://127.0.0.1:%d%s" % [http_port, path]


func current() -> Session:
	return sessions.back() if not sessions.is_empty() else null


func _process(_delta: float) -> void:
	while _tcp.is_connection_available():
		var peer := _tcp.take_connection()
		if not refuse_with.is_empty():
			_http_peers.append([peer, PackedByteArray(), 401, refuse_with])
			continue
		if refuse:
			peer.disconnect_from_host()
			continue
		var s := Session.new()
		s.ws = WebSocketPeer.new()
		s.ws.inbound_buffer_size = 1 << 20
		s.ws.outbound_buffer_size = 4 << 20
		s.ws.max_queued_packets = 4096
		s.ws.accept_stream(peer)
		sessions.append(s)
	for s in sessions:
		if s.closed:
			continue
		s.ws.poll()
		match s.ws.get_ready_state():
			WebSocketPeer.STATE_OPEN:
				if not s.opened:
					s.opened = true
					s.url = s.ws.get_requested_url()
					opened.emit(s)
				while not s.closed and s.ws.get_ready_state() == WebSocketPeer.STATE_OPEN and s.ws.get_available_packet_count() > 0:
					var packet := s.ws.get_packet()
					if s.ws.was_string_packet():
						var m = JSON.parse_string(packet.get_string_from_utf8())
						s.text.append(m)
						message.emit(s, m)
					else:
						s.audio.append_array(packet)
			WebSocketPeer.STATE_CLOSED:
				s.closed = true
	while _http.is_connection_available():
		_http_peers.append([_http.take_connection(), PackedByteArray(), http_status, http_body])
	for item in _http_peers.duplicate():
		_answer_http(item)


func _answer_http(item: Array) -> void:
	var peer: StreamPeerTCP = item[0]
	peer.poll()
	if peer.get_status() != StreamPeerTCP.STATUS_CONNECTED:
		_http_peers.erase(item)
		return
	var got: Array = peer.get_partial_data(1 << 20)
	if got[0] == OK:
		item[1].append_array(got[1])
	var raw: PackedByteArray = item[1]
	var head_end := raw.get_string_from_ascii().find("\r\n\r\n")
	if head_end == -1:
		return
	var head := raw.slice(0, head_end).get_string_from_ascii()
	var length := 0
	for line in head.split("\r\n"):
		if line.to_lower().begins_with("content-length:"):
			length = int(line.get_slice(":", 1))
	if raw.size() < head_end + 4 + length:
		return
	http_requests.append({"line": head.get_slice("\r\n", 0), "head": head, "body": JSON.parse_string(raw.slice(head_end + 4).get_string_from_utf8()) if length > 0 else null})
	var body := JSON.stringify(item[3]).to_utf8_buffer()
	peer.put_data(("HTTP/1.1 %d X\r\nContent-Type: application/json\r\nContent-Length: %d\r\nConnection: close\r\n\r\n" % [item[2], body.size()]).to_ascii_buffer() + body)
	_http_peers.erase(item)
