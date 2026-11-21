extends SceneTree
const ROLE = "__ROLE__"
const MODE = "__MODE__"
const SCENARIO = "__SCENARIO__"
const CAPTURE = __CAPTURE__
var session: Node
var hud: Node
var folder: String
var deadline: int
var serial = 0
func _initialize():
    folder = get_script().resource_path.get_base_dir()
    call_deferred("run")
func check(ok: bool, message: String):
    if not ok:
        push_error("SKILLS CHECK FAILED: " + message)
        quit(1)
        assert(ok, message)
func state(): return JSON.parse_string(session.InspectBackpack())
func progress(): return JSON.parse_string(session.InspectProgress())
func world(): return JSON.parse_string(session.InspectWorld())
func events(): return JSON.parse_string(session.InspectCombatEvents())
func reply(): return JSON.parse_string(session.InspectEconomyReply())
func me():
    for e in world().Entities:
        if e.Id == world().LocalEntityId: return e
    return null
func other():
    for e in world().Entities:
        if e.Kind == 0 and e.Id != world().LocalEntityId: return e
    return null
func stamp():
    var result = 0
    for e in events(): result = maxi(result,int(e.Sequence))
    return result
func event(kind: int, after: int, actor = 0):
    for e in events():
        if int(e.Kind) == kind and int(e.Sequence) > after and (actor == 0 or e.ActorId == actor): return e
    return null
func tick():
    check(Time.get_ticks_msec() < deadline, "Timed out in " + SCENARIO + " / " + ROLE)
    await process_frame
func wait_event(kind: int, after: int, actor = 0):
    while event(kind,after,actor) == null: await tick()
    await process_frame
    return event(kind,after,actor)
func flag(name: String, value = "ready"):
    var f = FileAccess.open(folder.path_join(name),FileAccess.WRITE)
    f.store_string(value)
func wait_flag(name: String):
    while not FileAccess.file_exists(folder.path_join(name)): await tick()
func command(kind: int, slot = 0, target = 0, destination = Vector2.ZERO):
    # Send is the same unfiltered intent entry point used by native UI and keeps its sequence coherent.
    session.Send(kind,int(target),slot,destination,0)
func key(code: Key):
    var e = InputEventKey.new()
    e.keycode=code; e.pressed=true; Input.parse_input_event(e)
    await process_frame
    e.pressed=false; Input.parse_input_event(e)
    await process_frame
func click(node: Button):
    node.emit_signal("pressed")
    await process_frame
    await process_frame
func find_item(id: String):
    for s in state().Slots:
        if s.Stack.ItemId == id: return s
    return null
func request(node, item = "00000000-0000-0000-0000-000000000000"):
    serial += 1
    return {"OperationId":("a2200000-1111-4444-8888-%012d" % serial) if ROLE == "a" else ("b2200000-1111-4444-8888-%012d" % serial),
        "ExpectedRevision":int(state().Revision), "Operation":4, "ItemId":item, "Slot":0,
        "NodeId":node.Definition.Id, "ProgressionRevision":int(progress().Revision)}
func raw(data: Dictionary, duplicate = false):
    data.ExpectedRevision=int(data.ExpectedRevision); data.Operation=int(data.Operation); data.Slot=int(data.Slot); data.ProgressionRevision=int(data.ProgressionRevision)
    session.rpc_id(1,"MutateEconomy",JSON.stringify(data))
    while reply() == null or reply().OperationId != data.OperationId or (duplicate and not reply().Duplicate): await tick()
    return reply()
func use_item(id: String):
    var item=find_item(id); check(item != null,"Missing item " + id)
    await click(hud.get_node("Backpack/Slots/Grid/Slot%d" % int(item.Index)))
    var revision=int(state().Revision)
    await click(hud.get_node("Backpack/UseItem"))
    while int(state().Revision) <= revision or session.BackpackBusy(): await tick()
    await process_frame
    await process_frame
func equip(id: String):
    await key(KEY_I); await use_item(id); await key(KEY_I)
func wait_ready():
    while me().Casting or me().RecoveryEndsAt > world().Time: await tick()
func cast(slot: int):
    await wait_ready(); command(4,slot,int(other().Id))
func timed_defense(slot: int, seconds: float):
    while not other().Casting or other().CastEndsAt - world().Time > seconds: await tick()
    await cast(slot)
func wait_cooldown(slot: String):
    while me().Cooldowns[slot] > world().Time: await tick()
func capture(name: String):
    if CAPTURE:
        await RenderingServer.frame_post_draw
        root.get_texture().get_image().save_png(folder.path_join(name + ".png"))
func drag(source: Control, target: Control):
    var start=source.get_global_transform_with_canvas()*(source.size/2)
    var finish=target.get_global_transform_with_canvas()*(target.size/2)
    var motion=InputEventMouseMotion.new(); motion.position=start; root.push_input(motion,true)
    var button=InputEventMouseButton.new(); button.position=start; button.button_index=MOUSE_BUTTON_LEFT; button.pressed=true; button.button_mask=MOUSE_BUTTON_MASK_LEFT; root.push_input(button,true)
    await process_frame
    motion=InputEventMouseMotion.new(); motion.position=start+Vector2(20,0); motion.relative=Vector2(20,0); motion.button_mask=MOUSE_BUTTON_MASK_LEFT; root.push_input(motion,true)
    await process_frame
    check(root.gui_is_dragging(),"Native learned-skill drag")
    motion=InputEventMouseMotion.new(); motion.position=finish; motion.relative=finish-start; motion.button_mask=MOUSE_BUTTON_MASK_LEFT; root.push_input(motion,true)
    await process_frame
    button=InputEventMouseButton.new(); button.position=finish; button.button_index=MOUSE_BUTTON_LEFT; button.pressed=false; root.push_input(button,true)
    await process_frame
    await process_frame
func prepare():
    var cursor=stamp(); command(4,5)
    var rejection=await wait_event(16,cursor)
    check(rejection.Detail.contains("not learned"),"Unlearned branch RPC rejected")
    if ROLE == "b":
        var before=JSON.stringify(state()); var r=await raw(request(progress().Tree[0]))
        check(not r.Accepted and r.Message.contains("points") and JSON.stringify(state()) == before,"Zero-point learning changes nothing")
        await equip("equipment.elm_shortbow" if SCENARIO == "warrior" else "equipment.rustleaf_sword" if SCENARIO == "mage" else "equipment.willow_staff")
        flag("prepared-b"); return
    var wrong = request(progress().Tree[0]); wrong.NodeId="mage.ember.flame_lance" if SCENARIO != "mage" else "warrior.guardian.perfect_block"
    wrong.ItemId=find_item("scroll.skill.mage.flame_lance" if SCENARIO != "mage" else "scroll.skill.warrior.perfect_block").Stack.Id
    var before=JSON.stringify(state()); var rejected=await raw(wrong)
    check(not rejected.Accepted and JSON.stringify(state()) == before,"Wrong-class owned scroll changes nothing")
    await key(KEY_K); await click(session.get_node("View/Progression/Skills/OpenTree"))
    var tree=session.get_node("View/Progression/SkillTree")
    for index in [0,2]:
        await click(tree.get_node("Node%d" % index)); await click(tree.get_node("Learn"))
        while progress().Tree[index].Rank != 1 or session.BackpackBusy(): await tick()
    await key(KEY_K)
    for index in [1,3]:
        var node=progress().Tree[index]; var scroll=find_item(node.Definition.ScrollItemId).Stack.Id
        var replay=request(node,scroll)
        await key(KEY_I); await use_item(node.Definition.ScrollItemId); await key(KEY_I)
        replay.OperationId=reply().OperationId
        var learned_points=progress().SkillPoints
        check((await raw(replay,true)).Duplicate,"Learning replay acknowledged exactly once")
        var spare=find_item(node.Definition.ScrollItemId)
        check(spare != null,"Duplicate owned scroll retained")
        rejected=await raw(request(node,spare.Stack.Id))
        check(not rejected.Accepted and progress().SkillPoints == learned_points and find_item(node.Definition.ScrollItemId) != null,"Already learned scroll not consumed")
        if index == 3: flag("last-learn.json",JSON.stringify(replay))
    check(progress().SkillPoints == 3 and progress().BranchSkills.size() == 2,"Two nonexclusive branches preserve point budget")
    await key(KEY_K); await click(session.get_node("View/Progression/Skills/OpenTree"))
    await drag(tree.get_node("Icon1"),session.get_node("View/Hotbars/Bar1/Slot7"))
    check(JSON.parse_string(session.get_node("View/Hotbars").InspectLayout()).Bars[0].Slots[7].ActionId == progress().Tree[1].Definition.SkillId,"Learned active assigned without replacing original skills")
    await capture("learned-tree"); await key(KEY_K)
    await equip("equipment.weapon.weathered_greatsword" if SCENARIO == "warrior" else "equipment.willow_staff" if SCENARIO == "mage" else "equipment.elm_shortbow")
    flag("prepared-a")
func warrior():
    var cursor=stamp()
    if ROLE == "b":
        await cast(1); await wait_flag("parry-done"); await create_timer(0.4).timeout
        await cast(3); await wait_flag("blood-done"); await wait_flag("perfect-ready"); await cast(1)
        await wait_event(22,cursor); flag("fight-b"); return
    await timed_defense(4,0.18); await wait_event(21,cursor)
    check(me().Hp == me().MaxHp,"Frontal parry prevents physical damage")
    flag("parry-done")
    await wait_event(11,cursor,int(other().Id)); await cast(6)
    var healed=await wait_event(24,cursor); check(int(healed.Damage)==1,"PvP lifesteal uses actual direct damage")
    flag("blood-done"); await create_timer(5.2).timeout
    await equip("equipment.rustleaf_sword"); await equip("equipment.training_shield")
    await wait_cooldown("Defense"); flag("perfect-ready")
    await timed_defense(5,0.25); await wait_event(22,cursor)
    await create_timer(0.15).timeout
    check(me().DefenseEndsAt == 0,"Consumed Perfect Block aura ends")
    cursor=stamp(); await cast(4); var r=await wait_event(16,cursor)
    check(r.Detail.contains("cooldown"),"Perfect Block shares cooldown with Guard")
    flag("fight-a")
func mage():
    var cursor=stamp()
    if ROLE == "b":
        while not other().Casting: await tick()
        await cast(3); await wait_flag("shell-ready"); command(2,0,int(other().Id))
        var count=0
        while count < 2:
            count=0
            for e in events():
                if int(e.Kind)==23 and int(e.Sequence)>cursor: count+=1
            await tick()
        command(3); flag("shell-spent"); await wait_flag("mage-done"); flag("fight-b"); return
    # Use the custom hotbar assignment for Flame Lance, with the shared client intent sequence.
    await key(KEY_8); await wait_event(9,cursor)
    check(me().Cooldowns.BranchOne > world().Time,"Interrupted advanced cast retains cooldown")
    await create_timer(0.3).timeout; await cast(6); flag("shell-ready")
    await wait_flag("shell-spent"); await create_timer(0.15).timeout
    var absorbed=0
    for e in events():
        if int(e.Kind)==23 and int(e.Sequence)>cursor: absorbed+=1
    check(absorbed==2 and me().ShellRemaining==0 and me().DefenseEndsAt==0,"Shell absorbs across two attacks and then expires")
    cursor=stamp(); await cast(4); check((await wait_event(16,cursor)).Detail.contains("cooldown"),"Shell shares Ward cooldown")
    await wait_cooldown("BranchOne"); cursor=stamp(); await key(KEY_8)
    var damage=await wait_event(11,cursor,int(me().Id)); check(int(damage.Damage)==27,"Flame Lance applies server damage and staff once")
    await cast(1); damage=await wait_event(11,int(damage.Sequence),int(me().Id)); check(int(damage.Damage)==24,"Embers adds PvP bonus only to Firebolt")
    flag("mage-done"); flag("fight-a")
func archer():
    var cursor=stamp()
    if ROLE == "b":
        await cast(1); await wait_flag("cut-ready")
        while not other().Casting or int(other().ActiveAbility)!=6: await tick()
        await cast(3); await wait_flag("archer-done"); flag("fight-b"); return
    await timed_defense(4,0.14); await wait_event(20,cursor)
    await wait_ready(); await key(KEY_8)
    var damage=await wait_event(11,cursor,int(me().Id)); check(int(damage.Damage)==25,"Piercing Shot damage")
    await cast(1); damage=await wait_event(11,int(damage.Sequence),int(me().Id)); check(int(damage.Damage)==23,"Keen Eye Power Shot damage")
    await create_timer(5.2).timeout; await equip("equipment.weapon.hunters_dagger")
    cursor=stamp(); await cast(5); check((await wait_event(16,cursor)).Detail.contains("Requires bow"),"Learned bow skill disabled with dagger")
    await cast(2); await create_timer(0.1).timeout
    check(me().Cooldowns.Mobility - world().Time > 14.5 and me().Cooldowns.Mobility - world().Time < 15.1,"Light Footwork changes authoritative Backstep cooldown")
    await create_timer(0.5).timeout; command(0,0,0,Vector2(500,448))
    while me().Action==1 or Vector2(me().Position.X,me().Position.Y).distance_to(Vector2(500,448)) > 1: await tick()
    cursor=stamp(); flag("cut-ready"); await cast(6)
    await wait_event(20,cursor); damage=await wait_event(11,cursor,int(me().Id))
    check(int(damage.Damage)==9,"Evasive Cut damages only after its authoritative side-step")
    await create_timer(5.2).timeout
    await equip("equipment.elm_shortbow")
    await wait_cooldown("BranchOne"); cursor=stamp(); await key(KEY_8)
    damage=await wait_event(11,cursor,int(me().Id))
    check(int(damage.Damage)==25,"Switching back to the bow restores the assigned Piercing Shot")
    flag("archer-done"); flag("fight-a")
func run():
    deadline=Time.get_ticks_msec()+110000
    session=load("res://Features/Arena/Arena.tscn").instantiate(); root.add_child(session)
    while state()==null or progress()==null or me()==null: await tick()
    hud=session.get_node("View/Inventory")
    if MODE == "exercise":
        while other()==null: await tick()
        await prepare(); await wait_flag("prepared-a"); await wait_flag("prepared-b")
        command(0,0,0,Vector2(478,448) if ROLE=="a" else Vector2(520,448))
        while me().Protected or me().Action==1: await tick()
        if ROLE=="a":
            command(0,0,0,Vector2(480,448))
            while Vector2(me().Position.X,me().Position.Y).distance_to(Vector2(480,448)) > 0.1: await tick()
        command(1,0,int(other().Id)); flag("position-"+ROLE)
        await wait_flag("position-a"); await wait_flag("position-b"); await create_timer(0.15).timeout
        if SCENARIO=="warrior": await warrior()
        elif SCENARIO=="mage": await mage()
        else: await archer()
        await wait_flag("fight-a"); await wait_flag("fight-b"); await create_timer(0.5).timeout
        var outcomes=[]
        for e in events():
            if int(e.Kind) in [9,11,20,21,22,23,24]: outcomes.append(e)
        print("SKILLS events="+JSON.stringify(outcomes))
        print("SKILLS hp="+JSON.stringify(world().Entities.filter(func(e): return e.Kind==0).map(func(e): return [e.Id,e.Hp,e.MaxHp])))
        if ROLE=="a":
            var saved=FileAccess.open(folder.path_join("last-learn.json"),FileAccess.READ)
            check((await raw(JSON.parse_string(saved.get_as_text()),true)).Duplicate,"Post-combat learn replay remains idempotent")
        print("SKILLS UI passed "+SCENARIO)
    elif MODE=="restore" and ROLE=="a":
        var saved=FileAccess.open(folder.path_join("last-learn.json"),FileAccess.READ)
        check((await raw(JSON.parse_string(saved.get_as_text()),true)).Duplicate,"Reconnect/restart receipt survives")
        var layout=JSON.parse_string(session.get_node("View/Hotbars").InspectLayout())
        check(layout.Bars[0].Slots[7].ActionId==progress().Tree[1].Definition.SkillId,"Learned hotbar restores per character")
    print("SKILLS state="+JSON.stringify({"Progress":progress(),"Inventory":state()}))
    print("SKILLS complete")
