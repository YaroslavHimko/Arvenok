# Authored world data

`woodvale-forest.json` extends the existing 60x40-tile world. The original sanctuary, player spawn points and four rock formations are retained. The Woodvale pack's sample layout is kept separately as reference material.

The simulation embeds this file at build time and validates it together with the supplied art manifest. Both client presentation and authoritative collision consume the resulting read models. After changing data, build root `Arvenok.sln` and restart the server and all clients.

| Field | Units and behavior |
| --- | --- |
| `widthTiles`, `heightTiles`, `tileSize` | Map extent; tile size is 32 pixels |
| `bodyRadius` | Shared moving-entity collision radius in pixels |
| `groundVariants` | Weighted list of walkable manifest tile IDs; variation is deterministic |
| `terrainPatches` | Ordered rectangles `[tileX, tileY, width, height]`; later entries override earlier ones |
| `rockFormations`, `protectedRegions` | World-pixel rectangles `[x, y, width, height]` |
| `playerSpawns` | Protected world-pixel positions `[x, y]` |
| `propFootprints` | Per-asset rectangles relative to bottom-center; `null` is nonblocking decoration |
| `props` | Unique placement ID, manifest asset ID and world-pixel feet position |
| `monsterSpawns` | Unique spawn ID, species, home position, HP, speed/radius in tiles, idle interval, respawn delay, level and baseExp |

Water and all four shoreline entries inherit `walkable: false` from the manifest. The renderer places the supplied shore edges manually: `shore_n` on the north bank, `shore_e` on the east, and so on. There are no supplied corner or autotile masks. Water blocks movement and mobility but does not obstruct sight by itself.

Trees, rocks, bushes, fences and barrels have small solid footprints. Flowers remain walkable. The same footprints create server navigation blockers and client CollisionShape2D nodes. Do not use full tree-canopy dimensions for collision.

Monster species are `Slime`, `Mushroom`, `Wolf` and `Sprout`. Default wandering speeds range from 0.6 to 1.3 tiles/sec and radii from 3 to 5 tiles. Idle bounds must remain within 1-3 seconds. Spawns must be on walkable wilderness land reachable from a player spawn. Paths respect the full roaming radius, including detours around obstacles. Monsters are passive but can be targeted and defeated through the existing combat system; they respawn at home after their configured delay.

Five instances use the four existing species: Slime L1/10 EXP, Sprout L2/18, Mushroom L3/31, Wolf L4/52, and a southeast Wolf variant L6/90. Level/reward are per spawn, independent of sprite kind. The level-six variant keeps level 10 reachable under low-level penalties. See [progression rules](../progression/README.md).

Run `tools/validate.ps1` after edits. `tools/smoke-game.ps1` additionally checks real client/server replication and reads the expected monster count from this data. `tools/smoke-progression.ps1` uses the distant level-six Wolf for shared kill rewards.
