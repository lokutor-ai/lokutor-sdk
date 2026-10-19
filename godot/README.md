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

`player_said(text)`, `character_said(text)`, `character_interrupted(heard)`, `state_changed(state)`,
`action_called(name, args, result)`, `error_occurred(code, message)`, `conversation_ended(reason)`.

```gdscript
shopkeeper.character_said.connect(func(text): $Subtitles.text = text)
shopkeeper.start_conversation()     # e.g. when the player walks up (turn off Start On Ready)
shopkeeper.stop_conversation()      # when they leave: minutes are billed while connected
shopkeeper.set_persona("You are now angry with the player.")
```

## Lip sync

Set **Jaw** (rotated around its X axis) or **Face** + **Mouth Blend Shape**. `mouth_open` (0..1) follows
the audio being heard, for anything else (sprites, AnimationTree parameters).

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

`tests/run_tests.gd` holds a real conversation with production, headless:

```
tests/make_lines.sh                       # the player's lines, spoken by macOS `say`
LOKUTOR_API_KEY=... godot --headless --path . --import
LOKUTOR_API_KEY=... godot --headless --path . --script res://tests/run_tests.gd
```
