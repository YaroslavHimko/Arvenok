extends SceneTree
const ROLE = "__ROLE__"
var session: Node
var sequence = 0
var deadline: int

func _initialize(): call_deferred("run")
func check(ok: bool, message: String):
    if not ok:
        push_error("EFFECTS CHECK FAILED: " + message)
        quit(1)
        assert(ok, message)
func world(): return JSON.parse_string(session.InspectWorld())
func events(): return JSON.parse_string(session.InspectCombatEvents())
func me():
    for entity in world().Entities:
        if entity.Id == world().LocalEntityId: return entity
    return null
func other():
    for entity in world().Entities:
        if entity.Kind == 0 and entity.Id != world().LocalEntityId: return entity
    return null
func command(kind: int, slot = 0, target = 0, destination = Vector2.ZERO, character = 0):
    sequence += 1
    session.rpc_id(1, "RequestCommand", sequence, world().Time, kind, target, slot, destination, character)
func tick():
    check(Time.get_ticks_msec() < deadline, "Timed out")
    await process_frame
func run():
    deadline = Time.get_ticks_msec() + 20000
    session = load("res://Features/Arena/Arena.tscn").instantiate()
    root.add_child(session)
    while me() == null or other() == null: await tick()
    command(5, 0, 0, Vector2.ZERO, 1 if ROLE == "a" else 2)
    command(0, 0, 0, Vector2(480,448) if ROLE == "a" else Vector2(520,448))
    while me().Protected or other().Protected or me().Action == 1 or other().Action == 1: await tick()
    if ROLE == "a":
        command(4, 1, int(other().Id))
    else:
        while not other().Casting or other().CastEndsAt - world().Time > 0.14: await tick()
        command(4, 4)
    var dodge = null
    while dodge == null:
        for entry in events():
            if int(entry.Kind) == 20: dodge = entry
        await tick()
    await create_timer(0.15).timeout
    var archer = other() if ROLE == "a" else me()
    check(archer.Hp == archer.MaxHp and archer.DodgeEndsAt == 0, "Dodge prevents HP loss and consumes its charge")
    check(archer.DefenseEndsAt > world().Time, "Ordinary mitigation remains")
    print("EFFECTS state=" + JSON.stringify(dodge))
    while archer.DefenseEndsAt > world().Time:
        await tick()
    print("EFFECTS complete")
