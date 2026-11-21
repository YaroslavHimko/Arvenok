extends SceneTree
const ROLE = "__ROLE__"
const MODE = "__MODE__"
var session: Node
var folder: String
var username: String
var nickname: String
func _initialize(): call_deferred("run")
func check(ok: bool, message: String):
    if not ok: push_error("ACCOUNT CHECK FAILED: "+message); quit(1); assert(ok,message)
func file(name: String, value: String):
    var f=FileAccess.open(folder.path_join(name),FileAccess.WRITE)
    f.store_string(value);f.close()
func wait_file(name: String):
    var deadline=Time.get_ticks_msec()+45000
    while not FileAccess.file_exists(folder.path_join(name)):
        check(Time.get_ticks_msec()<deadline,"Barrier "+name)
        await process_frame
    return FileAccess.get_file_as_string(folder.path_join(name))
func current(): return JSON.parse_string(session.InspectAccount())
func capture(name: String):
    if DisplayServer.get_name()!="headless":
        await process_frame
        await RenderingServer.frame_post_draw
        root.get_texture().get_image().save_png(folder.path_join(name+".png"))
func button(text: String):
    return session.find_children("*","Button",true,false).filter(func(b):return b.text==text)[0]
func lose_reply(operation: int, data: Dictionary):
    data=data.duplicate();data.Operation=operation
    multiplayer_poll=false
    session.rpc_id(1,"AccountIntent",JSON.stringify(data))
    root.multiplayer.multiplayer_peer.poll()
    await create_timer(0.5).timeout
    root.multiplayer.multiplayer_peer.close()
    multiplayer_poll=true
    session.ReconnectAccount()
    var deadline=Time.get_ticks_msec()+12000
    while not session.HasConnection:
        check(Time.get_ticks_msec()<deadline,"Reconnect after lost acknowledgement")
        await process_frame
    await request(1,{"Username":username,"Password":"Account test password 827!"})
func request(op: int, data: Dictionary = {}, accept: bool = true):
    var command = str(ResourceUID.create_id()).sha256_text()
    var id = command.substr(0,8)+"-"+command.substr(8,4)+"-4"+command.substr(13,3)+"-8"+command.substr(17,3)+"-"+command.substr(20,12)
    data=data.duplicate()
    data.merge({"CommandId":id,"Operation":op},false)
    session.SubmitAccount(JSON.stringify(data))
    var deadline=Time.get_ticks_msec()+20000
    while session.AccountBusy or current()==null or current().CommandId!=data.CommandId:
        check(Time.get_ticks_msec()<deadline,"Request timeout")
        await process_frame
    check(current().Accepted==accept,"Operation "+str(op)+" "+current().Message)
    return current()
func run():
    folder=get_script().resource_path.get_base_dir()
    username="account_"+("a" if ROLE=="duplicate" else ROLE)
    nickname="Hero"+("a" if ROLE=="duplicate" else ROLE)
    session=load("res://Features/Arena/Arena.tscn").instantiate();root.add_child(session)
    var deadline=Time.get_ticks_msec()+15000
    while not session.HasConnection:
        check(Time.get_ticks_msec()<deadline,"TLS connection")
        await process_frame
    check(session.LocalEntityId==0 and session.get_node_or_null("View")==null,"Guests have no world")
    if ROLE=="duplicate":
        await wait_file("a-inworld")
        await request(1,{"Username":username,"Password":"Account test password 827!"})
        var id=await wait_file("a-id")
        await request(5,{"CharacterId":id},false)
        await request(4,{"CharacterId":id,"Nickname":"Heroa"},false)
        file("duplicate-done","ok");print("ACCOUNT duplicate entry rejected");return
    if MODE=="restore":
        await request(1,{"Username":username,"Password":"Account test password 827!"})
        check(current().Characters.size()==5,"Delete persisted")
        var id=FileAccess.get_file_as_string(folder.path_join(ROLE+"-id"))
        await request(5,{"CharacterId":id})
        while JSON.parse_string(session.InspectProgress())==null: await process_frame
        var expected=JSON.parse_string(FileAccess.get_file_as_string(folder.path_join(ROLE+"-state")))
        check(JSON.parse_string(session.InspectBackpack())==expected.Inventory,"Inventory persisted")
        check(JSON.parse_string(session.InspectProgress()).CharacterId==expected.Progress.CharacterId,"Character persisted")
        var world=JSON.parse_string(session.InspectWorld())
        var me=world.Entities.filter(func(e):return e.Id==session.LocalEntityId)[0]
        check(abs(me.Position.X-expected.X)<1 and abs(me.Position.Y-expected.Y)<1,"Saved position restored")
        print("ACCOUNT restart passed");return
    await request(2,{},false)
    await request(1,{"Username":username,"Password":"incorrect password"},false)
    if ROLE=="a":
        await process_frame
        await capture("login")
        button("Register account").emit_signal("pressed")
        for input in session.find_children("*","LineEdit",true,false):
            input.text=username if input.placeholder_text=="Username" else "Account test password 827!"
        button("Register").emit_signal("pressed")
        while session.AccountBusy: await process_frame
        check(current().Accepted and current().Authenticated,"Native registration")
    else: await request(0,{"Username":username,"Password":"Account test password 827!"})
    check(current().Characters.is_empty(),"Fresh account empty")
    await process_frame
    check(session.find_children("*","Button",true,false).filter(func(b):return b.text=="Create character").size()==6,"Six native slots")
    if ROLE=="a":
        var creation={"CommandId":"ea000000-1234-4000-8000-000000000001","Nickname":"LostReply","Class":2}
        await lose_reply(3,creation)
        await request(3,creation)
        check(current().Characters.size()==1,"Lost create reply preserved exactly one character")
        var lost=current().Characters[0]
        var removal={"CommandId":"eb000000-1234-4000-8000-000000000001","Nickname":lost.Nickname,"CharacterId":lost.Id}
        await lose_reply(4,removal)
        await request(4,removal)
        check(current().Characters.is_empty(),"Lost delete reply remained deleted")
        print("ACCOUNT lost create and delete acknowledgements recovered")
    await request(3,{"Nickname":"1bad","Class":0},false)
    await request(3,{"Nickname":"Admin","Class":0},false)
    var reply=await request(3,{"Nickname":nickname,"Class":1 if ROLE=="a" else 0})
    var id=reply.Characters[0].Id;file(ROLE+"-id",id)
    var other="b" if ROLE=="a" else "a"
    var foreign=await wait_file(other+"-id")
    await request(5,{"CharacterId":foreign},false)
    await request(4,{"CharacterId":foreign,"Nickname":"Hero"+other},false)
    await request(3,{"Nickname":("Hero"+other).to_upper(),"Class":0},false)
    for i in range(1,6): await request(3,{"Nickname":nickname+str(i),"Class":i%3})
    check(current().Characters.size()==6,"Six occupied slots")
    if ROLE=="a": await capture("six-characters")
    await request(3,{"Nickname":nickname+"7","Class":0},false)
    var doomed=current().Characters[5]
    await request(4,{"CharacterId":doomed.Id,"Nickname":"wrong confirmation"},false)
    var deletion={"CharacterId":doomed.Id,"Nickname":doomed.Nickname,"CommandId":"f1000000-1234-4000-8000-000000000001"}
    await request(4,deletion);await request(4,deletion)
    check(current().Characters.size()==5,"Deletion replay frees one slot")
    await request(5,{"CharacterId":doomed.Id},false)
    await request(5,{"CharacterId":id})
    file(ROLE+"-inworld","ok")
    if ROLE=="a": await wait_file("duplicate-done")
    await wait_file(other+"-inworld")
    await create_timer(0.5).timeout
    var world=JSON.parse_string(session.InspectWorld())
    check(world.Entities.filter(func(e):return e.Kind==0).size()==2,"Two accounts share world")
    var x=480 if ROLE=="a" else 560
    session.rpc_id(1,"RequestCommand",1,world.Time,0,0,0,Vector2(x,448),0)
    await create_timer(6).timeout
    world=JSON.parse_string(session.InspectWorld())
    var me=world.Entities.filter(func(e):return e.Id==session.LocalEntityId)[0]
    check(abs(me.Position.X-x)<1 and abs(me.Position.Y-448)<1,"Authoritative movement")
    var state={"Progress":JSON.parse_string(session.InspectProgress()),"Inventory":JSON.parse_string(session.InspectBackpack()),"X":me.Position.X,"Y":me.Position.Y}
    file(ROLE+"-state",JSON.stringify(state))
    await request(6)
    check(session.LocalEntityId==0 and session.get_node_or_null("View")==null,"Return despawns world")
    var second=current().Characters[1].Id
    await request(5,{"CharacterId":second})
    while JSON.parse_string(session.InspectProgress())==null: await process_frame
    check(JSON.parse_string(session.InspectProgress()).CharacterId==second,"Switch selected character")
    await request(6);await request(7)
    await request(2,{},false)
    check(not current().Authenticated,"Logout revoked")
    print("ACCOUNT flow passed")
