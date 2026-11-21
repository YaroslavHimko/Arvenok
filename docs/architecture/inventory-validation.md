# Inventory v0.1 implementation and validation — 2026-11-19

The implementation extends the existing character aggregate, simulation, session adapter and save repository. No new project, equipment system, item-producing combat rule or world interaction was introduced. [ADR 0006](decisions/0006-persistent-inventory.md) records the boundaries and future transaction requirements. [Item definitions](../../content/items/README.md) describe the catalog and extension points.

| Phase | Result |
| --- | --- |
| Existing baseline | 70 checks passed before inventory changes |
| Domain | 79 checks passed after catalog, immutable stacks and atomic slot/add/remove/exchange rules |
| Server and persistence | 83 checks passed with peer ownership, concurrency, metadata, restart, legacy migration and one-time grants |
| Final Debug and Release | Root solution builds with zero warnings/errors; all 83 checks pass in each configuration; locked restore and asset/architecture checks pass |
| Two-client inventory | New characters have independent empty 24-slot backpacks; offline grants populate each once and repeated grants do nothing |
| Native UI and authority | Actual Godot Control drags move/swap/split/merge, including partial/full merges; sort, quantity labels, I/K/Escape, duplicate/stale requests and foreign stack identities verified |
| Persistence integration | Client success matches durable saved inventory revision; exact character GUID, capacity, revision, slots, item GUIDs and quantities survive reconnect and dedicated-server restart |
| Combat regression | Both clients agree on 18 visual-effect events and 24 common wandering-monster snapshots; interrupt, defense expiry, damage, death, protected respawn and reconnect pass |
| Progression regression | Two shared kills conserve 180 EXP; level-up, rank damage, duplicate upgrades and exact reconnect/server restart restoration pass |
| Assets and visuals | Ten 32×32 SVG placeholders resolve from the catalog; lossless imports and editor import pass. Backpack/inline split captures reviewed at 1280×720 and 1920×1080 windows |

The 13 inventory regression cases cover the specification's rule scenarios in grouped tests: empty/independent inventories, one-item insertion, partial-stack filling, overflow, full rollback, metadata/equipment incompatibility, valid/invalid slot operations, all-or-nothing multi-item removals/exchanges, deterministic sorting, trusted reward/removal replay, stale revisions beyond the 128-client-receipt window, immutable snapshots, parallel preparation, serialized concurrent commit, cross-owner rejection, corruption and concurrent stale-write protection. Persistence includes metadata and permanent trusted operation receipts. Public world snapshots contain no backpack contents.

Reproduce from the repository root:

```powershell
./tools/validate.ps1
./tools/validate.ps1 -Configuration Release
./tools/smoke-game.ps1 -Godot godot
./tools/smoke-progression.ps1 -Godot godot
./tools/smoke-inventory.ps1 -Godot godot -Capture
```

Final local evidence: `artifacts/inventory-debug.log`, `inventory-release.log`, `inventory-combat-smoke.log`, `inventory-progression-smoke.log` and `inventory-final-smoke.log`. Inventory native logs/captures are under `artifacts/inventory-smoke/20261119-065745-8a3944/`; screenshots include `inventory-1280x720.png`, `inventory-split.png` and `inventory-1920x1080.png`. These files are ignored local artifacts, not committed portable fixtures. Window captures preserve the original design aspect ratio and omit side margins.

A separate local `inventory-demo` profile was created with the fixed sample kit. With the default local server running, launch `godot --path apps/game -- --profile inventory-demo` and press I. Other new characters remain empty. Reopening this profile does not grant another kit.

The previously saved `verify-progression-server` development character was also restored through the real local server: schema 1 migrated to schema 2 with 24 empty slots and unchanged identity, class, class lock, level, EXP and skill points. Trusted reward receipt IDs are isolated from client IDs; an automated check confirms client-selected IDs cannot preempt a server reward.

No known item duplication or silent loss remains in the tested operations. Inventory operations are acknowledged after one atomic character-file commit; this is still development persistence, not production multi-owner transactions. Future world loot/crafting/trading coordinators must commit all ownership participants together. Trusted operation receipts currently have a visible 4,096-entry development limit; they are never silently evicted. No load/latency benchmark, destructive power-loss test or production export execution was performed. The offline grant command has a compile-time Release rejection and no RPC. NuGet vulnerability auditing was unavailable under restricted network access (`NuGetAudit=false`); Godot's existing certificate-store diagnostic remains, while local UDP tests passed.
