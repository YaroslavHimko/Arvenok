# Skills and Combat UI v0.2 validation

The integration was tested on Windows with Godot 4.7.2 .NET. Debug and Release each passed all 115 regression checks and built with zero warnings and errors. The asset check covered all 41 supplied runtime PNGs.

| Phase | Changes | Verification |
|---|---|---|
| Hotbars | Native controls, bindings and per-character layout storage in the existing Godot presentation component | 97 checks. Two clients edited their own layouts, observed server cooldowns and recovered the same layouts after restart. |
| Combat effects | Deterministic defense windows and lifesteal in the simulation hit pipeline, replicated outcomes and four effect strips | 101 checks. Two clients observed the same timed dodge. Existing combat and animation smoke passed. |
| Weapons | Additive catalog, recipes and loot, MainHand and OffHand transactions, weapon-specific intervals and skill gates | 105 checks. Native equipment and economy scenarios covered shared drops, full backpacks, competing pickups, reconnect and restart. |
| Branches | Six active and six passive nodes, class branches in the existing Skills window, schema 4 migration and persisted cooldowns | 110 checks. Two clients exercised learning and shared defense cooldown restoration. A rendered tree capture exposed overlapping detail text, which was fixed and retested. |
| Spell scrolls | Six new items and additive monster drops, atomic learning through the existing economy request and receipt | 114 checks. Tests covered each scroll, prerequisites, insufficient points, repeat requests, storage failure and older crafting/equipment receipts. |
| Final integration | All three class scenarios and acknowledgement-loss recovery. Added a regression for lifesteal on a partially shield-blocked hit. | 115 checks. The dedicated-server scenarios below passed. |

## Dedicated server and two clients

Every class test ran two Godot clients in the existing forest on a dedicated local ENet server. The harness initialized isolated advanced characters in offline saves so the clients could use the native learning interface and exercise the new skills. Each run compared combat outcomes and final HP on both clients, then verified restored ownership, progression and layout after reconnect and server restart.

| Scenario | Observed behavior |
|---|---|
| Warrior | Frontal parry and Perfect Block consumed a single hit. Guard shared its cooldown with Perfect Block. Sunder Strike and PvP lifesteal agreed on both clients. |
| Mage | Interrupting Flame Lance prevented damage and retained its cooldown. A later cast dealt 27 damage. Arcane Shell absorbed 16 damage across two hits and shared its defense cooldown with Ward. |
| Archer | Piercing Shot dealt 25 damage with a bow. Switching to a dagger disabled the bow skill and enabled Evasive Cut. Switching back restored the original learned bow skill and binding. |
| Lost acknowledgements | Two clients submitted a learn and an equip request, then stopped polling before either reply. The harness observed the durable writes, terminated both clients and the server, restarted them and replayed the original requests. Both returned duplicate success. The Mage kept the learned ability, two unspent points and the spare scroll. The Warrior kept one equipped instance of the original weapon. |
| Existing systems | World smoke compared 18 matching visual effects and 25 matching wandering-monster snapshots. Progression smoke observed two shared kills and 180 EXP. Inventory and economy smoke covered crafting, drag and drop, ownership, simultaneous pickup and persistence. |

Rare scroll drops were verified by killing monsters in the simulation with controlled loot rolls. The multiplayer loot test separately exercised natural deaths, protection and pickup through the server. The class scenarios used local loopback networking. Internet latency, sustained load, and exported client builds were outside these runs. Godot logged a Windows root certificate store diagnostic in the sandbox during startup. Local ENet connections completed.

The pack supplies skill icons and effect strips. New attacks use the existing replaceable character animations because the pack contains no dedicated greatsword or dagger body animations.

## Reproduce

From the repository root, build Debug before running the native tests. Each script prints the location of its per-process logs under `artifacts/`.

```powershell
./tools/validate.ps1
./tools/validate.ps1 -Configuration Release
./tools/smoke-hotbar.ps1
./tools/smoke-effects.ps1
./tools/smoke-weapons.ps1
./tools/smoke-tree.ps1 -Capture
foreach ($class in 'warrior','mage','archer') { ./tools/smoke-skills-v02.ps1 -Scenario $class }
./tools/smoke-skills-disconnect.ps1
./tools/smoke-game.ps1
./tools/smoke-progression.ps1
./tools/smoke-inventory.ps1
./tools/smoke-economy.ps1
```
