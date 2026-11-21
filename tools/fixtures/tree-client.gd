extends SceneTree
const ROLE = "__ROLE__"
const EXERCISE = __EXERCISE__
const CAPTURE = __CAPTURE__
var session: Node
var deadline: int
func _initialize(): call_deferred("run")
func check(ok: bool, message: String):
    if not ok:
        push_error("TREE CHECK FAILED: " + message)
        quit(1)
        assert(ok, message)
func progress(): return JSON.parse_string(session.InspectProgress())
func state(): return JSON.parse_string(session.InspectBackpack())
func world(): return JSON.parse_string(session.InspectWorld())
func me():
    for entity in world().Entities:
        if entity.Id == world().LocalEntityId: return entity
    return null
func tick():
    check(Time.get_ticks_msec() < deadline, "Timed out")
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
    while state() == null or progress() == null or me() == null: await tick()
    if EXERCISE:
        await key(KEY_K)
        session.get_node("View/Progression/Skills/OpenTree").emit_signal("pressed")
        await process_frame
        await process_frame
        var tree = session.get_node("View/Progression/SkillTree")
        check(tree.visible and not tree.get_node("Learn").disabled, "Eligible passive is offered")
        var points = int(progress().SkillPoints)
        tree.get_node("Learn").emit_signal("pressed")
        while int(progress().Tree[0].Rank) != 1 or session.BackpackBusy(): await tick()
        check(int(progress().SkillPoints) == points - 1, "Exactly one point was spent")
        await process_frame
        tree.get_node("Node1").emit_signal("pressed")
        await process_frame
        await process_frame
        check(tree.get_node("Learn").disabled and tree.get_node("Learn").tooltip_text.contains("scroll"), "Active node requires its owned spell scroll")
        if CAPTURE:
            await RenderingServer.frame_post_draw
            root.get_texture().get_image().save_png(get_script().resource_path.get_base_dir().path_join("tree.png"))
        await key(KEY_K)
        var revision = int(progress().Revision)
        await key(KEY_4)
        while me().Cooldowns.Defense <= world().Time or int(progress().Revision) <= revision: await tick()
        print("TREE UI passed: native branch learning, point conservation, active scroll gate, server cooldown")
    if int(progress().Tree[0].Rank) > 0:
        check(me().Cooldowns.Defense > world().Time, "Remaining defensive cooldown survives reconnect/restart")
    print("TREE state=" + JSON.stringify({"Progress":progress(), "Inventory":state()}))
    print("TREE complete")
