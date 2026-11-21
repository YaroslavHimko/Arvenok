# Godot host

Import `project.godot` from this directory; build `../../Arvenok.sln` for all projects. The engine loads `Features/Arena/Arena.tscn` in both client and dedicated-server modes. Preserve its root node path and RPC declarations on both sides.

## Run from a terminal

Use PowerShell with the Godot **.NET** executable available as `godot`. From this directory (`apps/game`), build the root solution:

```powershell
dotnet restore ../../Arvenok.sln --locked-mode
dotnet build ../../Arvenok.sln --configuration Debug --no-restore
```

On a fresh checkout, or after adding/replacing artwork, import with `godot --headless --editor --path . --quit`.

Open three terminals in `apps/game` and run one command in each:

**Terminal 1 — server:**

```powershell
godot --headless --path . -- --server
```

Wait for `Arvenok server ready` and leave this process running.

**Terminal 2 — first player:**

```powershell
godot --path .
```

**Terminal 3 — second player:**

```powershell
godot --path .
```

Both clients connect over verified TLS WebSockets to 127.0.0.1:7777. Register separate accounts, create a character in an empty slot, select it and enter the world. Choose class during creation. Click ground to move, select a target, press Space to attack and use 1?4 for core skills. H opens hotbar settings, K opens Skills and I opens inventory.

The world menu saves and returns to character selection or logs out. Offline deletion requires typing the exact nickname. Log in to restore each character's progression, equipment, inventory, learned abilities and position. The server stores the account database under .local/server in the chosen shard directory. Local TLS files live under .local/tls. Start the local server once before connecting clients so its certificate exists.

Use Reconnect after connection failure. See the [root launch instructions](../../README.md#run-the-game-from-a-terminal) and [account operations](../../docs/operations/accounts.md) for TLS configuration and legacy cleanup.

## Host architecture

`dotnet/project/solution_directory` points the editor to the root solution while the game `.csproj` stays beside `project.godot`, as supported by Godot's [multi-project setting](https://docs.godotengine.org/en/stable/classes/class_projectsettings.html#class-projectsettings-property-dotnet-project-solution-directory).

`ArenaSession` composes transport, configuration and `WorldSimulation`. `Presentation/ArenaView` is constructed only for clients. Historical Arena filenames and UIDs are retained for resource compatibility; they contain no arena or match rules. New features should own their scenes and adapters; use pure packages for authoritative rules and `Assets` for resources shared between features. Do not add server credentials or editor-source artwork here.

The host maps transport peers to server-assigned entity IDs. Reliable commands include a sequence and observed server timestamp, while reliable events/essential state and 15 Hz revisioned snapshots drive client presentation. Snapshot JSON is Brotli-compressed; payloads over 1100 bytes use reliable delivery to avoid oversized unreliable packets. Definitions and geometry use shared immutable contracts; the client never mutates authoritative state. `SmokeDriver` is an opt-in scripted client that uses the same intent path as the UI. See [combat MVP](../../docs/architecture/combat-mvp.md).

`ForestWorldView` creates TileMapLayers, collision footprints and Y-sorted props. `WorldEntityView` uses shared animation strips with feet-based roots. `EntityAnimationState` and `CombatEffectPlanner` consume authoritative snapshots/events; they never submit damage or movement. `ArenaView` retains input, interpolation and the combat HUD; `ProgressionHud` presents owner-only progression with native controls. These nodes are never created on the dedicated server. Shared [world data](../../content/worlds/README.md) defines terrain, placements and five passive monster instances across four species; wandering reuses existing navigation and combat.

`AccountSession` handles login and ownership over verified TLS. `AccountScreens` presents Login, registration and six character slots. `ArenaProgression` handles private progression RPCs and asynchronous save ordering. `ProgressionRules` and monster credit ledgers in Simulation own every reward/upgrade. Hosting's `ICharacterRepository` isolates SQLite character aggregates from future database work. See [ADR 0005](../../docs/architecture/decisions/0005-character-progression.md) and [UI replacement](Assets/UI/woodvale-progression/README.md).

`ArenaInventory` handles owner-only inventory requests and durable acknowledgements. Simulation's `InventoryRules` prepares atomic slot operations and trusted add/remove exchanges; `WorldInventory` derives ownership from the existing peer mapping. `InventoryHud`/`InventorySlotControl` use native Control drag/drop and confirmed snapshots. See [ADR 0006](../../docs/architecture/decisions/0006-persistent-inventory.md).

`ArenaEconomy`, `WorldEconomy` and the inventory equipment/crafting/pickup partials extend those same boundaries. Schema 3 adds two equipped ownership locations and migrates schema 1/2 records without dropping items. World snapshots carry transient ground loot; native `WorldLootView` renders it, and clients request pickup without choosing quantities/owners. I/E opens backpack/equipment; F collects nearby loot. Select or double-click package gear/scrolls to equip/craft. Equipment icons preserve existing animated sprites. [ADR 0007](../../docs/architecture/decisions/0007-loot-equipment-crafting.md) explains durable commit ordering and server-side authority; [economy content](../../content/items/economy-v1.md) covers authoring. The earlier economy report records its pre-account multiplayer run. Current account entry checks run through `../../tools/smoke-accounts.ps1`.

Replace animation files and mappings in [the combat animation collection](Assets/Art2D/woodvale-combat/README.md). Instant-hit rules remain: projectile travel is cosmetic, cancellation suppresses release, and defense/mobility effects follow server lifetimes. Native smoke compares visual effect sequences too. `../../tools/capture-combat.ps1` exercises all classes in the forest and saves rendered captures using an isolated local server.

Restart the server and all clients after rebuilding this integration. Existing running processes still use their earlier snapshot contract and world data.

Headless execution is a development path. Dedicated exports that strip visual assets need a verified export pipeline before deployment. Godot supports [dedicated-server exports and headless execution](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_dedicated_servers.html).
