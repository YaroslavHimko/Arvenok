# ADR 0002: Continuously running world and reusable class combat

- Status: accepted for the MVP
- Date: 2026-11-18

## Context

The existing ENet adapter, engine-independent simulation, client-only renderer, root solution and regression harness were reusable. The original two-player duel had peer IDs as entity IDs, unclipped straight-line movement, two attacks, damage-based interruption, no safe regions and no replication ordering. Those assumptions prevented the requested open-world combat slice.

## Decision

Keep all five projects and their existing dependency edges. Replace the duel rules in the simulation package with a single-writer WorldSimulation, CombatEntity, CombatRules, CombatBalance and WorldMap. Contracts own immutable entity/ability/map read models and command/event data. The game adapter composes authoritative values and exposes read-only definitions to presentation; presentation still does not reference simulation.

Retain Features/Arena/Arena.tscn, ArenaSession.cs, ArenaView.cs, their node path and their existing UID sidecars for resource compatibility. These historical filenames no longer describe game rules: there are no rounds, teams, scores, victories or reset-on-disconnect behavior. Renaming the scene is unnecessary migration work.

The host maps ENet peer IDs to monotonically assigned world entity IDs. Each reliable intent carries a sequence and observed server time, with bounded command age, per-entity token-bucket admission and semantic validation. Peer identity is connection ownership, not secure account authentication. Entity IDs are stable for the lifetime of this running process, not durable character identities.

Use one 32-pixel tile convention. WorldMap owns obstacle rectangles, protected region rectangles and protected spawn locations. Grid A* and swept body movement share collision geometry; line of sight uses obstacle segments. A common eligibility policy rejects protected-player combat and same-faction damage. No monster implementation or faction service is added.

Balance data defines exactly three classes and five actions per class. Generic casting, interrupts and nonstacking defense share implementations. Cooldowns start on acceptance. Resolve all valid same-tick cast completions before applying their damage, allowing lethal trades. Rounding happens once, away from zero at halves. Rush and Backstep commit to a fixed, swept endpoint for 0.18 seconds; Blink resolves instantly. None homes onto its target or bypasses walls.

Keep Godot's 60 Hz tick, configurable 15 Hz snapshots and reliable combat events/essential state. Complete world snapshots carry monotonic revisions so delayed packets cannot restore removed entities or older health. JSON is a development wire encoding with identical client/server builds required. The previous prototype had no relevant-recipient filtering; public state/events still go to connected peers, while rejected-command feedback goes only to its owner. Area-of-interest replication remains future work.

## Consequences

Combat and geometry are deterministic under the test harness's controlled tick inputs, but cross-platform lockstep/replay determinism is not claimed. The server continues advancing with zero clients. Disconnect removes the temporary character; reconnect creates a new entity in the sanctuary. There is no database, durable state, secure account admission, matchmaking, inventory or progression.

Class choice is available in the initial protected area until the character first leaves protection or uses a skill. Reconnect to test another class after selection locks; returning to sanctuary cannot reset HP/cooldowns via class changes.

The 32-player admission limit is a development ceiling, not a measured capacity claim. Full-state JSON replication, A* allocation on path requests, per-process IDs, floating point geometry and one process-wide world should be revisited using actual profiling before larger-scale hosting. The telemetry ring is bounded; the client exposes it only through the Debug combat log.

## Verification

The package-free harness covers all six class pairs and boundary cases for movement, geometry, casts, interrupts, defense, death, multiple attackers and invalid/duplicate commands. The native smoke script drives real ENet clients through movement, safe-zone rejection, a duplicate cast request, interruption, defense, damage, death, respawn, disconnect and reconnect, comparing both clients' outcomes. Human playtesting and internet latency/load testing remain separate from these checks.
