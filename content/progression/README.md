# Character progression v1

`woodvale-v1.json` is embedded into Simulation and rebuilt with `Arvenok.sln`. It supplies all nine next-level thresholds, rank gates and the twelve existing class skills. Stable IDs identify skills in upgrade requests; do not rename IDs without a migration plan. Four entries per modifier correspond to ranks 1–4. Omitted modifiers retain the existing `CombatBalance` values. Basic attacks and base HP do not scale with level.

| Current level | EXP to next level |
| --- | --- |
| 1 | 80 |
| 2 | 127 |
| 3 | 198 |
| 4 | 293 |
| 5 | 412 |
| 6 | 555 |
| 7 | 722 |
| 8 | 913 |
| 9 | 1128 |
| 10 | MAX |

Characters start at level 1, zero EXP and zero points, with all four skills at rank 1. Each level grants exactly one point. Rank 2 requires level 2, rank 3 level 4, and rank 4 level 7. Overflow carries through multiple levels; level 10 clears progress to zero and stops accumulating EXP. There are nine earned points at cap and twelve possible rank purchases.

| Skill category | Rank effects |
| --- | --- |
| Warrior Crushing Blow | Damage 18 / 20 / 22 / 24 |
| Mage Firebolt | Damage 20 / 22 / 24 / 26 |
| Archer Power Shot | Damage 19 / 21 / 23 / 25 |
| Rush | Range 4.0 / 4.2 / 4.4 / 4.6 tiles |
| Blink / Backstep | Range 3.2 / 3.4 / 3.6 / 3.8 tiles |
| Each class interrupt | Damage 5 / 6 / 7 / 8; cooldown 12 / 11.5 / 11 / 10.5 seconds |
| Each class defense | 35% reduction for 2.0 / 2.2 / 2.4 / 2.6 seconds |

Cast times, primary/mobility/defense cooldowns, collision, interrupt rules and accepted motion remain server-controlled. Upgrading during a cast changes the next cast; the accepted cast keeps its captured ability definition. The owner's Skills window receives current and next definitions from the server and submits only a stable skill ID, command sequence and expected progression revision.

Monster `level` and `baseExp` live in [world spawn data](../worlds/woodvale-forest.json). Slime L1 gives 10, Sprout L2 18, Mushroom L3 31, Wolf L4 52. A second Wolf in the southeast is L6, 95 HP and 90 base EXP, keeping level 10 reachable under the low-level penalties. These are passive monsters using the existing combat/death/respawn system.

Only effective damage in the last 15 seconds counts, capped at remaining HP. Connected, living attackers within 12 tiles at death need at least 10% of max HP. Split the base budget proportionally among eligible contributors, assigning leftover integers by largest fractional remainder and then entity ID. Apply each recipient's level difference penalty afterwards: up to 2 levels higher gives full share, 3 gives half, 4 gives quarter, 5 or more gives zero. Round half away from zero. Each monster life pays once. Death/respawn/disconnect clear contributions; PvP and non-combat removal give no EXP.

See [ADR 0005](../../docs/architecture/decisions/0005-character-progression.md) for persistence and networking boundaries, and [local operation](../../docs/operations/local-development.md) for profiles, saves and offline skill reset.
