# ARVENOK — Skill Bar, Branching Trees, Spell Scrolls, Defense Effects & Weapons (v0.2)

## Purpose and limits

Integrate this extension into the **existing Godot 4 .NET / C# open-world MMORPG**. The existing codebase already has some combination of character classes, combat, server-authoritative networking, monster drops, inventory, crafting, equipment, character EXP/levels, and persistence. **Inspect the repository first.** Do not create a new Godot project, replace existing working code, or introduce a MOBA/arena model.

This pack supplies **design-reference configuration files**, **32 usable independent PNG icons (96×96)**, **four six-frame transparent placeholder VFX strips**, **UI frame images**, and mockups. It does not supply ready-to-use combat animations for every weapon, nor does it include any fully implemented Godot scenes. Keep animations modular and reuse the existing action/VFX system.

The player goal: arrange skills in a customizable bar, see server-timed cooldowns, unlock class branches using skill points, occasionally find and consume monster-dropped spell scrolls to learn advanced skills, wield two-handed swords or daggers, and see deterministic lifesteal/dodge/parry/block combat feedback.

### Before editing — required reconnaissance

1. Map current player, monster, skills, class definitions, networking, actor animations, inventory/item definitions, equipment, loot tables, progression and persistence. Confirm which pieces work, including client/server roles.
2. Identify existing ability identifiers. The JSON names in this pack are **proposed stable names**, not proof of actual IDs. Map to current IDs; do not duplicate core skills.
3. Confirm existing numerical tuning and server timing. Preserve existing original four skills/ranks, level EXP progress, item ownership and loot rules. Do not reset characters.
4. Provide a brief integration plan listing file edits, state schemas/migrations, and functional test phases. Report deviations from these design defaults where they conflict with real code.

## 1. Config files

- `data/asset_manifest.json`: exact icon file paths and sizes.
- `data/new_skills_v02.json`: twelve new skills (six passives and six actives) plus four existing core-skill references per class.
- `data/skill_tree_v02.json`: class branch nodes, prerequisites, level gates, scroll requirements.
- `data/spell_scrolls_v02.json`: six scroll item definitions and additive initial drop rolls by monster.
- `data/new_equipment_v02.json`: four new weapon definitions plus hands/equipment rules.
- `data/effect_rules_v02.json`: deterministic combat effects and resolution order.
- `data/hotbar_defaults_v02.json`: initial layout, customization, save fields, and UI behavior.

Read these as **content specifications**. Convert to the existing project content pipeline: Godot Resources, JSON, database seed definitions, or C# data structs according to existing conventions. **Do not rely on clients to supply damage, cooldown, loot odds, skill rank, or weapon stats.**

## 2. Skill panel / hotbar

### Visual specification

- Default one 8-slot bar at bottom-center; support one to three bars with 8, 10, or 12 slots each (up to 36 total slots if all three enabled).
- Each slot shows an icon, configurable hotkey, optionally skill rank, disabled/locked state, cast/activation feedback, and cooldown.
- Cooldown: radial dark wedge or circular overlay, plus numeric `4.8s` or `12s` countdown. Numeric display can use tenths for under 10 seconds.
- Distinguish **personal skill cooldown**, **global action lockout**, and **casting progress**. A skill can show its own cooldown even while global lockout runs.
- Skill on cooldown is dimmed, but still visible and tooltip-accessible. A skill requiring a different weapon should be visually disabled with an explanation; do not clear its slot when gear changes.
- Active skill icons are draggable from Skills window, Skill Tree learned list, or other hotbar slots; drag-to-reorder, swap and clear are supported. Unlearned skills are not assignable.
- Position, visibility, bar count, row orientation, scaling (80%–125%), lock/unlock dragging, keyboard bindings and cooldown label toggle are configurable.
- Controls must not cover essential world clicking unexpectedly; use correct mouse filtering and UI focus.
- The hotbar saves per character. Layout changes are **presentation data** only and cannot grant skill ownership, reduce cooldowns or create new abilities.
- Use native Godot `Control` UI. Godot `Control` provides drag/drop callbacks; Godot `TextureProgressBar` supports radial fill modes. Do not make an entire panel a single image: icons and buttons need to be live controls.
- Keep initial shortcuts compatible with current bindings. Suggested first bar `1`–`8`; additional bars unbound unless explicitly configured. Do not steal the inventory `I` or existing actions. Skill tree can use `K` if available.
- Pressing a hotkey when disabled does not send invalid repeated RPCs; provide clear feedback for range, missing weapon, no target, casting, cooldown, safe-zone restrictions.

### Cooldown networking

- Server owns accepted cast start, skill availability, cast duration, effective recovery and cooldown end time. Client renders countdown using synchronized server clock/timestamp or time remaining from authoritative event.
- Server must validate ability use regardless of what the UI displays.
- Cast/interrupt/death/weapon-change events update appropriate UI states immediately; an old event must not overwrite newer authoritative state.
- Reconnect shows accurate remaining cooldowns (or resets only if that is explicitly established by the existing game design), never a free client-created reset.

## 3. Skill tree & existing progression

Preserve existing **three classes**: Warrior, Mage, Archer, and their **four active core skills each**, with existing ranks (previous v0.1 max rank 4), baseline stats, cooldowns and already-spent points.

Each class gains **two specializations**, each with **one passive node and one active scroll-taught node**. For v0.2, these new nodes have max rank 1. The current prototype's level cap remains **10**, retaining one skill point earned per level. No automatic reallocation, re-granting, or reset of existing points.

- **Warrior Guardian:** `Shield Mastery` passive (requires Guard rank 2 and level 4); `Perfect Block` active (requires Shield Mastery, level 6, one skill point, matching spell scroll, and shield equipped to use).
- **Warrior Reaver:** `Sanguine Edge` passive (requires Crushing Blow rank 2 and level 4); `Sunder Strike` active (requires passive, level 6, one point and scroll; two-handed sword required for casting).
- **Mage Ember:** `Embers` passive (requires Firebolt rank 2 and level 4); `Flame Lance` active (requires passive, level 6, point + scroll and staff to cast).
- **Mage Aegis:** `Ward Weaving` passive (requires Arcane Ward rank 2 and level 4); `Arcane Shell` active (requires passive, level 6, point + scroll and staff to cast).
- **Archer Marksman:** `Keen Eye` passive (requires Power Shot rank 2 and level 4); `Piercing Shot` active (requires passive, level 6, point + scroll and bow to cast).
- **Archer Skirmisher:** `Light Footwork` passive (requires Backstep rank 2 and level 4); `Evasive Cut` active (requires passive, level 6, point + scroll and dagger to cast).

**Branches are not exclusive in v0.2.** Points and weapon conditions produce trade-offs. Do not permanently lock a character into a path; a branch reset NPC/system is unnecessary for this milestone.

Tree UX:

- Class-specific tree only. Show four existing core skills at the root, and two branch columns below, each with an unlockable passive and active node linked by connectors.
- Nodes display learned / available / requirement unmet / scroll needed / can upgrade. On selection display exact current and future effects, level gate, prerequisite, weapon condition and scroll.
- Scroll ownership does not automatically teach or grant points. A character with a matching scroll may inspect it from inventory and click **Learn**; the server must verify prerequisites and deduct **one skill point + one scroll** atomically before adding the learned skill.
- If requirement is missing (wrong class, rank, level, point, safe state or scroll), no point, scroll or rank changes. Duplicate scrolls are ordinary tradable inventory items and cannot grant duplicate learning.
- Learned skills are permanent, even when the required weapon is unequipped; they merely become unusable until appropriate equipment is worn.

### Server progression / migration

- Character saved state includes learned node IDs and ranks, unspent/spent skill points, and current server validated weapon compatibility.
- Add a versioned migration to current persisted characters. **Preserve** EXP/level, original four rank values, original skillpoint balance and previous inventory.
- No double skill point awards. Do not migrate a branch icon or UI slot into an actually learned skill.
- All prerequisites and scroll/point spending must be validated on the dedicated server, ideally in one inventory/progression transaction.

## 4. Spell scroll monster drops

Six new **spell scroll item types** (not the existing crafting **recipe scroll** type). Scrolls have item IDs `scroll.skill.<class>.<skill>`, stack size 1, and a shared scroll icon in this pack. They are world loot through the **existing server authoritative loot pipeline**.

Suggested additive test drop percentages: Slime 0.5% Perfect Block; Sprout 1.5% Piercing Shot and 1% Arcane Shell; Mushroom 1.8% Flame Lance and 1% Evasive Cut; Wolf 1.8% Sunder Strike, 1.2% Evasive Cut, 1.2% Perfect Block. Numbers are preliminary, not a loot-economy promise. The sample table is **not complete coverage of all possible future monsters**.

- Merge with existing material/weapon/recipe loot, don't replace it.
- Loot items must be generated once per unique monster death and protected against duplicate pickup requests; use existing loot credit and ownership rules.
- Inventory-full conditions preserve remaining ground loot.
- Client may not choose loot result, request direct item grants, or learn a scroll it no longer owns.
- Learning cannot occur during combat. For initial design, being in combat means damage dealt/received within past five seconds, unless the existing project already has a defined combat-state policy.
- Learning success includes a clear toast and the newly available skill appears in Skills window, but does not automatically overwrite player hotbar layout.

## 5. Additional weapons & equipment slots

Add **two-handed sword and dagger** item subtypes without discarding earlier one-handed swords, bows, staves or armor. `data/new_equipment_v02.json` supplies four weapons and a fallback shield item definition (only if no existing shield definition):

| Item | Classes | Level | Damage bonus | Basic interval | Notes |
|---|---|---:|---:|---:|---|
| Weathered Greatsword | Warrior | 2 | +2 | 1.40s | two-hand, no shield |
| Forged Greatsword | Warrior | 6 | +3 | 1.40s | two-hand, no shield |
| Hunter's Dagger | Warrior, Archer | 2 | +1 | 1.15s | melee range 1.5 tiles |
| Nightsteel Dagger | Warrior, Archer | 6 | +2 | 1.15s | melee range 1.5 tiles |

- Also extend current world loot tables with the **additive weapon and recipe scroll entries** in `new_equipment_v02.json`: Weathered Greatsword and Training Shield from Wolf, Hunter's Dagger from Sprout/Mushroom, and low-chance crafting recipe scrolls for Forged Greatsword and Nightsteel Dagger. Use the existing atomic crafting system and existing material identifiers for those two optional equipment recipes. A crafting recipe scroll is NOT a spell scroll.
- If the project currently lacks shield items, create the Training Shield. Grant exactly one to **new Warrior characters only** via an idempotent starter-equipment path (not every login). Existing Warriors can find it as monster loot.
- Existing one-handed swords remain compatible with shield if class rules allow. Bows and staves count as two-handed. Initially no dual-wielding.
- Model `MainHand`, `OffHand`, `BodyArmor` logically. A two-handed weapon occupies `MainHand` and reserves `OffHand` as a lock, not as a duplicate item.
- Equipping a 2H weapon while a shield is equipped must atomically move displaced shield to backpack; if no room, **reject operation** with no item change.
- Item equip/unequip and combat stat recomputation remain server-authoritative and persist through restart/reconnect. Weapons may not be changed while in combat.
- Apply bonus damage **once per successful direct attack/skill**, and only after proper attack/cast validation, never per VFX frame.
- For Archer wearing a dagger, bow skills are disabled (not forgotten). The dagger changes basic attack from bow ranged to melee. Warrior retains the ability to interrupt: reuse `Shield Bash` gameplay with a `Hilt Strike`/`Knife Break` presentation alias without a shield, still 5 damage and same interrupt timing/cooldown. Do not silently remove Warrior's interrupt tool for Reaver builds.
- Additional equipping and loot should use the existing inventory transaction system and maintain stable item-instance ownership.

## 6. Lifesteal, dodge, parry, block — deliberate rather than RNG

**Do not create chance-based dodge/parry/block stats in this version.** All three defensive outcomes are deterministic responses to a server-validated defensive window or mitigation state. This is important to the design goal of fewer arbitrary PvP balance patches.

### Combat resolution order (server-side)

1. Validate hit and target, world/safe zone/line-of-sight and effective contact time.
2. If a valid **Dodge** window can evade this **single targeted direct hit**, consume that one evade charge, send `Dodged`, apply zero damage.
3. Otherwise if a valid **Parry** window matches a **single physical direct attack from the front**, consume the charge, send `Parried`, apply zero damage.
4. Otherwise if **Perfect Block** window matches a **single frontal direct attack** (physical or magic), consume the charge, send `PerfectBlocked`, apply zero damage.
5. Else apply ordinary `Guard`/shield block/ward mitigation (never stack reductions multiplicatively; use a single highest compatible effect or a clear explicit policy) and reduce current HP by final damage.
6. Apply eligible **Lifesteal** from actual positive HP damage to the attacker, capped and never beyond max HP. Broadcast combat feedback and status state.

Define deterministic tie-breaking when two defenses are simultaneously valid. Reject exploit attempts involving repeated same-hit IDs. Damage numbers, animations and sounds are **reactions** to server events; VFX must not resolve combat independently.

### Lifesteal

- `Sanguine Edge` passive only with a two-handed sword: 8% of eligible direct HP damage in PvE, 5% in PvP, with a small fractional accumulation bucket for low damage hits.
- Healing caps: no more than **5 HP per ability activation** in PvE and **3 HP per ability activation** in PvP (can tune later). Never heal more than missing HP.
- Excludes blocked/dodged/missed hits, environmental damage, DOT, reflected damage and other actors' damage. Multi-target or multi-hit abilities must enforce cap **per activation**, not per target, to prevent AoE lifesteal scaling.
- Display small red lifesteal/heal VFX and green `+HP` feedback on the actual recipient only.

### Dodge

- `Evasive Stance`: keep its prior 35% mitigation for 2 seconds and add a short **0.25s first-hit dodge window** that avoids *one direct targeted hit*. This does not make the Archer immune to area effects or environmental damage.
- `Evasive Cut` grants a 0.25s dodge window during its short validated side-step. Avoidance applies only while the authoritative window is active. No random evasion percentage.
- Blink and Rush do **not** automatically grant invulnerability. Movement and collision still remain authoritative.

### Parry

- With a two-handed sword, Warrior's existing Guard becomes **Parry Stance**: retains baseline 35% mitigation for two seconds but allows one frontal **physical direct hit** to be parried during the first 0.35s. This replaces, rather than stacks with, the shield Guard variant.
- Parry does not negate Firebolt, magical area effects, or attacks from behind; facing must be evaluated on the server. Keep Guard's existing recovery and 16s cooldown unless the game code already has a shared cooldown definition.

### Block

- Baseline Guard remains 35% reduction over two seconds. With a shield, Guard reduces physical damage by 45% while keeping magic mitigation 35%; Guardian passive Shield Mastery raises physical reduction to 50%.
- Guardian active Perfect Block: single frontal direct hit negation within 0.4s, 18s cooldown, requires shield. It shares a **major defense cooldown group** with Guard, preventing back-to-back Guard + Perfect Block stacking.
- Mage Arcane Shell similarly shares a defense cooldown group with Arcane Ward and cannot stack with it. Implement as 16 HP absorb for two seconds.
- One active defense effect source should win by deterministic precedence; prevent two simultaneous mitigation effects from multiplying into near-immunity.

### VFX / animation feedback

Included temporary six-frame transparent sprite strips `assets/vfx/lifesteal_6frames_96.png`, `dodge_6frames_96.png`, `parry_6frames_96.png`, `block_6frames_96.png`. Each strip has six **96×96** cells in one row, total width **576×96**. Play once; do not loop. Display outcomes over the relevant character with short, readable colored labels, e.g., `DODGE`, `PARRY`, `BLOCK`, `+2 HP`.

Reuse existing battle animations; these strips are **simple temporary effects**, not complete weapon-attack animations. If a 2H/dagger attack pose is missing, reuse existing animation with an honest placeholder and list required production assets. Do not switch logic based on animation frame indices.

## 7. Implementation phases

**Phase 1: UI only.** Import PNG icons using nearest-neighbor filtering, add hotbar controls, castbar and cooldown overlays, draggable skill layout and settings; preserve existing 4 skills/class. Build and show two clients with local per-character hotbar preferences. No changes to skill effects yet.

**Phase 2: Shared combat effects.** Implement deterministic lifesteal, dodge, parry, block hooks in the existing server damage pipeline; add status events and VFX. Do not implement RNG percentages. Verify all effects across two clients.

**Phase 3: Weapon compatibility.** Add 2H sword and dagger definitions/equipment hand constraints, keep old gear, and implement atomic 2H/offhand swap, weapon-specific combat range/interval, class compatibility, existing skill aliases and persistence.

**Phase 4: Tree nodes.** Add passive nodes, active nodes, level/rank prerequisites, skillpoint spending, migrations and skill tree UI. Maintain old progression exactly.

**Phase 5: Scrolls and monster loot.** Add six scroll items into existing monster drop tables, inventory preview, server-atomic learn action, and world notifications. Keep current XP, materials, recipes and equipment drops.

**Phase 6: Hardening and balance.** Automated combat-state tests, server/client sync, disconnected/reconnected characters, malicious RPC attempts, simultaneous pickups, and tuning across all three class matchups. Confirm no battle arenas or MOBA systems added.

Each phase should compile, be self-contained, and use existing test infrastructure where available. Stop and report actual blockers or contradictory existing architecture rather than silently rewriting unrelated code.

## 8. Acceptance tests

1. Two simultaneous clients can see own and each other's new skill VFX while maintaining the same server-authoritative damage result.
2. Skill icons can be dragged from learned skill list to any allowed hotbar slot; swap and unassign work.
3. Character has up to three hotbars, per-slot hotkeys, adjustable orientation, visible numerical cooldown, position lock and character-specific persistence.
4. Cooldown displayed from authoritative server timing; reconnect does not reset it or allow early casting.
5. Existing class skills, XP, bought ranks, character inventory, crafted equipment and monster loot remain functional.
6. Each class displays its two tree branches with correct level, skill-rank, scroll and point prerequisites.
7. Unlearned scroll ability cannot be cast via a forged RPC; learning wrong-class scroll or with no points does not consume anything.
8. Repeating a learn RPC with same request ID does not consume two scrolls or award multiple copies of a skill.
9. Already-learned scroll cannot be consumed again for another rank. Existing crafting recipe scroll behavior remains unchanged.
10. Monster kills can drop spell scrolls **in addition to** existing loot, respecting original loot ownership.
11. Equipping 2H sword blocks shield; no backpack space means equip action fails without dropping, cloning or overwriting anything.
12. Dagger Archer cannot fire Power Shot with dagger, but can switch back to bow outside combat; learned skill stays learned.
13. Warrior on 2H/dagger maintains their basic interrupt mechanic using same cooldown group/logic.
14. A mistimed parry fails; a precisely timed frontal physical hit is negated. Magic and rear attacks are not parried.
15. Successful dodge negates one targeted hit but not AoE/environment damage; never randomly dodges outside the explicit window.
16. Shield Guard reduction and Guardian passive values are accurate; Perfect Block lasts 0.4s and shares major defense cooldown with Guard.
17. Mage Arcane Shell cannot stack with Arcane Ward; 16-point absorb is consumed correctly across attacks.
18. Lifesteal is based on final actual direct damage, capped per activation and per target rules; cannot overheal or trigger from dodged/blocked damage.
19. Disconnect during scroll learning/equipping never produces a duplicated item or lost skill point. Restart restores equipped weapons, learned nodes, cooldown policy, and bar layout.
20. Changes work in the **existing open world**, including PvE and PvP legality, never adding match timers, arenas, or matchmaking.

## 9. Expected Codex response

Before changes, summarize discovered current architecture and map each new feature to existing classes/scenes. After each phase, state files modified, key integration decisions, tests run (and not run), evidence that clients and dedicated server behave correctly, and any art limitations. Avoid inventing tests or claiming a working Godot run when compilation or engine tools were unavailable.
