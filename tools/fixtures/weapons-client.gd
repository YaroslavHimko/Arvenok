extends SceneTree
const ROLE = "__ROLE__"
const EXERCISE = __EXERCISE__
var session: Node
var hud: Node
var sequence = 0
var deadline: int
func _initialize(): call_deferred("run")
func check(ok: bool, message: String):
    if not ok:
        push_error("WEAPONS CHECK FAILED: " + message)
        quit(1)
        assert(ok, message)
func state(): return JSON.parse_string(session.InspectBackpack())
func world(): return JSON.parse_string(session.InspectWorld())
func me():
    for entity in world().Entities:
        if entity.Id == world().LocalEntityId: return entity
    return null
func other():
    for entity in world().Entities:
        if entity.Kind == 0 and entity.Id != world().LocalEntityId: return entity
    return null
func tick():
    check(Time.get_ticks_msec() < deadline, "Timed out")
    await process_frame
func command(kind: int, slot = 0, target = 0, destination = Vector2.ZERO):
    sequence += 1
    session.rpc_id(1, "RequestCommand", sequence, world().Time, kind, target, slot, destination, 0)
func find_item(id: String):
    for slot in state().Slots:
        if slot.Stack.ItemId == id: return slot
    return null
func use_item(id: String):
    var slot = find_item(id)
    check(slot != null, "Missing item " + id)
    hud.get_node("Backpack/Slots/Grid/Slot%d" % int(slot.Index)).emit_signal("pressed")
    await process_frame
    await process_frame
    var revision = int(state().Revision)
    hud.get_node("Backpack/UseItem").emit_signal("pressed")
    while int(state().Revision) <= revision or session.BackpackBusy(): await tick()
    await process_frame
    await process_frame
func key(code: Key):
    var event = InputEventKey.new()
    event.keycode = code
    event.pressed = true
    Input.parse_input_event(event)
    await process_frame
    event.pressed = false
    Input.parse_input_event(event)
    await process_frame
func run():
    deadline = Time.get_ticks_msec() + 30000
    session = load("res://Features/Arena/Arena.tscn").instantiate()
    root.add_child(session)
    while state() == null or me() == null: await tick()
    hud = session.get_node("View/Inventory")
    if EXERCISE:
        await key(KEY_I)
        if ROLE == "a":
            await use_item("equipment.rustleaf_sword")
            await use_item("equipment.training_shield")
            check(state().Equipment.OffHand != null, "Shield equipped through native UI")
            await use_item("equipment.weapon.weathered_greatsword")
            check(state().Equipment.OffHand == null and find_item("equipment.training_shield") != null and find_item("equipment.rustleaf_sword") != null, "Two-handed swap returns both owned items")
            check(hud.get_node("Backpack/OffHand").text.contains("Reserved"), "OffHand lock is visible")
        else:
            await use_item("equipment.weapon.hunters_dagger")
        await key(KEY_I)
        while other() == null or other().WeaponType != (5 if ROLE == "a" else 4): await tick()
        command(0, 0, 0, Vector2(480,448) if ROLE == "a" else Vector2(520,448))
        while me().Protected or other().Protected or me().Action == 1 or other().Action == 1: await tick()
        if ROLE == "b":
            await key(KEY_1)
            check(session.get_node("View/Hotbars/Bar1/Slot0").tooltip_text.contains("Requires a bow"), "Dagger disables bow skill without losing its assignment")
            command(4, 1, int(other().Id))
            var rejected = false
            while not rejected:
                for entry in JSON.parse_string(session.InspectCombatEvents()):
                    if entry.Detail.contains("Requires a bow"): rejected = true
                await tick()
            command(2, 0, int(other().Id))
        var hit = null
        while hit == null:
            for entry in JSON.parse_string(session.InspectCombatEvents()):
                if int(entry.Kind) == 11: hit = entry
            await tick()
        if ROLE == "b": command(3)
        check(int(hit.Damage) == 9, "Dagger applies its bonus once")
        print("WEAPONS UI passed: atomic hands, snapshot weapon types, melee damage and bow restriction")
    print("WEAPONS state=" + session.InspectBackpack())
    print("WEAPONS complete")
