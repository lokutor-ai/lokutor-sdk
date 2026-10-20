# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

## [1.7.1] - 2026-10-20

The same code as 1.7.0, which was staged on npm and never made public.

## [1.7.0] - 2026-10-20 (not published)

`ConversationalPanel` draws its own orb:

- New `PixelOrb`, the panel's visualizer when `SphereVisualizer` is left out (the option is now optional):
  a lit ball in dithered pixels whose stains flow at the pace of whoever is speaking. It is drawn in the
  `accentColor` while the agent speaks and in the opposite colour on the wheel while it is the caller's
  turn, so whose turn it is shows at a glance; with no call it waits in the accent. Light or dark inks
  follow the panel's `backgroundColor`. lokutor.com and the Lokutor dashboard draw the same orb.
- With an idling visualizer (`PixelOrb`, or any class with a static `idles = true`), the panel is one
  surface before, during and after a call: the orb waits with the Start button on its lower edge, churns
  while the call connects, and the call draws on that same orb, so nothing moves, swaps or goes blank when
  a call starts or ends. It stops drawing while the panel is off screen. A visualizer you pass that does
  not idle keeps the previous behaviour exactly (accent curtain, one instance per call).
- New `texture` option: a faint drift of ASCII glyphs behind the orb (`AsciiField`, also exported).
- `orbInks()` exported for pages that draw the orb's colours elsewhere.

The panel follows its container through a call:

- The container is no longer frozen, in pixels, to its size at Start. A call started in a wide window
  stayed that wide when the window narrowed, and the visualizer ended up outside the visible panel.
- The visualizer's canvas is resized whenever its box changes during the call (a `ResizeObserver` on the
  visualizer's wrapper), not only once at Start, so it is never drawn squashed or stretched; the wrapper
  no longer animates its width and height on a resize.
## [1.6.1] - 2026-10-20

The microphone is downsampled properly before recognition:

- `StreamResampler` (which `BrowserAudioManager` uses to take the microphone's rate down to 16 kHz)
  low-passes before it downsamples: a 63-tap Blackman-windowed sinc at 0.45 x the output rate (7.2 kHz
  for 16 kHz), then linear interpolation, with the filter's history and the resampling phase carried
  across blocks. It used linear interpolation alone, so everything the microphone heard between 8 and
  24 kHz folded back into the speech band the recogniser listens to: a 12 kHz tone at 48 kHz came out at
  4 kHz at full amplitude. It now comes out 85 dB down (92 dB from 44.1 kHz); a 1 kHz tone keeps its
  amplitude. Same filter as the lokutor.com speech-to-text demo.
- From 44.1 kHz the output no longer depends on how the audio was split into blocks. The fractional
  resampling phase was dropped at every block, so the timeline jumped by up to one input sample per
  block and ran fast (19 extra samples per 3 s with 1024-sample blocks); it now equals the whole-signal
  output exactly, at 48 and 44.1 kHz.
- The filter delays the signal by 31 input samples (0.65 ms at 48 kHz) and costs about 0.06 ms per
  4096-sample block at 48 kHz (0.14 ms at 44.1 kHz) on an Apple M-series core, under 0.2% of real time.
- `process(chunk, true)` (flush) ends the stream when downsampling: it drains the filter with silence,
  returns the rest, and the resampler starts over. Upsampling and equal rates are unchanged.

## [1.6.0] - 2026-10-18

Protocol fixes, each checked against what the server sends (lokutor_tts pkg/api):

- `tool_call` fires once, with `call_id` (now on the `ToolCall` type). It fired twice, the first time
  without `call_id`, so a handler ran every tool twice and its first `submitToolResult` was dropped.
- Callbacks fire once. `onTranscription`, `onResponse` and `onStatus` were called directly and again
  through `emit`, so each fired twice; connection errors and `reconnecting`/`disconnected` now also reach
  `on('error')`/`on('status')` listeners.
- Partial user transcripts (`partial: true`) go to the new `transcription_partial` event and stay out of
  `getTranscript()`; only final lines are reported as transcriptions.
- With `agentId`, `voice`, `language` and `prompt` are sent only when set explicitly (`prompt` is optional
  then). Defaults used to overwrite the dashboard agent's voice, language and prompt, including its
  knowledge base and evaluation rules.
- New `auth` option: a callback that returns the credential before every connect and reconnect, so a
  browser can use short-lived tickets from `POST /ws-ticket` instead of embedding an API key.
- `api_key` and `agent_id` are URL-encoded.
- `TTSClient.synthesize` waits for the server's end of stream and rejects with a `LokutorError` on a server
  error frame or a stall (30 s before the first audio, 15 s between chunks). It resolved after 2 s without
  audio, so errors looked like success and requests queued for more than 2 s came back empty. `speed` and
  `steps` are sent only when set (it sent 1.05 and 24).
- Discovery helpers (`fetchVoices`, `fetchLanguages`, `fetchModels`, `fetchConfig`, `fetchStatus`) call the
  API root (`/voices`, not `/ws/agent/voices`, which is a 404); `fetchHealth` calls `/ready` (`/health` is
  the operator dashboard's).
- `SpeechToTextClient.connect` rejects when the microphone cannot start instead of hanging, and STT error
  frames keep the server's code.
- `ErrorCode` includes `agent.not_found` and the `billing.*` codes the server sends.

## [1.5.1] - 2026-09-28

- The microphone is asked for inside the tap that starts a call. `ConversationalPanel.start()` calls the new
  `requestMicrophone()` before anything else and hands the stream to the call (ConvoAgent option
  `microphone`, `BrowserAudioManager` config `microphone`), instead of asking only after the connection
  opened. On iPhones that later request could be refused with no prompt, most reliably after the page had
  played audio: the agent greeted, nothing the caller said reached it, and the panel never went live.
- `requestMicrophone()` also takes the page's iOS audio session out of `'playback'` (which pages set to play
  through the mute switch, and in which iOS refuses the microphone outright), back to `'auto'`.
- A microphone that cannot start now fails the call at once: `connect()` rejects with the new error code
  `audio.microphone_unavailable` (not retryable) and the session is closed. Before, `connect()` stayed
  pending while the session it had opened kept talking.
- `ConversationalPanel.start()` resolves `true` when the call is live and `false` when it could not start, so
  pages no longer present a panel that is not live; the panel names a refused or missing microphone
  (exported as `microphoneMessage`).
- `BrowserAudioManager.cleanup()` closes its AudioContext (each call left one running for the life of the
  page), and `init()` no longer waits indefinitely on `AudioContext.resume()`, which iOS can leave pending.
- Server errors reach `onError` once, not twice.

## [1.5.0] - 2026-09-28

- Reconnects only after the connection drops or the server goes away (close codes 1001, 1006,
  1011-1014). When the server ends the session on purpose (1000 when the agent ends the call, 1008
  when a limit is reached, 4xxx) the client stays closed, calls `onError` with a `session.ended`
  error carrying the server's reason, reports status `disconnected` and emits `ended`. Before, it
  reconnected into a fresh session: a new conversation the caller did not ask for.
- A reconnect reuses the running microphone instead of starting a second capture pipeline (which
  doubled and scrambled the caller's audio), and resets the reply generation counter (which made
  the client discard every reply of the new session as stale: the agent answered, nobody heard it).
- New `session_limit` event (`{ seconds, reason }`) when the server announces how long a session may
  run; `ConversationalPanel` shows it as a countdown.
- `BrowserAudioManager` keeps the screen awake while the microphone is live (Screen Wake Lock), takes
  the lock again when the page returns to the foreground, and resumes audio then. `keepAwake: false`
  opts out.
- `ConversationalPanel`: shows "Reconnecting…" while the client retries, ends the call visibly when the
  connection is gone instead of looking live, shows the server's reason when it ends a session, and
  has an `onEnded(message)` callback for pages that render their own chrome.

## [1.0.0] - 2024-03-21

- Initial release of the Lokutor JavaScript SDK.
- Full TypeScript support.
- `VoiceAgentClient` for real-time voice conversations.
- Support for streaming audio input and output.
- Support for user transcriptions and AI responses.
