extends SceneTree

# Expanded into an ignored artifact by capture-combat.ps1; uses the ordinary client intent API.
const ROLE = __ROLE__
const CLASSES = [1, 0, 2]
var session: Node
var scenery: Node
var saved: Dictionary = {}
var first_projectile_position: Dictionary = {}
var output_dir: String

func _initialize():
    output_dir = get_script().resource_path.get_base_dir()
    call_deferred("_run")

func _entity_id(class_name_part: String) -> int:
    for child in scenery.get_children():
        var sprite = child.get_node_or_null("Sprite")
        if sprite is AnimatedSprite2D and String(sprite.animation).begins_with("characters/" + class_name_part + "/"):
            return int(String(child.name).trim_prefix("Entity"))
    return 0

func _save(label: String):
    if saved.has(label):
        return
    saved[label] = true
    root.get_texture().get_image().save_png(output_dir.path_join(label + ".png"))
    print("CAPTURE saved " + label)

func _capture():
    if not is_instance_valid(scenery):
        return
    for child in scenery.get_children():
        if child is AnimatedSprite2D:
            if String(child.animation) in ["effects/mage/firebolt", "effects/archer/power_shot", "effects/archer/disrupting_arrow", "effects/mage/blink"]:
                var key = String(child.animation).replace("/", "-")
                if not first_projectile_position.has(child.get_instance_id()):
                    first_projectile_position[child.get_instance_id()] = child.position
                if String(child.animation) == "effects/mage/blink" or child.position.distance_to(first_projectile_position[child.get_instance_id()]) > 2:
                    _save(key)
        var sprite = child.get_node_or_null("Sprite")
        if sprite is AnimatedSprite2D:
            var clip = String(sprite.animation)
            if clip.begins_with("characters/mage/cast/") and sprite.frame >= 3:
                _save("mage-casting")
            if clip.begins_with("characters/warrior/basic_attack/") and sprite.frame >= 4:
                _save("warrior-attack")
            if clip.begins_with("monsters/") and clip.ends_with("/death") and sprite.frame >= 5:
                _save("monster-death")
            if clip.begins_with("characters/") and "/death/" in clip and sprite.frame >= 5:
                _save("player-death")
            var aura = child.get_node_or_null("DefenseAura")
            if aura and aura.visible:
                _save("defensive-auras")
            var trail = child.get_node_or_null("MotionTrail")
            if trail and trail.visible:
                _save("movement-trails")

func _run():
    root.size = Vector2i(1000, 650)
    session = load("res://Features/Arena/Arena.tscn").instantiate()
    root.add_child(session)
    await create_timer(1).timeout
    session.call("Send", 5, 0, 0, Vector2.ZERO, CLASSES[ROLE])
    await create_timer(1).timeout
    session.call("Send", 0, 0, 0, [Vector2(480,448), Vector2(520,448), Vector2(560,448)][ROLE], CLASSES[ROLE])
    await create_timer(4.5).timeout
    scenery = session.get_node("View/WorldViewport/Forest/SceneryAndEntities")
    var target = _entity_id("warrior" if ROLE == 0 else "mage")
    assert(target != 0, "Three class clients must be connected.")
    if ROLE == 0:
        RenderingServer.frame_post_draw.connect(_capture)
        # Exercise opaque-pixel targeting on an animated, padded monster strip.
        var wolf = scenery.get_node("Entity3/Sprite") as AnimatedSprite2D
        var image = wolf.sprite_frames.get_frame_texture(wolf.animation, wolf.frame).get_image()
        var opaque = image.get_used_rect().get_center() - Vector2i(48,48)
        var picked = scenery.get_parent().call("Pick", wolf.to_global(Vector2(opaque)), _entity_id("mage"))
        assert(picked == 3, "Animated monster pixel targeting must preserve entity identity.")
        print("CAPTURE animated pixel targeting passed")
    session.call("Send", 1, target, 0, Vector2.ZERO, CLASSES[ROLE])
    session.call("Send", 4, 0, 4, Vector2.ZERO, CLASSES[ROLE])
    await create_timer(0.4).timeout
    session.call("Send", 4, target, 1, Vector2.ZERO, CLASSES[ROLE])
    await create_timer(1.3).timeout
    session.call("Send", 4, target, 2, Vector2(432,448), CLASSES[ROLE])
    await create_timer(1).timeout
    session.call("Send", 4, target, 3, Vector2.ZERO, CLASSES[ROLE])
    await create_timer(1).timeout
    if ROLE == 0:
        for child in scenery.get_children():
            var aura = child.get_node_or_null("DefenseAura")
            var trail = child.get_node_or_null("MotionTrail")
            assert(not aura or not aura.visible, "Expired defense aura survived.")
            assert(not trail or not trail.visible, "Completed mobility trail survived.")
        print("CAPTURE aura and trail cleanup passed")
    # Shared server combat handles the monster kill; no visual script changes HP or AI.
    session.call("Send", 2, target if ROLE == 1 else 1, 0, Vector2.ZERO, CLASSES[ROLE])
    await create_timer(13).timeout
    if ROLE == 0:
        print("CAPTURE completed: " + str(saved.keys()))
    quit()
