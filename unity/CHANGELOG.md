# Changelog

## 0.2.0

- **Visemes.** The server's 15 mouth shapes drive `LokutorLipSync`: Oculus, Ready Player Me, VRChat, VRM, VRoid, ARKit,
  single-shape, custom, jaw and sprite mouths. On by default in `LokutorAgent`, which finds the faces by itself.
- **The clients of the other SDKs**: `VoiceAgentClient`, `TTSClient`, `STTClient` and `SpeechToTextClient`, with the same
  names, defaults, errors and close codes; `LokutorApi` and `LokutorAudio` helpers; `LokutorAudioManager`, Unity's
  microphone and speaker for them.
- **Reconnection.** A dropped connection is opened again (1001, 1006, 1011-1014; 2, 4, 8, 10, 10 s).
- **LokutorVoice**: written lines with lip sync, a queue and a disk cache; they can ship with the game.
- **LokutorAgent**: `SetContext`, `SetVoice`, `SetLanguage`, `SetPitch`, `AddActionAsync`, `RemoveAction`,
  `ResetConversation`, `GetTranscript`, `FeedMicrophone`, `HeardSeconds`, `VoiceLevel`, `IsSpeaking`, `Muted`, `pitch`,
  `autoReconnect`, `lipSync`, `contextHeading`, `actionFiller`, own model (`Llm`, `OnLlmRequest`), and the events `onCharacterInterrupted`,
  `onConversationEnded`, `onReconnecting`, `onReconnected`, `PlayerSaidPartial`, `ActionCalled`, `VisemesReceived`,
  `VoicePlayed`, `SessionLimit` and `Error`.
- `StartConversation` works again after the server ended the conversation, without calling `StopConversation` first.
- The voice stops at once when the player interrupts (its last 0.16 s used to play), and a long frame is heard as a pause
  instead of an echo of the last second.
- Agents with a background sound no longer keep the character speaking nor the microphone muted.
- A character no longer says "One moment" or the like while an action of the game runs, nor what the model wrote before the action, which can be its plan (`actionFiller` brings both back). `speakBeforeActions = false` also drops what it begins to say just before it.
- The microphone is also muted while another character or a `LokutorVoice` is heard (`mutesMicrophones`).
- A reply that takes two seconds to come is no longer taken for the player's turn.
- A refused connection says the server's reason (`auth.invalid_key`, `billing.insufficient_balance`...) instead of
  `ws.close`, and a reconnection that no attempt can fix ends at once.
- Unit tests with a mock server, and live checks that need no key (`-- public`).

Projects made with 0.1 work as they are, with two differences in events that few use: `LokutorSession.VisemesReceived`
now carries the 15 visemes (`Id`, `Name`, `Duration`; `Char` is the first letter of the name), and
`LokutorAgent.Session` is already set while the conversation is connecting. Building with `LokutorSession` directly: it
opens dropped connections again unless `SessionOptions.AutoReconnect` is false.

## 0.1.0

- `LokutorAgent`, `LokutorSession` and game actions.
