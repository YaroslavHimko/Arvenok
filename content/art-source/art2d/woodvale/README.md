# Woodvale Forest — Starter Art v0.1

An original, starter-scale 2D fantasy world art pack for an existing Godot 4 .NET open-world MMORPG. Includes actual importable PNG assets; this is **not** a finished animation set or a complete autotile collection.

## Contents

- `assets/characters/{warrior,mage,archer}/idle_{down,left,right,up}.png` — twelve 96×96 RGBA directional **idle** frames.
- `assets/terrain/terrain_atlas.png` — 6 columns × 4 rows of exact 32×32 tiles, 192×128 total; see `manifest.json` for mapping.
- `assets/props/*.png` — individual transparent trees, rocks, fence, barrel, foliage.
- `assets/monsters/*.png` — transparent **static** sprite images for slime, mushroom, wolf, sprout.
- `manifest.json` — exact paths and atlas coordinates, tile collision hints.
- `sample_world_layout.json` — optional illustrative world layout, object placements and monster spawn suggestions.
- `references/world_preview.png` — visual composition preview (not a Godot scene).
- `source_art/class_direction_reference.png` — full character reference sheet.
- `CODEX_HANDOFF.md` — instructions for Codex to integrate with existing project.

## Important limitations

- Player images are 4-direction idle poses. There are **no** authentic walk/attack/cast/hit/death animation sequences. For now select directional idle image while moving; optionally add gentle visual bob. Do not fake animation frames by cutting one frame into pieces.
- Monster images are **static** and have no directional or attack frames. Wandering is a gameplay AI behavior, not a sprite-sheet feature.
- The terrain atlas has no full terrain-autotiling bitmask. Water is present as four static animation frames and simple manual shoreline pieces.
- Props and monsters extracted from generated concept illustrations have slightly softer edges than the crisp terrain/characters. They are suitable for a prototype but should eventually be redrawn in a stricter pixel grid.
- The `arvenok` name was previously used as a **provisional** project name; this pack intentionally does not assume the final game brand.

The pack is game-world artwork generated for this project; verify suitability and production licensing under applicable platform terms before publishing commercially.
