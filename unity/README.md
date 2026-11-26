# Lokutor Voice Agents for Unity

Talk to your game characters. Add a **Lokutor Agent** to any character and it listens through the
microphone, answers in its own voice from where it stands (3D audio), moves its mouth, and calls your
game's functions when the conversation calls for it: sell an item, open a gate, start a quest.

Unity 2021.3 or newer. No other packages needed.

## Install

**Window > Package Manager > + > Add package from git URL**, then paste:

```
https://github.com/lokutor-ai/lokutor-sdk.git?path=/unity
```

## Your first talking character (2 minutes)

1. Select a character (or any GameObject) and **Add Component > Lokutor > Lokutor Agent**.
2. Paste your API key (lokutor.com dashboard > API keys) into **Api Key**.
3. Write who the character is in **Persona**, e.g. *"You are Mira, the innkeeper. You know every rumour in town."*
4. Press **Play** and talk. Headphones recommended.

That's it. The character greets the player, listens, and answers in the voice you picked
(`F1`-`F5`, `M1`-`M5`, or a voice from your dashboard) and language (`en`, `es`, `fr`, `pt`, ...). Set the language to `auto` and the character answers in whichever language the player speaks.

Or import the **Talking blacksmith** sample (Package Manager > Lokutor Voice Agents > Samples): add its
script to an empty GameObject, paste your key, press Play.

## Let the character do things

**In the Inspector, no code:** add an entry under **Game actions the character can use**. Give it a name
(`open_gate`), say when to use it (*"Opens the town gate for the player"*), and hook **On Called** to any
method, like `Gate.Open`. When the conversation calls for it, the character calls it.

**In code, with arguments:**

```csharp
using Lokutor;

var shopkeeper = GetComponent<LokutorAgent>();
shopkeeper.AddAction("give_item", "Sell an item to the player", args =>
{
    string item = args.GetString("item");
    inventory.Add(item);
    gold -= prices[item];
    return $"Gave the {item}. The player has {gold} gold left.";   // the character is told this
}, new ActionParam("item", "Which item", ParamType.String, true, "iron sword", "healing potion"));

shopkeeper.AddActionAsync("fetch_horse", "Bring the player's horse", async args =>
{
    await stable.BringHorse();                                       // the character waits for it
    return "The horse is at the door.";
});
```

Handlers run on Unity's main thread, so they can touch any game object. `RemoveAction(name)` takes one away.

## Lip sync

Visemes are on by default: the server sends the mouth shape of every sound (15 shapes) and **Lokutor Lip
Sync** plays them on the clock of what is being *heard*. Without any setup it moves every face under the
agent whose mouth it recognises by its blend shape names:

| Your face has | Used as |
|---|---|
| `viseme_aa`, `viseme_PP`... (Oculus, Ready Player Me), `vrc.v_aa`... (VRChat) | the 15 visemes, one to one |
| `aa ih ou ee oh` (VRM), `Fcl_MTH_A`... (VRoid), `MTH_A`... | mixed from the 15 |
| `jawOpen`, `mouthPucker`... (ARKit, 52 shapes) | mixed from the 15 |
| one of `mouthOpen`, `mouth_open`, `jawOpen`... | how open the mouth is |
| your own names | **Mapping**: viseme, blend shape, weight |
| a jaw bone or head part | **Jaw** |
| 2D drawings | **Mouth Sprite** with **Mouth Sprites** or **Viseme Sprites** |

To choose the faces or change the feel (`intensity`, `lead`, `blend`, `loudness`), add the component yourself;
`steppedFps = 12` animates on twos for a cartoon look. `MouthOpen` (0..1), `Weights`, `Current` and
`onVisemeChanged` drive anything else, like an animator parameter or a UI Image (`MouthSprite()`). Without
visemes it follows the loudness.

The agent's own **Face**, **Mouth Blend Shape** and **Jaw** fields still work for a simple mouth.

## Written lines

**Lokutor Voice** says lines you wrote (barks, narration, tutorials) with the same voices and lip sync, and
keeps them on disk, so a line said again costs nothing and starts at once:

```csharp
var guard = GetComponent<LokutorVoice>();
await guard.Say("Halt! Who goes there?");                 // returns when it has been heard
guard.Say("Behind you!", new SayOptions { Interrupt = true, Voice = "M3" });
yield return guard.SayAndWait("Pass, friend.");           // in a coroutine
await guard.PreloadLines(new[] { "Halt!", "Pass, friend." });

// In the editor, once: lines that ship with the game and need no network or key.
await guard.SaveLinesTo("Assets/Resources/LokutorVoices", allMyLines);
```

When the character also has an agent, put it on its own GameObject (a child at the head is a good place): each plays
through its own AudioSource. They share the character's **Lokutor Lip Sync**.

## Microphone

- **Always** (default): just talk; the server notices when the player stops.
- **Push To Talk**: hold the key (old Input Manager), or set **Push To Talk Action** (Input System), or
  call `agent.SetTalking(true/false)` from your own input code.
- **Off**: the character hears only what you give it with `agent.FeedMicrophone(samples, 0, count, rate)`.
- **Mute Mic While Speaking** (on by default) stops laptop speakers from making the character interrupt
  itself. Turn it off when players wear headphones so they can interrupt the character.

## Events and control

```csharp
agent.onPlayerSaid.AddListener(text => subtitles.ShowPlayer(text));
agent.onCharacterSaid.AddListener(text => subtitles.ShowNpc(LokutorApi.StripTags(text)));   // without [laugh]
agent.onStateChanged.AddListener(state => thinkingBubble.SetActive(state == AgentState.Thinking));
agent.onConversationEnded.AddListener(reason => Debug.Log(reason));

agent.StartConversation();          // e.g. when the player walks up (turn off Start On Awake)
agent.StopConversation();           // when they walk away: minutes are billed while connected
agent.SetPersona("You are now angry with the player.");
agent.SetContext("Gold the player has", "12");      // what is true right now; call it as often as you like
agent.SetVoice("M3"); agent.SetLanguage("es"); agent.SetPitch(4);
agent.ResetConversation();
foreach (var line in agent.GetTranscript()) Debug.Log(line.Role + ": " + line.Text);
```

A connection that drops is opened again by itself (`onReconnecting`, `onReconnected`); the character then
starts a fresh conversation. Also `onCharacterInterrupted`, and in code `PlayerSaidPartial`, `ActionCalled`,
`VisemesReceived`, `VoicePlayed`, `SessionLimit` and `Error`; `HeardSeconds`, `VoiceLevel`, `IsSpeaking`,
`Muted`. `agent.Session` is the `LokutorSession` underneath, and `agent.Session.Client` the protocol client.

**Your own model.** `agent.OnLlmRequest` answers the conversation from your game or your server, and
`agent.Llm` points it at an OpenAI-compatible endpoint:

```csharp
agent.OnLlmRequest = async (request, reply) =>
{
    await foreach (var text in myModel.Stream(request.Messages, reply.Token)) reply.Chunk(text);
    return null;
};
```

## The clients underneath

The same classes as the [JavaScript](../js) and [Python](../python) SDKs, with the same names, for when you want
the protocol without the character. They do not need Unity.

| Class | What it is |
|---|---|
| `VoiceAgentClient` | a conversation: `ConnectAsync()`, `SendAudio()`, `UpdatePrompt()`, `SubmitToolResult()`, `StartManagedAsync()`... It reconnects by itself. Call its `Update()` every frame: that is when its events are raised |
| `TTSClient` | `await SynthesizeAsync(text, new SynthesizeOptions { Voice, Language, Visemes })` |
| `STTClient` | `await TranscribeAsync(pcm16, new TranscribeOptions { Language, Keyterms })` |
| `SpeechToTextClient` | live transcription |
| `LokutorApi` | voices, languages, visemes, `GetVoicesAsync()`, `GetLanguagesAsync()`... (a class cannot be called `Lokutor` inside its namespace) |
| `LokutorAudio`, `LokutorError` | PCM helpers, and errors with `Code`, `Message`, `Retryable` |
| `LipSync`, `MouthRig`, `VoiceLines` | lip sync and written lines without the engine |

`LokutorAudioManager` is Unity's microphone and speaker for them:

```csharp
var dictation = new SpeechToTextClient(apiKey) { Keyterms = new List<string> { "Oakvale" } };
dictation.FinalTranscript += text => chat.Send(text);
await GetComponent<LokutorAudioManager>().StartManaged(dictation);
```

Full reference: [docs.lokutor.com](https://docs.lokutor.com/sdks/unity/reference).

## Shipping your game: keep the key on your server

An API key inside a game build can be extracted. For a release, have your backend trade the key for a
short-lived ticket and give the agent that instead:

```csharp
agent.TicketProvider = async () => await myBackend.GetLokutorTicketAsync();
// your server: POST https://api.lokutor.com/ws-ticket with "Authorization: Bearer <API key>"
//              -> { "ticket": "...", "expires_in": ... }
```

It is called on the main thread, before every connection.

## Platforms

Windows, macOS, Linux, Android and iOS, with Mono and IL2CPP. WebGL is not supported yet (Unity's WebGL
builds have neither `System.Net.WebSockets` nor the `Microphone` class); for web games use the
[JavaScript SDK](https://github.com/lokutor-ai/lokutor-sdk/tree/main/js). On mobile, add a microphone
usage description (iOS: *Player Settings > Microphone Usage Description*).

## Use a dashboard agent

Built your character in the Lokutor dashboard (prompt, voice, knowledge, tools)? Put its id in **Agent
Id** and leave Persona, Voice and Language empty to use the dashboard's.

## Tests

The core has no engine in it and is tested with plain .NET, against a mock server:

```
dotnet run --project Tests~/CoreTests -- unit
dotnet run --project Tests~/CoreTests -- public                  # production, without a key
dotnet run --project Tests~/CoreTests -- demo out                # a whole conversation with production (LOKUTOR_API_KEY)
dotnet build Tests~/CoreCompat                                  # as Unity compiles it
dotnet build Tests~/UnityCompileCheck                           # the Unity layer, against its signatures
dotnet build Tests~/UnityCompileCheck -p:InputSystem=true
```
