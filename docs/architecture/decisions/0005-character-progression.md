# ADR 0005: Authoritative character progression and development persistence

- Status: accepted for the progression milestone
- Date: 2026-11-19

The existing project had connection-local characters and no persistence or account identity. Keep its simulation, ENet adapter, entity IDs, combat and forest. Add progression records/read models to Contracts, rules and kill contributions to Simulation, and an interface-backed JSON save repository to Hosting. Hosting now explicitly depends on Contracts; no new project is added. Presentation receives owner-only progression snapshots and sends skill IDs plus sequence/revision preconditions, never EXP or ranks.

Server-issued random reconnect credentials identify stable server-owned character GUIDs. Local client profiles hold only those credentials. The server stores credential hashes separately from versioned character records, reserves an identity during admission, and prevents concurrent use. Automatic local profile leases preserve the existing two-terminal workflow; explicit `--profile` names make reconnect choice predictable. This is development identity over the existing unencrypted transport, not production authentication.

Default development files live under ignored repository `.local/`; `--data-dir` selects an alternate server repository. A file lease prevents two servers writing the same repository. Records are validated and saved with flushed temporary files and atomic rename. The adapter serializes writes off the simulation thread, queues logout before releasing identity, waits for prior writes before restoring, and flushes during orderly shutdown. Persistence failure stops the development server instead of silently continuing with unsaved progression. Offline `--server --reset-skills <character-guid>` resets ranks/refunds earned points, then exits; there is no client respec RPC.

Versioned progression JSON defines all nine EXP thresholds and twelve four-rank skills. Level adds no base HP or basic attack damage. Cast acceptance captures the effective ability definition, preventing later upgrades from changing an in-flight cast. The existing cooldown and defense/dash end timestamps preserve accepted timing.

Each monster has explicit level/base EXP and a bounded 15-second effective-damage ledger. Eligible living, connected contributors within twelve tiles need at least ten percent of max HP. Largest-remainder allocation (entity ID breaks ties) conserves the EXP budget; per-recipient level penalties follow the split, rounded half away from zero. Each monster life can pay once; respawn clears contributions and increments its life ID. Same-tick deaths are evaluated after the complete damage batch. No player kill or non-combat cleanup grants EXP.

The original four spawns retain their positions and gain the suggested levels/rewards. A fifth, level-six Wolf variant in the distant forest keeps the full level cap reachable after low-level penalties. This uses existing art, passive AI, navigation and combat; no new class, creature system or encounter mode is introduced.
