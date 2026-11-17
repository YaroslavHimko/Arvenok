# Arvenok

A modular foundation for an online game, with a working server-authoritative two-player arena. **Arvenok is a working title; trademark and domain clearance are not complete.**

The repository separates engine-independent gameplay, Godot presentation and networking, authored content, online services, and infrastructure. The current executable is still a local prototype. Regional hosting is designed in documentation; authentication, persistence, matchmaking, fleet management, and production deployment are not implemented.

## Repository map

```text
Arvenok.sln                    Build every C# project from here
apps/game/                     Godot project; client and dedicated session host
  project.godot                Import THIS file into Godot
  Features/Arena/              Scene and networking/composition adapter
    Presentation/              Client input and placeholder rendering
  Assets/                      Runtime 2D, 3D, animation, audio and UI resources
packages/
  Arvenok.Contracts/           Immutable state shared across module boundaries
  Arvenok.Simulation/          Authoritative rules; no engine or network dependency
  Arvenok.Hosting/             Validated process configuration
content/                       Authoring sources, narrative and localization
services/                      Boundary for future independently deployed services
infrastructure/                Regional deployment design and environment conventions
tests/                         Engine-independent regression checks
tools/                         Build, run, architecture and multiplayer smoke checks
catalog/                       Component ownership and permitted project dependencies
docs/                          Architecture, decisions, workflows and operations
.github/                       CI, review template and code-owner setup template
```

## Start developing

Use **Godot 4.7.2 .NET**, the **.NET 9.0.1xx SDK** selected by `global.json`, the **.NET 8 runtime/targeting pack**, and **Git LFS**. Engine and target versions are inherited from the prototype; version upgrades must update the lockfiles and pass validation together. The SDK version is a development tool choice, not a production support policy.

```powershell
git lfs install
git lfs pull
dotnet restore Arvenok.sln --locked-mode
dotnet build Arvenok.sln --no-restore
dotnet run --project tests/Arvenok.Tests --no-build
```

Import `apps/game/project.godot` into the Godot Project Manager. Open `Arvenok.sln` from the repository root in your C# IDE. The previous root `project.godot`, `Arena.cs`, and `Arena.tscn` have moved; remove the old Project Manager entry if it is still listed. Generated `.godot` caches are disposable and are not part of the migration.

From the repository root, run these in three terminals (replace `godot` with your .NET engine executable):

```powershell
# Terminal 1: dedicated duel server, loopback UDP 7777
godot --headless --path apps/game -- --server
# Terminal 2: client
godot --path apps/game
# Terminal 3: another client
godot --path apps/game
```

Left-click ground to move or an opponent to select them. Press **1** for melee or **2** for a spell. Damage interrupts casting; movement cancels your spell. Defeated players respawn after three seconds. All combat decisions run on the server.

## Checks and working agreements

```powershell
./tools/validate.ps1
./tools/validate.ps1 -Configuration Release
./tools/smoke-game.ps1 -Godot godot
```

Use PowerShell 7 (`pwsh`) on Linux/macOS; scripts also support Windows PowerShell 5.1. If your local Windows execution policy blocks repository scripts, a process-scoped invocation is `powershell -NoProfile -ExecutionPolicy Bypass -File ./tools/validate.ps1`; this does not change the machine policy. The regression suite is an executable harness, run by `validate.ps1`, rather than a `dotnet test` project. The smoke check requires the Godot .NET executable and a built Debug project.

- [Architecture and dependency boundaries](docs/architecture/README.md)
- [Contribution and ownership rules](CONTRIBUTING.md)
- [Art, animation, story and localization workflow](docs/contributing/content.md)
- [Regional hosting and capacity design](docs/operations/regional-hosting.md)
- [Local configuration and troubleshooting](docs/operations/local-development.md)
- [Production milestones](docs/architecture/roadmap.md)

CI builds Debug and Release and runs the regression suite on Windows and Linux. The solution maps Release to Godot's `ExportRelease`; the game has configuration-specific lockfiles because editor and export dependencies differ. Native Godot smoke checks are a separate command until an engine-equipped CI runner is configured. The [code-owner template](.github/CODEOWNERS.example) needs real organization teams and repository branch protection before owner review can be enforced.
