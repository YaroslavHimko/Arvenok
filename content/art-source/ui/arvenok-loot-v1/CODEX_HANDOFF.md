# CODEX IMPLEMENTATION HANDOFF — ARVENOK
## Open-world monster loot, equippable gear, direct-use recipe crafting — v0.1

**Project:** Existing 2D open-world Godot 4 .NET / C# MMORPG. Authoritative headless/dedicated server. The user already has a working repository with a 24-slot inventory, combat, monsters, and experience progression, to the extent implemented in that specific repo. **Inspect the repo and never assume these features are implemented in a particular way.** This handoff is design/content, not a replacement repository.

**IMPORTANT:** No arenas, match rounds, matchmaking, gacha, auction houses, warehouses, enchantments, harvesting trees/rocks, or full profession crafting in this milestone. The world is persistent; loot drops are transient world entities. Implement only monster ground loot, equipment, crafting via inventory recipe items, and the associated UI.

## A. First: inspect and plan

1. Find existing item definitions, inventory slots/stacks, persistence, identity handling, network RPCs, monster death/EXP, combat damage, HP, and HUD. Read the current source. Avoid duplicating existing infrastructure.
2. Identify what can be reused and what needs a small extension. Present a short repo-specific plan with the exact real files you will edit, migration risks, and tests.
3. Review `data/items.json`, `data/recipes.json`, `data/loot_tables.json`, and `data/gameplay_rules.json`. These files are **authoritative design input**, not automatically an executable in-game data format. Translate to the repository's current resource/config convention and preserve the IDs and semantic rules.
4. Keep class combat, skill leveling, and networking functional. Compile and test after each phase. Do not claim tests ran without running them.

## B. Canonical v0.1 gameplay

- Four existing monsters: `slime`, `sprout`, `mushroom`, `wolf`.
- Three classes: `warrior`, `mage`, `archer`.
- Two equipment slots per player: `weapon`, `body`. The character has one weapon and one body armor; no helmet/shield/boots/accessory slots yet. The Warrior's drawn shield is part of the base sprite, not a separate gameplay equipment slot.
- Twelve equippable items: six Tier I common drops and six Tier II uncommon items primarily obtained by crafting from recipe scrolls (extremely rare direct drops also configured). See `data/items.json`.
- Equipping a Tier I weapon grants **+1 flat damage once per damaging basic attack/skill resolution**. Tier II weapon grants +2. This applies to warrior, mage, and archer skills without adding new damage types. NEVER per animation frame, projectile visual, or damage-over-time tick.
- Equipping a Tier I body armor grants **+5 maximum HP**; Tier II grants +10 maximum HP. On equip keep current HP unchanged (no free healing); on unequip clamp current HP to new maximum. Recompute final stats from base class + existing level/skill modifiers + currently equipped gear; do not mutate class base stats permanently.
- Tier I requires level 1, Tier II requires level 5, and gear is **class-locked for EQUIPPING**, not for pickup/storage. No gear crits, haste, attack-speed, PvP damage multipliers, stun immunity, random affixes, item enhancement, or other bonuses.
- Any player can collect ingredients, equipment and scrolls; only the correct class/level can equip them.
- Existing inventory remains 24 slots; equipment entities move between backpack and two separate equipped slots without cloning the item.
- Monster death grants existing EXP as before **and separately rolls ground loot once on server**. Do not also automatically insert drops into inventory.
- Loot owner for initial multiplayer implementation: eligible player with highest credited contribution (server side, deterministic tie-break); owner has exclusive pickup for 20 seconds, then public pickup is allowed for 100 seconds, after which uncollected items disappear. This is a prototype ownership policy; party loot will be added later. The ground loot location must be reachable, near the corpse.
- One-use recipes: a recipe scroll lives in the backpack. Selecting it displays needed materials, owned quantities, resulting gear and tier. Using it (double-click or a `Craft` action) immediately attempts crafting if all materials exist, no station or timing. A **successful** attempt consumes precisely one scroll plus ingredients and creates exactly one item; failed attempts consume nothing. No recipe learning/unlock ledger yet. Later learned recipes can be an alternate recipe activation model.
- Existing starter/training equipment should be mapped or grandfathered rather than silently deleted.

## C. Item data model

Ensure current item model supports:
- Stable ItemDefinitionId (string, unique); display name, description, icon, category, max stack, tradable, rarity.
- For equipment: slot, class restriction, minimum level, tier, stat modifiers, stack=1.
- For recipe scroll: recipe reference/output, stack=1.
- Unique ItemInstanceId for each owned equipment item. Material stacks can use stack identity + count.
- Equipment instance ownership represented at exactly one location: backpack slot OR equipment slot OR ground loot, never multiple.
- Enough serialized metadata to support future crafted item quality/affixes without designing the quality system now.

Do not use translated display names, sprite filenames or inventory slot numbers as item identity. Reuse the project's existing server-generated IDs and inventory revision/idempotency mechanism where present.

## D. Monster death → ground loot

### Server-only rolling

When an authoritative monster reaches death state, process the death exactly once. Persist the dead/alive transition or otherwise guard against duplicate death callbacks. Resolve the credited loot owner using server combat contribution/kill attribution (do not let clients claim they killed it).

Read `loot_tables.json`:
- For each `material_rolls` and `keypiece_rolls` entry, run a separate Bernoulli chance and random quantity in inclusive `[min,max]`.
- For `equipment_pool`, `recipe_pool`, `rare_equipment_pool`, first roll the group's chance; on success pick exactly ONE entry, weighted by its positive `weight`.
- Nothing in a group may be awarded if the group chance fails.
- The server creates a ground-loot entity with unique ID, ItemDefinitionId, quantity, owner, spawn/death event ID, timestamp, expiry, and state. If multiple items drop, create a compact pile or separate tokens using existing world approach; avoid item-count explosion if possible.
- Ground loot is transient; durable database storage of unpicked ground loot is not required yet. Server restarts may clear uncollected loot; never clear items already committed to inventory.
- Position must be valid for pickup and not spawned inside water or an obstacle.
- Show small loot-bag pickup marker/ground item sprite and tooltip; use `assets/icons/world/loot_bag.png` unless a better current world-loot renderer exists.

### Pickup workflow (atomic & idempotent)

Request: loot entity ID + request/op ID (and optionally expected inventory revision). On authoritative server:
1. Establish authenticated character identity from the sender, not client-supplied owner IDs.
2. Validate loot exists, is not expired/claimed/removed, player is alive, within **2 tiles** (convert using current game world units), and player has pickup rights at this timestamp.
3. Reserve/lock claim so two simultaneous requests cannot both collect same item.
4. Simulate complete insertion into inventory, merging stacks and checking capacity. Reject if it won't fully fit. On failure leave loot on ground, with no inventory mutation.
5. Commit item insertion and loot removal exactly once. Keep backend updates and persistence consistent; if a process crashes during commit, recovery must not duplicate the item. Use existing transaction or durable operation journal strategy if needed; keep it minimal.
6. Emit inventory updated and world-loot removed events to relevant clients. Repeated op ID should return previous outcome, never grant again.
7. A pick-up already being processed cannot be processed by a second client concurrently.

No client-authoritative loot roll, pickup, ownership, amount, or monster death.

### Display

A drop should be visible in the world. Click the item or use an interaction key near it to pick it up. Show a subtle ground label/icon and optional rarity border (common gray, uncommon blue). Keep labels compact and Y-sorted. Optional nearby-only prompt. An inventory-full attempt should display `Inventory Full` without deleting loot.

## E. Equipment system

Equipment must be represented separately from backpack slots, but owned by the same persistent character. Define `EquipmentSlot.Weapon` and `EquipmentSlot.Body` in the existing style.

### Equip action

An equip request identifies the character's inventory item instance, not an arbitrary item definition. Server verifies ownership, type=equipment, class match, required level, out-of-combat status, and version. "Out of combat" requires at least **5 seconds since dealing or receiving damage**, and no current casting/committed attack state. Failed requests must leave state unchanged.

Equipping from a backpack slot moves that instance to an equipment slot. If an item is already equipped, swap it into the source backpack slot atomically (no free-space problem, because the new item vacated that backpack slot).

Unequipping transfers instance to available backpack slot; if none, reject without losing it.

Never auto-generate extra instances or leave a ghost copy in the backpack. The item survives disconnect, save/load and server restart. Match effective stats and HP to the two slots after every load.

### Stat recomputation

`effectiveDamage = skillRankBaseDamage + equippedWeapon.flatDamage` before existing damage reduction rules; for v0.1 apply once on each server-confirmed damaging attack or single-target skill hit. Do not change animation timing, cast time or cooldown.

`effectiveMaxHP = class/level max HP + equippedBody.maxHP` (plus any already-established legitimate persistent buffs). Recompute from source-of-truth each time, not additive mutation on each equip call. On gaining max HP don't heal; on losing it clamp current HP. Death/resurrection uses current effective maximum as appropriate. An armor swap cannot produce repeated HP.

No separate defense, armor penetration, crit, attack speed, skill rank, or ability-modifier effects for equipment yet.

### UI

- Inventory item tooltip: name, category, tier, class requirement, minimum level, slot, bonus, current comparison, icon, rarity.
- Double-click or context `Equip`; dragging equipment into its compatible equipment slot is allowed if existing UI supports it.
- Equipment panel shows exactly 2 slots (`Weapon`, `Body`), equipped icons, and computed bonuses; use `E` key if unbound or show a tab in existing inventory UI.
- If armor/weapon art doesn't yet support appearance swaps, **keep current animated character sprites**. These PNGs are inventory/equipment **icons only**; do not falsely treat icons as four-direction equipped character sprites. Later add dedicated body/weapon overlay art.

## F. Crafting via recipe scrolls

**Recipe data:** six Tier II recipes in `data/recipes.json`; 3 different class key piece materials are consumed (one each for Warrior, Mage, Archer). Tier I gear is primarily looted and not crafted in this first batch.

Selecting the scroll in the inventory shows a small recipe preview: output item (icon/name/class/level/bonus), required ingredient rows with `owned / required` and green/red eligibility, and a Craft / Use action. Double-click on a recipe (or context-menu `Use`) triggers immediate server crafting when requirements pass. The UI can support both selection preview and direct use without requiring visiting an NPC.

Crafting is deterministic; no success/failure RNG, station, casting time or crafting profession level. The **scroll is consumable, one per successful craft**, unlike a permanently learned recipe. This is a deliberate prototype interpretation of direct-use recipe scrolls; design the logic such that later permanent learned recipes could reuse exactly the same craft transaction.

Server validates:
- identity and ownership of scroll instance;
- recipe exists and is bound to correct scroll;
- item definition output and quantity fixed by server recipe;
- required character level and class for crafting if game rules require it (v0.1: **no class restriction on crafting**, only equip requirement; keep minimum output level an equip restriction);
- quantities across all material stacks;
- full post-craft inventory capacity, taking into account consumed scroll/ingredients freeing space;
- stale/duplicate requests, inventory revision and in-flight lock.

One atomic transaction: consume exactly 1 scroll and ingredient quantities across stacks, insert exactly 1 equipment instance, save updated inventory, respond with created instance ID. If any validation, capacity, persistence or transaction step fails, **all items remain**. Two concurrent requests on one scroll or material pool must not both succeed. Replay with same request ID returns previous result, not second output.

If the player's inventory is full but consumption of scroll/materials would free one slot, allow craft if the resulting inventory fits; do not reject solely based on pre-craft occupied slot count. Failed recipe use must never consume scroll.

On success show crafted item popup and optional sound/particle effect, refresh inventory immediately. No crafting time or recipe-learning window needed yet.

## G. Art package and Godot integration

Visual pack contains 30 PNGs:
- `assets/icons/equipment/*.png`: 12 96×96 RGBA equipment inventory icons (some extracted/reconstructed from concept art; some custom pixel drawings).
- `assets/icons/materials/*.png`: 11 materials/key-piece icons (9 currently from monsters; `wood_log` and `iron_ore` are **future gathering-only placeholders** and intentionally excluded from the current loot tables).
- `assets/icons/recipes/*.png`: six one-use recipe scroll icons.
- `assets/icons/world/loot_bag.png`: ground pickup marker.
- `references/item_contact_sheet.png`: catalog contact sheet.
- `references/ui_preview.jpg`: mockup showing inventory, equipment, and direct-use recipe preview; NOT a playable screen and not a spritesheet.

Place icons in a suitable current art folder and update resource paths in imported item definitions; preserve stable IDs. Godot 4 pixel-art: nearest-neighbor texture filtering, no unnecessary MIP maps, transparent RGBA, consistent icon scaling. Use native Godot `Control` nodes, tooltips, buttons, and drag/drop instead of displaying the mockup as a baked interactive panel.

The icons are **prototype-quality**; a few extracted elements may show minor fringes; don't confuse them with fully rendered character equipment sprites. No new character combat animations are supplied.

## H. Persistent save format and migrations

Use existing character persistence; add equipment slots and ensure inventory item definitions referenced by IDs resolve after loading. Don't delete unknown or legacy items automatically. Provide a versioned migration or safe fallback. Save equipment and inventory consistently; at minimum, equip/unequip and crafting must be durable and atomic or recoverable.

A successful server transaction should never be applied in memory only and then lost on reconnect. Avoid overwriting current inventory with stale snapshots in asynchronous saves. Maintain server-side operation idempotency across reconnects, and ideally over restart for recent committed operations, especially crafting and ground pickup.

## I. Acceptance test matrix

1. Monster death event produces a single loot roll; duplicate death signal cannot reroll.
2. Drops are based on actual monster type and configured probabilities, not client requests.
3. Slime can drop Slime Gel; Mushroom Spores come from Mushroom; Wolf Pelt/Fangs from Wolf; Sprout Fiber from Sprout.
4. Tier I equipment can drop, and Tier II recipes/key pieces can drop according to config.
5. Two clients see the same server-spawned drop and removal after pickup.
6. Only eligible contributor picks up during 20s protection; anybody can during next 100s.
7. Pickup succeeds only in range, on valid loot, with capacity; other players cannot bypass owner protection.
8. Two players racing for one drop yield exactly one successful pickup.
9. Replayed pickup request never duplicates items; a full backpack leaves loot in world.
10. Items stack according to existing max stack logic; equipment and recipe scrolls do not stack.
11. Warrior can equip Warrior sword/armor; Mage cannot equip them but may store them.
12. Level 1 cannot equip Tier II; level 5 can.
13. Equipped item disappears from backpack; swap/unequip preserve exact instance ID and metadata.
14. Unequipping with full backpack safely rejects; swapping from an inventory source slot works even when backpack was full.
15. Equipping is rejected in combat and for five seconds after last damage.
16. Tier II weapon adds exactly +2 damage to a valid skill hit, once, and not for each projectile visual frame.
17. Tier II body raises maximum HP exactly +10; never heals; on removal clamps current HP.
18. Equip/unequip spam never permanently increases damage or HP.
19. Skill ranks still work; no duplicate application of equipment bonuses after reconnect.
20. Recipe selection shows correct `owned / required` material counts, even across stacks.
21. Recipe scroll used with enough ingredients consumes exactly one scroll and listed materials and creates exactly one output instance.
22. Recipe scroll used without enough ingredients consumes nothing, including scroll.
23. Full pre-craft inventory may succeed if inputs consumed free sufficient space; otherwise reject without partial losses.
24. Double-click spam and delayed/duplicated craft packets cannot create two outputs from one scroll.
25. Two simultaneous craft attempts with shared materials cannot overspend.
26. Crafting and gear persist after disconnect and server restart; no ghost copies.
27. Unknown item IDs/corrupted saved entries get safe migration/diagnostic handling.
28. Existing XP, skill upgrades, combat cooldowns, movement, and monsters still function.
29. Headless server still builds/launches; no UI dependencies introduced server-side.
30. UI matches server state and displays useful errors for inventory-full, wrong class/level, missing ingredients and combat-lock.

## J. Implementation order

**Phase 1 — Data:** adapt/import item definitions, 6 recipes, 4 drop tables; verify all IDs resolve; tests for probabilities and missing catalog entries. **Do not** grant items yet.

**Phase 2 — Equipment:** add two slots, equip/unequip/swap, stat recalculation, persistence, equipment panel. Test no cloning or HP exploit.

**Phase 3 — Ground loot:** on-death rolls, world entities, ownership windows, pickup with atomic commit, multiplayer replication, expiry. Test duplication races.

**Phase 4 — Crafting:** direct-use recipe scroll, ingredients preview, atomic consumption+output, idempotency, success/failure feedback. Test full-bag edge cases.

**Phase 5 — Integration/QA:** manual run with two clients plus headless server and automated core invariant tests; adjust any actual codebase constraints, explain files changed and unresolved risks.

## K. Explicit scope boundaries

Do NOT add new monsters, XP curves, character levels, random equipment affixes, refining/enchanting, durability, true equipment paper-doll/weapon overlays, resource harvesting, multi-recipe learning ledger, crafting professions/stations, trading, player markets, currency, quests, NPC shop, party loot, auction house, or broad architectural reorganization.

Build a **small correct shared-world loot→inventory→equip/craft loop**. All operations authoritative. No code generation requested in the chat; implementation belongs in the existing Codex workspace.
