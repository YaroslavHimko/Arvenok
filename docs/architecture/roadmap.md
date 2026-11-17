# Gates to a large online game

These are engineering gates, not a promise of player capacity. Increase scale after measuring the preceding stage.

| Gate | Required evidence |
| --- | --- |
| Foundation (this change) | Buildable module graph, preserved playable duel, rule/configuration regression suite, source-content separation, contribution and regional design docs |
| Reliable multiplayer slice | Engine smoke in CI, command/RPC integration tests, snapshot sequencing and reconciliation, disconnect/reconnect tests, transport limits and hostile-input tests |
| Team-scale content | Staffed ownership, required reviews, importer validation, content-ID/schema checks, automated art budgets and localization tests, reproducible content builds |
| Deployable session | Pinned server export/container, separate client export, readiness/liveness/drain behavior, authenticated admission, build/protocol handshake, workload identity, metrics and graceful shutdown tests |
| One production region | Allocator and fleet, per-service persistence/migrations/backups, account identity, canary/rollback, measured session density, load/soak tests and recovery drills |
| Multiple regions | Region routing, data-residency policy, one-writer ownership transfer, cross-region latency tests, outage drills, capacity budgets and operational staffing |
| Large persistent world | Zone/interest management, streaming, durable world model, anti-abuse, partition recovery and scale targets backed by load evidence |

Do not raise the duel's two-player capacity and infer MMO support. Choose shard/zone boundaries, simulation cost, network fan-out and consistency requirements from the intended gameplay, then measure them. Keep account IDs, transport peer IDs, character IDs and world entity IDs separate when those domains are introduced.
