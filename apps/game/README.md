# Godot host

Import `project.godot` from this directory; build `../../Arvenok.sln` for all projects. The engine loads `Features/Arena/Arena.tscn` in both client and dedicated-server modes. Preserve its root node path and RPC declarations on both sides.

`dotnet/project/solution_directory` points the editor to the root solution while the game `.csproj` stays beside `project.godot`, as supported by Godot's [multi-project setting](https://docs.godotengine.org/en/stable/classes/class_projectsettings.html#class-projectsettings-property-dotnet-project-solution-directory).

`ArenaSession` composes transport, configuration and simulation. `Presentation/ArenaView` is constructed only for clients. New features should own their scenes and adapters; use pure packages for authoritative rules and `Assets` for resources shared between features. Do not add server credentials or editor-source artwork here.

Headless execution is a development path. Dedicated exports that strip visual assets need a verified export pipeline before deployment. Godot supports [dedicated-server exports and headless execution](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_dedicated_servers.html).
