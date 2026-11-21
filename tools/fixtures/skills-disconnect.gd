extends SceneTree

const ROLE = "__ROLE__"
const MODE = "__MODE__"
var session: Node
var folder: String

func _initialize(): call_deferred("run")

func check(ok: bool, message: String):
    if not ok:
        push_error("DISCONNECT CHECK FAILED: " + message)
        quit(1)
        assert(ok, message)

func run():
    folder = get_script().resource_path.get_base_dir()
    session = load("res://Features/Arena/Arena.tscn").instantiate()
    root.add_child(session)
    var deadline = Time.get_ticks_msec() + 20000
    while JSON.parse_string(session.InspectProgress()) == null or JSON.parse_string(session.InspectBackpack()) == null:
        check(Time.get_ticks_msec() < deadline, "Character did not load")
        await process_frame
    var backpack = JSON.parse_string(session.InspectBackpack())
    var progress = JSON.parse_string(session.InspectProgress())
    var path = folder.path_join("operation-" + ROLE + ".json")
    if MODE == "create":
        print("CHARACTER " + backpack.CharacterId)
    elif MODE == "submit":
        var request = {"OperationId":"c0000000-1234-4000-8000-000000000001" if ROLE == "a" else "d0000000-1234-4000-8000-000000000002",
            "ExpectedRevision":int(backpack.Revision), "Operation":4 if ROLE == "a" else 0, "ItemId":backpack.Slots[0].Stack.Id, "Slot":0}
        if ROLE == "a":
            request.NodeId = "mage.ember.flame_lance"
            request.ProgressionRevision = int(progress.Revision)
        var output = FileAccess.open(path, FileAccess.WRITE)
        output.store_string(JSON.stringify(request)); output.close()
        # Flush the outgoing packet, then stop client-side network polling before the response can be consumed.
        multiplayer_poll = false
        session.rpc_id(1,"MutateEconomy",JSON.stringify(request))
        root.multiplayer.multiplayer_peer.host.flush()
        print("SUBMITTED without reading acknowledgement")
    else:
        var source = FileAccess.open(path,FileAccess.READ)
        var request = JSON.parse_string(source.get_as_text())
        request.ExpectedRevision=int(request.ExpectedRevision); request.Operation=int(request.Operation); request.Slot=int(request.Slot)
        if ROLE == "a": request.ProgressionRevision=int(request.ProgressionRevision)
        session.rpc_id(1,"MutateEconomy",JSON.stringify(request))
        var response = JSON.parse_string(session.InspectEconomyReply())
        while response == null or response.OperationId != request.OperationId:
            check(Time.get_ticks_msec() < deadline, "Missing durable receipt")
            await process_frame
            response=JSON.parse_string(session.InspectEconomyReply())
        check(response.Accepted and response.Duplicate,"Reconnect confirms committed request")
        if ROLE == "a":
            check(progress.SkillPoints == 2 and progress.BranchSkills.size() == 1 and progress.BranchSkills[0].Id == "mage.flame_lance","Exactly one skill point spent and ability learned")
            check(backpack.Slots.size() == 1 and backpack.Slots[0].Stack.Id != request.ItemId,"Only the selected scroll was consumed")
        else:
            check(backpack.Equipment.Weapon.Id == request.ItemId and backpack.Slots.is_empty(),"Equipped item has one owner location")
        print("DURABLE replay passed")
    print("DISCONNECT complete")
