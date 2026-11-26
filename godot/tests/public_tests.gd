extends SceneTree
## Live checks against production that need no key: the public endpoints, and how a key that is not one is refused.
##   godot --headless --path . --script res://tests/public_tests.gd
## LOKUTOR_BASE_URL is another server, such as ws://127.0.0.1:8791. Exit code 0 = passed. tests/run_tests.gd is the
## conversation, which needs a key.

var failed := 0


func _initialize() -> void:
	_run()


func _check(ok: bool, what: String, detail := "") -> void:
	print(("ok    " if ok else "FAIL  ") + what + ("" if detail == "" else "  (" + detail + ")"))
	if not ok:
		failed += 1


func _run() -> void:
	await process_frame
	var base := OS.get_environment("LOKUTOR_BASE_URL")
	if base == "":
		base = "wss://api.lokutor.com"
	var health := await Lokutor.fetch_health(base)
	_check(health.get("ready") == true, "the service is ready", str(health))

	var voices := await Lokutor.fetch_voices(base)
	var ids := voices.map(func(v): return v.id)
	_check(ids.has("F1") and ids.has("M1"), "the default voices exist", "%d voices" % ids.size())
	_check(voices.all(func(v): return v.id is String and v.name is String and v.gender is String and v.languages is Array), "a voice is {id, name, gender, languages}")

	var languages := (await Lokutor.fetch_languages(base)).map(func(l): return l.code)
	_check(languages.has("en") and languages.has("es"), "languages", ", ".join(languages))
	_check((await Lokutor.fetch_models(base)).any(func(m): return m.get("default") == true), "a default model")

	var config := await Lokutor.fetch_config(base)
	_check(int(config.get("sample_rate", 0)) == Lokutor.OUTPUT_RATE, "the sample rate is the one the SDK plays", str(config))
	_check(not (await Lokutor.fetch_status(base)).is_empty(), "status")

	var missing := await Lokutor.request_json(Lokutor.http_origin(base) + "/no-such-thing")
	_check(not missing.ok and missing.error is LokutorError, "a path that does not exist is an error", str(missing.error))

	var agent := LokutorVoiceAgentClient.new()
	root.add_child(agent)
	agent.server_url = base + "/ws/agent"
	agent.auto_reconnect = false
	agent.api_key = "lk_not_a_real_key"
	var errors: Array[LokutorError] = []
	var ended: Array = []
	agent.error.connect(func(e): errors.append(e))
	agent.ended.connect(func(code, reason): ended.append([code, reason]))
	var opened := await agent.open()
	var until := Time.get_ticks_msec() + 5000
	while errors.is_empty() and Time.get_ticks_msec() < until:
		await process_frame
	_check(not opened and not errors.is_empty(), "agent: a wrong key does not connect", str(errors))
	_check(errors.all(func(e): return e.code == "auth.invalid_key" and not e.retryable), "agent: and says why", str(errors.map(func(e): return e.code)))
	agent.queue_free()

	var tts := LokutorTTSClient.new()
	root.add_child(tts)
	tts.server_url = base + "/ws/tts"
	tts.api_key = "lk_not_a_real_key"
	var said := await tts.synthesize("Hello.")
	_check(not said.ok and said.audio.is_empty() and said.error.code == "auth.invalid_key", "text to speech: a wrong key", str(said.error))
	tts.queue_free()

	var stt := LokutorSpeechToTextClient.new()
	root.add_child(stt)
	stt.server_url = base + "/ws/stt"
	stt.use_microphone = false
	stt.api_key = "lk_not_a_real_key"
	var refused: Array[LokutorError] = []
	stt.error.connect(func(e): refused.append(e))
	var listening := await stt.open()
	_check(not listening and refused.size() == 1 and refused[0].code == "auth.invalid_key", "live speech to text: a wrong key", str(refused))
	stt.queue_free()

	var client := LokutorSTTClient.new("lk_not_a_real_key")
	client.server_url = base + "/ws/stt"
	var heard := await client.transcribe(PackedByteArray([0, 0, 0, 0]), {"sample_rate": 16000})
	_check(not heard.ok and heard.error.code == "auth.invalid_key", "speech to text: a wrong key", str(heard.error))

	print("PUBLIC TESTS: %d failed" % failed)
	quit(1 if failed else 0)
