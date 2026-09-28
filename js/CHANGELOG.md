# Changelog

All notable changes to this project will be documented in this file.

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
