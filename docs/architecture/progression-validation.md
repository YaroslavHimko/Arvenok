# Progression integration results — 2026-11-19

The existing open world, authoritative simulation, navigation, player/monster combat, animation mapping and ENet transport were retained. No prior character store existed, so this milestone adds the development repository described in [ADR 0005](decisions/0005-character-progression.md). No new C# project was required; Hosting's Contracts dependency is registered in the component catalog and both game configuration lockfiles.

| Phase | Validation |
| --- | --- |
| Baseline | Existing 56 checks passed before progression changes |
| Rules and combat | 67 checks passed with thresholds, contribution sharing, rank effects and accepted-cast consistency |
| Persistence and owner RPCs | 69 checks passed including exact file restoration, corruption, rollback and concurrent-writer rejection |
| UI and complete integration | Debug and Release builds: zero warnings/errors; all 70 checks passed, including actual ranked mobility/interrupt/defense effects for all three classes |
| Existing native gameplay | Two Godot clients passed combat, interrupt, aura expiry, death, protected respawn and reconnect; 18 matching visual-effect events and 25 common wandering-monster snapshots |
| Native progression | Two clients shared two natural Wolf kills for exactly 180 EXP, reached level 2, upgraded once, dealt 20/22 ranked primary damage, rejected duplicate/stale upgrades and restored identical GUID/class/EXP/points/ranks/revision after reconnect and server restart |
| Rendered UI | Captured EXP HUD, level-up effect and Skills at 1280×720 and 1920×1080 window sizes; checked K opens, Escape closes and spent points disable upgrade buttons. Aspect-preserving viewport captures exclude window side margins |
| Assets | All seven runtime PNG SHA256 hashes match the ZIP; RGBA dimensions and lossless/no-mipmap imports pass; editor import completed |

Reproduce with `tools/validate.ps1`, `tools/validate.ps1 -Configuration Release`, `tools/smoke-game.ps1`, and `tools/smoke-progression.ps1 -Capture`. The native scenarios create isolated storage/profiles and stop only processes they launch. The simulation harness covers all rank thresholds and level-10 cap without requiring hours of interactive leveling. Native progression exercises actual normal RPCs; there is no test-only EXP grant RPC.

Final local logs are in ignored `artifacts/progression-debug.log`, `artifacts/progression-release.log`, `artifacts/progression-final-combat.log` and `artifacts/progression-final-smoke.log`. Final rendered captures are under `artifacts/progression-smoke/20261119-014107-18e972/`. These are local evidence, not tracked source or portable CI artifacts.

All requested assets were present. Decorations are approximate static cutouts; the level-up image uses a short fade/motion effect rather than an invented animation sheet. No existing combat/networking conflict remained after integration. A fifth spawn reuses the Wolf species/art at level 6 so low-level penalties do not make levels 9–10 unreachable in this forest.

NuGet vulnerability auditing was disabled for these sandbox builds because network access was restricted; dependency audit results are unavailable. Godot emitted its local certificate-store diagnostic in this environment, while loopback ENet tests passed. Development saves are atomic per character and restore completed writes; they are not production authentication, a transactional cross-character database, or a crash-recovery guarantee for writes still in flight. Position, HP and cooldowns remain session state. No internet-scale or production-load claim is made.
