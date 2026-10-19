extends CanvasLayer
## The forge demo's screen: a status pill, gold and bag, subtitles, an "item received" card, and
## title and end cards for the video.

const ORANGE := Color("ff9a4a")
const BLUE := Color("8fc3ff")
const GREEN := Color("6fe39a")
const AMBER := Color("ffcf5a")
const PANEL := Color(0.06, 0.05, 0.05, 0.62)

var _status_dot: Panel
var _dot_style: StyleBoxFlat
var _status_text: Label
var _bars: Array[ColorRect] = []
var _gold_label: Label
var _bag_box: HBoxContainer
var _subs: VBoxContainer
var _card: PanelContainer
var _card_title: Label
var _card_detail: Label
var _title: Control
var _end: Control
var _gold_shown := 30.0
var _gold_target := 30
var _state := 0
var _t := 0.0
var _level := 0.0
var _sub_time := {}   # Label -> seconds left


func _ready() -> void:
	_build_status()
	_build_inventory()
	_build_subtitles()
	_build_card()


func set_state(state: int) -> void:
	_state = state


func set_level(level: float) -> void:
	_level = level


func set_gold(gold: int) -> void:
	_gold_target = gold


func add_to_bag(item_name: String) -> void:
	_bag_box.add_child(_tag(item_name.capitalize()))
	_card_title.text = "Received: " + item_name.capitalize()
	_card.modulate.a = 0.0
	_card.visible = true
	var tw := create_tween()
	tw.tween_property(_card, "modulate:a", 1.0, 0.25)
	tw.tween_interval(2.6)
	tw.tween_property(_card, "modulate:a", 0.0, 0.5)


func set_card_detail(text: String) -> void:
	_card_detail.text = text


## Shows a subtitle line; agent lines are revealed at roughly speaking pace.
func say(speaker: String, text: String, is_player: bool) -> void:
	var row := PanelContainer.new()
	row.add_theme_stylebox_override("panel", _style(PANEL, 14, 18, 10))
	var label := RichTextLabel.new()
	label.bbcode_enabled = true
	label.fit_content = true
	label.scroll_active = false
	label.custom_minimum_size = Vector2(980, 0)
	label.add_theme_font_size_override("normal_font_size", 30)
	label.add_theme_font_size_override("bold_font_size", 30)
	var color := BLUE if is_player else ORANGE
	label.text = "[b][color=#%s]%s[/color][/b]   %s" % [color.to_html(false), speaker, text]
	row.add_child(label)
	row.modulate.a = 0.0
	_subs.add_child(row)
	while _subs.get_child_count() > 2:
		var old := _subs.get_child(0)
		_subs.remove_child(old)
		old.queue_free()
	var tw := create_tween()
	tw.tween_property(row, "modulate:a", 1.0, 0.2)
	if not is_player:
		label.visible_ratio = 0.0
		tw.parallel().tween_property(label, "visible_ratio", 1.0, maxf(0.6, text.length() / 16.0))
	_sub_time[row] = 6.0 + text.length() / 16.0


func show_title(title: String, subtitle: String, seconds: float) -> void:
	_title = _overlay(0.85)
	var box := _centered_box(_title)
	box.add_child(_label(title, 64, Color.WHITE, true))
	box.add_child(_label(subtitle, 28, Color(1, 1, 1, 0.75)))
	var tw := create_tween()
	tw.tween_interval(seconds)
	tw.tween_property(_title, "modulate:a", 0.0, 0.9)
	tw.tween_callback(_title.queue_free)


func show_end(code: String, tagline: String, url: String) -> void:
	_end = _overlay(0.9)
	_end.modulate.a = 0.0
	var box := _centered_box(_end)
	box.add_child(_label(tagline, 46, Color.WHITE, true))
	var code_panel := PanelContainer.new()
	code_panel.add_theme_stylebox_override("panel", _style(Color(0.1, 0.1, 0.12, 0.95), 16, 28, 22))
	var code_label := Label.new()
	code_label.text = code
	var mono := SystemFont.new()
	mono.font_names = PackedStringArray(["Menlo", "SF Mono", "Consolas", "DejaVu Sans Mono", "monospace"])
	code_label.add_theme_font_override("font", mono)
	code_label.add_theme_font_size_override("font_size", 24)
	code_label.add_theme_color_override("font_color", Color("e6e1d6"))
	code_panel.add_child(code_label)
	box.add_child(code_panel)
	box.add_child(_label(url, 30, ORANGE, true))
	create_tween().tween_property(_end, "modulate:a", 1.0, 0.8)


func _process(delta: float) -> void:
	_t += delta
	# Status pill
	var names := ["Away", "Arriving", "Listening", "Hearing you", "Thinking", "Speaking"]
	var colors := [Color(0.6, 0.6, 0.6), Color(0.6, 0.6, 0.6), GREEN, BLUE, AMBER, ORANGE]
	var text: String = names[_state]
	if _state == 4:
		text += ".".repeat(int(_t * 3.0) % 4)
	_status_text.text = text
	var c: Color = colors[_state]
	var pulse := 0.65 + 0.35 * sin(_t * 4.0) if _state == 2 else 1.0
	_dot_style.bg_color = Color(c.r, c.g, c.b, pulse)
	for i in _bars.size():
		var h := 4.0
		if _state == 5:
			h = 4.0 + 22.0 * clampf(_level * (0.6 + 0.4 * sin(_t * 17.0 + i * 1.7)), 0.0, 1.0)
		_bars[i].custom_minimum_size = Vector2(5, h)
		_bars[i].color = Color(c.r, c.g, c.b, 0.9 if _state == 5 else 0.35)
	# Gold counts towards its new value
	_gold_shown = move_toward(_gold_shown, _gold_target, delta * 25.0)
	_gold_label.text = str(int(round(_gold_shown)))
	# Subtitles fade out after a while
	for row in _sub_time.keys():
		if not is_instance_valid(row):
			_sub_time.erase(row)
			continue
		_sub_time[row] -= delta
		if _sub_time[row] <= 0.0:
			_sub_time.erase(row)
			var tw := create_tween()
			tw.tween_property(row, "modulate:a", 0.0, 0.6)
			tw.tween_callback(row.queue_free)


# ---- Building -----------------------------------------------------------------------------------

func _build_status() -> void:
	var pill := PanelContainer.new()
	pill.add_theme_stylebox_override("panel", _style(PANEL, 26, 22, 12))
	pill.position = Vector2(36, 32)
	add_child(pill)
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 14)
	pill.add_child(row)
	_status_dot = Panel.new()
	_status_dot.custom_minimum_size = Vector2(16, 16)
	_status_dot.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	_dot_style = _style(GREEN, 8, 0, 0)
	_status_dot.add_theme_stylebox_override("panel", _dot_style)
	row.add_child(_status_dot)
	var name_label := _label("Brom", 26, Color.WHITE, true)
	row.add_child(name_label)
	_status_text = _label("Arriving", 26, Color(1, 1, 1, 0.8))
	_status_text.custom_minimum_size = Vector2(170, 0)
	row.add_child(_status_text)
	var bars := HBoxContainer.new()
	bars.add_theme_constant_override("separation", 4)
	bars.alignment = BoxContainer.ALIGNMENT_CENTER
	for i in 5:
		var b := ColorRect.new()
		b.custom_minimum_size = Vector2(5, 4)
		b.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		bars.add_child(b)
		_bars.append(b)
	row.add_child(bars)


func _build_inventory() -> void:
	var panel := PanelContainer.new()
	panel.add_theme_stylebox_override("panel", _style(PANEL, 26, 22, 12))
	panel.anchor_left = 1.0
	panel.anchor_right = 1.0
	panel.offset_left = -36
	panel.offset_right = -36
	panel.offset_top = 32
	panel.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	add_child(panel)
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 12)
	row.alignment = BoxContainer.ALIGNMENT_END
	panel.add_child(row)
	_bag_box = HBoxContainer.new()
	_bag_box.add_theme_constant_override("separation", 8)
	row.add_child(_bag_box)
	var coin := Panel.new()
	coin.custom_minimum_size = Vector2(24, 24)
	coin.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	var coin_style := _style(Color("f2c14e"), 12, 0, 0)
	coin_style.border_color = Color("b8862a")
	coin_style.set_border_width_all(3)
	coin.add_theme_stylebox_override("panel", coin_style)
	row.add_child(coin)
	_gold_label = _label("30", 28, Color("ffe08a"), true)
	row.add_child(_gold_label)


func _build_subtitles() -> void:
	_subs = VBoxContainer.new()
	_subs.anchor_left = 0.5
	_subs.anchor_right = 0.5
	_subs.anchor_top = 1.0
	_subs.anchor_bottom = 1.0
	_subs.offset_left = -520
	_subs.offset_right = 520
	_subs.offset_bottom = -48
	_subs.grow_vertical = Control.GROW_DIRECTION_BEGIN
	_subs.alignment = BoxContainer.ALIGNMENT_END
	_subs.add_theme_constant_override("separation", 10)
	add_child(_subs)


func _build_card() -> void:
	_card = PanelContainer.new()
	var st := _style(Color(0.08, 0.06, 0.04, 0.85), 18, 26, 18)
	st.border_color = Color("f2c14e")
	st.border_width_left = 6
	_card.add_theme_stylebox_override("panel", st)
	_card.anchor_left = 1.0
	_card.anchor_right = 1.0
	_card.anchor_top = 0.5
	_card.anchor_bottom = 0.5
	_card.offset_left = -500
	_card.offset_right = -60
	_card.offset_top = -80
	_card.visible = false
	add_child(_card)
	var col := VBoxContainer.new()
	_card.add_child(col)
	_card_title = _label("", 32, Color("ffe08a"), true)
	col.add_child(_card_title)
	_card_detail = _label("", 22, Color(1, 1, 1, 0.8))
	col.add_child(_card_detail)


func _tag(text: String) -> PanelContainer:
	var tag := PanelContainer.new()
	tag.add_theme_stylebox_override("panel", _style(Color(1, 1, 1, 0.12), 12, 12, 4))
	tag.add_child(_label(text, 20, Color(1, 1, 1, 0.9)))
	return tag


func _overlay(alpha: float) -> Control:
	var root := Control.new()
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	var bg := ColorRect.new()
	bg.color = Color(0.03, 0.02, 0.02, alpha)
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.add_child(bg)
	add_child(root)
	return root


func _centered_box(parent: Control) -> VBoxContainer:
	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	parent.add_child(center)
	var box := VBoxContainer.new()
	box.alignment = BoxContainer.ALIGNMENT_CENTER
	box.add_theme_constant_override("separation", 22)
	center.add_child(box)
	return box


func _label(text: String, size: int, color: Color, bold := false) -> Label:
	var l := Label.new()
	l.text = text
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", color)
	if bold:
		var f := SystemFont.new()
		f.font_weight = 700
		l.add_theme_font_override("font", f)
	return l


func _style(color: Color, radius: int, pad_x: int, pad_y: int) -> StyleBoxFlat:
	var s := StyleBoxFlat.new()
	s.bg_color = color
	s.set_corner_radius_all(radius)
	s.content_margin_left = pad_x
	s.content_margin_right = pad_x
	s.content_margin_top = pad_y
	s.content_margin_bottom = pad_y
	s.anti_aliasing = true
	return s
