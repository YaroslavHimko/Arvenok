# Codex handoff — Woodvale combat animation extension v0.1

## Mission
Add visibly animated attacks, skill effects, damage responses, death, movement, and monster actions to the **existing Godot 4 .NET / C# open-world MMORPG**. Keep the authoritative server, movement code, world scenes, class logic, skill balance, and combat rules that already exist. **This is not an arena/MOBA**. No matches, rounds, scores, or new gameplay modes. This assignment is primarily **visual presentation**; do not change damage, range, cast duration, cooldowns, AI authority, or collision rules to match the art.

## Required first step: inspect the actual repository
1. Read relevant scenes and scripts: network layer; player spawn/rendering; movement/facing; existing Sprite2D/AnimatedSprite2D/AnimationPlayer; combat/skill definitions; target selection; world entities/monsters; and any current animation event flow.
2. Identify the real node paths, existing entity/peer ownership, class names, and animation API. Make a short integration plan tied to real files. Do not create redundant controller/combat architectures.
3. Read `animation_manifest.json` and image files before referencing a clip. The asset pack from the earlier milestone is a prerequisite for terrain/static sprites, but this pack provides action/FX strips and animation metadata.
4. Run whatever project build/test command already exists. Preserve existing behavior.

## Exact file format: no guessing
- Files in `assets/animations/characters/<class>/<clip>_<direction>.png`: **768×96 RGBA**, exactly **8 horizontal frames of 96×96**, no gaps, margins, labels, or background. Directions: `down`, `left`, `right`, `up`. Classes: `warrior`, `mage`, `archer`. Clips: `idle`, `walk`, `basic_attack`, `cast`, `interrupt`, `defend`, `hurt`, `death`.
- Files in `assets/animations/monsters/<type>/<clip>.png`: same 768×96, 8 frames. Types: `slime`, `mushroom`, `sprout`, `wolf`; clips: `idle`, `move`, `attack`, `hurt`, `death`.
- Files in `assets/animations/vfx/`: same 768×96, 8 frames; use manifest IDs rather than filename guesses.
- Every PNG is **actual transparent RGBA**. Keep pixels crisp: nearest-neighbor sampling; do not apply blur or linear filtering; avoid lossy re-encoding or auto-cropping individual cells.
- Source art has padded cells; feet are near `x=48,y=90` in each frame. Set visual root/offset such that the feet stay anchored to the game's world-space entity origin. **CharacterBody2D/CollisionShape2D/NavigationAgent2D must not move because a sprite frame bobs/leans.** Projectiles and VFX attach to designated spawn points, not the entity's collider center.
- The v0.1 base package uses `assets/characters/<class>/idle_<direction>.png` as reference skins. Import this animation extension under `res://assets/woodvale/animations/` or fit existing folder conventions; update asset references accordingly.
- Use `animation_manifest.json` for paths, clip fps, loop flags, class mappings, and count; do not infer clip metadata from artwork.

## What quality level these assets represent
These are **usable animation prototype spritesheets**, not a complete polished hand-drawn production animation library. The down-facing Warrior attack, Mage cast, and Archer attack use individually illustrated poses. Other directional body animations are procedural pose/bob/lean placeholders derived from the v0.1 idle skins. Some VFX (especially projectiles) are simple looping pixel effects. This is deliberate; build a replaceable animation pipeline so more detailed art can be swapped in later without touching networking or combat logic.

## Integration architecture
- Extend/reuse the existing view/presentation layer for player and monster entities; the server remains authoritative about movement, target, skill acceptance, cast progression, damage, interrupt, death, respawn, and live temporary buffs.
- A **single client-side animation router/presenter** can map authoritative entity state + events to class/monster clip names. Avoid putting animation code inside server combat calculations.
- Use `AnimatedSprite2D` + `SpriteFrames` (or the project’s existing AnimationPlayer pipeline). Create SpriteFrames resources programmatically in editor or by a safe import script, or configure in editor; frame width/height **96×96**, and frame count **8**. Do not set region 32×32 or manually recrop incorrectly.
- Set `CanvasItem.TextureFilter` to nearest for sprite and VFX trees. Respect camera zoom and existing `YSortEnabled` ordering.
- Respect existing class/monster entity IDs. Visual objects never determine network identity or gameplay damage.
- Keep the visual root attached to game-world **feet position**. Do not move the physics body to play Rush, Backstep, Blink, hit recoil, walking bob, or death squash. Only the server’s gameplay movement code moves entities.

## State-to-clip mapping and priority
Candidate animation priority (adapt to existing implementation): `death > hurt/interrupt > cast/skill > basic_attack > walk > idle`. Defensive loops should preferably be overlays, not mutually exclusive movement states.

- Idle player: `idle_<facing>` looping.
- Server-confirmed movement or visual interpolation: `walk_<facing>` looping at roughly 10 fps. Walk animation is cosmetic; world movement speed is independent.
- Basic attack accepted/started: `basic_attack_<facing>`, play once; animation release should match the **server-confirmed resolution point**. Do not infer that every frame 4 is a hit.
- Cast/windup started: `cast_<facing>`, loop/stretch to actual server cast duration; optionally show casting aura separately. Cast bar uses server end timestamp, not local animation length.
- Interrupt: stop current cast/skill visuals, play `interrupt_<facing>` once, spawn interrupt impact VFX at the target. The interrupted skill **must not** complete visually or apply damage.
- Defensive skill: use `defend_<facing>` as initial gesture, then restore movement/idle and loop an attached defensive aura while server says effect remains active.
- Hit confirmed: `hurt_<facing>` once if not dead; don't freeze navigation or cancel casts unless server itself confirms an interrupt.
- Server death: `death_<facing>` once, hold last frame; remove/clear VFX according to actual effect expiration. Respawn resumes idle with full visual state reset.
- Monsters: `idle`, `move`, `attack`, `hurt`, `death` with server AI/combat events. Do not simulate world movement or AI independently on clients.

## Skill-to-animation/VFX mapping (EXACT manifest IDs)

| Class | Skill | Player clip | Attached VFX ID | Trigger and lifetime |
|---|---|---|---|---|
| Warrior | Basic Attack | `basic_attack` | `basic/sword_slash` | Spawn slash at server-resolved hit |
| Warrior | Crushing Blow | `cast` for windup then `basic_attack` | `warrior/crushing_blow`, optional `warrior/crushing_impact` | Slash/hit only on authoritative completion |
| Warrior | Rush | optional `walk` / `basic_attack` | `warrior/rush` | Cosmetic speed trails during server-authoritative movement; no client teleport |
| Warrior | Shield Bash | `basic_attack` | `warrior/shield_bash` | On accepted ability and confirmed interrupt/hit |
| Warrior | Guard | `defend` | `warrior/guard` | Loop barrier only while server buff active |
| Mage | Basic Attack | `basic_attack` or `cast` | `basic/magic_orb` | Cosmetic projectile; damage comes from server |
| Mage | Firebolt | `cast` | `mage/firebolt`, `mage/firebolt_impact` | Start visual cast; release on cast completion; hit impact on server hit |
| Mage | Blink | `cast` or `idle` | `mage/blink` | Departure/arrival at server-confirmed positions only |
| Mage | Counterspell | `cast` | `mage/counterspell` | Spawn on target when server accepts resolution; cast cancelled only if server confirms |
| Mage | Arcane Ward | `defend` | `mage/arcane_ward` | Loop on target while buff state active |
| Archer | Basic Attack | `basic_attack` | `basic/arrow` | Cosmetic projectile; server decides attack result |
| Archer | Power Shot | `cast` then `basic_attack` | `archer/power_shot` | Release on authoritative windup completion |
| Archer | Backstep | optional `walk` | `archer/backstep` | Speed trail during authoritative backstep; never move collider cosmetically |
| Archer | Disrupting Arrow | `basic_attack` | `archer/disrupting_arrow`, `archer/disrupting_impact` | Projectile/impact and interrupt based on authoritative resolution |
| Archer | Evasive Stance | `defend` | `archer/evasive_stance` | Aura while server effect persists |

**Timing:** Example skill values previously discussed: Warrior Crushing Blow 0.7 s windup, Mage Firebolt 1.0 s cast, Archer Power Shot 0.85 s windup, and typical basic attack interval 1.25 s. **Read actual values from the current repository**; these are references, not license to overwrite existing balance. Cast clip speed should be stretched or looped to cover gameplay cast duration. Recovery is visual only unless server already defines recovery.

## Projectile rule (particularly important)
The provided arrow and fireball sheets animate an object **in place**; they do not move it across the world. For an optional visible projectile:
1. On server-confirmed release, render a projectile moving along a cosmetic trajectory from the caster's existing attack origin toward the server-specified target/impact position.
2. Orient it toward the target using visual rotation or the existing direction-aware projectile system. The sheet points to the **right** by default.
3. If the game's current combat model resolves hits instantly on windup completion, do not secretly delay actual HP damage until the cosmetic projectile arrives. You may choose a very fast VFX trajectory and spawn the hit impact on the authoritative damage event, even if latency makes animations imperfect.
4. Never let the client claim hits or apply damage because a projectile Sprite2D overlaps a target. Client collision on VFX is disabled.
5. Do not spawn a projectile for a rejected skill, cancelled cast, dead target, or missing authoritative release event.
6. Reconcile delayed/duplicate events by cast ID / action sequence number if present; otherwise use current event deduplication pipeline.

## Network event sequencing
- Prefer current server events/state updates; don't invent a parallel ability networking system just for animation.
- A client may receive `cast_started`, `cast_cancelled`, `cast_completed`, `damage_applied`, `effect_started`, `effect_ended`, `dead`, `respawned`, etc.; map existing equivalents. For events carrying timestamps, derive progress from server time offset rather than restarting casts from 0 on delayed receipt.
- **Idempotence:** duplicate attack/impact events must not play multiple identical animations or spawn additional projectiles.
- **Late joins:** obtain entity movement state and active defensive effects from authoritative snapshots, so newly joined clients see guard/ward correctly.
- **Disconnect/death:** clear queued VFX, hide stale targets, and release visual nodes. Do not leave immortal auras.
- Visual and gameplay state must be robust to out-of-order unreliable position snapshots. Events affecting combat outcome should follow the existing reliable channel convention.
- For large open-world later: use appropriate recipient/visibility filtering for transient effects, but do not implement world sharding now.

## Monster animations and wilderness behavior
- The v0.1 monster animations are cosmetic: `idle` subtle breathing, `move` hops/gait, `attack` lunge, `hurt` red recoil, `death` collapse. They are the same 96×96 cells even for smaller monsters; keep monster feet anchored.
- Existing server-owned wandering (spawn radius, nav, obstacle avoidance) controls movement. On clients, use `move` animation while authoritative velocity exceeds a small threshold; otherwise `idle`.
- Damage/attack/death clips only trigger from server combat outcomes. Continue existing target/pathfinding/persistence decisions; do not add aggressive AI unless it already exists or user requests it.
- Only instantiate visual VFX for entities relevant to a client and in the loaded world chunk.

## Integration phases and checks
1. Import a single character and one effect manually; verify actual frames are crisp, transparent and aligned at feet with a stable collider.
2. Add a reusable animation presenter. Confirm all three class mappings and four facings; do not accidentally override network position interpolation.
3. Wire basic attack and Firebolt casting, projectile, impact and interruption; run with **two clients plus dedicated server in shared wilderness**, not a match arena.
4. Wire all other skill VFX, buff loops, projectile/trail orientation, death/hurt/respawn.
5. Wire the four monster animation sets into existing wandering/combat scene; test monster updates from server.
6. Verify 0% gameplay authority moved to client visual scripts; build the Godot C# project and use existing test suite. Do not claim integration complete without actual Godot runtime verification.

## Acceptance test matrix
- Two clients see the *same* Warrior attack and Mage cast animation event in the open world; visual frames can progress smoothly despite network interpolation.
- A cancelled Firebolt **never releases** its projectile and the cast indicator immediately stops. A successfully completed Firebolt emits one projectile and one damage resolution, not multiple.
- Shield Bash / Counterspell / Disrupting Arrow visibly interrupt a cast **only** when server confirms interruption; ordinary hit just plays `hurt`.
- Guard, Ward, Evasive Stance show an aura throughout server-defined effect duration; aura remains on moving characters and disappears on expire/death/disconnect.
- Rush, Blink, and Backstep remain authoritative and respect world collision; VFX never changes physics coordinates.
- Monsters roam in world with synchronized movement; every client sees animation change between idle/move without independent AI.
- Sprites render clearly at game camera zoom with no atlas bleeding or leftover background; all strips are 768×96.
- Neither client can spoof animation/combat events to inflict damage; existing PvP safe-zone rules remain enforced.

## Final report requested from Codex
List modified files, asset import paths, current animation mappings, screens/videos demonstrating in-world combat (if the environment supports capture), actual build/test results, known placeholders, remaining missing directions or polished frames, and precise next integration steps. Do not claim hand-drawn 4-way attack animations exist: only the **down** actions have individually generated key poses; other directions remain procedural placeholders.
