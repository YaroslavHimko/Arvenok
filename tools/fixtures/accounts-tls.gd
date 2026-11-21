extends SceneTree
func _initialize(): call_deferred("run")
func run():
    var args=OS.get_cmdline_user_args()
    var port=int(args[args.find("--port")+1])
    var certificate=X509Certificate.new()
    assert(certificate.load(ProjectSettings.globalize_path("res://../../.local/tls/localhost.crt"))==OK)
    var crypto=Crypto.new()
    var wrong=crypto.generate_self_signed_certificate(crypto.generate_rsa(2048),"CN=untrusted")
    for mode in range(3):
        var peer=WebSocketMultiplayerPeer.new()
        var tls=null if mode==0 else TLSOptions.client(wrong if mode==1 else certificate,"wrong.example" if mode==2 else "")
        var result=peer.create_client(("ws" if mode==0 else "wss")+"://127.0.0.1:"+str(port),tls)
        var deadline=Time.get_ticks_msec()+4000
        while result==OK and Time.get_ticks_msec()<deadline and peer.get_connection_status()==MultiplayerPeer.CONNECTION_CONNECTING:
            peer.poll()
            await process_frame
        assert(peer.get_connection_status()!=MultiplayerPeer.CONNECTION_CONNECTED,"Insecure transport accepted")
        peer.close()
    print("ACCOUNT TLS rejects plaintext, untrusted certificate and wrong hostname")
    quit(0)
