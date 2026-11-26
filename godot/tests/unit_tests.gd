extends SceneTree
## Headless tests of the whole addon against a mock server in this process: no key, no network, no sound card.
##   godot --headless --path . --script res://tests/unit_tests.gd
## Exit code 0 = passed. tests/run_tests.gd is the live conversation with production.

var server: LokutorMockServer
var passed := 0
var failed := 0
var _name := ""


func _initialize() -> void:
	_run()


func _run() -> void:
	await process_frame
	server = LokutorMockServer.new()
	root.add_child(server)
	if not server.start():
		print("could not start the mock server")
		quit(2)
		return
	for test in ["helpers", "resampler", "errors", "config", "messages", "transcript", "socket", "reconnect", "refused", "own_model",
			"player_clock", "lip_rigs", "lip_timeline", "lip_fallback", "agent_conversation", "agent_actions", "agent_interrupted",
			"agent_mute", "agent_echo", "agent_filler", "agent_others", "agent_background_sound", "agent_context", "agent_end", "agent_demo", "tts", "tts_errors", "voice_cache", "voice_queue",
			"stt", "stt_stream", "client_lifecycle", "agent_no_credential", "agent_late_thinking", "agent_dashboard",
			"agent_mute_button", "lip_shared", "lip_names", "tts_late_eos", "tts_cancel", "voice_stop_keeps_preload", "voice_removed"]:
		_name = test
		var before := failed
		await call("_test_" + test)
		print(("ok    " if failed == before else "FAIL  ") + test)
		for s in server.sessions:
			s.closed = true
		server.sessions.clear()
		server.refuse = false
	print("GODOT UNIT TESTS: %d checks passed, %d failed" % [passed, failed])
	quit(0 if failed == 0 else 1)


func check(ok: bool, what: String) -> void:
	if ok:
		passed += 1
	else:
		failed += 1
		print("  FAIL %s: %s" % [_name, what])


func eq(got, want, what: String) -> void:
	check(got == want, "%s: got %s, wanted %s" % [what, got, want])


func near(got: float, want: float, tolerance: float, what: String) -> void:
	check(absf(got - want) <= tolerance, "%s: got %.4f, wanted %.4f" % [what, got, want])


## Waits until `condition` holds, at most `seconds`.
func until(condition: Callable, seconds := 3.0) -> bool:
	var end := Time.get_ticks_msec() + int(seconds * 1000.0)
	while Time.get_ticks_msec() < end:
		if condition.call():
			return true
		await process_frame
	return condition.call()


func pause(seconds: float) -> void:
	await create_timer(seconds).timeout


## A tone as PCM16 at the output rate.
func tone(seconds: float, volume := 0.3) -> PackedByteArray:
	var n := int(seconds * Lokutor.OUTPUT_RATE)
	var out := PackedByteArray()
	out.resize(n * 2)
	for i in n:
		out.encode_s16(i * 2, int(sin(i * TAU * 220.0 / Lokutor.OUTPUT_RATE) * volume * 32767.0))
	return out


func new_client(options := {}) -> LokutorVoiceAgentClient:
	var o := {"api_key": "sk_test", "server_url": server.url("/ws/agent"), "prompt": "You are Mira."}
	o.merge(options, true)
	var c := LokutorVoiceAgentClient.create(o)
	root.add_child(c)
	return c


func new_agent(options := {}) -> LokutorAgent:
	var a := LokutorAgent.new()
	a.api_key = "sk_test"
	a.url = server.url("/ws/agent")
	a.start_on_ready = false
	a.mic_mode = LokutorAgent.MicMode.OFF
	a.simulate_audio_device = true
	for k in options:
		a.set(k, options[k])
	root.add_child(a)
	return a


## A mesh with these blend shapes.
func face_with(shapes: Array) -> MeshInstance3D:
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = PackedVector3Array([Vector3(0, 0, 0), Vector3(1, 0, 0), Vector3(0, 1, 0)])
	var mesh := ArrayMesh.new()
	var blends: Array[Array] = []
	for s in shapes:
		mesh.add_blend_shape(s)
		var b := []
		b.resize(Mesh.ARRAY_MAX)
		b[Mesh.ARRAY_VERTEX] = PackedVector3Array([Vector3(0, 0, 1), Vector3(1, 0, 1), Vector3(0, 1, 1)])
		blends.append(b.slice(0, 3))
	for b in blends:
		b.resize(3)
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays, blends)
	var node := MeshInstance3D.new()
	node.mesh = mesh
	root.add_child(node)
	return node


# ---- Helpers -------------------------------------------------------------------------------------

func _test_helpers() -> void:
	eq(Lokutor.language_to_wire("auto"), "", "auto follows the player")
	eq(Lokutor.language_to_wire("es"), "es", "a language is itself")
	eq(Lokutor.strip_tags("Okay. [laugh] That's it! [Sigh]"), "Okay. That's it!", "sound tags")
	eq(Lokutor.strip_tags("[sad] I'm sorry."), "I'm sorry.", "emotion tags")
	eq(Lokutor.strip_tags("Take the [iron sword]."), "Take the [iron sword].", "other brackets stay")
	eq(Lokutor.viseme_id("pp"), 1, "viseme names ignore case (the JavaScript SDK lowercases them)")
	eq(Lokutor.viseme_id("what"), 0, "unknown viseme")
	eq(Lokutor.http_origin("wss://api.lokutor.com/ws/agent"), "https://api.lokutor.com", "origin")
	eq(Lokutor.http_origin("ws://localhost:8080/ws/agent"), "http://localhost:8080", "local origin")
	eq(Lokutor.VISEMES.size(), 15, "15 visemes")
	var pcm := LokutorAudio.float32_to_pcm16(PackedFloat32Array([0.0, 0.5, -0.5, 2.0]))
	var back := LokutorAudio.pcm16_to_float32(pcm)
	near(back[1], 0.5, 0.001, "PCM round trip")
	near(back[3], 1.0, 0.001, "PCM clips")
	near(LokutorAudio.calculate_rms(PackedFloat32Array([0.5, -0.5])), 0.5, 0.001, "rms")
	near(LokutorAudio.normalize_audio(PackedFloat32Array([0.1, -0.2]))[1], -0.95, 0.001, "normalise")
	eq(LokutorAudio.wav_bytes(pcm, 16000).size(), 44 + pcm.size(), "WAV size")
	server.http_body = {"voices": [{"id": "F1"}]}
	server.http_status = 200
	var voices := await Lokutor.fetch_voices(server.http_url("/ws/agent"))
	eq(voices.size(), 1, "fetch_voices")
	check(server.http_requests.back().line.begins_with("GET /voices"), "asks the API's root, not the socket's path")


func _test_resampler() -> void:
	var input := PackedFloat32Array()
	for i in 4800:
		input.append(sin(i * TAU * 300.0 / 48000.0))
	var whole := LokutorAudio.StreamResampler.new(48000, 16000).process(input)
	var r := LokutorAudio.StreamResampler.new(48000, 16000)
	var chunked := PackedFloat32Array()
	var i := 0
	while i < input.size():
		chunked.append_array(r.process(input.slice(i, i + 331)))
		i += 331
	near(whole.size(), 1600, 2, "a third of the samples")
	check(absi(chunked.size() - whole.size()) <= 2, "chunked has the same length")
	var worst := 0.0
	for k in mini(chunked.size(), whole.size()):
		worst = maxf(worst, absf(chunked[k] - whole[k]))
	check(worst < 0.001, "chunked equals one shot (worst %.4f)" % worst)
	var up := LokutorAudio.resample(whole, 16000, 44100)
	near(up.size(), 4410, 10, "upsampling")


func _test_errors() -> void:
	var e := LokutorError.from_server({"code": "billing.insufficient_balance", "message": "No", "detail": "d"})
	eq(e.code, "billing.insufficient_balance", "code")
	eq(e.retryable, false, "billing is not retryable")
	eq(LokutorError.from_server("AI response service error.").code, "internal.error", "plain string errors")
	eq(LokutorError.new("internal.timeout", "t").retryable, true, "timeouts are")
	eq(LokutorError.from_server({"code": "internal.error", "message": "", "retryable": false}).retryable, false, "the server's word wins")


# ---- Voice agent client --------------------------------------------------------------------------

func _test_config() -> void:
	var c := LokutorVoiceAgentClient.create({"api_key": "k", "prompt": "Hi", "voice": "M2", "language": "auto", "visemes": true,
		"tools": [{"type": "function", "function": {"name": "f"}}]})
	var types := c.config_messages().map(func(m): return m.type)
	eq(types, ["voice", "language", "tools", "visemes", "prompt", "rates"], "order, rates last")
	var m := c.config_messages()
	eq(m[1].data, "", "auto is sent empty")
	eq(m[3].data, true, "visemes")
	eq([m[5].playback, m[5].input], [44100, 16000], "rates")
	c.free()
	c = LokutorVoiceAgentClient.create({"api_key": "k", "agent_id": "a1"})
	eq(c.config_messages().map(func(x): return x.type), ["visemes", "rates"], "a dashboard agent keeps its own voice, language and prompt")
	c.update_voice("F3")
	eq(c.config_messages().map(func(x): return x.type), ["voice", "visemes", "rates"], "unless set")
	c.free()
	c = LokutorVoiceAgentClient.create({"api_key": "k", "prompt": "p", "llm": {"base_url": "https://x/v1", "model": "m"}, "pitch": 2.0})
	var llm = c.config_messages().filter(func(x): return x.type == "llm")[0]
	eq(llm.data, {"base_url": "https://x/v1", "model": "m"}, "own endpoint")
	eq(c.config_messages().filter(func(x): return x.type == "pitch")[0].data, 2.0, "pitch")
	c.on_llm_request = func(_a, _b, _c): return "hi"
	c.llm = {}
	eq(c.config_messages().filter(func(x): return x.type == "llm")[0].data.mode, "client", "own model in the game")
	c.free()


func _test_messages() -> void:
	var c := LokutorVoiceAgentClient.new()
	root.add_child(c)
	var got := {"audio": [], "visemes": [], "errors": [], "calls": [], "status": [], "partial": [], "limit": [], "gens": []}
	c.audio.connect(func(pcm, g): got.audio.append([pcm.size(), g]))
	c.visemes.connect(func(l, g): got.visemes.append([l, g]))
	c.error.connect(func(e): got.errors.append(e))
	c.tool_call.connect(func(t): got.calls.append(t))
	c.status.connect(func(s): got.status.append(s))
	c.transcription_partial.connect(func(t): got.partial.append(t))
	c.session_limit.connect(func(s, r): got.limit = [s, r])
	c.generation_started.connect(func(g): got.gens.append(g))
	var b64 := Marshalls.raw_to_base64(PackedByteArray([0, 0, 0, 0]))
	c.handle_message('{"type":"status","data":"thinking","generation":2}')
	c.handle_message('{"type":"audio","data":"%s","generation":1}' % b64)
	c.handle_message('{"type":"audio","data":"%s","generation":2}' % b64)
	eq(got.audio, [[4, 2]], "stale audio is dropped")
	c.handle_message('{"type":"visemes","generation":1,"data":[{"v":1,"c":"PP","t":0,"d":0.1}]}')
	c.handle_message('{"type":"visemes","generation":2,"data":[{"v":10,"c":"aa","t":0.1,"d":0.2},{"v":0,"c":"sil","t":0.3}]}')
	eq(got.visemes.size(), 1, "stale visemes are dropped")
	eq(got.visemes[0][0][0], {"v": 10, "c": "aa", "t": 0.1, "d": 0.2}, "viseme")
	check(got.visemes[0][0][1].d > 0.0, "a missing duration gets one")
	var odd := LokutorVoiceAgentClient.normalize_visemes([{"v": 15, "c": "oh", "t": 0.0}, {"v": -3, "t": 0.1}, {"c": "FF", "t": 0.2}, {"v": null, "c": "aa", "t": 0.3}])
	eq(odd.map(func(x): return x.v), [13, 0, 2, 10], "ids out of range fall back to the name")
	c.handle_message('{"type":"audio","data":"%s","generation":3}' % b64)
	eq(got.gens, [2, 3], "a newer reply announces itself before its audio")
	c.handle_message('{"type":"error","data":{"code":"validation.invalid_voice","message":"No such voice"}}')
	c.handle_message('{"type":"error","data":"AI response service error."}')
	eq(got.errors.map(func(e): return e.code), ["validation.invalid_voice", "internal.error"], "both error shapes")
	c.handle_message('{"type":"tool_call","name":"open","arguments":"{\\"x\\":1}","call_id":"c1"}')
	eq(got.calls, [{"name": "open", "arguments": '{"x":1}', "call_id": "c1"}], "tool call")
	c.handle_message('{"type":"transcript","role":"user","data":"hel","partial":true}')
	eq(got.partial, ["hel"], "partial")
	c.handle_message('{"type":"session_limit","data":{"seconds":90,"reason":"demo"}}')
	eq(got.limit, [90.0, "demo"], "session limit")
	c.handle_message('{"type":"server_rates","playback":48000,"input":16000}')
	eq(c.playback_rate, 48000, "server rates")
	c.handle_message('[1,2]')
	check(true, "rubbish is ignored")
	c.free()


func _test_transcript() -> void:
	var c := LokutorVoiceAgentClient.new()
	root.add_child(c)
	var full := "Welcome to my shop, take a look around."
	c.handle_message('{"type":"transcript","role":"user","data":"Hello"}')
	c.handle_message('{"type":"transcript","role":"agent","data":"%s"}' % full)
	c.handle_message('{"type":"transcript","role":"user","data":"par","partial":true}')
	eq(c.get_transcript_text(), "You: Hello\nAgent: " + full, "history, without partials")
	c.handle_message('{"type":"transcript_truncated","role":"agent","data":"Welcome to…","full":"%s","emitted":true}' % full)
	eq(c.get_transcript()[1].text, "Welcome to…", "an interrupted line keeps what was heard")
	c.handle_message('{"type":"transcript","role":"agent","data":"Never heard"}')
	c.handle_message('{"type":"transcript_truncated","role":"agent","data":"","full":"Never heard","emitted":true}')
	eq(c.get_transcript().size(), 2, "a line never heard is withdrawn")
	c.handle_message('{"type":"transcript_truncated","role":"agent","data":"One mo…","full":"One moment","emitted":false}')
	eq(c.get_transcript()[2].text, "One mo…", "a line cut before its transcript is added")
	c.handle_message('{"type":"transcript","role":"user","data":"I want"}')
	c.handle_message('{"type":"transcript","role":"agent","data":"Yes?"}')
	c.handle_message('{"type":"transcript_revised","role":"user","data":"I want a sword","previous":"I want"}')
	eq(c.get_transcript()[-1].text, "I want a sword", "a paused sentence becomes one line")
	eq(c.get_transcript()[-1].role, "user", "and the agent line after it is dropped")
	c.get_transcript().clear()
	check(c.get_transcript().size() > 0, "get_transcript is a copy")
	c.reset()
	eq(c.get_transcript().size(), 0, "reset")
	c.free()


func _test_socket() -> void:
	var c := new_client({"visemes": true})
	var log: Array = []
	c.status.connect(func(s): log.append(s))
	c.ended.connect(func(code, reason): log.append("ended %d %s" % [code, reason]))
	c.error.connect(func(e): log.append("error " + e.code))
	var ok := await c.open()
	check(ok and c.connected, "opens")
	await until(func(): return server.current() != null and server.current().last("rates") != null)
	var s := server.current()
	eq(s.types(), ["voice", "language", "visemes", "prompt", "rates"], "the server receives the configuration")
	s.send({"type": "status", "data": "connected", "session_id": "s-1"})
	await until(func(): return c.session_id == "s-1")
	eq(c.session_id, "s-1", "session id")
	c.send_audio(PackedByteArray([1, 0, 2, 0]))
	c.send_audio_float(PackedFloat32Array([0.0, 0.1, 0.2]), 16000)
	await until(func(): return s.audio.size() >= 10)
	eq(s.audio.size(), 10, "microphone audio arrives")
	c.update_prompt("You are angry.")
	c.update_voice("M3")
	c.update_language("auto")
	c.update_tools([])
	c.update_pitch(3)
	c.submit_tool_result("c9", {"ok": true})
	await until(func(): return s.last("tool_result") != null)
	eq(s.last("prompt").data, "You are angry.", "prompt update")
	eq(s.last("language").data, "", "language update")
	eq(s.last("tool_result").data, {"call_id": "c9", "result": '{"ok":true}'}, "tool result")
	s.ws.close(1000, "goodbye")
	await until(func(): return log.has("ended 1000 goodbye"))
	check(log.has("ended 1000 goodbye"), "a deliberate end is reported")
	check(log.has("error session.ended"), "with its error, as in JavaScript")
	await pause(0.3)
	check(not log.has("reconnecting"), "and not reopened")
	# The credential: a key in a header, a ticket in the query.
	check(not s.url.contains("api_key"), "an API key is not in the URL")
	c.auth = func(): return "wst_abc"
	check(await c.open(), "opens again")
	await until(func(): return server.sessions.size() == 2 and server.current().opened)
	check(server.current().url.contains("api_key=wst_abc"), "a ticket is in the query")
	c.auth = func(): return ""
	c.close()
	check(not await c.open(), "no credential, no connection")
	check(log.has("error auth.missing_key"), "and it says why")
	c.queue_free()


func _test_reconnect() -> void:
	var c := new_client()
	var log: Array = []
	c.status.connect(func(x): log.append(x))
	c.reconnected.connect(func(): log.append("reconnected"))
	await c.open()
	await until(func(): return server.current() != null and server.current().last("rates") != null)
	server.current().send({"type": "status", "data": "thinking", "generation": 7})
	await until(func(): return c.generation == 7)
	server.current().drop()
	await until(func(): return log.has("reconnecting"))
	check(log.has("reconnecting"), "a dropped connection is noticed")
	await until(func(): return log.has("reconnected"), 5.0)
	check(log.has("reconnected"), "and opened again")
	eq(c.generation, 0, "the new session counts its replies from the start")
	await until(func(): return server.current().last("rates") != null)
	eq(server.current().types(), ["voice", "language", "visemes", "prompt", "rates"], "with the whole configuration")
	c.close()
	check(log.back() == "disconnected", "close")
	c.queue_free()


func _test_refused() -> void:
	server.refuse = true
	var c := new_client()
	var log: Array = []
	c.error.connect(func(e): log.append(e.code))
	var ok := await c.open()
	check(not ok, "open() is false")
	eq(log, ["ws.close"], "one error")
	await pause(0.2)
	eq(log.size(), 1, "a first connection that fails is not retried")
	c.queue_free()
	server.refuse = false

	# The API says why over HTTP, and so do the clients.
	server.refuse_with = {"code": "auth.invalid_key", "message": "The provided API key is invalid or has been revoked.", "retryable": false}
	c = new_client()
	var errors: Array = []
	var states: Array = []
	c.error.connect(func(e): errors.append(e))
	c.status.connect(func(x): states.append(x))
	check(not await c.open(), "a wrong key does not open")
	eq(errors.map(func(e): return e.code), ["auth.invalid_key"], "and says why, once")
	check(not errors[0].retryable and errors[0].message.contains("revoked"), "in the server's words")
	eq(states.back(), "disconnected", "and ends")
	errors.clear()
	(func(): c.open()).call()
	await process_frame
	c.close()
	await pause(0.5)
	eq(errors, [], "closed while it asks why: nothing more is said")
	server.refuse_with = {}
	errors.clear()
	check(await c.open(), "with the right one it opens")
	await until(func(): return server.current() != null and server.current().last("rates") != null)

	var n := server.sessions.size()
	server.refuse_with = {"code": "billing.insufficient_balance", "message": "The balance is empty.", "retryable": false}
	server.current().drop()
	await until(func(): return not errors.is_empty(), 6.0)
	eq(errors.map(func(e): return e.code), ["billing.insufficient_balance"], "a reconnection that no attempt can fix")
	await pause(2.5)
	eq([server.sessions.size(), states.back()], [n, "disconnected"], "is not attempted again")
	server.refuse_with = {"code": "auth.invalid_key", "message": "No.", "retryable": false}
	c.queue_free()

	var tts := LokutorTTSClient.new()
	root.add_child(tts)
	tts.api_key = "sk_test"
	tts.server_url = server.url("/ws/tts")
	var r := await tts.synthesize("Hello.")
	check(not r.ok and r.error.code == "auth.invalid_key", "text to speech says why")
	tts.queue_free()
	var stt := LokutorSpeechToTextClient.new()
	root.add_child(stt)
	stt.api_key = "sk_test"
	stt.use_microphone = false
	stt.server_url = server.url("/ws/stt")
	var heard: Array = []
	stt.error.connect(func(e): heard.append(e.code))
	check(not await stt.open(), "speech to text does not open")
	eq(heard, ["auth.invalid_key"], "and says why")
	stt.queue_free()
	server.refuse_with = {}


func _test_own_model() -> void:
	var c := new_client({"llm_tools": "none"})
	c.on_llm_request = func(messages: Array, _tools: Array, reply: LokutorVoiceAgentClient.LlmReply):
		reply.chunk("You said ")
		await process_frame
		reply.chunk(messages[-1].content)
		reply.tool_call("wave", {"hand": "left"})
	await c.open()
	await until(func(): return server.current() != null and server.current().last("rates") != null)
	var s := server.current()
	s.send({"type": "llm_request", "request_id": "r1", "messages": [{"role": "user", "content": "hi"}], "tools": []})
	await until(func(): return s.last("llm_done") != null)
	eq(s.types().slice(-4), ["llm_chunk", "llm_chunk", "llm_tool_call", "llm_done"], "streams the reply")
	eq(s.last("llm_tool_call").arguments, '{"hand":"left"}', "tool call")
	var seen := {"cancelled": false}
	c.on_llm_request = func(_m, _t, reply):
		for i in 60:
			await process_frame
			if reply.cancelled:
				seen.cancelled = true
				return
		reply.chunk("late")
	s.send({"type": "llm_request", "request_id": "r2", "messages": [], "tools": []})
	await pause(0.1)
	s.send({"type": "llm_cancel", "request_id": "r2"})
	await until(func(): return seen.cancelled)
	check(seen.cancelled, "the player speaking over it cancels it")
	c.queue_free()


# ---- Voice player and lip sync -------------------------------------------------------------------

func _test_player_clock() -> void:
	var p := LokutorVoicePlayer.new()
	p.simulate_audio_device = true
	p.record = true
	root.add_child(p)
	var log: Array = []
	p.started.connect(func(g): log.append("started %d" % g))
	p.finished.connect(func(g): log.append("finished %d" % g))
	p.begin(1)
	p.push(tone(0.6), 1)
	var t0 := Time.get_ticks_msec()
	await until(func(): return p.heard_seconds() > 0.25)
	near(p.heard_seconds(), (Time.get_ticks_msec() - t0) / 1000.0, 0.06, "the clock runs in real time")
	check(p.level > 0.1, "loudness of what is heard (%.2f)" % p.level)
	check(p.is_playing() and p.is_audible(), "playing")
	await until(func(): return log.has("finished 1"))
	eq(log, ["started 1", "finished 1"], "start and finish")
	near(p.heard_seconds(), 0.6, 0.01, "all of it was heard")
	near(p.recorded.size(), 0.6 * 44100, 2, "and recorded")
	eq(p.level, 0.0, "silent")
	p.begin(2)
	eq(p.heard_seconds(), 0.0, "a new reply counts from 0")
	p.push(tone(1.0), 1)
	check(not p.is_playing(), "audio of an older reply is ignored")
	p.push(tone(1.0), 2)
	await pause(0.1)
	p.clear()
	check(not p.is_playing(), "clear stops")
	p.push(tone(0.2), 3)
	eq(p.generation, 3, "audio of a newer reply begins it")
	p.queue_free()


func _test_lip_rigs() -> void:
	var rigs := {
		"viseme": ["viseme_sil", "viseme_PP", "viseme_FF", "viseme_TH", "viseme_DD", "viseme_kk", "viseme_CH", "viseme_SS", "viseme_nn", "viseme_RR", "viseme_aa", "viseme_E", "viseme_I", "viseme_O", "viseme_U"],
		"vrm": ["aa", "ih", "ou", "ee", "oh", "blink"],
		"arkit": ["jawOpen", "mouthFunnel", "mouthPucker", "mouthSmileLeft", "mouthSmileRight", "mouthClose"],
		"open": ["blink", "mouthOpen"],
	}
	var want := {"viseme": LokutorLipSync.Rig.VISEMES, "vrm": LokutorLipSync.Rig.VRM, "arkit": LokutorLipSync.Rig.ARKIT, "open": LokutorLipSync.Rig.MOUTH_OPEN}
	var open_shape := {"viseme": "viseme_aa", "vrm": "aa", "arkit": "jawOpen", "open": "mouthOpen"}
	for r in rigs:
		var face := face_with(rigs[r])
		var lips := LokutorLipSync.new()
		lips.faces = [face]
		root.add_child(lips)
		eq(lips.rig(), want[r], r + " rig is recognised")
		lips.push_visemes([{"v": 10, "c": "aa", "t": 0.0, "d": 1.0}])
		for i in 30:
			lips.advance(0.4, 0.3, true, 1.0 / 60.0)
		var index := face.find_blend_shape_by_name(open_shape[r])
		check(face.get_blend_shape_value(index) > 0.5, "%s: aa opens the mouth (%.2f)" % [r, face.get_blend_shape_value(index)])
		lips.clear()
		lips.push_visemes([{"v": 14, "c": "ou", "t": 0.0, "d": 1.0}])
		for i in 30:
			lips.advance(0.4, 0.3, true, 1.0 / 60.0)
		var round_shape: String = {"viseme": "viseme_U", "vrm": "ou", "arkit": "mouthPucker", "open": "mouthOpen"}[r]
		check(face.get_blend_shape_value(face.find_blend_shape_by_name(round_shape)) > 0.2, r + ": ou rounds it")
		lips.clear()
		for i in 60:
			lips.advance(0.0, 0.0, false, 1.0 / 60.0)
		check(face.get_blend_shape_value(index) < 0.02, r + ": and silence closes it")
		lips.free()
		face.free()
	var f := face_with(["smile", "gape"])
	var custom := LokutorLipSync.new()
	custom.faces = [f]
	custom.mapping = {"aa": {"gape": 0.8}}
	root.add_child(custom)
	eq(custom.rig(), LokutorLipSync.Rig.CUSTOM, "your own names")
	custom.free()
	f.free()


func _test_lip_timeline() -> void:
	var lips := LokutorLipSync.new()
	lips.lead = 0.0
	lips.loudness = 0.0
	root.add_child(lips)
	lips.push_visemes([{"v": 1, "c": "PP", "t": 0.0, "d": 0.1}, {"v": 10, "c": "aa", "t": 0.1, "d": 0.2}, {"v": 14, "c": "ou", "t": 0.3, "d": 0.2}, {"v": 0, "c": "sil", "t": 0.5, "d": 0.3}])
	var seen: Array = []
	lips.viseme_changed.connect(func(v): seen.append(v))
	var at := {}
	var t := 0.0
	while t < 0.75:
		lips.advance(t, 0.2, true, 1.0 / 120.0)
		at[snappedf(t, 0.01)] = [lips.weights.duplicate(), lips.mouth_open]
		t += 1.0 / 120.0
	eq(seen, ["PP", "aa", "ou", "sil"], "each shape in order")
	check(at[0.05][0][1] > 0.7 and at[0.05][1] < 0.1, "p closes the lips")
	check(at[0.2][0][10] > 0.9, "aa is open in its middle")
	check(at[0.3][0][10] > 0.2 and at[0.3][0][14] > 0.2, "neighbours overlap at the boundary")
	check(at[0.4][0][14] > 0.9, "ou is round in its middle")
	check(at[0.74][1] < 0.02, "sil closes")
	# A very short shape between two others never reaches its full weight.
	lips.clear()
	lips.push_visemes([{"v": 10, "c": "aa", "t": 0.0, "d": 0.2}, {"v": 4, "c": "DD", "t": 0.2, "d": 0.02}, {"v": 10, "c": "aa", "t": 0.22, "d": 0.2}])
	var peak := 0.0
	t = 0.0
	while t < 0.42:
		lips.advance(t, 0.2, true, 1.0 / 120.0)
		peak = maxf(peak, lips.weights[4])
		t += 1.0 / 120.0
	check(peak < 0.6, "a 20 ms tongue tap is only hinted (%.2f)" % peak)
	lips.free()


func _test_lip_fallback() -> void:
	var jaw := Node3D.new()
	root.add_child(jaw)
	var lips := LokutorLipSync.new()
	lips.jaw = jaw
	lips.jaw_max_angle_degrees = 20.0
	root.add_child(lips)
	for i in 30:
		lips.advance(0.1, 0.25, true, 1.0 / 60.0)
	check(lips.mouth_open > 0.6, "without visemes the mouth follows the loudness")
	check(rad_to_deg(jaw.rotation.x) > 8.0, "and the jaw turns")
	var sprite := Sprite2D.new()
	sprite.hframes = 4
	root.add_child(sprite)
	lips.sprite = sprite
	lips.push_visemes([{"v": 13, "c": "oh", "t": 0.0, "d": 1.0}])
	for i in 30:
		lips.advance(0.5, 0.25, true, 1.0 / 60.0)
	eq(sprite.frame, 3, "a sprite with mouth frames shows the round one")
	lips.free()
	jaw.free()
	sprite.free()


# ---- LokutorAgent --------------------------------------------------------------------------------

## Starts a conversation and returns the server's side of it, connected.
func talk(a: LokutorAgent) -> LokutorMockServer.Session:
	var before := server.sessions.size()
	a.start_conversation()
	await until(func(): return server.sessions.size() > before and server.current().last("rates") != null)
	server.current().send({"type": "status", "data": "connected", "session_id": "x"})
	await until(func(): return a.state == LokutorAgent.State.READY)
	return server.current()


func reply(s: LokutorMockServer.Session, gen: int, text: String, seconds: float) -> void:
	s.send({"type": "status", "data": "thinking", "generation": gen})
	s.send({"type": "transcript", "role": "agent", "data": text})
	s.send({"type": "status", "data": "speaking", "generation": gen})
	s.send({"type": "visemes", "generation": gen, "data": [{"v": 1, "c": "PP", "t": 0.0, "d": 0.1}, {"v": 10, "c": "aa", "t": 0.1, "d": seconds - 0.15}, {"v": 0, "c": "sil", "t": seconds - 0.05, "d": 0.05}]})
	s.send_audio(tone(seconds), gen)


func _test_agent_conversation() -> void:
	var a := new_agent({"persona": "You are Brom.", "voice": "M1"})
	var states: Array = []
	var said: Array = []
	var heard: Array = []
	a.state_changed.connect(func(s): states.append(LokutorAgent.State.keys()[s]))
	a.character_said.connect(func(t): said.append(t))
	a.player_said.connect(func(t): heard.append(t))
	var s := await talk(a)
	eq(s.last("visemes").data, true, "asks for visemes")
	eq(s.last("voice").data, "M1", "voice")
	reply(s, 1, "Welcome! [laugh]", 0.5)
	await until(func(): return a.state == LokutorAgent.State.SPEAKING and a.mouth_open > 0.3)
	check(a.mouth_open > 0.3, "the mouth opens with the voice (%.2f)" % a.mouth_open)
	check(a.heard_seconds() > 0.05 and a.voice_level() > 0.05, "clock and loudness")
	await until(func(): return a.state == LokutorAgent.State.READY)
	eq(states, ["CONNECTING", "READY", "THINKING", "SPEAKING", "READY"], "states")
	eq(said, ["Welcome! [laugh]"], "said once")
	check(a.mouth_open < 0.15, "and closes after it")
	s.send({"type": "status", "data": "listening"})
	s.send({"type": "transcript", "role": "user", "data": "A sword please"})
	await until(func(): return heard.size() == 1)
	eq(a.state, LokutorAgent.State.PLAYER_TALKING, "hears the player")
	s.send({"type": "status", "data": "thinking", "generation": 2})
	s.send({"type": "status", "data": "thinking", "generation": 2})
	s.send({"type": "status", "data": "speaking", "generation": 2})
	s.send_audio(tone(0.5), 2)
	await until(func(): return a.state == LokutorAgent.State.SPEAKING)
	s.send({"type": "status", "data": "thinking", "generation": 2})
	await pause(0.1)
	eq(a.state, LokutorAgent.State.SPEAKING, "a second sentence of the same reply does not stop it")
	a.set_persona("You are angry.")
	await until(func(): return s.last("prompt").data == "You are angry.")
	eq(s.last("prompt").data, "You are angry.", "set_persona")
	a.queue_free()


func _test_agent_actions() -> void:
	var a := new_agent()
	var called: Array = []
	a.add_action("give_item", "Sell an item", func(args):
		called.append(args)
		await process_frame
		return "Gave the %s." % args.item, {"item": {"type": "string", "enum": ["sword"]}, "note": {"optional": true}})
	var s := await talk(a)
	var def: Dictionary = s.last("tools").data[0].function
	eq(def.name, "give_item", "tool definition")
	eq(def.parameters.required, ["item"], "optional arguments are not required")
	s.send({"type": "tool_call", "name": "give_item", "arguments": '{"item":"sword"}', "call_id": "c1"})
	await until(func(): return s.last("tool_result") != null)
	eq(called, [{"item": "sword"}], "the handler runs once")
	eq(s.last("tool_result").data, {"call_id": "c1", "result": "Gave the sword."}, "and an awaited result goes back")
	eq(a.state, LokutorAgent.State.THINKING, "it thinks until it has used the result")
	s.send({"type": "tool_call", "name": "fly", "arguments": "{}", "call_id": "c2"})
	await until(func(): return s.last("tool_result").data.call_id == "c2")
	check(s.last("tool_result").data.result.contains("no action called fly"), "an unknown action is answered too")
	a.add_action("wave", "Wave", func(_x): return null)
	await until(func(): return s.last("tools").data.size() == 2)
	eq(s.last("tools").data.size(), 2, "actions added mid-conversation are sent")
	a.remove_action("wave")
	await until(func(): return s.last("tools").data.size() == 1)
	eq(s.last("tools").data.size(), 1, "and removed")
	a.queue_free()


func _test_agent_interrupted() -> void:
	var a := new_agent()
	var cut: Array = []
	a.character_interrupted.connect(func(t): cut.append(t))
	var s := await talk(a)
	reply(s, 1, "A long story about dragons", 5.0)
	await until(func(): return a.state == LokutorAgent.State.SPEAKING and a.heard_seconds() > 0.2)
	s.send({"type": "status", "data": "listening"})
	await pause(0.15)
	eq(a.state, LokutorAgent.State.SPEAKING, "a cough is not an interruption")
	s.send({"type": "status", "data": "interrupted"})
	s.send({"type": "transcript_truncated", "role": "agent", "data": "A long…", "full": "A long story about dragons", "emitted": true})
	await until(func(): return cut.size() == 1)
	eq(a.state, LokutorAgent.State.PLAYER_TALKING, "interrupted")
	check(not a.player.is_playing(), "and silent")
	eq(cut, ["A long…"], "what was heard")
	s.send_audio(tone(0.5), 1)
	reply(s, 2, "Yes?", 0.3)
	await until(func(): return a.state == LokutorAgent.State.SPEAKING)
	eq(a.player.generation, 2, "the next reply plays")
	a.queue_free()


func _test_agent_filler() -> void:
	# As production does it: the call, the filler as a generation of its own, the reply's text, the reply as another one.
	var a := new_agent()
	a.add_action("look", "Look", func(_x): return "Nothing.")
	var played := {"n": 0, "visemes": 0}
	a.voice_played.connect(func(x, _r): played.n += x.size())
	a.visemes_received.connect(func(l): played.visemes += l.size())
	var states: Array = []
	a.state_changed.connect(func(st): states.append(LokutorAgent.State.keys()[st]))
	var s := await talk(a)
	var filler := func(gen: int, seconds: float):
		s.send({"type": "status", "data": "listening"})
		s.send({"type": "tool_call", "name": "look", "arguments": "{}", "call_id": "c%d" % gen})
		s.send({"type": "status", "data": "thinking", "generation": gen})
		s.send({"type": "status", "data": "speaking", "generation": gen})
		s.send({"type": "visemes", "generation": gen, "data": [{"v": 1, "c": "PP", "t": 0.0, "d": 0.1}]})
		s.send_audio(tone(seconds), gen)
	filler.call(1, 0.6)
	await pause(0.5)
	check(played.n == 0 and played.visemes == 0 and a.state == LokutorAgent.State.THINKING, "what it would say while an action runs is not heard")
	s.send({"type": "transcript", "role": "agent", "data": "There is nothing."})
	await pause(0.3)
	reply(s, 2, "", 0.5)
	await until(func(): return a.state == LokutorAgent.State.READY)
	check(absf(played.n / float(a.player.rate) - 0.5) < 0.1, "the reply is, and only it: %.2f s" % (played.n / float(a.player.rate)))
	check(not states.slice(states.find("THINKING"), states.find("SPEAKING")).has("READY"), "it is thinking meanwhile: %s" % [states])
	# What the model wrote before the action, its plan for instance, is as long as it likes.
	played.n = 0
	filler.call(6, 4.5)
	s.send({"type": "transcript", "role": "agent", "data": "Nothing."})
	await pause(3.5)
	eq(played.n, 0, "nor is a long one")
	reply(s, 7, "", 0.5)
	await until(func(): return a.state == LokutorAgent.State.SPEAKING)
	await until(func(): return a.state == LokutorAgent.State.READY)
	check(absf(played.n / float(a.player.rate) - 0.5) < 0.1, "only the reply after it: %.2f s" % (played.n / float(a.player.rate)))
	# A server that says nothing meanwhile: the text comes before the voice.
	played.n = 0
	s.send({"type": "status", "data": "listening"})
	s.send({"type": "tool_call", "name": "look", "arguments": "{}", "call_id": "d"})
	s.send({"type": "transcript", "role": "agent", "data": "Nothing."})
	reply(s, 8, "", 0.5)
	await until(func(): return a.state == LokutorAgent.State.SPEAKING)
	await until(func(): return a.state == LokutorAgent.State.READY)
	check(played.n / float(a.player.rate) > 0.4, "a reply with no filler before it is heard")
	# The reply itself, taken for a filler: late, not lost.
	played.n = 0
	filler.call(9, 0.6)
	s.send({"type": "transcript", "role": "agent", "data": "Nothing."})
	await until(func(): return played.n > 0, 5.0)
	check(played.n > 0, "and one that nothing follows is played after all")
	await until(func(): return a.state == LokutorAgent.State.READY)
	# The player had only paused, and goes on: what was held is never heard.
	played.n = 0
	filler.call(10, 0.6)
	await pause(0.1)
	s.send({"type": "status", "data": "listening"})
	s.send({"type": "transcript", "role": "agent", "data": "Nothing."})
	await pause(3.6)
	eq(played.n, 0, "nor when the player goes on talking")
	a.action_filler = true
	played.n = 0
	filler.call(11, 0.4)
	await until(func(): return played.n > 0, 2.0)
	check(played.n > 0, "action_filler lets it be heard")
	a.action_filler = false
	# Words before the action: heard, unless the action has to come before any.
	for heard in [true, false]:
		a.speak_before_actions = heard
		var gen: int = 20 if heard else 30
		s.send({"type": "status", "data": "interrupted"})
		await pause(0.3)
		played.n = 0
		reply(s, gen, "", 0.6)
		await until(func(): return a.state == LokutorAgent.State.SPEAKING)
		s.send({"type": "tool_call", "name": "look", "arguments": "{}", "call_id": "w%d" % gen})
		await pause(0.2)
		s.send_audio(tone(0.6), gen)
		await pause(1.6)
		var sec: float = played.n / float(a.player.rate)
		check(sec > 1.0 if heard else sec < 0.5, "words before an action, heard %s: %.2f s" % [heard, sec])
		s.send({"type": "transcript", "role": "agent", "data": "Nothing."})
		played.n = 0
		reply(s, gen + 1, "", 0.5)
		await until(func(): return played.n / float(a.player.rate) > 0.4, 4.0)
		check(absf(played.n / float(a.player.rate) - 0.5) < 0.1, "and the reply after it: %.2f s" % (played.n / float(a.player.rate)))
		await until(func(): return a.state == LokutorAgent.State.READY)
	# The next line of a player who has not been heard to stop: no "listening", only what they said.
	played.n = 0
	reply(s, 40, "", 0.6)
	await until(func(): return a.state == LokutorAgent.State.SPEAKING)
	s.send({"type": "transcript", "role": "user", "data": "Look."})
	s.send({"type": "tool_call", "name": "look", "arguments": "{}", "call_id": "w40"})
	await pause(0.2)
	s.send_audio(tone(0.6), 40)
	await pause(1.6)
	check(played.n / float(a.player.rate) < 0.5, "nor before the action of the player's next line: %.2f s" % (played.n / float(a.player.rate)))
	# A server that numbers the turn before the action, and answers with that number.
	s.send({"type": "status", "data": "listening"})
	await pause(0.1)
	played.n = 0
	s.send({"type": "status", "data": "thinking", "generation": 50})
	s.send({"type": "tool_call", "name": "look", "arguments": "{}", "call_id": "w50"})
	await pause(0.2)
	reply(s, 50, "Nothing.", 0.5)
	await until(func(): return played.n / float(a.player.rate) > 0.4, 4.0)
	check(absf(played.n / float(a.player.rate) - 0.5) < 0.1, "a reply of which nothing had been said is heard: %.2f s" % (played.n / float(a.player.rate)))
	a.queue_free()


func _test_agent_others() -> void:
	var a := new_agent()
	var b := new_agent()
	var s := await talk(a)
	var t := await talk(b)
	var loud := PackedFloat32Array()
	loud.resize(1600)
	loud.fill(0.5)
	reply(t, 1, "Hello", 1.0)
	await until(func(): return b.state == LokutorAgent.State.SPEAKING and b.heard_seconds() > 0.1)
	a.feed_microphone(loud, 16000)
	await until(func(): return s.audio.size() == 3200)
	eq(s.audio.decode_s16(100), 0, "it does not listen while another character speaks")
	b.queue_free()
	var v := LokutorVoice.new()
	v.simulate_audio_device = true
	root.add_child(v)
	v.player.begin(1)
	v.player.push(tone(1.0), 1)
	await pause(0.1)
	s.audio.clear()
	a.feed_microphone(loud, 16000)
	await until(func(): return s.audio.size() == 3200)
	eq(s.audio.decode_s16(100), 0, "nor while a written line is heard")
	v.mutes_microphones = false
	s.audio.clear()
	a.feed_microphone(loud, 16000)
	await until(func(): return s.audio.size() == 3200)
	check(s.audio.decode_s16(100) > 10000, "unless that voice is far away")
	v.queue_free()
	a.queue_free()


func _test_agent_mute() -> void:
	var a := new_agent()
	var s := await talk(a)
	var loud := PackedFloat32Array()
	loud.resize(1600)
	loud.fill(0.5)
	a.feed_microphone(loud, 16000)
	await until(func(): return s.audio.size() == 3200)
	check(s.audio.decode_s16(100) > 10000, "the microphone is heard")
	reply(s, 1, "Hello", 0.6)
	await until(func(): return a.state == LokutorAgent.State.SPEAKING and a.heard_seconds() > 0.1)
	s.audio.clear()
	a.feed_microphone(loud, 16000)
	await until(func(): return s.audio.size() == 3200)
	eq(s.audio.decode_s16(100), 0, "but not while the character speaks")
	a.mute_mic_while_speaking = false
	s.audio.clear()
	a.feed_microphone(loud, 16000)
	await until(func(): return s.audio.size() == 3200)
	check(s.audio.decode_s16(100) > 10000, "unless players wear headphones")
	a.queue_free()



func _test_agent_echo() -> void:
	var a := new_agent()
	a.mute_mic_while_speaking = false          # headphones, it is thought
	var heard: Array = []
	var echoes: Array = []
	a.player_said.connect(func(t): heard.append(t))
	a.echo_heard.connect(func(t): echoes.append(t))
	var s := await talk(a)
	reply(s, 1, "[happy] I was in the kitchen, making cocoa for everybody.", 0.4)
	await until(func(): return a.state == LokutorAgent.State.READY)
	s.send({"type": "transcript", "role": "user", "data": "in the kitchen making cocoa"})
	await until(func(): return echoes.size() == 1)
	eq(echoes.size(), 1, "its own words coming back are its echo")
	eq(heard.size(), 0, "and not the player's")
	check(a.mute_mic_while_speaking, "so the microphone closes while it talks from then on")
	s.send({"type": "transcript", "role": "user", "data": "Where were you during the blackout?"})
	await until(func(): return heard.size() == 1)
	eq(heard, ["Where were you during the blackout?"], "the player's own question is heard")
	eq(LokutorVoicePlayer.echo_tail_ms(), LokutorVoicePlayer.ECHO_TAIL_MS, "a computer's echo tail")
	a.queue_free()


func _test_agent_background_sound() -> void:
	# An agent with a background sound never stops sending audio. Its replies must still end.
	var a := new_agent()
	var s := await talk(a)
	reply(s, 1, "Hello", 0.4)
	await until(func(): return a.state == LokutorAgent.State.SPEAKING)
	var end := Time.get_ticks_msec() + 4000
	while a.state == LokutorAgent.State.SPEAKING and Time.get_ticks_msec() < end:
		s.send_audio(tone(0.1, 0.01), 1)
		await pause(0.1)
	eq(a.state, LokutorAgent.State.READY, "the reply ends although the room goes on")
	for i in 3:
		s.send_audio(tone(0.1, 0.01), 1)
		await pause(0.1)
	eq(a.state, LokutorAgent.State.READY, "and the room is not a reply")
	var loud := PackedFloat32Array()
	loud.resize(1600)
	loud.fill(0.5)
	await pause(0.3)
	s.audio.clear()
	s.send_audio(tone(0.3, 0.01), 1)
	await pause(0.05)
	a.feed_microphone(loud, 16000)
	await until(func(): return s.audio.size() == 3200)
	check(s.audio.decode_s16(100) > 10000, "nor does it mute the microphone")
	a.queue_free()


func _test_agent_context() -> void:
	var a := new_agent({"persona": "You are a king.", "context_heading": "Ahora:"})
	a.set_context("Mood", "bored")
	var s := await talk(a)
	check(s.last("prompt").data.begins_with("You are a king.") and s.last("prompt").data.contains("- Mood: bored"), "context goes with the persona")
	check(s.last("prompt").data.contains("\n\nAhora:\n- Mood"), "under a heading of the game's")
	var before := s.types().count("prompt")
	a.set_context("Mood", "amused")
	a.set_context("Time left", "20 s")
	a.set_context("Mood", "amused")
	await pause(0.5)
	eq(s.types().count("prompt"), before + 1, "changes in one moment are one message")
	check(s.last("prompt").data.contains("- Mood: amused") and s.last("prompt").data.contains("- Time left: 20 s"), "with both")
	a.set_context("Time left", null)
	await pause(0.4)
	check(not s.last("prompt").data.contains("Time left"), "null takes one out")
	a.set_context("Time left", null)
	check(a._context_due_ms == 0, "forgetting what it did not know sends nothing")
	a.queue_free()


func _test_agent_demo() -> void:
	eq(LokutorError.demo_limit("demo_daily_limit: Demo time limit reached for today."), "daily", "the visitor's day")
	eq(LokutorError.demo_limit("demo_busy: The demo is busy."), "busy", "a full demo")
	eq(LokutorError.demo_limit("demo_call_limit: This demo call reached its time limit."), "call", "the call's length")
	eq(LokutorError.demo_limit("account balance exhausted"), "", "anything else is not the demo's")
	var e := LokutorError.from_demo_close("demo_busy: The demo is busy right now.")
	eq([e.code, e.message, e.detail, e.retryable], ["auth.time_limited", "The demo is busy right now.", "demo_busy", true], "as an error")
	var a := new_agent()
	var limits: Array = []
	var ended: Array = []
	a.demo_limit.connect(func(l, m): limits.append([l, m]))
	a.conversation_ended.connect(func(r): ended.append(r))
	var s := await talk(a)
	s.ws.close(1008, "demo_call_limit: This demo call reached its time limit.")
	await until(func(): return ended.size() == 1)
	eq(limits, [["call", "This demo call reached its time limit."]], "the demo's limit, once")
	eq(a.state, LokutorAgent.State.DISCONNECTED, "and no reconnection")
	a.queue_free()


func _test_agent_end() -> void:
	var a := new_agent()
	var ended: Array = []
	a.conversation_ended.connect(func(r): ended.append(r))
	var s := await talk(a)
	s.ws.close(1008, "account balance exhausted")
	await until(func(): return ended.size() == 1)
	eq(ended, ["account balance exhausted"], "the server's reason")
	eq(a.state, LokutorAgent.State.DISCONNECTED, "disconnected")
	await process_frame
	s = await talk(a)
	check(s != null and a.state == LokutorAgent.State.READY, "can start again")
	a.player.recorded = PackedFloat32Array([0.5, 0.25])
	a.stop_conversation()
	eq(ended.back(), "stopped", "stop")
	a.stop_conversation()
	eq(ended.size(), 2, "once")
	await process_frame
	await process_frame
	eq(a.get_children().filter(func(n): return n is LokutorVoiceAgentClient).size(), 0, "leaves nothing behind")
	eq(a.recorded_output, PackedFloat32Array([0.5, 0.25]), "but what it recorded")
	server.refuse = true
	a.start_conversation()
	await until(func(): return ended.size() == 3)
	check(ended.back().contains("Could not connect"), "a refused connection ends with a reason")
	a.queue_free()


# ---- Text to speech and speech to text -----------------------------------------------------------

func _serve_tts(s: LokutorMockServer.Session, msg) -> void:
	if not (msg is Dictionary) or not msg.has("text"):
		return
	if msg.text == "bad":
		s.send({"type": "error", "data": {"code": "validation.invalid_voice", "message": "No such voice"}})
		return
	if msg.text == "broken":
		s.send({"type": "error", "data": {"code": "tts.synthesis_failed", "message": "Failed", "retryable": true}})
		s.send("EOS")
		return
	if msg.get("visemes", false):
		s.send({"type": "visemes", "data": [{"v": 10, "c": "aa", "t": 0.0, "d": 0.2}]})
	s.ws.send(tone(0.2))
	s.ws.send(tone(0.2))
	s.send("EOS")


func _test_tts() -> void:
	server.message.connect(_serve_tts)
	var tts := LokutorTTSClient.new()
	tts.api_key = "sk_test"
	tts.server_url = server.url("/ws/tts")
	root.add_child(tts)
	var got := {"chunks": 0, "ttfb": -1}
	var r := await tts.synthesize("Hello", {"voice": "M2", "visemes": true, "on_audio": func(_p): got.chunks += 1, "on_ttfb": func(ms): got.ttfb = ms})
	check(r.ok, "ok")
	near(r.audio.size() / 2.0 / 44100.0, 0.4, 0.001, "all the audio")
	eq(r.visemes.size(), 1, "visemes")
	eq(got.chunks, 2, "streams")
	check(got.ttfb >= 0, "time to first byte")
	var first := server.current()
	var keys: Array = first.text[0].keys()
	keys.sort()
	eq(keys, ["language", "text", "visemes", "voice"], "sends only what is set")
	# Two requests at once run one after the other, on the same connection.
	var results: Array = []
	(func(): results.append((await tts.synthesize("One")).ok)).call()
	(func(): results.append((await tts.synthesize("Two")).ok)).call()
	await until(func(): return results.size() == 2)
	eq(results, [true, true], "requests queue")
	eq(server.sessions.size(), 1, "one connection")
	eq(server.current().text.map(func(m): return m.text), ["Hello", "One", "Two"], "in order")
	tts.queue_free()
	server.message.disconnect(_serve_tts)


func _test_tts_errors() -> void:
	server.message.connect(_serve_tts)
	var tts := LokutorTTSClient.new()
	tts.api_key = "sk_test"
	tts.server_url = server.url("/ws/tts")
	root.add_child(tts)
	var r := await tts.synthesize("bad")
	check(not r.ok and r.error.code == "validation.invalid_voice", "a refused request")
	r = await tts.synthesize("broken")
	check(not r.ok and r.error.code == "tts.synthesis_failed" and r.error.retryable, "a failed synthesis, followed by EOS")
	r = await tts.synthesize("fine")
	check(r.ok, "the connection still works")
	tts.close()
	server.refuse = true
	r = await tts.synthesize("nobody")
	check(not r.ok and r.error.code == "ws.close", "a server that is down")
	tts.queue_free()
	server.message.disconnect(_serve_tts)


func _test_voice_cache() -> void:
	server.message.connect(_serve_tts)
	var dir := "user://test_voices"
	for f in DirAccess.get_files_at(dir) if DirAccess.dir_exists_absolute(dir) else []:
		DirAccess.remove_absolute(dir.path_join(f))
	var v := LokutorVoice.new()
	v.api_key = "sk_test"
	v.url = server.url("/ws/tts")
	v.cache_dir = dir
	v.simulate_audio_device = true
	root.add_child(v)
	var log: Array = []
	v.line_started.connect(func(t): log.append("start " + t))
	v.line_finished.connect(func(t): log.append("end " + t))
	var t0 := Time.get_ticks_msec()
	check(await v.say("Halt!"), "said")
	check(Time.get_ticks_msec() - t0 > 380, "returns once it has been heard")
	eq(log, ["start Halt!", "end Halt!"], "signals")
	check(v.has_line("Halt!"), "kept")
	check(await v.say("Halt!"), "said again")
	eq(server.sessions.size(), 1, "without asking again")
	eq(server.current().text.size(), 1, "one request in all")
	check(not v.has_line("Halt!", {"voice": "M1"}), "another voice is another line")
	await v.preload_lines(["Who goes there?"])
	check(v.has_line("Who goes there?"), "preloaded")
	v.queue_free()
	server.message.disconnect(_serve_tts)


func _test_voice_queue() -> void:
	server.message.connect(_serve_tts)
	var v := LokutorVoice.new()
	v.api_key = "sk_test"
	v.url = server.url("/ws/tts")
	v.cache_dir = ""
	v.simulate_audio_device = true
	root.add_child(v)
	var log: Array = []
	v.line_started.connect(func(t): log.append(t))
	var done := {}
	(func(): done["a"] = await v.say("A")).call()
	(func(): done["b"] = await v.say("B")).call()
	await until(func(): return done.size() == 2, 4.0)
	eq(log, ["A", "B"], "lines wait for each other")
	eq(done, {"a": true, "b": true}, "both heard")
	done.clear()
	(func(): done["c"] = await v.say("C")).call()
	await until(func(): return v.player.heard_seconds() > 0.05)
	check(v.mouth_open > 0.0, "the mouth moves")
	(func(): done["d"] = await v.say("D", {"interrupt": true})).call()
	await until(func(): return done.size() == 2, 4.0)
	eq(done, {"c": false, "d": true}, "an urgent line cuts the one being said")
	v.queue_free()
	server.message.disconnect(_serve_tts)


func _test_stt() -> void:
	server.http_status = 200
	server.http_body = {"text": "hello there", "latency_ms": 41.5, "engine": "conv-2.0", "sample_rate": 16000, "language": "en", "duration_seconds": 1.2}
	var stt := LokutorSTTClient.new("sk_test")
	stt.server_url = server.http_url("/ws/stt")
	var r := await stt.transcribe(PackedByteArray([0, 0, 1, 0]), {"sample_rate": 16000, "language": "en", "keyterms": ["Brom"]})
	check(r.ok and r.text == "hello there", "transcribes")
	var req: Dictionary = server.http_requests.back()
	check(req.line.begins_with("POST /stt/transcribe"), "route")
	check(req.head.contains("X-API-Key: sk_test"), "key")
	eq(req.body.format, "pcm16", "format")
	eq(req.body.keyterms, ["Brom"], "keyterms")
	server.http_status = 402
	server.http_body = {"code": "billing.insufficient_balance", "message": "Top up"}
	r = await stt.transcribe(PackedByteArray([0, 0]))
	check(not r.ok and r.error.code == "billing.insufficient_balance", "a refused request says why")
	var asked := server.http_requests.size()
	stt.auth = func(): return null
	r = await stt.transcribe(PackedByteArray([0, 0]))
	check(not r.ok and r.error.code == "auth.missing_key", "an auth callback with nothing to give")
	eq(server.http_requests.size(), asked, "is not sent as a key")


func _test_stt_stream() -> void:
	var stt := LokutorSpeechToTextClient.new()
	stt.api_key = "sk_test"
	stt.server_url = server.url("/ws/stt")
	stt.use_microphone = false
	stt.keyterms = ["Brom"]
	root.add_child(stt)
	var log: Array = []
	stt.partial_transcript.connect(func(t): log.append("p " + t))
	stt.final_transcript.connect(func(t): log.append("f " + t))
	stt.status_changed.connect(func(s): log.append(s))
	check(await stt.open(), "opens")
	await until(func(): return server.current() != null and server.current().text.size() == 1)
	var s := server.current()
	eq(s.text[0], {"language": "en", "vad": "silero", "keyterms": ["Brom"]}, "first message")
	stt.send_audio(PackedByteArray([1, 0]))
	stt.end_utterance()
	await until(func(): return s.last("end") != null and s.audio.size() == 2)
	check(s.audio.size() == 2, "audio")
	s.send({"type": "transcript", "data": "open the", "isFinal": false})
	s.send({"type": "transcript", "data": "open the gate", "isFinal": true})
	await until(func(): return log.has("f open the gate"))
	eq(log, ["connecting", "connected", "p open the", "f open the gate"], "transcripts")
	stt.close()
	eq(log.back(), "disconnected", "close")
	var errors: Array = []
	stt.error.connect(func(e): errors.append(e.code))
	stt.auth = func(): return null
	check(not await stt.open(), "no credential, no connection")
	eq(errors, ["auth.missing_key"], "and it says why")
	stt.queue_free()


# ---- Corners -------------------------------------------------------------------------------------

func _test_client_lifecycle() -> void:
	# close() while reconnecting, then a fresh start that fails: one error, no retries.
	var c := new_client()
	var log: Array = []
	c.status.connect(func(x): log.append(x))
	c.reconnected.connect(func(): log.append("reconnected"))
	c.error.connect(func(e): log.append("error " + e.code))
	await c.open()
	await until(func(): return server.current() != null and server.current().last("rates") != null)
	server.current().drop()
	await until(func(): return log.has("reconnecting"))
	c.close()
	eq(log.back(), "disconnected", "closing while reconnecting says so")
	var n := server.sessions.size()
	await pause(2.3)
	eq(server.sessions.size(), n, "and does not reconnect")
	log.clear()
	server.refuse = true
	check(not await c.open(), "open() against a server that is down")
	await pause(0.2)
	eq(log, ["error ws.close", "disconnected"], "fails once, without retrying")
	server.refuse = false
	log.clear()
	check(await c.open(), "opens again")
	check(not log.has("reconnected"), "which is not a reconnection")
	# A reconnection that no attempt can fix.
	await until(func(): return server.current().last("rates") != null)
	server.current().drop()
	await until(func(): return log.has("reconnecting"))
	c.api_key = ""
	await until(func(): return log.has("error auth.missing_key"), 4.0)
	eq(log.slice(-2), ["error auth.missing_key", "disconnected"], "ends")
	c.api_key = "sk_test"
	log.clear()
	check(await c.open(), "and can start again")
	check(not log.has("reconnected"), "as a new one")
	c.close()
	# Closed while the credential is on its way: that connection never happens.
	n = server.sessions.size()
	c.auth = func():
		await pause(0.2)
		return "wst_slow"
	var results: Array = []
	(func(): results.append(await c.open())).call()
	await process_frame
	c.close()
	c.auth = func(): return "wst_fast"
	(func(): results.append(await c.open())).call()
	await until(func(): return results.size() == 2)
	await pause(0.3)
	eq(results, [true, false], "the second open() connects, the first one, given up, is false")
	eq(server.sessions.size(), n + 1, "one connection")
	check(server.current().url.contains("wst_fast") and c.connected, "the one asked for last")
	c.queue_free()


func _test_agent_no_credential() -> void:
	var a := new_agent({"api_key": ""})
	a.ticket_provider = func(): return ""
	var ended: Array = []
	var errors: Array = []
	a.conversation_ended.connect(func(r): ended.append(r))
	a.error_occurred.connect(func(code, _m): errors.append(code))
	a.start_conversation()
	await until(func(): return ended.size() == 1)
	eq(errors, ["auth.missing_key"], "a ticket provider with no ticket")
	eq(a.state, LokutorAgent.State.DISCONNECTED, "does not leave the character connecting for ever")
	eq(ended.size(), 1, "and the conversation ends")
	a.ticket_provider = func(): return "wst_x"
	await process_frame
	await talk(a)
	eq(a.state, LokutorAgent.State.READY, "it can be started again")
	a.queue_free()


func _test_agent_late_thinking() -> void:
	var a := new_agent()
	var s := await talk(a)
	s.send_audio(tone(0.6), 1)
	await until(func(): return a.state == LokutorAgent.State.SPEAKING)
	check(a.is_speaking, "is_speaking")
	s.send({"type": "status", "data": "thinking", "generation": 1})
	await pause(0.15)
	eq(a.state, LokutorAgent.State.SPEAKING, "a reply already heard is not being thought")
	a.queue_free()


func _test_agent_dashboard() -> void:
	var a := new_agent({"agent_id": "ag_1", "persona": "", "voice": "", "language": ""})
	a.set_context("Mood", "bored")
	var s := await talk(a)
	eq(s.types(), ["visemes", "rates"], "a dashboard agent keeps its prompt, context or not")
	a.set_context("Mood", "amused")
	await pause(0.4)
	eq(s.last("prompt"), null, "context does not replace it later either")
	a.set_persona("You are Brom.")
	await until(func(): return s.last("prompt") != null)
	check(s.last("prompt").data.begins_with("You are Brom.") and s.last("prompt").data.contains("- Mood: amused"), "a persona of the game's does, with the context")
	a.queue_free()


func _test_agent_mute_button() -> void:
	var a := new_agent({"muted": true, "auto_reconnect": false})
	var ended: Array = []
	a.conversation_ended.connect(func(r): ended.append(r))
	var s := await talk(a)
	var loud := PackedFloat32Array()
	loud.resize(1600)
	loud.fill(0.5)
	a.feed_microphone(loud, 16000)
	await until(func(): return s.audio.size() == 3200)
	eq(s.audio.decode_s16(100), 0, "muted before it starts: the character hears silence")
	a.muted = false
	s.audio.clear()
	a.feed_microphone(loud, 16000)
	await until(func(): return s.audio.size() == 3200)
	check(s.audio.decode_s16(100) > 10000, "and then the player")
	s.drop()
	await until(func(): return ended.size() == 1)
	eq(a.state, LokutorAgent.State.DISCONNECTED, "auto_reconnect off: a dropped connection ends it")
	a.queue_free()


func _test_lip_shared() -> void:
	var lips := LokutorLipSync.new()
	lips.lead = 0.0
	lips.loudness = 0.0
	root.add_child(lips)
	var a := RefCounted.new()
	var b := RefCounted.new()
	lips.push_visemes([{"v": 10, "c": "aa", "t": 0.0, "d": 2.0}], a)
	for i in 30:
		lips.advance(0.5, 0.3, true, 1.0 / 60.0, a)
		lips.advance(0.0, 0.0, false, 1.0 / 60.0, b)
	check(lips.mouth_open > 0.9, "a silent voice does not close the mouth of the one speaking (%.2f)" % lips.mouth_open)
	lips.clear(b)
	lips.push_visemes([{"v": 1, "c": "PP", "t": 0.0, "d": 2.0}], b)
	for i in 10:
		lips.advance(0.6, 0.3, true, 1.0 / 60.0, a)
		lips.advance(0.1, 0.3, true, 1.0 / 60.0, b)
	check(lips.weights[10] > 0.9, "nor does one that starts in the middle")
	for i in 30:
		lips.advance(0.6, 0.0, false, 1.0 / 60.0, a)
		lips.advance(0.2, 0.3, true, 1.0 / 60.0, b)
	check(lips.weights[1] > 0.9 and lips.weights[10] < 0.05, "the next has its own timeline")
	lips.release(b)
	check(lips.mouth_open < 0.05 and lips.weights[1] < 0.05, "a voice that goes closes it")
	lips.advance(0.5, 0.3, true, 0.2, a)
	check(lips.weights[10] > 0.5, "and leaves it to the other")
	lips.free()
	# The nodes: an agent's reply, on a face that is also a LokutorVoice's.
	server.message.connect(_serve_tts)
	var agent := new_agent()
	var mouth := LokutorLipSync.new()
	agent.lip_sync = mouth
	root.add_child(mouth)
	var v := LokutorVoice.new()
	v.api_key = "sk_test"
	v.url = server.url("/ws/tts")
	v.cache_dir = ""
	v.simulate_audio_device = true
	v.lip_sync = mouth
	root.add_child(v)
	var s := await talk(agent)
	reply(s, 1, "Hello", 0.8)
	await until(func(): return agent.heard_seconds() > 0.3)
	check(mouth.mouth_open > 0.5, "the agent moves it (%.2f)" % mouth.mouth_open)
	v.stop()
	await pause(0.1)
	check(mouth.mouth_open > 0.5, "stopping the idle voice does not cut the agent's visemes (%.2f)" % mouth.mouth_open)
	await until(func(): return agent.state == LokutorAgent.State.READY)
	var ok := {"said": false}
	(func(): ok.said = await v.say("Hi")).call()
	await until(func(): return v.player.heard_seconds() > 0.1)
	check(mouth.weights[10] > 0.3, "then the voice does (%.2f)" % mouth.weights[10])
	await until(func(): return ok.said)
	check(ok.said, "said")
	agent.queue_free()
	v.queue_free()
	mouth.queue_free()
	server.message.disconnect(_serve_tts)


func _test_lip_names() -> void:
	var rigs := {
		"VRoid": [["Fcl_MTH_A", "Fcl_MTH_I", "Fcl_MTH_U", "Fcl_MTH_E", "Fcl_MTH_O", "Fcl_ALL_Joy"], LokutorLipSync.Rig.VRM, "Fcl_MTH_A", "Fcl_MTH_U"],
		"MTH": [["blendShape1.MTH_A", "blendShape1.MTH_I", "blendShape1.MTH_U", "blendShape1.MTH_E", "blendShape1.MTH_O"], LokutorLipSync.Rig.VRM, "blendShape1.MTH_A", "blendShape1.MTH_U"],
		"FBX": [Array(Lokutor.VISEMES).map(func(n): return "Face.viseme_" + n), LokutorLipSync.Rig.VISEMES, "Face.viseme_aa", "Face.viseme_ou"],
	}
	for r in rigs:
		var face := face_with(rigs[r][0])
		var lips := LokutorLipSync.new()
		lips.faces = [face]
		root.add_child(lips)
		eq(lips.rig(), rigs[r][1], r + " is recognised")
		for pair in [[10, "aa", 2], [14, "ou", 3]]:
			lips.clear()
			lips.push_visemes([{"v": pair[0], "c": pair[1], "t": 0.0, "d": 1.0}])
			for i in 30:
				lips.advance(0.4, 0.3, true, 1.0 / 60.0)
			var index := face.find_blend_shape_by_name(str(rigs[r][pair[2]]))
			check(index >= 0 and face.get_blend_shape_value(index) > 0.5, "%s: %s" % [r, pair[1]])
		lips.free()
		face.free()


func _test_tts_late_eos() -> void:
	# The EOS of a failed request, arriving when the next one is already waiting for its audio.
	var late := func(s: LokutorMockServer.Session, msg):
		if msg.text == "broken":
			s.send({"type": "error", "data": {"code": "tts.synthesis_failed", "message": "Failed", "retryable": true}})
			await pause(0.15)
			s.send("EOS")
		else:
			await pause(0.3)
			s.ws.send(tone(0.2))
			s.send("EOS")
	server.message.connect(late)
	var tts := LokutorTTSClient.new()
	tts.api_key = "sk_test"
	tts.server_url = server.url("/ws/tts")
	root.add_child(tts)
	var r := await tts.synthesize("broken")
	check(not r.ok and r.error.code == "tts.synthesis_failed", "fails")
	r = await tts.synthesize("fine")
	check(r.ok and r.audio.size() > 0, "and the next one is not given its ending (%d bytes)" % r.audio.size())
	tts.queue_free()
	server.message.disconnect(late)


func _test_tts_cancel() -> void:
	var slow := func(s: LokutorMockServer.Session, _msg):
		await pause(0.25)
		if s.ws.get_ready_state() == WebSocketPeer.STATE_OPEN:
			s.ws.send(tone(0.1))
			s.send("EOS")
	server.message.connect(slow)
	var tts := LokutorTTSClient.new()
	tts.api_key = "sk_test"
	tts.server_url = server.url("/ws/tts")
	root.add_child(tts)
	var out := {}
	var say := func(text: String, options := {}):
		var r: Dictionary = await tts.synthesize(text, options)
		out[text] = "ok" if r.ok else r.error.code
	say.call("one")
	say.call("two")
	say.call("three")
	await until(func(): return server.current() != null and server.current().text.size() == 1)
	tts.cancel()
	await until(func(): return out.size() == 3)
	eq(out, {"one": "internal.cancelled", "two": "internal.cancelled", "three": "internal.cancelled"}, "cancel() forgets the ones waiting too")
	out.clear()
	var ticket := RefCounted.new()
	say.call("four")
	say.call("five", {"ticket": ticket})
	say.call("six")
	tts.cancel(ticket)
	await until(func(): return out.size() == 3)
	eq(out, {"four": "ok", "five": "internal.cancelled", "six": "ok"}, "with a ticket, only that one")
	tts.close()
	tts.auth = func():
		await pause(0.2)
		return "wst_late"
	out.clear()
	var sessions := server.sessions.size()
	say.call("seven")
	await pause(0.05)
	tts.cancel()
	await until(func(): return out.size() == 1)
	await pause(0.3)
	eq(out, {"seven": "internal.cancelled"}, "cancelled while the credential came")
	eq(server.sessions.size(), sessions, "no connection")
	tts.queue_free()
	server.message.disconnect(slow)


func _test_voice_stop_keeps_preload() -> void:
	server.message.connect(_serve_tts)
	var dir := "user://test_voices"
	for f in DirAccess.get_files_at(dir) if DirAccess.dir_exists_absolute(dir) else PackedStringArray():
		DirAccess.remove_absolute(dir.path_join(f))
	var v := LokutorVoice.new()
	v.api_key = "sk_test"
	v.url = server.url("/ws/tts")
	v.cache_dir = dir
	v.simulate_audio_device = true
	root.add_child(v)
	var done := {"preload": false}
	(func():
		await v.preload_lines(["P1", "P2", "P3"])
		done.preload = true).call()
	(func(): done["said"] = await v.say("Cut")).call()
	await process_frame
	v.stop()
	await until(func(): return done.preload and done.has("said"), 4.0)
	eq([v.has_line("P1"), v.has_line("P2"), v.has_line("P3")], [true, true, true], "stop() does not stop the lines being fetched for later")
	eq(done.get("said"), false, "only the one being said")
	v.queue_free()
	server.message.disconnect(_serve_tts)


func _test_voice_removed() -> void:
	server.message.connect(_serve_tts)
	var v := LokutorVoice.new()
	v.api_key = "sk_test"
	v.url = server.url("/ws/tts")
	v.cache_dir = ""
	v.simulate_audio_device = true
	root.add_child(v)
	var done := {}
	(func(): done["a"] = await v.say("A")).call()
	(func(): done["b"] = await v.say("B")).call()
	await until(func(): return v.player.heard_seconds() > 0.05)
	root.remove_child(v)
	await until(func(): return done.size() == 2, 2.0)
	eq(done, {"a": false, "b": false}, "a character taken out of the scene stops, and nothing waits for ever")
	v.free()
	server.message.disconnect(_serve_tts)
