# Contributing

Start with the repository [map and build steps](README.md), then find the component in [the catalog](catalog/components.json). Owner names are responsibility groups to staff, not existing account names. Maintainers act as the interim owner until teams exist.

## Choosing a home for work

| Work | Home | Boundary |
| --- | --- | --- |
| Gameplay and authoritative physics rules | `packages/Arvenok.Simulation` | Engine-independent; use supplied simulation time |
| Godot physics/navigation integration | A feature adapter under `apps/game/Features` | Server decides authoritative outcomes; no rendering dependency |
| Client UI, graphics and animation playback | `apps/game/Features/<Feature>/Presentation` | Consume state; submit intent |
| Runtime reusable resources | `apps/game/Assets` | Source provenance and explicit budgets |
| Art/rig/animation authoring | `content/art-source` | Export deliberately into runtime resources |
| Story, quests, characters and canon | `content/narrative` | Stable IDs; reviewed gameplay integration |
| Translations | `content/localization` | Stable keys; preserve placeholders |
| New online backend | `services/<bounded-context>` when approved by design review | Own its data, API, deployment, runbook and migrations |
| Hosting and fleet operations | `infrastructure` | Environment/region isolation; no gameplay rules |

Keep feature-specific scenes, code, resources and tests together. Extract shared packages only when there is a real independent responsibility or a second consumer. Avoid general `Managers`, `Helpers`, or `Utils` collections and global service locators. Feature assemblies are introduced when ownership and build size justify them, not one project per class.

## Change process

1. Make a small branch from the main integration branch. Describe the problem and affected components before a cross-team change.
2. Add an ADR for a new boundary, external dependency, public protocol, persistence design or deployment model. Routine implementation changes do not need an ADR.
3. Keep a PR independently buildable. Include source and generated runtime asset changes together, or record the immutable asset-build reference.
4. Run `tools/validate.ps1`; also run `tools/smoke-game.ps1` for engine, networking or scene changes. Test affected player behavior and record actual results. Content-only changes need import/visual validation and provenance review, not artificial C# tests.
5. Request affected owners. Protocol changes require both producer and consumer review. Changes to common build files, catalog and CI require maintainer review.
6. Merge through protected branches once the repository administrator has configured required checks and owner review. Use expand/migrate/contract for saved-data changes; retain rollback compatibility through the rollout window.

`tools/check-architecture.ps1` validates the explicit project graph, detects cycles and unregistered projects, and checks engine/presentation coupling. It is a guard against common mistakes, not a security boundary or a complete static analyzer. New projects must appear in both `Arvenok.sln` and `catalog/components.json`; dependency changes require corresponding catalog review.

## Reproducibility and assets

Commit NuGet lockfiles (`packages.lock.json` and the game's configuration-specific variants), Godot script `.uid` files, textual scenes/resources and intentional import settings. Do not commit `.godot`, `bin`, `obj`, credentials, exports or local logs. To intentionally refresh NuGet resolution, run `tools/validate.ps1 -UpdateLocks` for both Debug and Release and review all lockfile diffs. Refresh the game's `ExportDebug` lock as well if you use debug exports. Do not update engine, SDK or dependency versions incidentally in feature PRs.

Install Git LFS before adding binary art. Acquire a Git LFS lock for nonmergeable authoring files when the configured remote supports locking; coordinate with the owner when it does not. Text scenes should compose small child scenes to reduce shared-file conflicts. Never serialize credentials, account data or personal workstation paths into assets.

For very large asset collections, use a dedicated source-art repository/object store with immutable manifests and keep runtime packages here. Split code repositories only when independent ownership and release cadence outweigh atomic cross-component changes. Sparse checkout can reduce artist/designer worktrees; engineers building the solution need `apps`, `packages`, `tests`, `catalog`, `tools` and the root build files.

CI currently validates the entire small solution. When build time becomes material, derive affected components and their transitive consumers from the catalog, cache artifacts by source/toolchain/dependency hashes, and retain scheduled full builds. Path filters alone are insufficient for shared-contract and build-policy changes. Add build-time and review-queue measurements before splitting the toolchain.

## Repository setup still needed

Assign real team members, adapt `.github/CODEOWNERS.example` into `.github/CODEOWNERS`, and configure required reviews/checks and restricted release permissions. The project owner must choose code/content licensing, third-party asset rights and a private security contact before external contributions are accepted. This restructuring does not grant a license or invent an organization policy.
