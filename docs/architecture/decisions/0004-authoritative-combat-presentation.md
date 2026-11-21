# ADR 0004: Combat animation presentation from authoritative state

- Status: accepted for the Woodvale combat animation integration
- Date: 2026-11-18

## Context

The existing forest has snapshot interpolation, reliable sequenced combat events, passive server-owned monsters, and a single authoritative movement/combat simulation. `WorldEntityView` previously rendered static poses. The supplied Woodvale Combat Animations v0.1 contains 96 character strips, 20 monster strips and 18 effects, all eight horizontal 96x96 RGBA cells. Down-facing actions have illustrated key poses; other directions and monster movement are procedural prototypes.

## Decision

Keep the existing scene, node paths, entity IDs, RPCs, navigation, terrain and combat rules. Reuse `ForestWorldView` and `WorldEntityView`, with `AnimatedSprite2D` and shared `SpriteFrames`. Import the PNGs unchanged under `apps/game/Assets/Art2D/woodvale-combat/assets/animations/`. Retain original documentation, manifest, contact sheet and illustrative GIF under `content/art-source/art2d/woodvale-combat/`. No production license or art approval is inferred.

The client loads `animation_manifest.json` for paths, cells, counts, FPS and loop flags. `visual_bindings.json` maps existing class/ability slots to clips and effects, and defines anchors and cosmetic playback/travel durations. These definitions can change without modifying networking or combat. `AnimationCatalog` validates required clips and effect references. `CombatAnimationLibrary` builds atlas frames and verifies actual dimensions. Imports remain lossless, transparent and nearest-neighbor sampled.

`EntityAnimationState` selects frames from server time, snapshots and reliable events. Death holds the final frame. Brief hurt/interrupt reactions take priority, then cast/windup and release gestures, then walk/idle. Hurt does not cancel casting: afterward, progress resumes at the original timestamp. Basic attacks have windup and release phases; frames never determine hits. Cancellation/expiry/death/respawn events reconcile outcomes against older snapshots. Snapshot state restores casting, defense, motion and dead poses for late joins. Disconnect removes attached visuals immediately.

Defense uses separate attached loops, so players can move while defending. Server expiry and end/death/disconnect events remove them. Rush and Backstep trails use the active server dash interval; Blink creates departure and arrival effects at accepted positions. Cells may bob or lean, but entity roots only follow existing snapshot interpolation. Visuals never move a collider or change navigation.

`CombatEffectPlanner` produces finite cosmetic trajectories from confirmed damage events. Existing combat resolves damage instantly at windup completion; a damage event confirms both release and impact. Firebolt, orbs and arrows travel to the recorded impact point in 0.08-0.22 seconds. Impacts begin at the authoritative damage timestamp, which can precede projectile arrival. No damage is delayed, predicted or applied by an effect. Cancelled/rejected casts emit no projectile. Late events cannot restart expired effects; sequence checks prevent duplicates. A committed killing-shot visual can finish its short flight during death playback, but attached defense/motion ends immediately. Disconnect/despawn clears transient effects. Effects render above bodies so their elevated centers do not sort behind the caster's feet.

Previous events lacked geometry for delayed effects and Blink departure. Add read-only actor/target positions, facing, optional movement origin and effect end time to the existing event DTO. Add death start and active dash end timestamps to snapshots. The simulation records these at existing transitions; balance, casting, cooldown, damage and movement calculations stay unchanged. Reliable events, compressed snapshots and revision checks remain. Client and server must be rebuilt/restarted together.

Monsters select idle/move from authoritative movement, and hurt/death from shared combat. Attack strips are mapped for authoritative attack states, but current monsters remain passive. No attack AI or loot is added.

## Boundaries and validation

No project or dependency edge is added. Presentation consumes contracts, never simulation. Three pure presentation files are linked into the existing regression executable to test timing, replacement definitions, idempotence and trajectories without native engine dependencies. The game compiles those same source files normally.

`tools/validate.ps1` checks 134 RGBA strips/import settings and runs gameplay plus presentation regressions. The native two-client smoke checks casting, interruption, projectile displacement, aura expiry, death, matching effect sequences, monsters, combat and reconnects. `tools/capture-combat.ps1` launches three clients in the real forest, checks animated pixel targeting and cleanup, and captures all classes' combat under ignored artifacts. It needs a graphical rendering device; the server and two auxiliary clients remain headless. Captures and logs are development evidence, not production latency/load tests.
