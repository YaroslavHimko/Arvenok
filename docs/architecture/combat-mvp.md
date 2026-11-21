# Combat MVP

The existing ENet host, fixed physics tick, separate pure simulation, snapshot-based presentation and executable test harness were retained. The old duel-only rules were replaced inside the existing project boundaries; no new service, project or engine dependency was added. See [ADR 0002](decisions/0002-shared-world-combat.md). The subsequent [Woodvale integration](decisions/0003-woodvale-world-content.md) adds supplied forest art and passive monsters without replacing those rules.

## Implementation map

| Area | Files | Responsibility |
| --- | --- | --- |
| Read models and intent | packages/Arvenok.Contracts/CombatContracts.cs | Entity IDs, classes, ability definitions, immutable snapshots, commands, events, map descriptions |
| Balance | packages/Arvenok.Simulation/CombatBalance.cs | Class HP/speed, all 15 actions, cooldowns, casts, movement distances, recovery, leash and timing |
| World geometry | packages/Arvenok.Simulation/WorldMap.cs | Outdoor map, sanctuary, obstacle sweeps, line of sight and A* paths |
| Rules | packages/Arvenok.Simulation/WorldSimulation.cs, CombatEntity.cs, CombatRules.cs | Admission, authoritative state machine, movement, pursuit, damage, defense, death and respawn |
| Transport | apps/game/Features/Arena/ArenaSession.cs | Peer-to-entity ownership, reliable intent/events/state, revisioned snapshots and time |
| Presentation | apps/game/Features/Arena/Presentation/ArenaView.cs | Click movement/selection, class choice, camera, interpolation, cast/HP/cooldown UI and feedback |
| Native scenarios | apps/game/Features/Arena/SmokeDriver.cs, tools/smoke-game.ps1 | Two real client combat and reconnect scenario |
| Regression checks | tests/Arvenok.Tests/Program.cs | Controlled-time gameplay/configuration/serialization checks |

## Play

Start a dedicated server and two clients using the root README commands. Each character enters the green sanctuary with full HP. Choose Warrior, Mage or Archer on a new character using the visible buttons or F1/F2/F3 before leaving or using a skill. Class choice then remains attached to that saved character.

- Click ground to navigate; click another traveler to select without attacking.
- Space or the Basic Attack button starts attacks and limited pursuit.
- 1 uses Crushing Blow / Firebolt / Power Shot. Stop moving first.
- 2 uses Rush / Blink / Backstep. Blink aims toward the mouse; its button uses facing if the cursor is over the HUD.
- 3 uses Shield Bash / Counterspell / Disrupting Arrow.
- 4 uses Guard / Arcane Ward / Evasive Stance.
- K opens Skills; Escape closes it, or otherwise stops walking, pursuit and casting. Ground movement cancels casting/pursuit too.
- F8 toggles recent combat diagnostics in Debug builds.

The outdoor map is 60 by 40 tiles. One tile is 32 pixels, and bodies have a 12-pixel radius. The marked starter sanctuary forbids PvP when either participant is protected. Obstacles block movement and line of sight. There are no player-body collisions in this milestone.

Basic attacks have a configurable 0.2-second stationary windup and a 1.25-second start-to-start interval. Skills have 0.25 seconds of shared recovery after execution; cooldowns start on acceptance and survive cancellation, interruption and death. Rush and Backstep travel over 0.18 seconds and can be cancelled by ground movement or Escape; neither follows a target after activation. Blink is instant and clipped to the nearest reachable point on its segment.

All targeting, range and eligibility checks occur on the server. A failed completion deals no damage. Ordinary damage never interrupts. Interrupts cancel only active interruptible casts and do not silence or stun. Defense reduces damage by 35% for 2 seconds; damage rounds once to the nearest integer, halves away from zero. Same-tick valid lethal hits can trade.

Pursuit is limited to 12 tiles from the attack's starting position, repaths at most four times per second, and stops when the target dies, leaves, becomes protected or unreachable. Death clears casts, target, movement, attack intent and defense. Respawn is automatic after 3 seconds, at full HP in the sanctuary. Cooldowns retain their original expiry times.

## Verification and limits

Run tools/validate.ps1 for architecture, locked restore, root solution build and deterministic regression checks. Run tools/smoke-game.ps1 with a Godot .NET executable for actual ENet gameplay. The smoke script checks that both clients agree on damage and protected full-health respawn, then reconnects new clients to the same server.

These checks establish implemented rules and local networking, not class balance or internet scalability. The six pairwise tests verify both classes' damage and timing; they do not imply equal win rates. Human tests should include terrain, surprise engagements, escape, multi-attacker encounters and interrupted mobility.

The server keeps running when empty. Development profiles now restore class, level, EXP, points and ranks through atomic server files; entity IDs, positions and combat state remain session-local. Four passive monster species wander through the same navigation and damage pipeline and respawn at home. Monster kills award shared EXP; PvP awards none. See [progression rules](../../content/progression/README.md) for rank modifiers; the balance values above are rank 1. Accepted casts retain their effective definition across upgrades.

Monster attacks, loot, production accounts/database storage, equipment, parties, guilds and sieges remain future work. The current 32-player limit is unbenchmarked. Client/server builds must match; there is no rolling protocol compatibility or network prediction. Connection failure is shown in the client; restart it with the same profile to reconnect.
