# Changelog

All notable changes to this project will be documented in this file.

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
