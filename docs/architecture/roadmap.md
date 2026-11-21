# Gates to a large online game

These are engineering gates, not a promise of player capacity. Increase scale after measuring the preceding stage.

| Gate | Required evidence |
| --- | --- |
| Foundation (implemented) | Buildable module graph, source-content separation, contribution and regional design docs |
| Shared-world combat MVP (implemented locally) | Three classes, protected/wilderness rules, navigation, snapshot sequencing, command validation, deterministic regression tests and native ENet combat/reconnect smoke |
| Character progression (implemented locally) | Shared monster EXP, levels 1–10, four skill ranks, private HUD, duplicate-safe upgrades and development saves verified through reconnect/server restart |
| Persistent inventory (implemented locally) | Private 24-slot backpack, atomic stack operations, item metadata, catalog validation, migration, durable acknowledgements, native drag/drop and two-client reconnect/restart verification |
| Loot, equipment and crafting (implemented locally) | Contribution-owned ground loot, two equipment slots, exact server bonuses, one-use recipes, atomic item ownership, native controls and two-client pickup race/reconnect/restart verification |
| Reliable multiplayer release | Engine smoke in CI, protocol negotiation, transport/bandwidth budgets, latency/loss/load tests and secure account admission |
| Team-scale content | Staffed ownership, required reviews, importer validation, content-ID/schema checks, automated art budgets and localization tests, reproducible content builds |
| Deployable session | Pinned server export/container, separate client export, readiness/liveness/drain behavior, authenticated admission, build/protocol handshake, workload identity, metrics and graceful shutdown tests |
| One production region | Allocator and fleet, per-service persistence/migrations/backups, account identity, canary/rollback, measured session density, load/soak tests and recovery drills |
| Multiple regions | Region routing, data-residency policy, one-writer ownership transfer, cross-region latency tests, outage drills, capacity budgets and operational staffing |
| Large persistent world | Zone/interest management, streaming, durable world model, anti-abuse, partition recovery and scale targets backed by load evidence |

The MVP's 32-player admission ceiling is not measured MMO capacity. Choose shard/zone boundaries, simulation cost, network fan-out and consistency requirements from the intended gameplay, then measure them. Transport peer IDs and world entity IDs are separate now; keep future account and persistent character identities separate too.
