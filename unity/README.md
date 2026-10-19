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
(`F1`-`F5`, `M1`-`M5`, or a voice from your dashboard) and language (`en`, `es`, `fr`, `pt`, ...).

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
```

Handlers run on Unity's main thread, so they can touch any game object.

## Lip sync

Set **Face** and **Mouth Blend Shape** (e.g. `jawOpen`), or a **Jaw** bone. `agent.MouthOpen` (0..1)
follows the audio you actually hear, if you want to drive something else, like a sprite or an animator
parameter.

## Microphone

- **Always** (default): just talk; the server notices when the player stops.
- **Push To Talk**: hold the key (old Input Manager), or set **Push To Talk Action** (Input System), or
  call `agent.SetTalking(true/false)` from your own input code.
- **Mute Mic While Speaking** (on by default) stops laptop speakers from making the character interrupt
  itself. Turn it off when players wear headphones so they can interrupt the character.

## Events and control

```csharp
agent.onPlayerSaid.AddListener(text => subtitles.ShowPlayer(text));
agent.onCharacterSaid.AddListener(text => subtitles.ShowNpc(text));
agent.onStateChanged.AddListener(state => thinkingBubble.SetActive(state == AgentState.Thinking));

agent.StartConversation();          // e.g. when the player walks up (turn off Start On Awake)
agent.StopConversation();           // when they walk away: minutes are billed while connected
agent.SetPersona("You are now angry with the player.");
```

`agent.Session` exposes the lower-level `LokutorSession` (partial transcripts, interruptions, visemes,
session limits).

## Shipping your game: keep the key on your server

An API key inside a game build can be extracted. For a release, have your backend trade the key for a
short-lived ticket and give the agent that instead:

```csharp
agent.TicketProvider = async () => await myBackend.GetLokutorTicketAsync();
// your server: POST https://api.lokutor.com/ws-ticket with "Authorization: Bearer <API key>"
//              -> { "ticket": "...", "expires_in": ... }
```

## Platforms

Windows, macOS, Linux, Android and iOS. WebGL is not supported yet (Unity's WebGL builds have neither
`System.Net.WebSockets` nor the `Microphone` class); for web games use the
[JavaScript SDK](https://github.com/lokutor-ai/lokutor-sdk/tree/main/js). On mobile, add a microphone
usage description (iOS: *Player Settings > Microphone Usage Description*).

## Use a dashboard agent

Built your character in the Lokutor dashboard (prompt, voice, knowledge, tools)? Put its id in **Agent
Id** and leave Persona, Voice and Language empty to use the dashboard's.
