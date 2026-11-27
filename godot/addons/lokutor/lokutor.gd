class_name Lokutor
extends RefCounted
## Constants and helpers shared by the Lokutor nodes: voices, languages, visemes, addresses, and the
## public discovery endpoints (no API key needed). The same names as the JavaScript and Python SDKs.

const VERSION := "0.2.1"

const VOICE_AGENT_URL := "wss://api.lokutor.com/ws/agent"
const TTS_URL := "wss://api.lokutor.com/ws/tts"
const STT_URL := "wss://api.lokutor.com/ws/stt"

## Voice slots: each is a native speaker of the language in use. GET /voices has the live list.
const VOICES: PackedStringArray = ["F1", "F2", "F3", "F4", "F5", "M1", "M2", "M3", "M4", "M5"]

## Languages the API documents for SDKs. fetch_languages() has the live list.
const LANGUAGES: Dictionary = {
	"en": "English", "es": "Spanish", "ca": "Catalan", "gl": "Galician", "eu": "Basque",
	"pt": "Portuguese", "fr": "French", "it": "Italian", "de": "German",
}

## Pass as a voice agent's language so the character follows the language the player speaks.
const AUTO_LANGUAGE := "auto"

## The 15 mouth shapes (the Oculus / MPEG-4 set), by id. Viseme events carry the id (`v`) and the name (`c`).
const VISEMES: PackedStringArray = ["sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "ih", "oh", "ou"]

## Rates of the audio the API takes and sends (16-bit PCM, mono).
const INPUT_RATE := 16000
const OUTPUT_RATE := 44100

## Close codes after which a session is opened again: the connection dropped or the server went away.
## Every other close is the server ending the session on purpose.
const RECONNECTABLE_CLOSE_CODES: Array = [1001, 1006, 1011, 1012, 1013, 1014]

static var _tag: RegEx


## In a browser a session may open with no key at all: the server decides. Lokutor's own pages (lokutor.com) get its
## bounded demo, every other site is refused with auth.missing_key. Anywhere else a key (or a ticket) is needed.
static func keyless_allowed() -> bool:
	return OS.has_feature("web")


## In a browser, the parameters of the page's address (?lang=es&level=1 is {"lang": "es", "level": "1"}); elsewhere {}.
## A web build has no command line: this is where a game's options come from there.
static func page_args() -> Dictionary:
	var out := {}
	if not OS.has_feature("web"):
		return out
	for pair in str(JavaScriptBridge.eval("location.search", true)).trim_prefix("?").split("&", false):
		var p := pair.split("=", true, 1)
		out[p[0].uri_decode()] = p[1].uri_decode() if p.size() > 1 else "1"
	return out


## In a browser, tells the page the game is embedded in (or its own page) what happened, as a message event with
## {source: "lokutor", type, ...data}. The SDK sends "session_limit" {seconds, reason} when the server says how long a
## conversation may last, and "demo_limit" {limit, message} when the lokutor.com demo stops one. Elsewhere it does nothing.
static func tell_page(type: String, data: Dictionary = {}) -> void:
	if not OS.has_feature("web"):
		return
	var message := data.duplicate()
	message["source"] = "lokutor"
	message["type"] = type
	JavaScriptBridge.eval("(window.parent || window).postMessage(%s, '*')" % JSON.stringify(message), true)


## The value the protocol carries for a language: "auto" and "" are both "" (follow the player).
static func language_to_wire(language: String) -> String:
	return "" if language == AUTO_LANGUAGE else language


## A line without the expression tags the voice performs ([laugh], [sad]...), for subtitles.
static func strip_tags(text: String) -> String:
	if _tag == null:
		_tag = RegEx.create_from_string(r"(?i)(?:\s*\[(?:breath|laugh|sigh|happy|sad|angry|surprised|fearful|disgusted|excited|anxious)\])+\s*")   # tags in a row: one space
	return _tag.sub(text, " ", true).strip_edges()


## The id (0-14) of a viseme name, whatever its case; 0 (closed) for an unknown one.
static func viseme_id(viseme_name: String) -> int:
	for i in VISEMES.size():
		if VISEMES[i].nocasecmp_to(viseme_name) == 0:
			return i
	return 0


## ws://host/path -> http://host
static func http_origin(url: String) -> String:
	var http := url.replace("wss://", "https://").replace("ws://", "http://")
	var scheme_end := http.find("://") + 3
	var slash := http.find("/", scheme_end)
	return http if slash == -1 else http.substr(0, slash)


## How every connection of the SDK checks the server's certificate; null is the engine's own way. For a server with its
## own certificate authority: TLSOptions.client(load("res://company.crt")).
static var tls_options: TLSOptions


## Why a WebSocket was refused. The engine does not say, and the server answers the same request over HTTP with the
## reason: a key that is wrong, a balance that is empty, an agent that does not exist.
static func _why_refused(ws_url: String, headers: PackedStringArray, detail := "") -> LokutorError:
	var reply := await request_json(ws_url.replace("wss://", "https://").replace("ws://", "http://"), HTTPClient.METHOD_GET, headers, "", 8.0)
	if reply.status >= 400 and reply.body is Dictionary and reply.body.has("code"):
		return reply.error
	if reply.result == HTTPRequest.RESULT_TLS_HANDSHAKE_ERROR:
		return LokutorError.new("ws.close", "The secure connection could not be verified. An antivirus or a proxy that inspects HTTPS (Avast and AVG do) or a wrong clock are the usual reasons.", detail, true)
	return LokutorError.new("ws.close", "Could not connect: check the API key and the network.", detail, true)


## Voices the API offers: [{id, name, gender, languages}].
static func fetch_voices(base_url: String = VOICE_AGENT_URL) -> Array:
	return (await _fetch(base_url, "/voices")).get("voices", [])


## Languages the API offers: [{code, name}].
static func fetch_languages(base_url: String = VOICE_AGENT_URL) -> Array:
	return (await _fetch(base_url, "/languages")).get("languages", [])


## Speech models: [{name, description, default}].
static func fetch_models(base_url: String = VOICE_AGENT_URL) -> Array:
	return (await _fetch(base_url, "/models")).get("models", [])


## Request limits and defaults: {max_text_length, min_speed, max_speed, min_steps, max_steps, sample_rate, channels}.
static func fetch_config(base_url: String = VOICE_AGENT_URL) -> Dictionary:
	return await _fetch(base_url, "/config")


## Runtime status of the service.
static func fetch_status(base_url: String = VOICE_AGENT_URL) -> Dictionary:
	return await _fetch(base_url, "/status")


## Readiness (GET /ready): {ready, status}.
static func fetch_health(base_url: String = VOICE_AGENT_URL) -> Dictionary:
	return await _fetch(base_url, "/ready")


static func _fetch(base_url: String, path: String) -> Dictionary:
	var reply := await request_json(http_origin(base_url) + path)
	return reply.body if reply.body is Dictionary else {}


## One HTTP request. Returns {ok, status, body, error, result}: body is the parsed JSON, error a LokutorError or null,
## result what HTTPRequest said.
static func request_json(url: String, method := HTTPClient.METHOD_GET, headers := PackedStringArray(), body := "", timeout := 15.0) -> Dictionary:
	var tree := Engine.get_main_loop() as SceneTree
	var http := HTTPRequest.new()
	http.timeout = timeout
	if tls_options != null:
		http.set_tls_options(tls_options)
	tree.root.add_child.call_deferred(http)
	await http.ready
	var started := http.request(url, headers + PackedStringArray(["Accept: application/json"]), method, body)
	if started != OK:
		http.queue_free()
		return {"ok": false, "status": 0, "body": null, "error": LokutorError.new("internal.error", "Could not request %s (%s)." % [url, error_string(started)]), "result": HTTPRequest.RESULT_REQUEST_FAILED}
	var r: Array = await http.request_completed
	http.queue_free()
	var status: int = r[1]
	var text: String = (r[3] as PackedByteArray).get_string_from_utf8()
	var parser := JSON.new()
	var json = parser.data if text != "" and parser.parse(text) == OK else null
	if r[0] != HTTPRequest.RESULT_SUCCESS:
		var code := "internal.timeout" if r[0] == HTTPRequest.RESULT_TIMEOUT else "internal.error"
		return {"ok": false, "status": status, "body": json, "error": LokutorError.new(code, "The request to %s failed (%d)." % [url.get_slice("?", 0), r[0]]), "result": r[0]}
	if status < 200 or status >= 300:
		var err := LokutorError.from_server(json)
		if not (json is Dictionary and json.has("code")):
			err = LokutorError.new("internal.error", "HTTP %d from %s" % [status, url.get_slice("?", 0)], text.left(300), status >= 500)
		return {"ok": false, "status": status, "body": json, "error": err, "result": r[0]}
	return {"ok": true, "status": status, "body": json, "error": null, "result": r[0]}
