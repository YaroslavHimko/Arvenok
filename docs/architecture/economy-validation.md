# Loot, equipment and crafting validation

Implementation date: 2026-11-20. Input: Arvenok Loot, Equipment & Crafting v0.1. Read and retained the handoff and all four data JSON files before implementation. Existing simulation, inventory revisions/receipts, contribution ledger, character repository, native backpack and ENet transport were extended; the forest, animations, skill ranks and EXP rules remain in place.

## Incremental phases

| Phase | Result |
|---|---|
| Data | Imported all 30 PNGs and 29 definitions; retained ten legacy IDs. Six recipes and four loot tables validate references, restrictions, probabilities and weights. 84 checks passed. |
| Equipment | Two separate slots, exact-instance swaps, restrictions, effective damage/HP, schema-3 migration and durable commits. 87 checks passed. |
| Loot | Once-per-monster-life rolls, contribution ownership, reachable tokens, protection/public/expiry, atomic pickup and snapshot replication. 89 checks passed. |
| Crafting | Exact scroll consumption, ingredients across stacks, one output, full-bag handling and persisted operation results. 92 checks passed. |
| Integration | Added all-class rank/defense combinations and contribution-tie coverage. Debug and Release builds: 94/94 checks, zero warnings/errors. Native economy, inventory, combat and progression scenarios run separately. |

## Handoff acceptance coverage

| Handoff cases | Verification |
|---|---|
| 1–4: death rolls and configured sources | Natural-death/life guards, forced success/failure boundaries for each species, invalid probability/reference/weight rejection and deterministic statistical sampling. No gathering-only materials enter tables. |
| 5–9: shared loot, protection, public pickup, race/replay/full bag | Two real clients compare identical drop identity, item, position, owner and timestamps. Non-owner pickup rejects during protection; a full backpack rejects after public access. Both race one token; exactly one wins and both see removal. Winning receipt replays safely after reconnect/restart. Core tests cover range, expiry and failed storage without loss. |
| 10–14: stacking and equipment ownership | Existing inventory tests plus class/level gates, exact ID/metadata preservation, full-bag swap, full-bag unequip rejection, native equipment controls and item persistence. |
| 15–19: combat restrictions and bonuses | Five-second lock, no cast/attack/recovery/dash mutation, +2 once for basic/primary/interrupt across all classes, rank combination before defense, unchanged cooldowns, no-heal armor swaps, HP clamp and effective max HP after restore. |
| 20–25: recipe preview and atomic use | Native output icon/requirements and green/red counts; all six recipes tested, including level-1 cross-class crafting, split ingredient stacks, exact selected scroll, missing inputs, full pre-craft backpack, stale/concurrent input requests and duplicate output prevention. Current one-output recipes always fit the slot freed by their stack-one scroll; generic exchange tests cover larger-output capacity rollback. |
| 26–27: persistence and corruption | Whole-character commits survive reconnect and full server restart with unchanged IDs, metadata, gear and receipts. Injected write failures preserve inputs/ground claims. Schema 1/2 migrate once; unknown IDs, duplicate instances and corrupt records fail with diagnostics without overwriting them. |
| 28–30: preserved gameplay, headless host, useful UI | Existing combat/animation, inventory and progression smokes; engine-independent packages; native UI error text for wrong class/level, combat lock, missing ingredients, protection and full inventory. Rendered captures at 1280×720 and 1920×1080 window sizes. |

Run from the repository root:

```powershell
./tools/validate.ps1
./tools/validate.ps1 -Configuration Release
./tools/smoke-game.ps1
./tools/smoke-inventory.ps1 -Capture
./tools/smoke-progression.ps1
./tools/smoke-economy.ps1 -Capture
```

Build summaries are in ignored `artifacts/economy-debug.log` and `artifacts/economy-release.log`. Economy smoke logs/captures are in timestamped `artifacts/economy-smoke/` directories; other smoke tools have their own directories. The new scenario uses isolated offline fixture saves and normal client intent; normal profiles receive no test equipment or progression. Test setup initially exposed an invalid fixture launch flag, a selection-refresh issue and GDScript's JSON integer conversion on replay; these were corrected before the passing integration run.

Final recorded evidence:

- Economy: `artifacts/economy-smoke/20261120-031332-d1b8bf` — native equipment drag/unequip, recipe use/replay, F pickup, one race winner, exact reconnect/restart restoration, and reviewed UI/world captures.
- Combat: `artifacts/smoke/20261120-031144-4a3dc2f1` — 18 matching visual effects and 25 matching authoritative monster snapshots, with combat/respawn/reconnect preserved.
- Inventory: `artifacts/inventory-smoke/20261120-031145-050da0` — existing stack operations and persistence passed; rendered inventory captures also passed in `20261120-030209-8ba674`.
- Progression: `artifacts/progression-smoke/20261120-031006-6ff6e2` — two shared kills, 180 total EXP, rank damage and exact restoration passed.
- All 30 imported PNGs were checked byte-for-byte against the supplied ZIP. The existing `inventory-demo` profile also reconnected to the updated default server and migrated schema 2 to 3 with all 11 occupied slots, item IDs/quantities and progression unchanged.

## Limits and compatibility decisions

- No missing package PNGs, equipment definitions or recipes were found. These are icons, not new equipped-character animation overlays; original animated characters remain visible.
- The ten legacy definitions remain stored and resolvable. Training sword/shield and the old sample scroll are explicitly nonfunctional placeholders; old `resource.*` and `monster.wolf_pelt` IDs are not silently converted into package materials.
- Ground loot is transient. Restart clears uncollected tokens; collected items are durable. Fault injection and process restarts were tested, but physical power-loss/storage-controller failure, production database recovery, exports, WAN loss/latency and large-scale soak/load remain unverified.
- The JSON backend synchronously saves economy commits after draining earlier writes. This keeps ownership atomic in the prototype but may stall simulation under slow storage; it is not a production throughput claim. Recent successful client receipts retain 128 operations; older exact replays fail their revision/source checks.
- Locked dependency restores were run with `NuGetAudit=false` in the restricted-network environment; online vulnerability audit was unavailable. Godot emitted the existing local certificate-store diagnostic while ENet tests completed successfully.
- No harvesting, trading, enchantments, extra slots, learned recipes, new monsters, match systems or unrelated progression were introduced.
