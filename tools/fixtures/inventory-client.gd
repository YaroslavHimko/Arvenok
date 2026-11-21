extends SceneTree

const EXERCISE = __EXERCISE__
const CAPTURE = __CAPTURE__
const FOREIGN_STACK = "__FOREIGN_STACK__"
var session: Node
var hud: Node
var grid: Control
var output_dir: String

func _initialize():
    output_dir = get_script().resource_path.get_base_dir()
    call_deferred("_run")

func state():
    return JSON.parse_string(session.InspectBackpack())

func entry(index: int):
    for slot in state().Slots:
        if int(slot.Index) == index:
            return slot.Stack
    return null

func _check(ok: bool, message: String):
    if not ok:
        push_error("INVENTORY CHECK FAILED: " + message)
        quit(1)
        assert(ok, message)

func _wait_revision(revision: int):
    var deadline = Time.get_ticks_msec() + 10000
    while int(state().Revision) <= revision or session.BackpackBusy():
        _check(Time.get_ticks_msec() < deadline, "Timed out awaiting inventory revision")
        await process_frame
    await process_frame
    for slot in state().Slots:
        var displayed = grid.get_node("Slot%d/Quantity" % int(slot.Index)).text
        _check(displayed == "" or displayed == str(int(slot.Stack.Quantity)), "UI quantity disagrees with server")

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

func _key(code: Key, down = true):
    var event = InputEventKey.new()
    event.keycode = code
    event.pressed = down
    Input.parse_input_event(event)

func _drag(source: int, destination: int, split = false):
    var source_node: Control = grid.get_node("Slot%d" % source)
    var target_node: Control = grid.get_node("Slot%d" % destination)
    var start = source_node.get_global_transform_with_canvas() * (source_node.size / 2)
    var target = target_node.get_global_transform_with_canvas() * (target_node.size / 2)
    if split:
        _key(KEY_SHIFT)
    _motion(start, false)
    _mouse(start, true)
    await process_frame
    _motion(start + Vector2(20, 0), true)
    await process_frame
    _check(root.gui_is_dragging(), "Godot must start a native Control drag")
    _motion(target, true)
    await process_frame
    _mouse(target, false)
    await process_frame
    if split:
        _key(KEY_SHIFT, false)

func _save(label: String):
    if not CAPTURE:
        return
    await RenderingServer.frame_post_draw
    root.get_texture().get_image().save_png(output_dir.path_join(label + ".png"))
    print("CAPTURE " + label)

func _raw(request: Dictionary, expected: String):
    session.rpc_id(1, "MutateInventory", JSON.stringify(request))
    var deadline = Time.get_ticks_msec() + 10000
    while not session.BackpackFeedback().contains(expected):
        _check(Time.get_ticks_msec() < deadline, "Missing inventory response: " + expected)
        await process_frame

func _run():
    session = load("res://Features/Arena/Arena.tscn").instantiate()
    root.add_child(session)
    var deadline = Time.get_ticks_msec() + 15000
    while state() == null:
        _check(Time.get_ticks_msec() < deadline, "Character inventory did not load")
        await process_frame
    hud = session.get_node("View/Inventory")
    grid = hud.get_node("Backpack/Slots/Grid")
    if EXERCISE:
        _check(state().Capacity == 24 and state().Slots.size() == 11, "Development kit must occupy eleven slots")
        var total = 0
        for slot in state().Slots:
            total += int(slot.Stack.Quantity)
        var sword = entry(0).Id
        var shield = entry(1).Id
        _key(KEY_I)
        await create_timer(0.3).timeout
        _check(hud.get_node("Backpack").visible, "I must open Backpack")
        _check(grid.get_child_count() == int(state().Capacity), "Capacity must drive grid size")
        await _save("inventory-1280x720")
        var revision = int(state().Revision)
        await _drag(0, 23)
        await _wait_revision(revision)
        _check(entry(0) == null and entry(23).Id == sword, "Move preserves instance ID")
        revision = int(state().Revision)
        await _drag(23, 1)
        await _wait_revision(revision)
        _check(entry(1).Id == sword and entry(23).Id == shield, "Swap preserves both IDs")
        revision = int(state().Revision)
        await _drag(2, 0, true)
        _check(hud.get_node("Backpack/SplitQuantity").visible, "Shift-drag opens quantity choice")
        hud.get_node("Backpack/SplitQuantity/Amount").value = 100
        await _save("inventory-split")
        hud.get_node("Backpack/SplitQuantity/Confirm").emit_signal("pressed")
        await _wait_revision(revision)
        _check(entry(0).Quantity == 100 and entry(2).Quantity == 899, "Split preserves quantity")
        revision = int(state().Revision)
        await _drag(2, 3)
        await _wait_revision(revision)
        _check(entry(2).Quantity == 101 and entry(3).Quantity == 999, "Partial merge respects maximum")
        revision = int(state().Revision)
        await _drag(0, 2)
        await _wait_revision(revision)
        _check(entry(0) == null and entry(2).Quantity == 201, "Full merge removes only source stack")
        revision = int(state().Revision)
        hud.get_node("Backpack/Sort").emit_signal("pressed")
        await _wait_revision(revision)
        _check(entry(0).ItemId.begins_with("equipment."), "Equipment sorts first")
        var request = {"OperationId":"e2049d63-2bf1-4c84-b452-f0a660054d67", "ExpectedRevision":int(state().Revision), "Operation":1,
            "Source":0, "Destination":23, "SourceId":entry(0).Id, "DestinationId":"00000000-0000-0000-0000-000000000000", "Quantity":0}
        revision = int(state().Revision)
        session.rpc_id(1, "MutateInventory", JSON.stringify(request))
        await _wait_revision(revision)
        await _raw(request, "Already applied")
        _check(int(state().Revision) == revision + 1, "Duplicate request must not mutate")
        request.OperationId = "e2049d63-2bf1-4c84-b452-f0a660054d68"
        await _raw(request, "Inventory changed")
        request.OperationId = "e2049d63-2bf1-4c84-b452-f0a660054d69"
        request.ExpectedRevision = int(state().Revision)
        request.Source = 23
        request.Destination = 0
        request.SourceId = FOREIGN_STACK
        await _raw(request, "slot identity changed")
        _check(int(state().Revision) == revision + 1, "Foreign stack identity must not mutate ownership")
        var after_total = 0
        for slot in state().Slots:
            after_total += int(slot.Stack.Quantity)
        _check(after_total == total, "All UI operations conserve total quantity")
        if CAPTURE:
            root.size = Vector2i(1920, 1080)
            await create_timer(0.2).timeout
            await _save("inventory-1920x1080")
        _key(KEY_K)
        await process_frame
        _check(not hud.get_node("Backpack").visible and session.get_node("View/Progression/Skills").visible, "Skills and Backpack must not overlap")
        _key(KEY_I)
        await process_frame
        _check(hud.get_node("Backpack").visible and not session.get_node("View/Progression/Skills").visible, "Backpack closes Skills")
        _key(KEY_ESCAPE)
        await process_frame
        _check(not hud.get_node("Backpack").visible, "Escape closes Backpack")
        print("INVENTORY UI passed: native drag, swap, split, merge, sort, quantity, keyboard, duplicate, stale and foreign identity")
    print("INVENTORY state=" + session.InspectBackpack())
    print("INVENTORY complete")
