class_name LokutorSTTClient
extends RefCounted
## Transcribes one complete recording (POST /stt/transcribe), as STTClient in the JavaScript and Python SDKs.
## For speech as it happens use LokutorSpeechToTextClient.

var api_key: String = ""
var auth: Callable
var server_url: String = Lokutor.STT_URL


func _init(p_api_key: String = "") -> void:
	api_key = p_api_key


## `audio` is 16-bit PCM mono (give sample_rate) or a whole WAV file's bytes. Options: sample_rate, format ("pcm16" or
## "wav"), language, keyterms. Returns {ok, text, latency_ms, engine, sample_rate, language, duration_seconds,
## segments, error}.
func transcribe(audio: PackedByteArray, options: Dictionary = {}) -> Dictionary:
	var key := api_key if api_key != "" else OS.get_environment("LOKUTOR_API_KEY")
	if auth.is_valid():
		var credential = await auth.call()
		key = credential if credential is String else ""
	if key == "":
		return {"ok": false, "text": "", "error": LokutorError.new("auth.missing_key",
			"The auth callback returned no credential." if auth.is_valid() else "No API key: set api_key, or auth for a shipped game.")}
	var is_wav := audio.size() > 12 and audio.slice(0, 4).get_string_from_ascii() == "RIFF"
	var body := {"audio": Marshalls.raw_to_base64(audio), "format": options.get("format", "wav" if is_wav else "pcm16")}
	if options.has("sample_rate") or not is_wav:
		body["sample_rate"] = int(options.get("sample_rate", Lokutor.INPUT_RATE))
	if options.has("language"):
		body["language"] = options.language
	if options.has("keyterms") and not options.keyterms.is_empty():
		body["keyterms"] = options.keyterms
	var reply := await Lokutor.request_json(Lokutor.http_origin(server_url) + "/stt/transcribe", HTTPClient.METHOD_POST,
		PackedStringArray(["Content-Type: application/json", "X-API-Key: " + key]), JSON.stringify(body), 60.0)
	var d: Dictionary = reply.body if reply.body is Dictionary else {}
	return {"ok": reply.ok, "text": str(d.get("text", "")), "latency_ms": float(d.get("latency_ms", 0.0)), "engine": str(d.get("engine", "")),
		"sample_rate": int(d.get("sample_rate", 0)), "language": str(d.get("language", "")),
		"duration_seconds": float(d.get("duration_seconds", 0.0)), "segments": d.get("segments", []), "error": reply.error}
