# Local operation

Build the root solution and import `apps/game/project.godot`. The scripts in `tools` resolve paths relative to themselves, so they work from any working directory. `run-game.ps1` accepts `-Mode client|server`, `-Godot <executable>` and `-SessionArguments @('--port','7778')`.

Configuration priority is command line, then environment, then default. Put application arguments after Godot's `--` separator.

| CLI | Environment | Default | Meaning |
| --- | --- | --- | --- |
| `--server` | none | client | Run authoritative session; dedicated-server exports also select this mode |
| `--host` | `ARVENOK_HOST` | `127.0.0.1` | Client destination, hostname or IP |
| `--bind` | `ARVENOK_BIND` | `127.0.0.1` | IPv4 interface to listen on |
| `--port` | `ARVENOK_PORT` | `7777` | TLS/TCP port, 1 through 65535 |
| `--region` | `ARVENOK_REGION` | `local` | Lowercase log label, up to 63 characters |
| `--shard` | `ARVENOK_SHARD` | `world-001` | Lowercase log label, up to 63 characters |
| `--profile` | `ARVENOK_PROFILE` | `auto` | Local client reconnect profile; lowercase letters, digits, hyphens |
| `--data-dir` | `ARVENOK_DATA_DIR` | `.local/server/<shard>` under repository | Server-only development character directory |
| `--reset-skills` | none | disabled | Offline development reset: requires `--server` and saved character GUID |
| `--grant-inventory` | none | disabled | Debug-only offline fixed item kit for one saved character GUID; cannot combine with reset |
| `--smoke-role` | none | `none` | Internal combat/progression client drivers; set by smoke scripts |

Unknown/duplicate options or invalid values fail before a peer starts. A startup configuration error exits with code 2; a server listen failure exits with code 1. An interactive client keeps its status visible on connection failure. `CombatBalance.MaxPlayers` defaults to 32; this is a development admission limit, not a tested capacity claim.

For a deliberate trusted-LAN test, run the server with `--server --bind 0.0.0.0`, allow UDP only from the test network, and connect with `--host <server-address>`. The loopback default does not accept remote clients. Intents are rate-limited and checked for duplication/age, but account authentication and encrypted session admission remain future work.

`smoke-game.ps1` starts hidden headless server/client processes on an ephemeral loopback TLS/TCP port, drives two clients through movement and combat, compares their damage/respawn outcomes and authoritative monster snapshots, then reconnects two fresh clients to the same world. It checks safe-zone rejection, duplicate command rejection, interrupt, defense, death, protected respawn, visible art loading, monster movement and monster identity continuity. It writes logs under ignored `artifacts/smoke/<run-id>` and terminates only processes it created. It does not establish visual quality, internet behavior or game balance.

If scripts are blocked by Windows execution policy, use the process-scoped invocation in the root README. In restricted automation, set `DOTNET_CLI_HOME` to a writable task-local cache before invoking dotnet; no global configuration change is needed. Godot's `.NET` executable is required; some renamed console wrappers expect a companion filename, so use the working main executable directly.

After a scene/script move, rebuild and run a Godot editor import to refresh generated metadata. Preserve script `.uid` files. Old generated root `.godot` caches can be removed manually after closing the old editor entry; they are not source files. If the port is occupied, choose another port consistently for server and clients. On disconnect, the client clears its displayed state; there is no automatic reconnect yet.

## Account saves

Use Login to register an account, create a character and enter the world. The server stores owned character aggregates in accounts.sqlite3 under .local/server/<shard>. Characters retain their own progression, inventory, equipment, learned abilities and position. Local hotbar layouts are keyed by character GUID. The historical --profile argument has no role in account ownership.

Returning to selection, logout and disconnect save and despawn the active character. A second world entry for the same account is rejected. Account sessions expire after four hours and are revoked on disconnect. See [account operations](accounts.md) for TLS, backup, deletion and the guarded legacy cleanup command.

The current native tests are smoke-game.ps1 and smoke-accounts.ps1. Earlier progression and inventory scripts have legacy JSON fixture setup. Their simulation regression checks remain part of validate.ps1.

## Inventory test items

New characters have 24 empty slots. To populate one test character, build Debug, start the server, register an account and enter its selected character. Copy its stable GUID from the client's `Backpack loaded: character=...` log line, or inspect the account database while the server is stopped. Do not use the temporary numeric entity ID.

Close that client and stop the server, then run from the repository root (replace the example GUID with your character's GUID):

```powershell
godot --headless --path apps/game -- --server --grant-inventory 00000000-0000-0000-0000-000000000001
# The grant process saves and exits. Start the server again:
godot --headless --path apps/game -- --server
# In a second terminal, log in and select the same character:
godot --path apps/game
```

Include the same `--data-dir` or `--shard` on the grant command if using custom storage. The exclusive directory lock prevents granting while that server is running. The command grants a fixed 11-slot sample kit once: sword, shield, 1,200 logs (two stacks), stone, ore, pelt, blade fragments, recipe scroll, ingots and tonic. Repeating it does not duplicate items. It is disabled in Release and has no client RPC. These legacy sample items remain stored placeholders; functional package equipment, materials and recipes come from monster loot. Never hand-edit live character quantities to bypass server validation.

Inventory capacity, ordered occupied slots, stack IDs, quantities, metadata, revision, receipts and two equipped instances share the progression character file. Schema 1 migrates to an empty backpack/equipment; schema 2 retains its inventory and gains empty equipment in schema 3. Invalid records fail with diagnostics instead of deleting items. Successful ownership acknowledgements follow atomic save. Keep `.local` private and outside Git. Trusted internal exchanges retain their 4,096-receipt development limit; normal crafting/pickup uses revisioned client operations, recent persisted receipts and one-use source identities. Never clear receipts to replay grants.

Run `tools/smoke-inventory.ps1 -Capture` for isolated two-client testing. It verifies new empty backpacks, offline grants/replays, native Control drag/drop, move/swap/split/partial and full merge/sort, quantities, revision/identity checks, private ownership, save acknowledgements and exact reconnect/server-restart state. Captures and logs go to ignored `artifacts/inventory-smoke`.

## Loot, equipment and crafting checks

The pre-account economy smoke fixture uses legacy JSON saves and requires adaptation for the account database. Current storage and gameplay regression checks run through validate.ps1, with account and shared-world native checks in smoke-accounts.ps1 and smoke-game.ps1.

For normal play use the server-first commands in the root README. I/E opens inventory/equipment, F picks nearby loot, and selecting a scroll reveals its recipe. The server validates every equip, pickup and craft. Uncollected ground loot is intentionally transient across restart. Equipment/crafting/pickup temporarily drain the save queue and perform a synchronous atomic JSON commit before mutating ownership; this is development storage, not a production database/throughput guarantee. See [ADR 0007](../architecture/decisions/0007-loot-equipment-crafting.md).
