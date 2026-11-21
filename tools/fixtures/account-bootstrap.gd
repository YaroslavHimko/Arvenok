extends SceneTree
const PROFILE = "__PROFILE__"
const KIND = __KIND__
var session: Node
func _initialize(): call_deferred("run")
func request(operation: int, character: String = "00000000-0000-0000-0000-000000000000"):
    var command = str(ResourceUID.create_id()).sha256_text()
    var id = command.substr(0,8)+"-"+command.substr(8,4)+"-4"+command.substr(13,3)+"-8"+command.substr(17,3)+"-"+command.substr(20,12)
    session.SubmitAccount(JSON.stringify({"CommandId":id,"Operation":operation,"Username":PROFILE,"Password":"test-only password 721!","Nickname":PROFILE,"Class":KIND,"CharacterId":character}))
    var deadline = Time.get_ticks_msec()+20000
    while true:
        var reply=JSON.parse_string(session.InspectAccount())
        if reply != null and reply.CommandId == id:
            if not reply.Accepted: push_error("Account bootstrap failed: "+reply.Message); quit(1)
            return reply
        if Time.get_ticks_msec()>deadline: push_error("Account bootstrap timeout"); quit(1); return null
        await process_frame
func run():
    session=load("res://Features/Arena/Arena.tscn").instantiate()
    root.add_child(session)
    var deadline=Time.get_ticks_msec()+15000
    while not session.HasConnection:
        if Time.get_ticks_msec()>deadline: push_error("Secure connection timeout"); quit(1); return
        await process_frame
    var reply=await request(0)
    if reply.Characters.is_empty(): reply=await request(3)
    await request(5,reply.Characters[0].Id)
    print("ACCOUNT authenticated world entry")
