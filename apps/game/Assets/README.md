# Runtime assets

Only runtime-ready, licensed resources belong here. Use collection/asset-ID subfolders under `Art2D`, `Art3D`, `Animation`, `Audio` and `UI`. Keep feature-only resources with their feature; promote an asset here when multiple features share it. Editable source files belong in `content/art-source` outside the Godot import root.

Follow the [content workflow](../../../docs/contributing/content.md). `Art2D/woodvale` contains the supplied forest collection; `Art2D/woodvale-combat` adds replaceable animation strips and skill bindings. Source documentation and provenance are retained under matching `content/art-source/art2d/` collections. Final production art acceptance remains separate from integration.

`UI/woodvale-progression` contains seven transparent progression decorations, used by native EXP/Skills controls and a server-event-driven level-up visual. Its original source documentation is under `content/art-source/ui/woodvale-progression`; functional layout and replacement instructions are in the runtime collection README.

`UI/items` contains ten authored 32×32 SVG placeholders plus 30 supplied RGBA PNGs from Arvenok Loot, Equipment & Crafting v0.1. Twelve equipment icons, eleven material icons, six scrolls and one ground-loot bag use nearest filtering, lossless import and no mipmaps. They are inventory/world icons, not character appearance overlays. `tools/check-item-assets.ps1` validates catalog references; source specifications remain in `content/art-source/ui/arvenok-loot-v1`.
