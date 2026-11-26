# Changelog

## 0.2.1

- `LokutorAgent.hears_microphone`: whether what the microphone hears reaches the character right now. It is false
  while the microphone is muted, and with `mute_mic_while_speaking` while a voice can still be heard (the echo tail
  after it included: 700 ms on phones and in browsers). Show it, not `is_speaking`, as the sign that the player may
  talk, or the start of what they say is lost.
- The microphone keeps a second of audio instead of a tenth: a frame that takes long (a web browser compiling a shader)
  no longer loses what the player said meanwhile. It adds no delay.
- `LokutorLipSync` sets a mouth shape that is barely open (under 0.004) to 0. In a web browser every face shape that is
  not at rest costs a pass over the mesh on the GPU every frame, and the weights fade towards 0 without reaching it.

## 0.2.0

- **Visemes.** The server's 15 mouth shapes drive `LokutorLipSync`: Oculus, VRM, ARKit, single-shape, custom, jaw and
  sprite mouths. On by default in `LokutorAgent`.
- **The clients of the other SDKs**: `LokutorVoiceAgentClient`, `LokutorTTSClient`, `LokutorSTTClient` and
  `LokutorSpeechToTextClient`, with the same names, defaults, errors and close codes.
- **Reconnection.** A dropped connection is opened again (1001, 1006, 1011-1014; 2, 4, 8, 10, 10 s).
- **LokutorVoice**: written lines with lip sync, a queue and a disk cache. It can share a LokutorLipSync with an agent:
  the mouth follows whoever is speaking.
- **LokutorAgent**: `set_context`, `set_voice`, `set_language`, `set_pitch`, `remove_action`, `reset_conversation`,
  `get_transcript`, `heard_seconds`, `voice_level`, `muted`, `is_speaking`, `auto_reconnect`, `pitch`, `voice_bus`,
  `voice_output`, `context_heading`, `action_filler`, own model (`llm`, `on_llm_request`), and the signals `reconnecting`, `reconnected`, `visemes_received`, `voice_played`, `session_limit`.
- Agents with a background sound no longer keep the character speaking nor the microphone muted.
- A credential that never comes ends the conversation (`conversation_ended`) instead of leaving it connecting.
- `set_context` on a dashboard agent without a persona no longer replaces the dashboard's prompt.
- A character no longer says "One moment" or the like while an action of the game runs, nor what the model wrote before the action, which can be its plan (`action_filler` brings both back). `speak_before_actions = false` also drops what it begins to say just before it.
- `set_context(key, null)` sends nothing when there was no such fact.
- Android: the microphone waits for the player to allow it, and the game no longer crashes when it quits while listening.
- The microphone is also muted while another character or a `LokutorVoice` is heard (`mutes_microphones`).
- `record_output` keeps the recording after the conversation ends, and a reply that takes two seconds to come is no longer
  taken for the player's turn.
- A refused connection says the server's reason (`auth.invalid_key`, `billing.insufficient_balance`...) instead of
  `ws.close`, and a reconnection that no attempt can fix ends at once.
- `Lokutor.tls_options`, for a server with its own certificate authority. An antivirus that inspects HTTPS (Avast, AVG) is
  named in the error.
- **Web browsers.** A web export works: no WebSocket headers in a browser (it cannot send them), `Lokutor.page_args()` for
  the page's address parameters, and `Lokutor.tell_page(type, data)`, with which the SDK tells the page around the game
  `session_limit` and `demo_limit`. On lokutor.com a game needs no key (`Lokutor.keyless_allowed()`): the demo's limits
  end a conversation with `auth.time_limited` and the signal `demo_limit(limit, message)` on `LokutorAgent` and
  `LokutorVoice` (`LokutorError.demo_limit`, `LokutorError.from_demo_close`), and all `LokutorVoice`s share one
  connection. See the guide *Games in a web browser*.
- **Echo.** With speakers, the character's own words coming back through the microphone are not taken for the player's
  (`guard_against_echo`, `is_echo`, signal `echo_heard`), and on phones and in browsers the room's echo is waited for
  longer (`LokutorVoicePlayer.echo_tail_ms()`, 700 ms).
- Unit tests with a mock server (`tests/unit_tests.gd`), and live checks that need no key (`tests/public_tests.gd`).

Nothing was removed: projects made with 0.1 work as they are.

## 0.1.0

- `LokutorAgent` and `LokutorAction`.
