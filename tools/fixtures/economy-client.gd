extends SceneTree

const ROLE = "__ROLE__"
const MODE = "__MODE__"
const CAPTURE = __CAPTURE__
var session: Node
var hud: Node
var folder: String
var serial = 0
var sequence = 0
var deadline: int

func _initialize():
    folder = get_script().resource_path.get_base_dir()
    call_deferred("run")

func check(ok: bool, message: String):
    if not ok:
        push_error("ECONOMY CHECK FAILED: " + message)
        quit(1)
        assert(ok, message)

func state(): return JSON.parse_string(session.InspectBackpack())
func world(): return JSON.parse_string(session.InspectWorld())
func loot(): return JSON.parse_string(session.InspectLoot())
func reply(): return JSON.parse_string(session.InspectEconomyReply())
func point(entity): return Vector2(entity.Position.X, entity.Position.Y)
func me():
    for entity in world().Entities:
        if entity.Id == world().LocalEntityId: return entity
    return null
func find_item(id: String):
    for slot in state().Slots:
        if slot.Stack.ItemId == id: return slot
    return null
func command(kind: int, destination = Vector2.ZERO, target = 0):
    sequence += 1
    session.rpc_id(1, "RequestCommand", sequence, world().Time, kind, target, 0, destination, 0)
func make_request(operation: int, id: String, slot = 0):
    serial += 1
    return {"OperationId":("c7777777-1111-4444-8888-%012d" % serial) if ROLE == "a" else ("d7777777-1111-4444-8888-%012d" % serial),
        "ExpectedRevision": int(state().Revision), "Operation": operation, "ItemId": id, "Slot":slot}
func request(data: Dictionary):
    # JSON parse returns floating-point numbers in GDScript; restore exact integer wire fields.
    data.ExpectedRevision = int(data.ExpectedRevision)
    data.Operation = int(data.Operation)
    data.Slot = int(data.Slot)
    session.rpc_id(1, "MutateEconomy", JSON.stringify(data))
    var end = Time.get_ticks_msec() + 10000
    while reply() == null or reply().OperationId != data.OperationId:
        check(Time.get_ticks_msec() < end, "Missing economy response")
        await process_frame
    return reply()
func flag(name: String, content = "ready"):
    var file = FileAccess.open(folder.path_join(name), FileAccess.WRITE)
    file.store_string(content)
func wait_flag(name: String):
    while not FileAccess.file_exists(folder.path_join(name)):
        check(Time.get_ticks_msec() < deadline, "Coordination timeout: " + name)
        await process_frame
func key(code: Key):
    var event = InputEventKey.new()
    event.keycode = code
    event.pressed = true
    Input.parse_input_event(event)
func wait_revision(before: int):
    var end = Time.get_ticks_msec() + 10000
    while int(state().Revision) <= before or session.BackpackBusy():
        check(Time.get_ticks_msec() < end, "UI action did not commit: " + session.BackpackFeedback())
        await process_frame
    await process_frame
func save(label: String):
    if CAPTURE:
        await RenderingServer.frame_post_draw
        root.get_texture().get_image().save_png(folder.path_join(label + ".png"))
func click_button(node: Button):
    node.emit_signal("pressed")
    await process_frame
    await process_frame
func use_item(id: String):
    var item = find_item(id)
    check(item != null, "Missing UI item " + id)
    await click_button(hud.get_node("Backpack/Slots/Grid/Slot%d" % int(item.Index)))
    var before = int(state().Revision)
    await click_button(hud.get_node("Backpack/UseItem"))
    await wait_revision(before)

func mouse(point_at: Vector2, down: bool):
    var event = InputEventMouseButton.new()
    event.position = point_at
    event.global_position = point_at
    event.button_index = MOUSE_BUTTON_LEFT
    event.pressed = down
    event.button_mask = MOUSE_BUTTON_MASK_LEFT if down else 0
    root.push_input(event, true)
func motion(point_at: Vector2, held: bool):
    var event = InputEventMouseMotion.new()
    event.position = point_at
    event.global_position = point_at
    event.relative = Vector2(20, 0)
    event.button_mask = MOUSE_BUTTON_MASK_LEFT if held else 0
    root.push_input(event, true)
func drag_weapon():
    var source: Control = hud.get_node("Backpack/Slots/Grid/Slot0")
    var target: Control = hud.get_node("Backpack/Weapon")
    var start = source.get_global_transform_with_canvas() * (source.size / 2)
    var end = target.get_global_transform_with_canvas() * (target.size / 2)
    var before = int(state().Revision)
    motion(start, false)
    mouse(start, true)
    await process_frame
    motion(start + Vector2(20, 0), true)
    await process_frame
    check(root.gui_is_dragging(), "Native equipment drag started")
    motion(end, true)
    await process_frame
    mouse(end, false)
    motion(Vector2(990, 640), false)
    await wait_revision(before)

func run():
    deadline = Time.get_ticks_msec() + 180000
    session = load("res://Features/Arena/Arena.tscn").instantiate()
    root.add_child(session)
    while state() == null or me() == null:
        check(Time.get_ticks_msec() < deadline, "Character did not load")
        await process_frame
    hud = session.get_node("View/Inventory")
    if MODE != "exercise":
        if MODE == "restore" and FileAccess.file_exists(folder.path_join(ROLE + "-replay.json")):
            var replay = JSON.parse_string(FileAccess.get_file_as_string(folder.path_join(ROLE + "-replay.json")))
            var result = await request(replay)
            check(result.Accepted and result.Duplicate, "Pickup replay after reconnect/restart")
        print("ECONOMY state=" + session.InspectBackpack())
        print("ECONOMY complete")
        return
    key(KEY_E)
    await create_timer(0.25).timeout
    check(hud.get_node("Backpack").visible, "E opens equipment/backpack")
    if ROLE == "a":
        var hp = me().Hp
        await drag_weapon()
        await use_item("equipment.padded_guard_coat")
        await create_timer(0.25).timeout
        check(me().MaxHp == hp + 5 and me().Hp == hp, "Armor adds exactly 5 without healing")
        var body_id = state().Equipment.Body.Id
        await click_button(hud.get_node("Backpack/Body"))
        var unequip_revision = int(state().Revision)
        await click_button(hud.get_node("Backpack/UseItem"))
        await wait_revision(unequip_revision)
        check(state().Equipment.Body == null and find_item("equipment.padded_guard_coat").Stack.Id == body_id, "Native unequip preserves the instance")
        await use_item("equipment.padded_guard_coat")
        var scroll = find_item("recipe.ironbark_sword")
        await click_button(hud.get_node("Backpack/Slots/Grid/Slot%d" % int(scroll.Index)))
        check(hud.get_node("Backpack/Ingredients").visible, "Recipe preview is visible")
        var rows = hud.get_node("Backpack/Ingredients").get_children()
        check(rows[0].text.contains("12 / 12") and rows[1].text.contains("7 / 7"), "Preview uses server ingredient totals")
        await save("craft-preview-1280x720")
        var before = int(state().Revision)
        await click_button(hud.get_node("Backpack/UseItem"))
        await wait_revision(before)
        var craft = {"OperationId": reply().OperationId, "ExpectedRevision":before, "Operation":2,"ItemId":scroll.Stack.Id,"Slot":0}
        var crafted = reply().CreatedItemId
        # Clear the locally observed reply so the replay must be seen in server log; state remains unchanged.
        session.rpc_id(1, "MutateEconomy", JSON.stringify(craft))
        await create_timer(0.4).timeout
        check(reply().Duplicate and int(state().Revision) == before + 1, "Craft replay cannot duplicate output")
        check(find_item("equipment.ironbark_sword").Stack.Id == crafted, "Craft result ID matches the output")
        await use_item("equipment.ironbark_sword")
        check(state().Equipment.Weapon.Id == crafted and find_item("equipment.rustleaf_sword") != null, "Swap preserves old weapon")
        var missing = await request(make_request(2, find_item("recipe.ironbark_sword").Stack.Id))
        check(not missing.Accepted and missing.Message.contains("Missing ingredients"), "Missing materials consume no scroll")
        await save("equipment-1280x720")
        if CAPTURE:
            root.size = Vector2i(1920, 1080)
            await create_timer(0.2).timeout
            await save("equipment-1920x1080")
        print("ECONOMY UI passed: preview, craft, replay, swap, bonus and missing ingredients")
    else:
        var wrong = await request(make_request(0, find_item("equipment.rustleaf_sword").Stack.Id))
        check(not wrong.Accepted and wrong.Message.contains("Wrong class"), "Class restriction")
        var low = await request(make_request(0, find_item("equipment.runestone_staff").Stack.Id))
        check(not low.Accepted and low.Message.contains("level 5"), "Level restriction")
        check(state().Slots.size() == 24, "Full backpack fixture")
    key(KEY_ESCAPE)
    await process_frame
    flag(ROLE + "-ui.txt")
    await wait_flag(("b" if ROLE == "a" else "a") + "-ui.txt")
    var next_action = 0.0
    var chosen = null
    while chosen == null:
        check(Time.get_ticks_msec() < deadline, "No natural loot arrived")
        var w = world()
        for drop in loot():
            if drop.Item.ItemId == "material.slime_gel": chosen = drop; break
        if chosen != null: break
        if w.Time >= next_action:
            next_action = w.Time + 0.5
            var slime = null
            var players = []
            for entity in w.Entities:
                if entity.Kind == 1 and entity.Species == 0: slime = entity
                if entity.Kind == 0: players.append(entity)
            if slime != null:
                if point(me()).distance_to(point(slime)) > 45: command(0, point(slime))
                elif ROLE == "a" and slime.Hp > 0 and not me().AutoAttacking and players.size() == 2 and players.all(func(p): return point(p).distance_to(point(slime)) < 180): command(2, Vector2.ZERO, slime.Id)
        await process_frame
    command(3)
    print("ECONOMY drop=" + JSON.stringify(chosen))
    if ROLE == "a":
        var locked = await request(make_request(1, state().Equipment.Weapon.Id))
        check(not locked.Accepted and locked.Message.contains("combat"), "Equipment locks after dealing damage")
    command(0, point(chosen))
    while point(me()).distance_to(point(chosen)) > 15:
        check(Time.get_ticks_msec() < deadline, "Could not reach loot")
        await process_frame
    command(3)
    await save("ground-loot-1920x1080")
    if ROLE == "b":
        var denied = await request(make_request(3, chosen.Id))
        check(not denied.Accepted and denied.Message.contains("protected"), "Other client cannot bypass owner protection")
        while world().Time < chosen.PublicAt + 0.25: await process_frame
        var full = await request(make_request(3, chosen.Id))
        check(not full.Accepted and full.Message.contains("Inventory Full"), "Full bag leaves public loot in world")
        check(loot().any(func(d): return d.Id == chosen.Id), "Failed pickup did not delete drop")
        var equip = await request(make_request(0, find_item("equipment.willow_staff").Stack.Id))
        check(equip.Accepted and state().Slots.size() == 23, "Equipment frees a backpack slot")
        flag("b-ready.txt")
        await wait_flag("race-time.txt")
    else:
        await wait_flag("b-ready.txt")
        flag("race-time.txt", str(world().Time + 2))
    var race_time = float(FileAccess.get_file_as_string(folder.path_join("race-time.txt")))
    while world().Time < race_time: await process_frame
    var pickup = make_request(3, chosen.Id)
    var result
    if ROLE == "a":
        var previous = reply().OperationId
        key(KEY_F)
        var end = Time.get_ticks_msec() + 10000
        while reply().OperationId == previous:
            check(Time.get_ticks_msec() < end, "F must request nearby loot")
            await process_frame
        result = reply()
        pickup.OperationId = result.OperationId
    else:
        result = await request(pickup)
    if result.Accepted: flag(ROLE + "-replay.json", JSON.stringify(pickup))
    print("ECONOMY race=" + str(result.Accepted))
    flag(ROLE + "-race.txt")
    await wait_flag(("b" if ROLE == "a" else "a") + "-race.txt")
    await create_timer(0.5).timeout
    check(not loot().any(func(d): return d.Id == chosen.Id), "Both clients observe removal")
    print("ECONOMY removed=" + chosen.Id)
    print("ECONOMY state=" + session.InspectBackpack())
    print("ECONOMY complete")
