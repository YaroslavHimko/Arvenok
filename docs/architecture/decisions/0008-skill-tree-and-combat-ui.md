# 0008. Skill ownership, equipment and combat timing

The dedicated server owns learned skills, weapon requirements, hit resolution, cooldowns and item ownership in the existing shared world. Godot uses reliable outcomes for short effects and current snapshots for active casts, health and movement. The renderer uses the server's cast name for both local and remote characters. The original character, world and networking scenes still host the game.

## Learning and persistence

Schema 4 adds learned node IDs with ranks and absolute server cooldown deadlines to the character aggregate. Loading a schema 1–3 save preserves class, EXP, level, core ranks, points, equipment, backpack identities and transaction receipts. The migration increments the aggregate revision once. Core ranks spent above rank 1 plus learned branch ranks plus unspent points must equal `level - 1`. A character can invest in both branches. The development core-rank reset rejects a reset that would invalidate learned prerequisites.

Learning uses the existing economy transaction and server-side file repository. The server validates current inventory and character revisions, class, level, prerequisite, point balance, combat state and the exact owned scroll instance. It commits inventory, learned rank, point deduction and the operation receipt in one durable write before sending success. Existing economy requests retain their original fingerprint. Recipe scrolls produce items through crafting. Spell scrolls teach abilities through learning. The six new scrolls, four weapons and two recipes are loaded from the supplied JSON and merged with existing content.

Combat uses simulation time. Saved deadline values use the server's UTC epoch so elapsed time during disconnect or server downtime counts toward expiry. Rejoin retains the existing sanctuary spawn policy and restores remaining skill, global recovery and combat lockouts. An accepted cast does not resume after disconnect. A normal disconnect flushes pending character saves through the existing host queue. Equipment and learning use synchronous durable commits.

Hotbar bindings are local preferences stored per character under `.local/client/hotbars`. Editing a preference cannot teach a skill. Each activation submits an intent and the server resolves the class slot and validates eligibility. Public world snapshots expose weapon type, shield and active effects. Only the owning client receives inventory and progression details.

## Hands and hit resolution

The original serialized `Weapon` and `Body` slots retain their values and display as MainHand and Body Armor. OffHand stores a single Warrior shield. The original `equipment.training_shield` item was already present, so its existing instances become usable and Wolves provide a field source. New characters retain the existing empty-backpack start. Bows, staves and greatswords reserve OffHand. The server prepares all displaced items together and rejects the swap if either item cannot fit. Original core kits work from an empty MainHand. Equipping a dagger switches Archer basics to melee and disables bow attacks. Advanced skills require their actual weapon or shield.

Each server activation can hit a given target once. The server tests the first applicable charged window in order: dodge, frontal physical parry, then frontal Perfect Block. Frontal means the attacker is in the forward 180 degrees according to the defender's server facing at contact. Window end times are exclusive. The remaining hit uses one compatible mitigation or absorb. Guard with a shield applies 45% physical reduction, or 50% with Shield Mastery, and 35% magic reduction. Arcane Shell spends up to 16 absorb over two seconds. Guard and Perfect Block share a cooldown, as do Ward and Shell. A mistimed dodge or parry still leaves its ordinary stance mitigation until expiry.

Sanguine Edge with a greatsword earns healing from actual eligible HP removed. PvE earns 8% with a five-HP activation cap. PvP earns 5% with a three-HP cap. Fractional credit carries to later hits. Healing is limited to missing HP, and avoided, shield-blocked and absorbed hits earn no healing. Evasive Cut sweeps a perpendicular step against the existing map and checks range and sight again before dealing damage.

## Art and validation

The extension's source JSON and handoff are under `content/art-source/ui/arvenok-skills-v02`. Branch attacks reuse replaceable mappings in `apps/game/Assets/Art2D/woodvale-combat/visual_bindings.json`. The supplied pack has no dedicated greatsword or dagger body animations. Dodge, parry, block and healing use the supplied six-frame strips and a label on the affected character. Visual frame timing never determines a hit.

Run the solution checks and isolated multiplayer scenarios from the repository root with a Godot .NET executable on `PATH`.

```powershell
./tools/validate.ps1
./tools/validate.ps1 -Configuration Release
./tools/smoke-game.ps1
./tools/smoke-hotbar.ps1
./tools/smoke-effects.ps1
./tools/smoke-weapons.ps1
./tools/smoke-tree.ps1 -Capture
foreach ($class in 'warrior','mage','archer') { ./tools/smoke-skills-v02.ps1 -Scenario $class }
./tools/smoke-skills-disconnect.ps1
```

Each class scenario starts an isolated dedicated server and two Godot clients. The harness seeds only its throwaway offline saves, then exercises the normal UI and gameplay protocol. It compares both clients' combat events and HP and checks restored ownership, learned skills, saved layouts and repeat requests after reconnect and restart. The simulation tests also drive actual monster deaths with a controlled loot roll so rare scroll drops can be tested reproducibly.

The [v0.2 validation report](../skills-v02-validation.md) records phase results, observed multiplayer behavior and test scope.
