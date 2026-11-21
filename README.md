# Arvenok

A modular foundation for an open-world 2D MMORPG, with a server-authoritative three-class combat MVP. **Arvenok is a working title; trademark and domain clearance are not complete.**

The world uses **Woodvale v0.1** forest art and **Combat Animations v0.1**: animated Warrior/Mage/Archer characters, skill effects, traveling projectiles and four passive server-owned wandering monsters. See [world authoring](content/worlds/README.md) and [animation mappings and replacement instructions](apps/game/Assets/Art2D/woodvale-combat/README.md). Animations follow server events; the artwork combines illustrated action poses with procedural prototype animation.

Characters now earn shared monster EXP, progress through levels 1–10, and spend one point per level on four ranks of their existing skills. **K** opens Skills. Progression is validated and saved by the dedicated server; account login and character selection restore the chosen character. See [progression rules and tuning](content/progression/README.md).

**I** or **E** opens the persistent backpack and its MainHand, OffHand and Body Armor slots. Monsters drop shared-world loot; pick it up with **F** nearby or click a bag. Select a recipe scroll to preview ingredients, then use **Craft** or double-click it. Equipment, crafting, pickup and ownership are server-authoritative and saved across reconnects/restarts. New characters start empty. See [items, loot and recipes](content/items/economy-v1.md).

The repository separates engine-independent gameplay, Godot presentation and networking, authored content, online services, and infrastructure. Accounts and owned character aggregates use a local SQLite database. Login and gameplay use verified TLS WebSockets. Fleet management and deployment remain future work.

## Repository map

```text
Arvenok.sln                    Build every C# project from here
apps/game/                     Godot project; client and dedicated session host
  project.godot                Import THIS file into Godot
  Features/Arena/              Shared-world adapter (historical scene path retained)
    Presentation/              Client input, TileMapLayers, sprites and HUD
  Assets/                      Runtime 2D, 3D, animation, audio and UI resources
packages/
  Arvenok.Contracts/           Immutable state shared across module boundaries
  Arvenok.Simulation/          Authoritative rules; no engine or network dependency
  Arvenok.Hosting/             Process configuration and development character repository
content/                       Authoring sources, world layouts, narrative and localization
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

## Run the game from a terminal

Use PowerShell and the **Godot .NET** executable. Open each terminal in the repository root: the folder containing `Arvenok.sln`. If your terminal is currently in `apps/game`, run `Set-Location ../..` first.

**1. Build the game.** Run this before the first launch and after changing C# code:

```powershell
dotnet restore Arvenok.sln --locked-mode
dotnet build Arvenok.sln --configuration Debug --no-restore
```

On a fresh checkout, or after adding/replacing artwork, import the project:

```powershell
godot --headless --editor --path apps/game --quit
```

**2. Start the server in terminal 1.** It runs the shared world without a game window:

```powershell
godot --headless --path apps/game -- --server
```

Wait for `Arvenok server ready` and leave the server running. The default address is `127.0.0.1`, TLS/TCP port `7777`.

**3. Start a client in terminal 2.** This opens the playable game window and connects to the local server:

```powershell
godot --path apps/game
```

**4. Start another client in terminal 3** to test multiplayer combat:

```powershell
godot --path apps/game
```

The client opens Login. Choose **Register account**, enter a username and a password with 12?128 characters, then confirm the password. Login opens six character slots. Select an empty slot, choose Warrior, Mage or Archer, enter a unique nickname and create the character. Select its card and choose **Enter world**. Use a different account in the second client to play together.

**Character selection** in the world menu saves and despawns your current character while keeping you logged in. **Logout** saves the character and ends the session. To permanently delete an offline character, select its card, click **Delete character** and type its exact nickname. Each account supports six characters and one active world character.

The server stores accounts and characters under .local/server/world-001 in accounts.sqlite3. The first local server start generates a localhost certificate and private key in .local/tls. Local clients explicitly trust that certificate. Start the server before the first client. **Reconnect** retries a failed connection. Per-character hotbars remain in .local/client/hotbars.

Old anonymous profiles are obsolete. The [account operations guide](docs/operations/accounts.md) covers the backup and reviewed cleanup workflow, remote TLS configuration and acceptance tests.

If PowerShell cannot find `godot`, replace it with the full path to your Godot .NET executable, preceded by PowerShell's `&` operator. For example, adjust this path to your installation:

```powershell
& 'C:\Tools\Godot\godot.exe' --headless --path apps/game -- --server
```

The repository also provides `./tools/run-game.ps1 -Mode server` and `./tools/run-game.ps1 -Mode client` as alternatives to the launch commands above. Use a separate terminal for each process. If port `7777` is occupied, append `--port 7778` to the server command and use `godot --path apps/game -- --port 7778` for **both** clients. Application options belong after the `--` separator.

### Controls

Choose **Warrior, Mage or Archer** during character creation. Left-click ground to navigate around obstacles, or a player or monster to select them. **Space** begins basic attacks and pursuit. By default **1** uses your damage skill, **2** mobility (Blink toward the cursor), **3** interrupt and **4** defense. **K** or the points button opens Skills to compare and upgrade ranks. **Escape** closes Skills, or otherwise cancels movement/pursuit/casting. **F8** shows combat activity in Debug builds.

**I**, **E** or the Backpack button opens inventory/equipment. Click to inspect; drag to move/swap/merge. **Shift-drag** to an empty slot or **Split stack** chooses a quantity; **Sort** groups items. Double-click gear or select **Equip**; dragging it onto its equipment slot also works. Select equipped gear and **Unequip** to return it to the backpack. Inspect gear for its class and level requirements. Greatswords and daggers unlock at levels 2 and 6. Stop attacking and wait five seconds after damage to change equipment. Weapons add their listed direct damage bonus; body armor adds +5/+10 maximum HP without healing.

Select a crafting recipe scroll for its output and ingredient checklist; double-click or **Craft / Use scroll** consumes one scroll and ingredients only on success. Any class can craft. **F** picks nearby loot, or click a bag within two tiles. Loot is protected for its highest eligible contributor for 20 seconds, then public for 100 seconds. A full backpack leaves it on the ground. Uncollected bags expire and clear on restart; collected/crafted/equipped items persist. **Escape** closes the active window. The existing Training Shield fits the Warrior OffHand. Greatswords, staves and bows reserve OffHand. Equipping a greatsword returns a worn shield to the backpack if there is room.

**H** opens hotbar settings. Choose up to three bars with 8, 10 or 12 slots each. Set hotkeys, orientation, scale and visibility there. Unlock a bar and drag its header to move it. Drag a learned skill from **K → Skills → Skill tree** to a slot, drag between slots to swap, or right-click a slot to clear it. Each character keeps its layout on this computer. Cooldown numbers, cast progress and the recovery sweep follow the dedicated server.

Each class has two branches. Guardian and Reaver belong to Warrior, Ember and Aegis to Mage, and Marksman and Skirmisher to Archer. At level 4, a passive costs one skill point and requires rank 2 in its core skill. At level 6, its active also requires the passive and a matching monster-dropped spell scroll. Select **Learn spell** in the backpack or **Learn** in the tree while out of combat. Learning spends one point and the selected scroll together. Both branches can be learned. Changing equipment can disable an assigned skill until its required weapon is equipped again.

Wolves have a 15% independent chance to drop 1–2 `material.iron_ore` for the Forged Greatsword. The new weapon recipes use the existing crafting flow. See the [v0.2 combat, equipment and save rules](docs/architecture/decisions/0008-skill-tree-and-combat-ui.md).

The sanctuary protects players from combat, including PvE; combat is enabled in the wilderness. Moving cancels casting; ordinary damage does not interrupt. Defeated players respawn at full HP in the sanctuary after three seconds without losing progression. PvP awards no EXP. The server continues running when empty. See the [combat MVP guide](docs/architecture/combat-mvp.md) for rules, balance data, tests and limitations.

Slime, Mushroom, Wolf and Sprout wander in the wilderness. They share targeting/damage, roll their configured loot once when killed, and respawn at home; they remain passive. Water, shores, trunks, rocks and solid props block authoritative movement. Characters and monsters animate from server state. Firebolt and arrows travel cosmetically after confirmed resolution; server damage still occurs immediately at cast completion. Defensive auras expire with server effects. Rebuild and restart the server and every client after updating the world or protocol; running processes retain their previous assembly.

## Checks and working agreements

```powershell
./tools/validate.ps1
./tools/validate.ps1 -Configuration Release
./tools/smoke-game.ps1 -Godot godot
./tools/smoke-accounts.ps1 -Godot godot -Capture
```

Use PowerShell 7 (`pwsh`) on Linux/macOS; scripts also support Windows PowerShell 5.1. If your local Windows execution policy blocks repository scripts, a process-scoped invocation is `powershell -NoProfile -ExecutionPolicy Bypass -File ./tools/validate.ps1`; this does not change the machine policy. The regression suite is an executable harness, run by `validate.ps1`, rather than a `dotnet test` project. The smoke check requires the Godot .NET executable and a built Debug project.

- [Architecture and dependency boundaries](docs/architecture/README.md)
- [Contribution and ownership rules](CONTRIBUTING.md)
- [Art, animation, story and localization workflow](docs/contributing/content.md)
- [Regional hosting and capacity design](docs/operations/regional-hosting.md)
- [Local configuration and troubleshooting](docs/operations/local-development.md)
- [Progression implementation and validation results](docs/architecture/progression-validation.md)
- [Inventory implementation and validation results](docs/architecture/inventory-validation.md)
- [Account login, TLS, character management and legacy cleanup](docs/operations/accounts.md)
- [Skills and Combat UI v0.2 validation and multiplayer tests](docs/architecture/skills-v02-validation.md)
- [Production milestones](docs/architecture/roadmap.md)

CI builds Debug and Release and runs the regression suite on Windows and Linux. The solution maps Release to Godot's `ExportRelease`; the game has configuration-specific lockfiles because editor and export dependencies differ. Native Godot smoke checks are a separate command until an engine-equipped CI runner is configured. The [code-owner template](.github/CODEOWNERS.example) needs real organization teams and repository branch protection before owner review can be enforced.
