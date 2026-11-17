# Repository instructions

- Read `README.md` and the relevant component documentation before changing boundaries.
- Godot's project root is `apps/game`; build the repository using root `Arvenok.sln`.
- Keep packages engine-independent. Presentation consumes snapshots and submits intent; it does not mutate authoritative simulation.
- Register new C# projects and dependency edges in `catalog/components.json` and the solution. Explain architectural boundary changes in an ADR.
- Preserve `.uid` sidecars when moving Godot scripts and update resource paths in the same change.
- Run `tools/validate.ps1` for code/build changes and `tools/smoke-game.ps1` for Godot scene or RPC changes when the engine is available. Report unavailable checks accurately.
- Keep engine caches, local credentials, exports and build outputs out of Git. Do not invent deployment resources, team accounts or licensing decisions.
- Prefer small feature modules and explicit dependencies over speculative frameworks.
