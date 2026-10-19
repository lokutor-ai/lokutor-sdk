@icon("res://addons/lokutor/icon.svg")
class_name LokutorAction
extends Resource
## A game action set up in the Inspector: when the character decides to use it, `method` is
## called on the node at `target` (relative to the LokutorAgent).

## What the character calls it, e.g. open_gate. Letters, digits and _ only.
@export var action_name: String = "open_gate"
## When the character should use it, in plain words.
@export_multiline var description: String = "Opens the town gate for the player."
## What the character is told happened, so it can say so.
@export var reply: String = "The gate is now open."
## The node to call, relative to the LokutorAgent.
@export var target: NodePath
## The method to call on it (no arguments).
@export var method: StringName
