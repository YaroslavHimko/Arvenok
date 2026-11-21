extends SceneTree

var session: Node
var output_dir: String
var captured_level_up = false

func _initialize():
    output_dir = get_script().resource_path.get_base_dir()
    call_deferred("_run")

func _save(label: String):
    await RenderingServer.frame_post_draw
    root.get_texture().get_image().save_png(output_dir.path_join(label + ".png"))
    print("CAPTURE " + label)

func _run():
    session = load("res://Features/Arena/Arena.tscn").instantiate()
    root.add_child(session)
    await create_timer(2).timeout
    await _save("hud-1280x720")
    var scenery = session.get_node("View/WorldViewport/Forest/SceneryAndEntities")
    while not session.IsProgressionSmokeFinished():
        if not captured_level_up:
            for child in scenery.get_children():
                if child is Sprite2D and child.texture and child.texture.resource_path.ends_with("level_up_effect.png"):
                    captured_level_up = true
                    await _save("level-up-1280x720")
                    break
        await process_frame
    var hud = session.get_node("View/Progression")
    Input.parse_input_event(_key(KEY_K))
    await create_timer(0.2).timeout
    assert(hud.get_node("Skills").visible, "K must open Skills")
    for slot in ["Primary", "Mobility", "Interrupt", "Defense"]:
        assert(hud.get_node("Skills/Upgrade" + slot).disabled, "Spent points must disable upgrades")
    await _save("skills-1280x720")
    root.size = Vector2i(1920, 1080)
    await create_timer(0.2).timeout
    await _save("skills-1920x1080")
    Input.parse_input_event(_key(KEY_ESCAPE))
    await process_frame
    assert(not hud.get_node("Skills").visible, "Escape must close Skills")
    await _save("hud-1920x1080")
    assert(captured_level_up, "Missing authoritative level-up effect")
    print("CAPTURE progression complete")

func _key(code: Key) -> InputEventKey:
    var event = InputEventKey.new()
    event.keycode = code
    event.pressed = true
    return event
