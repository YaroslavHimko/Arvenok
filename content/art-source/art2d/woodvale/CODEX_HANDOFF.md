# CODEX HANDOFF — integrate starter art into existing Godot 4 .NET open-world MMORPG

## Task and scope
Integrate the supplied PNGs into the **existing** game and use them to visually represent three player classes, walkable forest terrain, water, trees/rocks, and server-authoritative wandering monsters. **Do not start a new project or replace working code.** The game is an open-world MMORPG, not an arena/MOBA. Do not create rounds, match timers, or a new arena mode.

## Step 0 — audit before changing files
1. Review `project.godot`, world scenes, character scenes, input controller, shared entity state, networking code, spawn handling, existing combat, and navigation.
2. Identify the current game-world tile size, rendering approach (`TileMapLayer`, custom renderer, etc.), Y-sort setup, collision/navigation representation, and authoritative server loop.
3. Produce a short change plan that uses existing systems; list affected files before editing.
4. Read `manifest.json` rather than inferring sprite dimensions or atlas offsets from screenshots. Do not assume artwork not in the pack exists.

## Import setup
- Place or reference `assets/` in the current Godot project (usually `res://assets/woodvale/`); maintain paths or remap by a clearly documented copy step.
- For pixel-art PNGs set nearest-neighbor sampling (`CanvasItem.texture_filter = NEAREST` on relevant ancestors, and pixel-friendly project texture defaults). Turn off mipmaps for low-resolution sprites; avoid lossy texture compression.
- Use the project's existing rendering resolution/camera zoom. Preserve the PNGs' original aspect ratio and transparency; do not enable sprite region cropping inadvertently.
- Character frames are exactly 96×96 pixels each. Their images contain transparent padding with feet around y=91; keep each character's **world-space root at feet**. For an animated/sprite node with center origin, use a visual offset around `(0,-43)` pixels, then fine-tune visually. The collision capsule/rectangle belongs at the feet, not centered on artwork.
- Monsters and props have different pixel dimensions; query actual image sizes or use manifest metadata. Place their visible bottom-center at their logical world position. Do not scale all props to 32×32.

## Terrain and world composition
- `terrain_atlas.png` is exactly 192×128, with 6 columns and 4 rows of 32×32 tiles. Source atlas coordinates are specified in `manifest.json` under `terrain_tiles`. Use those exact coordinates in a Godot `TileSetAtlasSource` with region size 32×32.
- Use existing `TileMapLayer` setup or construct compatible layers if none exist: Ground (grass/dirt/stone), Water (blocked), and optional Decals (grass/reeds/shore). Godot 4.x `TileMapLayer` is preferred over deprecated `TileMap` for new tilemaps.
- Basic `water_0` to `water_3` can form an animation if desired, but treat them as frames of one water tile, not four unrelated land materials. Water is unwalkable for the current prototype.
- `shore_n/e/s/w` are **manual** transitional tiles, not an autoterrain. Add collision consistent with water; do not incorrectly mark shores as universally walkable. Avoid using them as an autotile rule without real mask art.
- Map sample: `sample_world_layout.json` is a suggested illustration; **do not overwrite an existing world map**. If world already exists, introduce a small forest area or use a test chunk in the existing world.
- Ensure collision and navigation agree on water, rocks, and trees. If props block movement, use small trunk/boulder physics footprints, not huge canopy-wide shapes. Use feet-based Y sorting so characters appear behind a tree when appropriate.

## Character skins
- Warrior, Mage, Archer each have `idle_down.png`, `idle_left.png`, `idle_right.png`, `idle_up.png` (each 96×96 RGBA).
- Reuse current character scenes/controllers and swap visual sprite by class and direction. Drive facing direction from **server-confirmed** movement or last explicit aim direction; keep movement and combat state as is.
- If an existing `AnimatedSprite2D` is used, map each direction to a single idle frame. There are NO walk/cast/attack/hurt/death animation frames provided. Do not populate fictitious animations or claim they are complete. A subtle local visual bob during movement is acceptable as optional cosmetic feedback; do not alter physics coordinates to fake a walk.
- Keep equipment/skin layer extension possible; do not hard-wire unique collision, stats, or class logic into the Sprite2D itself.

## Monster placement and wandering
- Available static sprites: `slime.png`, `mushroom.png`, `wolf.png`, `sprout.png`. Treat names as placeholder original monsters.
- If monsters/NPCs already exist, extend their visual mapping and spawn data. Otherwise build a minimal reusable server-owned monster world entity (visual Sprite2D, small collision body, navigation agent only if world navigation is already implemented).
- Spawn few monsters at the optional locations in `sample_world_layout.json`, or place near wilderness using the game's existing spawn pipeline. Never spawn in water, a collision footprint, an inaccessible island, or the protected starter area.
- Minimal server-authoritative wandering AI: `Idle` (random pause 1–3s), `ChooseDestination` (point within 3–6 tile leash of home spawn on valid walkable land), `Roam` (move using world navigation or validated collision-aware movement), `ReturnHome` (when outside leash or path failed), `Dead/Respawn` only if current combat system supports it. Avoid per-frame random direction jitter. Stop and idle if blocked; do not teleport through geometry.
- Begin with 2–4 total monsters or low local density. 0.6–1.3 tiles/sec wandering speeds, per-monster configurable; server controls movement and broadcasts snapshots; clients render interpolated transforms and optionally subtle sprite bob/flip for direction.
- Monsters are world entities, not decorative local-only sprites. Their positions, IDs, state and combat eligibility must be authoritative. If PvE is already working, integrate into existing target/damage/death pipeline. If not, leave roam passive and do not add a second damage system or full drop/loot system just to enable wandering.
- At high distance from all players, monster AI may tick at a lower rate later; do not prematurely build distributed AI services.

## World rules, network constraints
- Safe zone/PvP gating is **server-side**; colors or decorative tiles do not establish combat eligibility.
- Maintain stable network entity IDs for players and monsters, server-side movement authority, and existing interpolation rules. Clients must not independently choose monster destinations or calculate attack outcomes.
- World continues running with no client connected. Do not build round resets or arena-specific state.

## Suggested minimal integration phases
1. Import art into a compatible folder, verify textures load and are crisp/transparency-correct.
2. Replace placeholder player graphics with 4-direction class skins and keep existing input/networking unchanged.
3. Add terrain textures to existing world/new small forest chunk; mark water blocked, test click-to-move and pathing.
4. Add props with correct feet anchor, physics footprints, Y sorting and navigation.
5. Add 2–4 server-owned wandering monsters with minimal, bounded roam AI.
6. Validate multi-client monster positions, latency interpolation, obstacle avoidance, safe zones, disconnections.

## Definition of done
- Two existing clients connect to the same dedicated world, and 3 selectable character class skins display correctly with facing direction.
- A wooded area displays aligned ground tiles plus recognizable water, obstacles and decorative props.
- Players cannot walk on water or through boulders/tree trunks. Navigation and server collision agree.
- At least two monsters wander slowly and predictably **at the same authoritative positions on both clients**; they respect water/terrain and stay near spawn.
- Existing combat, identity, movement and networking functionality still work. No new arena/match/round mechanics.
- Report tests actually run, remaining placeholder art, known layout issues and how to update monsters/tiles later.
