# Validation

`Arvenok.Tests` is a dependency-free executable regression harness. Run it with `dotnet run --project tests/Arvenok.Tests`; it returns a nonzero status if any check fails. It tests observable simulation and configuration behavior with explicit time steps and no engine, network, wall clock or sleep. It is intentionally not registered with `dotnet test`.

`tools/validate.ps1` runs architecture checks, locked restore, the full build and this harness. `tools/smoke-game.ps1` adds an actual Godot server/two-client startup and replication check. For future service/data work add integration tests against isolated disposable dependencies, protocol fixtures for version compatibility, and load/soak workloads with explicit budgets. Choose a standard test runner when suites need richer discovery/reporting; keep the same separation of unit, integration, content and performance checks.
