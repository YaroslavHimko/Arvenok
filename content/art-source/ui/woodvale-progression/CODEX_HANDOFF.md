# CODEX HANDOFF — Open-world EXP, leveling, skill ranks, and HUD (v0.1)

## NON-NEGOTIABLE CONTEXT

This is an existing Godot 4 .NET / C# 2D open-world MMORPG with an authoritative dedicated server. It is **not** an arena or MOBA. The current project already has work in progress. Do not create a new Godot project or replace existing systems. Read the current repository, its networking/combat/monster code, its HUD implementation, and the accompanying `asset_manifest.json` first. Reuse current world entity IDs, character ownership, persistence, and data conventions whenever possible. Propose a short file-by-file integration plan before editing. Make minimal incremental changes and test each step. Avoid adding code to this conversation; this is a specification for Codex in the user's IDE.

## FEATURE GOAL

Monsters give EXP when defeated. Player characters gain levels. Each level after level 1 grants exactly one skill point that may be invested into an existing class skill. Skills improve in a restrained, mechanically meaningful way. The HUD shows EXP and current level, and the player can open a skill-upgrade panel with clear current-versus-next-rank effects. Both progress and skill ranks survive reconnects. All awards and upgrades are server-authoritative.

## PROGRESSION RULES

- Initial test level cap: 10. Starting level: 1; starting EXP: 0; starting unspent skill points: 0.
- Every character begins with their 4 class skills at rank 1, with the same basic attack as the current combat system. Basic attack cannot be ranked in this phase.
- Each transition to a new level grants **one unspent skill point**. Skills cap at **rank 4**.
- By level 10: 9 upgrade points earned; all 4 skills would need 12 points to max, so meaningful choices remain.
- Required character level to purchase rank 2 = 2; rank 3 = 4; rank 4 = 7. Unspent points carry over. A player can save points and allocate them later.
- No stat allocation UI, additional skills, loot rarity system, talent trees, secondary EXP bar, prestige, or XP from PvP kills in this milestone.
- Do not automatically increase base attack damage or HP with character level yet. The earned skill ranks are the initial progression reward. Preserve balance testing around equal-level characters.
- Level-up awards can process several levels in one XP grant (loop until current EXP below requirement or cap reached). At cap, set EXP progress to 0 and display MAX; no XP accumulation for future releases by default.
- Required EXP for the next level, where L is current level 1-9: `80 + 35*(L-1) + 12*(L-1)^2` (whole number). Requirements by level: 1:80, 2:127, 3:198, 4:293, 5:412, 6:555, 7:722, 8:913, 9:1128.
- Store **EXP within the current level** as an integer; preserve the remainder on leveling. Avoid floating-point currency or fractional EXP.

## MONSTER XP SOURCE AND LEVEL-DIFFERENCE RULES

- Every monster definition/spawn instance needs a level and configurable `baseExp` or equivalent. Prefer explicit values stored with monster data, not separate XP logic in each monster script.
- Suggested initial examples: Level 1 Slime 10 EXP; Level 2 Sprout 18 EXP; Level 3 Mushroom 31 EXP; Level 4 Wolf 52 EXP. These are tuning starting points, not hardwired by sprite kind; allow variants of the same monster at different levels for later maps.
- Level difference adjustment uses `playerLevel - monsterLevel`:
  - Difference <= 2: 100% of the player's eligible share. A higher-level monster's configured XP already reflects its difficulty; don't add another arbitrary higher-level XP bonus.
  - Difference = 3: 50%.
  - Difference = 4: 25%.
  - Difference >= 5: 0%.
  - Round final awarded EXP to nearest integer consistently. Award 0 if the rounded value is 0.
- Apply level penalty independently to each recipient and **after** the base monster XP has been split. Low-level mob farming should not remain optimal indefinitely.
- PvP kills, despawns, GM commands, deleted monsters, and environmental non-combat cleanup award no EXP.
- Monster resurrection/respawn creates a new life instance. The same death must never grant EXP twice.

## EXP CREDIT — ROBUST SMALL-SCOPE BASELINE

- The server is the sole EXP authority. An authoritative monster death produces one unique, processed-once death event.
- Track effective combat damage per attacker (cap counted damage at the target's remaining HP; ignore overkill) across the most recent 15 seconds. Only attackers with >=10% of the monster's maximum HP contributed and who are alive and within 12 tiles at death are eligible. Allow one attacker to receive full base EXP if it is the sole eligible contributor.
- Split the monster's total base EXP among eligible attackers proportionally to their recorded eligible damage; distribute integer remainders deterministically so the total never exceeds the monster's XP budget. Then apply each player's level-difference modifier.
- Contributions are assigned to the trusted owner of a player-controlled actor, not to untrusted client-supplied character IDs. No credit to a disconnected character unless supported by the existing persistent session rules.
- These rules avoid last-hit stealing and XP duplication. Party XP sharing/healing contribution can be added later through a distinct, explicit policy; don't invent a party system in this implementation.
- Remove/deactivate contribution data when a monster dies, resets, or despawns; bound memory usage.

## SKILL UPGRADE TABLE — ALL RANKS SERVER-OWNED

Each class retains basic attack plus the same four active skills from current combat design. Do not introduce fifth skills. All values below are initial PvE/PvP shared tuning; keep the server's rank-to-effect configuration data-driven and versionable.

**Warrior**
- Crushing Blow damage at ranks 1/2/3/4: **18 / 20 / 22 / 24**. Windup 0.7s, cooldown 7s unchanged.
- Rush maximum travel distance: **4.0 / 4.2 / 4.4 / 4.6 tiles**. Cooldown 16s unchanged. Respect collision/pathing; no automatic damage or stun.
- Shield Bash damage: **5 / 6 / 7 / 8**; cooldown **12 / 11.5 / 11 / 10.5s**. Interrupts active eligible casts, never causes an additional silence or stun.
- Guard protection: **35% damage reduction** for **2.0 / 2.2 / 2.4 / 2.6s**. Cooldown 16s unchanged.

**Mage**
- Firebolt damage: **20 / 22 / 24 / 26**. Interruptible cast 1.0s, cooldown 7s unchanged.
- Blink max range: **3.2 / 3.4 / 3.6 / 3.8 tiles**. Cooldown 16s unchanged. Valid server-authorized destination only; cannot cross impassable walls under v0.1 rules.
- Counterspell damage: **5 / 6 / 7 / 8**; cooldown **12 / 11.5 / 11 / 10.5s**. Interrupts active eligible casts only; no silence.
- Arcane Ward: **35% damage reduction** for **2.0 / 2.2 / 2.4 / 2.6s**, cooldown 16s unchanged.

**Archer**
- Power Shot damage: **19 / 21 / 23 / 25**. Interruptible windup 0.85s, cooldown 7s unchanged.
- Backstep distance: **3.2 / 3.4 / 3.6 / 3.8 tiles**. Cooldown 16s unchanged. Collision-aware; no invulnerability.
- Disrupting Arrow damage: **5 / 6 / 7 / 8**; cooldown **12 / 11.5 / 11 / 10.5s**. Interrupts active eligible casts only, no root/stun.
- Evasive Stance: **35% damage reduction** for **2.0 / 2.2 / 2.4 / 2.6s**, cooldown 16s unchanged. No random evasion.

Range and cooldown modifiers are potentially more valuable in PvP than raw damage. Keep all definitions in one tunable server-owned configuration and run the matchup tests before considering release. Preserve the visual skill animations already in the project. The rank changes should affect **actual combat calculations**, not merely tooltip numbers.

## RESPEC FOR TESTING

- Include a development-only, server-authorized reset of spent skill ranks back to rank 1, refunding exactly the earned skill points for the character. This makes repeated balance comparisons possible without leveling a fresh character. Do not expose unlimited in-combat respec to players. A future player-facing respec should happen at a protected town NPC while out of combat.

## LEVEL-UP + SPEND POINT PROTOCOL

- On authoritative monster death, compute eligible EXP and invoke one atomic player progression operation. Progress can level up multiple times.
- A server event/state update communicates character ID, total displayed level, EXP within level, XP to next level, unspent skill points, and current skill ranks to the owning client. Nearby players only need visible level-up emotes, not private EXP values.
- Skill upgrades are requested by stable skill ID only. Derive character identity from the authenticated network session and server state. Validate: right class, owned character, required level reached, enough unspent points, rank < 4, skill is one of the four; consume exactly one point and increment rank once.
- Requests must be idempotent or duplicate-safe; rapid double-clicks and replayed requests must not spend two points accidentally. If two legitimate upgrades are permitted, they must be separate deliberate accepted operations.
- Set balance values from authoritative rank when calculating cast/damage/movement, not a client-submitted rank.
- Rank changes must not retroactively modify casts already running: snapshot relevant rank-derived effects at server cast acceptance, or document another deterministic rule.
- Defeat in PvP does not remove EXP or level. No EXP from player kills for v0.1.

## PERSISTENCE AND RECONNECTION

- Save character level, current EXP, unspent skill points, four skill rank values, and schema version under stable server-owned character ID. Reuse an existing persistence system if present.
- If persistence is entirely absent, implement a minimal server-only development save repository with atomic file replace and versioned schema (not client saves), clearly isolated behind a repository interface to later replace with a database.
- Save on successful skill allocation and level-up, and on safe character logout; avoid synchronously writing every combat tick. Reconnect must restore the exact progression state and resend it to the owning client.
- Validate corrupted or out-of-range saved values against caps; reject malformed client updates rather than accepting them.

## HUD, VISUAL ASSETS, GODOT NODES

The ZIP contains separately extracted transparent PNGs for optional decoration. Their edges are approximate artwork cutouts and may require polish; don't treat the decorative images as fully skinnable/nine-patch-ready controls. Prefer native Godot controls for functional UI.

Suggested structure (adapt existing HUD):

- `CanvasLayer` / root `Control`
- EXP region anchored near bottom of screen, just above the existing skill/action bar:
  - `TextureProgressBar` or themed `ProgressBar`: current EXP / EXP to next level, fill left-to-right.
  - A level badge + native Label: `Lv. 4`.
  - EXP readout or hover tooltip: `263 / 412 EXP (63.8%)`.
  - A brief floating `+31 EXP` on receipt; no client-side EXP calculation.
  - An unobtrusive glowing Skill Points indicator with `+1` when any unspent points remain; clickable to open the skill upgrade panel.
- Skills panel (toggle via existing keybind or configurable `K` if unassigned): four class skill rows/cards, current rank /4, what upgrading changes at next rank (e.g. `Damage 20 → 22`), minimum level for next rank, available points, plus button disabled if unavailable, and optional confirmation to avoid accidental spending.
- On level-up: small `LEVEL UP` notification + short glow effect/animation; show how many skill points were gained, do not block world controls or play a long fullscreen cinematic.
- At cap: show `Lv. 10 — MAX` and an empty/full-styled bar with no misleading to-next values. Explain cap in tooltip.
- UI must respond to the server's authoritative progression updates, not be guessed from combat VFX. Smooth only the visual bar fill between confirmed values; no gameplay effects in UI code.
- Use nearest texture filtering for pixel art, keep sprites sharp at integer scale factors when possible; high-DPI responsive anchors; support at least 1280x720 and 1920x1080.
- Recommended Godot nodes from stable docs: `CanvasLayer` for HUD; `TextureProgressBar` for segmented art or `ProgressBar` for a simpler initial implementation; `Control` and container nodes for layout.

Optional PNGs in `ui/`: `exp_frame.png`, `exp_fill.png`, `level_badge.png`, `skill_point_icon.png`, `level_up_effect.png`, `exp_sparkles.png`, `upgrade_panel.png`. `UI_CONCEPT_PREVIEW.png` is a mockup, never a texture to display as the entire HUD. The text and percentage must be native dynamic controls.

## IMPLEMENTATION PLAN FOR CODEX

1. **Audit** repository, locate monster death, combat ownership, character saves, skill data, and HUD. Explain what exists and a minimal file-by-file plan.
2. **Progression model** and unit tests for EXP requirement, level-up, multiple level-ups, cap, remainder, point accrual, save/load.
3. **Monster EXP award** from existing server death event with processed-once logic; damage contribution tracking and level-difference reduction; tests against duplicate/deferred events.
4. **Skill rank data and upgrades** with required levels, rank caps, client request/ack, validation, persistence; wire rank effects into actual combat formulas and tooltips.
5. **UI** overlay with actual bar, dynamic numeric data, available point indicator, upgrade window, hover tooltips, level-up toast; import only the PNG decorations that fit.
6. **Multiplayer integration tests** for two clients, reconnect, simultaneous attackers, lag/retry, rank effects, server isolation, and headless server startup.
7. **Manual smoke test**: kill 8 Lv1 Slimes (10 XP each) to reach level 2; see +1 point and a full bar reset; upgrade a Warrior skill; verify its new server result; reconnect and confirm rank/XP retained.

## ACCEPTANCE CRITERIA

- Every valid monster death produces EXP exactly once per eligible recipient with no client trust.
- Level 1 → 2 at 80 EXP; an 85 EXP award causes level 2 with 5/127 EXP and 1 point.
- Level-up can span several levels without losing overflow EXP.
- Low-level farming eventually gives zero EXP using level-difference rules.
- Player earns 9 skill points by level 10 (when starting with zero), can upgrade each existing skill up to rank 4, and cannot max all 4 skills at level 10.
- Upgraded skill parameters affect server combat exactly as configured, not just HUD text.
- Skill points cannot be double-spent by duplicated requests; no rank may exceed cap or be bought below its required level.
- HUD remains visible while exploring the shared world, tracks confirmed EXP/level/points, and has a functional skills panel.
- Progression persists across a disconnect/reconnect. Dedicated headless server still runs without visual dependencies.
- No arenas, matchmaking, new abilities, random stat rolls, PvP EXP farming, or full MMO backend are introduced.
