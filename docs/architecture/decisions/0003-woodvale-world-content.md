# ADR 0003: Woodvale art, shared world data and passive monsters

- Status: accepted for the forest integration
- Date: 2026-11-18

## Context

The existing world has authoritative movement, A* navigation, swept collision, entity IDs, combat, respawn and revisioned ENet snapshots. Its client draws placeholders directly; there are no independent character scenes or monster systems to extend. The user supplied Woodvale v0.1 and requested an incremental forest integration while preserving these systems.

The pack's manifest and handoff are source documentation. The supplied sample map is smaller than the existing world and is illustrative, so it is retained as a reference rather than replacing the playable map. The pack contains directional idle poses and static monsters, with no complete animation sets or terrain autotiling rules.

## Decision

Keep the existing projects, dependency edges, root scene, RPC node path and script UIDs. Copy the 27 runtime PNGs byte-for-byte into `apps/game/Assets/Art2D/woodvale/assets/`. Retain source art, references and original documentation under `content/art-source/art2d/woodvale/`, outside the engine import root. Record provenance without inventing a new license.

Author terrain patches, the existing rock formations, sanctuary, player spawns, prop footprints/placements and monster spawns in `content/worlds/woodvale-forest.json`. Embed this file and the original manifest into the simulation assembly. `WorldContentLoader` validates them and produces immutable contracts used by both server and client. The simulation does not load images or reference engine types. No new C# dependency edges are needed.

`WorldMap` remains the authority for navigation, body sweeps and sight. Water and all shoreline cells block movement; water itself does not block sight. Prop collision comes from small feet/trunk/boulder footprints, not canopy bounds. Adjacent water cells are coalesced for efficient path queries. The original world dimensions, rock formations, protected region and player spawn points remain intact.

The client creates `Ground` and `WaterAndShore` TileMapLayers and a Y-sorted scenery/entity container within the existing HUD viewport. All sprite roots are at feet. The 96x96 character canvases use a (0,-43) center offset; props and monsters use their actual image dimensions. CollisionShape2D footprints and blocked TileSet cells mirror shared data; gameplay still uses the pure authoritative map. Textures use lossless import, no mipmaps and nearest-neighbor sampling. No animation frames are invented.

Add four passive monster entities to the existing entity dictionary. They have their own configured HP, speed, spawn and roaming parameters; player classes apply only to player entities. `MonsterWander` chooses paths on the server after seeded 1-3 second idle intervals. Every path segment stays within the spawn leash and avoids protected regions. Failed paths wait or return home through navigation; they never teleport. Existing movement, target eligibility, damage, death and respawn handle monsters too. There is no aggro, attack AI, loot or progression system. The sanctuary protects player combat with either entity type.

Extend the existing snapshots with optional species and wander state. Clients interpolate monster motion exactly as they interpolate player motion, and never instantiate AI. Monster IDs survive client disconnects; monsters and world time continue with no clients. Player admission limits count players, not monsters.

The larger entity roster exposed ENet MTU warnings in the first native test. Keep the existing RPC model and revision logic, but compress snapshot JSON with built-in Brotli. Payloads up to 1100 bytes use the normal unreliable snapshot path; larger payloads use the reliable state RPC to avoid lossy fragmentation. Combat events stay reliable. This is a development fallback, not area-of-interest replication or a capacity guarantee. Client and server must be rebuilt and restarted together.

## Consequences and verification

Map and spawn edits are data changes followed by a root solution build. New art IDs require manifest entries and matching PNGs. The original asset limitations remain visible: static poses, static monsters, manual shoreline edges, no corner/autotile set or additional buildings/VFX/icons in this ZIP.

The repository check verifies all PNG paths, dimensions, alpha channels and available import settings. Pure tests cover preserved world landmarks, water/props, roaming idle intervals, speed/leash/collision, deterministic wandering, no-client continuity, invalid spawns, ownership and the shared PvE death/respawn pipeline. The native smoke test compares monster positions, facing, states and HP at common authoritative revisions on two clients, then reconnects fresh clients and checks monster identity continuity alongside the existing combat scenario. Rendered review captures exercise all three class skins and the forest.

The architecture check also required a small PowerShell correction: `Select-String -Quiet` returned an array of false results for multiple presentation files, which PowerShell treated as true. The check now tests actual matches and continues to prohibit presentation dependencies on simulation.
