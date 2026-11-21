extends SceneTree

const EXERCISE = __EXERCISE__
var session: Node
var hud: Node

func _initialize():
    call_deferred("_run")

func _check(ok: bool, message: String):
    if not ok:
        push_error("HOTBAR CHECK FAILED: " + message)
        quit(1)
        assert(ok, message)

func layout():
    return JSON.parse_string(hud.InspectLayout())

func _key(code: Key):
    var event = InputEventKey.new()
    event.keycode = code
    event.pressed = true
    Input.parse_input_event(event)
    await process_frame
    event.pressed = false
    Input.parse_input_event(event)
    await process_frame

func _mouse(point: Vector2, down: bool):
    var event = InputEventMouseButton.new()
    event.position = point
    event.global_position = point
    event.button_index = MOUSE_BUTTON_LEFT
    event.pressed = down
    event.button_mask = MOUSE_BUTTON_MASK_LEFT if down else 0
    root.push_input(event, true)

func _motion(point: Vector2, held: bool):
    var event = InputEventMouseMotion.new()
    event.position = point
    event.global_position = point
    event.relative = Vector2(20, 0)
    event.button_mask = MOUSE_BUTTON_MASK_LEFT if held else 0
    root.push_input(event, true)

func _drag(source: Control, target: Control):
    var start = source.get_global_transform_with_canvas() * (source.size / 2)
    var end = target.get_global_transform_with_canvas() * (target.size / 2)
    _motion(start, false)
    _mouse(start, true)
    await process_frame
    _motion(start + Vector2(20, 0), true)
    await process_frame
    _check(root.gui_is_dragging(), "Native Control drag did not begin")
    _motion(end, true)
    await process_frame
    _mouse(end, false)
    await process_frame
    await process_frame

func _choose(control: OptionButton, index: int):
    control.select(index)
    control.emit_signal("item_selected", index)
    await process_frame

func _run():
    session = load("res://Features/Arena/Arena.tscn").instantiate()
    root.add_child(session)
    hud = session.get_node("View/Hotbars")
    var deadline = Time.get_ticks_msec() + 15000
    while layout() == null:
        _check(Time.get_ticks_msec() < deadline, "Character hotbar did not load")
        await process_frame
    if EXERCISE:
        _check(layout().Bars[0].SlotCount == 8, "Default bar has eight slots")
        await _key(KEY_H)
        var settings = hud.get_node("Settings")
        _check(settings.visible, "H opens settings")
        await _choose(settings.get_node("SlotCount"), 1)
        await _choose(settings.get_node("BarChoice"), 1)
        settings.get_node("BarVisible").button_pressed = true
        settings.get_node("Vertical").button_pressed = true
        settings.get_node("Locked").button_pressed = false
        settings.get_node("Scale").value = 80
        settings.get_node("Binding").emit_signal("pressed")
        await _key(KEY_Z)
        _check(layout().Bars[1].Slots[0].Key == "Z", "Custom binding is saved")
        await _choose(settings.get_node("BarChoice"), 2)
        settings.get_node("BarVisible").button_pressed = true
        await _choose(settings.get_node("SlotCount"), 2)
        settings.get_node("Scale").value = 125
        await _key(KEY_ESCAPE)
        _check(layout().Bars[0].SlotCount == 10 and layout().Bars[2].SlotCount == 12, "Slot counts are configurable")
        _check(layout().Bars[1].Vertical and layout().Bars[1].Scale == 0.8, "Orientation and scale are configurable")
        # Move the second bar away from the Skills window and other bars.
        var bar = hud.get_node("Bar2")
        var handle = bar.get_node("Handle")
        var start = handle.get_global_transform_with_canvas() * (handle.size / 2)
        _motion(start, false)
        _mouse(start, true)
        await process_frame
        _motion(start + Vector2(430, 0), true)
        await process_frame
        _mouse(start + Vector2(430, 0), false)
        await process_frame
        _check(layout().Bars[1].X > 0.8, "Unlocked header moves the bar")
        await _key(KEY_K)
        var icon = session.get_node("View/Progression/Skills/SkillIcon2")
        var action = layout().Bars[0].Slots[2].ActionId
        await _drag(icon, hud.get_node("Bar1/Slot9"))
        _check(layout().Bars[0].Slots[9].ActionId == action, "Skills window supplies learned icons")
        await _key(KEY_K)
        await _drag(hud.get_node("Bar1/Slot9"), hud.get_node("Bar2/Slot0"))
        _check(layout().Bars[1].Slots[0].ActionId == action and layout().Bars[0].Slots[9].ActionId == "", "Native cross-bar move preserves binding")
        await _key(KEY_4)
        await create_timer(0.3).timeout
        _check(hud.get_node("Bar1/Slot3/Timer").text.length() > 0, "Confirmed defense cooldown is displayed")
        print("HOTBAR UI passed: native drag, positioning, sizes, orientation, bindings and server cooldown")
    print("HOTBAR state=" + hud.InspectLayout())
    print("HOTBAR complete")
