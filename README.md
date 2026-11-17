# ARVENOK — first playable PvP prototype

**Arvenok is a WORKING TITLE only. Trademark and domain clearance NOT completed.**

Godot **4.7.2 .NET** + C# targeting .NET 8. One authoritative headless server, up to 2 connected clients, and a deliberately tiny 2D arena.

## Install

1. Download **Godot 4.7.2 .NET**, *not* the normal Godot build.
   https://godotengine.org/download/windows/
2. Install a **.NET 8 or newer SDK** (plus the .NET 8 targeting pack if your SDK does not include it).
   https://dotnet.microsoft.com/download
3. Open this directory in Godot's Project Manager (Import -> choose `project.godot`).
4. In Godot, build the C# solution via the **Build** button / **Project > Tools > C# > Build** depending on your editor layout.
5. Optional: configure Godot Editor Settings > Dotnet > Editor > External Editor = VS Code; install the Microsoft C# extension.

## Run locally on Windows

Replace `godot` with the full path to your **Godot .NET executable** if it isn't on PATH.
Open three terminal windows in this directory.

**Window A: dedicated server**

```powershell
godot --headless --path . -- --server
```

**Window B: client 1**

```powershell
godot --path .
```

**Window C: client 2**

```powershell
godot --path .
```

If you get a missing C# assembly error, first build the project from the editor (or run `dotnet build`).
The server listens on **UDP 7777** on localhost. No router or firewall forwarding is necessary for this local test.

## Controls

- Left-click empty arena: **click-to-move** (server simulates movement).
- Left-click opposing player's circle: **select target** (yellow ring).
- Press **1**: melee strike, within 90 pixels, 15 damage, 0.8-second cooldown.
- Press **2**: magic bolt, within 240 pixels, 1.25-second cast, 30 damage.
- Damage interrupts casts. Moving cancels your own cast.
- Defeated players revive after 3 seconds with full HP.

Both clients should see the same positions and hit points. Disconnecting a client removes that fighter on the other client. The server is **not a player**.

## Scope / limitations

- No authentication; use only on trusted local networking for now.
- The code is illustrative prototype code and has **not been compiled in this environment**. If your installed Godot SDK API differs, fix compilation before continuing.
- Server-authoritative movement, combat, hit points, cast timer, and cooldowns.
- Networking uses reliable RPCs for player intent and unreliable RPCs for state snapshots.
- No client interpolation, lag compensation, pathfinding, collisions, obstacles, replication batching, persistence, guilds, equipment, or security hardening yet.

## The next milestone

1. Add pathfinding/navigation and movement interpolation.
2. Add attack feedback, cast bars, cooldown display and server-side input throttling.
3. Add one Warrior, one Mage with differing skills.
4. Add a third/fourth client for 2v2 teams.
5. Only then add equipment and SQLite persistence.

## Why this is shaped this way

- A tiny playable duel proves the core combat loop without the pressure of building an MMORPG.
- Both clients share a single stable `Arena.tscn` scene path, which is required for Godot's high-level RPCs.
- Incoming client RPCs submit *intent*. The server obtains the sender ID from the transport (not user-supplied parameters) and checks distance, cooldown, and health before changing game state.
