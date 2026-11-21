# Woodvale combat animations v0.1

This extends the existing Woodvale forest art with 134 unchanged PNG strips: 96 character clips, 20 monster clips and 18 effects. Every strip is 768x96: eight 96x96 RGBA cells. Original documentation and reference artwork are in [the source collection](../../../../../content/art-source/art2d/woodvale-combat/).

`animation_manifest.json` describes artwork. `visual_bindings.json` maps existing class/ability slots to it. Edit the runtime manifest and bindings to replace art; retain the source collection as original provenance. Keep required semantic clip keys and supply every direction. Paths, counts, FPS and loop modes come from the manifest. Import checks and client startup validate files and dimensions. No combat timing belongs in either file.

Entities use feet pixel (48,90), a centered offset of (0,-42). Terrain, collision, Y sorting and root interpolation remain unchanged. VFX are nonphysical and render above bodies; defense and motion effects attach to the visual root. Imports are lossless, no mipmaps, nearest-neighbor filtered. Preserve `.import` sidecars.

| Server signal | Visual response |
| --- | --- |
| Idle / movement | Directional `idle` / `walk`; monsters `idle` / `move` |
| Basic windup / resolution | `basic_attack` windup and release phases |
| Primary cast / completion | `cast`, mapped release gesture and confirmed effect |
| Ordinary damage | Brief `hurt`, then resume authoritative state |
| Confirmed interruption | `interrupt`; stop cast playback immediately |
| Defense | Initial `defend` plus separate aura until server expiry |
| Death / respawn | `death` once, hold final frame; reset on respawn |
| Disconnect | Hide entity and remove attached/transient effects |

| Class | Basic | Primary | Mobility | Interrupt | Defense |
| --- | --- | --- | --- | --- | --- |
| Warrior | Sword slash | Crushing Blow + impact | Rush trail | Shield Bash impact | Guard aura |
| Mage | Traveling orb | Traveling Firebolt + impact | Blink departure/arrival | Counterspell impact | Arcane Ward aura |
| Archer | Traveling arrow | Traveling Power Shot | Backstep trail | Traveling Disrupting Arrow + impact | Evasive Stance aura |

Projectiles follow confirmed positions without collision or damage callbacks. Current combat applies HP damage immediately at cast completion, so cosmetic travel may finish after impact. That gameplay model is preserved. Monster attack clips are available, but existing passive AI does not attack.

This is prototype art. Down-facing actions contain illustrated key poses; side/back actions and monster gait use procedural poses. All requested clip keys/directions exist. No separate cast-start aura or audio is supplied; the existing authoritative cast bar remains.

Run root validation and multiplayer smoke commands from [README](../../../../../README.md). For rendered captures, run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/capture-combat.ps1 -Godot godot` from the repository root. It uses an ephemeral server and closes only its own processes. See [ADR 0004](../../../../../docs/architecture/decisions/0004-authoritative-combat-presentation.md) for timing and boundaries.
