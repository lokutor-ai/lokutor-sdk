# Lokutor Voice Agents for Godot

Talk to your game characters. Add a **LokutorAgent** node to any character and it listens through the
microphone, answers in its own voice from where it stands (3D audio), moves its mouth, and calls your
game's functions when the conversation calls for it.

Godot 4.3 or newer. Pure GDScript: nothing to compile, works on every platform Godot exports to that has
a microphone.

## Install

Copy `addons/lokutor` into your project's `addons/` folder (or install **Lokutor Voice Agents** from the
Asset Library), then **Project > Project Settings > Plugins > Lokutor Voice Agents > Enable**. Enabling it
turns on **Audio > Driver > Enable Input**, which Godot leaves off by default and the microphone needs.

## Your first talking character (2 minutes)

1. Select your character and add a child node: **LokutorAgent** (it is a Node3D, so the voice comes from
   wherever you put it: the head is a good place).
2. Paste your API key (lokutor.com dashboard > API keys) into **Account > Api Key**.
3. Write who the character is in **Character > Persona**.
4. Press **Play** and talk. Headphones recommended.

Or open this folder as a project and press Play: **Brom's Forge**, a blacksmith you can talk to and buy
from (`demo/forge/`). `demo/brom_demo.tscn` is the same idea in 60 lines, the smallest complete example.

## Let the character do things

**In the Inspector, no code:** under **Game Actions**, add a `LokutorAction`. Give it a name
(`open_gate`), say when to use it, and point **Target** and **Method** at any node's method, like the
gate's `open`. The character calls it when the conversation calls for it.

**In code, with arguments:**

```gdscript
@onready var shopkeeper: LokutorAgent = $Shopkeeper/LokutorAgent

func _ready():
    shopkeeper.add_action("give_item", "Sell an item to the player", _give_item,
        {"item": {"type": "string", "description": "Which item", "enum": ["iron sword", "healing potion"]}})

func _give_item(args: Dictionary) -> String:
    inventory.append(args.item)
    return "Gave the %s." % args.item     # the character is told this
```

A parameter can also be just a description: `{"item": "Which item"}`.

## Signals

`player_said(text)`, `player_said_partial(text)`, `character_said(text)`, `character_interrupted(heard)`,
`state_changed(state)`, `action_called(name, args, result)`, `visemes_received(list)`, `voice_played(samples, rate)`,
`reconnecting()`, `reconnected()`, `session_limit(seconds, reason)`, `error_occurred(code, message)`,
`conversation_ended(reason)`.

```gdscript
shopkeeper.character_said.connect(func(text): $Subtitles.text = text)
shopkeeper.start_conversation()     # e.g. when the player walks up (turn off Start On Ready)
shopkeeper.stop_conversation()      # when they leave: minutes are billed while connected
shopkeeper.set_persona("You are now angry with the player.")
shopkeeper.set_context("Gold the player has", 12)     # what is true right now; call it as often as you like
shopkeeper.muted = true                                # the player's microphone off
```

## Lip sync

Visemes are on by default: the server sends the mouth shape of every sound (15 shapes, `{v, c, t, d}`) and
**LokutorLipSync** plays them on the clock of what is being *heard*. Put one beside the agent and drag your
meshes into **Faces**; the rig is recognised by its blend shape names:

| Your face has | Used as |
|---|---|
| `viseme_aa`, `viseme_PP`... (Oculus, Ready Player Me, Meta), `vrc.v_aa`... (VRChat) | the 15 visemes, one to one |
| `aa ih ou ee oh` (VRM), `Fcl_MTH_A`... (VRoid), `MTH_A`... | mixed from the 15 |
| `jawOpen`, `mouthPucker`... (ARKit, 52 shapes) | mixed from the 15 |
| one of `mouthOpen`, `MouthOpen`, `mouth_open`... | how open the mouth is |
| your own names | **Mapping**: `{"aa": {"gape": 0.8}, "PP": {"lips_closed": 1.0}}` |
| a jaw node or bone | **Jaw**, or **Skeleton** + **Jaw Bone** |
| a 2D sprite | **Sprite** with mouth frames, or **Sprite Frames** |

`intensity`, `lead`, `blend` and `loudness` set the feel; `stepped_fps = 12` animates on twos for a cartoon
look. `mouth_open` (0..1), `weights` and `viseme_changed` drive anything else. Without visemes it follows the
loudness. An agent and a LokutorVoice can share one: it follows whoever is speaking.

## Written lines

**LokutorVoice** says lines you wrote (barks, narration, tutorials) with the same voices and lip sync, and keeps
them on disk, so a line said again costs nothing and starts at once:

```gdscript
await $Guard/LokutorVoice.say("Halt! Who goes there?")
$Guard/LokutorVoice.say("Behind you!", {"interrupt": true, "voice": "M3"})
await $Guard/LokutorVoice.preload_lines(["Halt!", "Pass, friend."])
```

## The clients underneath

The same classes as the [JavaScript](../js) and [Python](../python) SDKs, with the same names, for when you want
the protocol without the character:

| Class | What it is |
|---|---|
| `LokutorVoiceAgentClient` | a conversation: `open()`, `send_audio()`, `update_prompt()`, `submit_tool_result()`, `start_managed()`... It reconnects by itself |
| `LokutorTTSClient` | `await synthesize(text, {voice, language, visemes})` |
| `LokutorSTTClient` | `await transcribe(pcm16, {language, keyterms})` |
| `LokutorSpeechToTextClient` | live transcription from the microphone |
| `Lokutor` | voices, languages, visemes, `fetch_voices()`, `fetch_languages()`... |
| `LokutorAudio`, `LokutorError` | PCM helpers, and errors with `code`, `message`, `retryable` |

`connect` and `disconnect` are called `open` and `close`, because every Godot object already has those two.

**Your own model.** `on_llm_request` answers the conversation from your game or your server, and `llm` points it at
an OpenAI-compatible endpoint.

Full reference: [docs.lokutor.com](https://docs.lokutor.com/sdks/godot/reference).

## Microphone

- **ALWAYS** (default): just talk; the server notices when the player stops.
- **PUSH_TO_TALK**: hold the Input Map action in **Push To Talk Action**, or call `set_talking(true/false)`.
- **Mute Mic While Speaking** (on by default) stops laptop speakers from making the character interrupt
  itself. Turn it off when players wear headphones so they can interrupt.
- `feed_microphone(samples, rate)` sends audio you capture yourself.

On macOS and iOS exports, fill in the export preset's microphone usage description.

## Shipping your game: keep the key on your server

An API key inside a game build can be extracted. For a release, let your backend trade it for a
short-lived ticket:

```gdscript
shopkeeper.ticket_provider = func(): return await my_backend.get_lokutor_ticket()
# your server: POST https://api.lokutor.com/ws-ticket with "Authorization: Bearer <API key>"
```

## Recording a video of the demo

Godot's Movie Maker records the game's frames and audio together. With `--write-movie`, the forge demo
plays a scripted player (lines spoken by Lokutor TTS) and holds a real conversation with production:

```
LOKUTOR_API_KEY=... python3 demo/forge/make_lines.py
LOKUTOR_API_KEY=... godot --path . --write-movie forge.avi res://demo/forge/forge_demo.tscn
ffmpeg -i forge.avi -c:v libx264 -crf 18 -pix_fmt yuv420p -c:a aac forge.mp4
```

## Tests

`tests/unit_tests.gd` runs the whole addon against a mock server in the same process: no key, no network, no
sound card. Run it on every change.

```
godot --headless --path . --import
godot --headless --path . --script res://tests/unit_tests.gd
```

`tests/public_tests.gd` checks production without a key: the public endpoints, and how a wrong key is refused.

```
godot --headless --path . --script res://tests/public_tests.gd
```

`tests/run_tests.gd` holds a real conversation with production:

```
tests/make_lines.sh                       # optional: the player's lines spoken by macOS `say`, instead of by the API
LOKUTOR_API_KEY=... godot --headless --path . --script res://tests/run_tests.gd
```
