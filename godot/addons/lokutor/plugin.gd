@tool
extends EditorPlugin

# Characters only hear the player if the project captures audio input, which Godot turns off by
# default. Turned on here once, when the plugin is enabled.
func _enter_tree() -> void:
	if not ProjectSettings.get_setting("audio/driver/enable_input", false):
		ProjectSettings.set_setting("audio/driver/enable_input", true)
		ProjectSettings.save()
		print("Lokutor: turned on Project Settings > Audio > Driver > Enable Input, so characters can hear the microphone.")
