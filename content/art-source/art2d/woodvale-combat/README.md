# Woodvale Combat Animation Pack v0.1

**Add-on to** `Woodvale_OpenWorld_MMO_ArtPack_v01.zip` for the existing Godot 4 .NET / C# authoritative open-world MMORPG. This pack **does not replace the base art pack or your repository**.

## Contents
- **Players:** Warrior, Mage, Archer. 8 clips (`idle`, `walk`, `basic_attack`, `cast`, `interrupt`, `defend`, `hurt`, `death`) × 4 directions × 8 frames = **96 ready-to-import animation strips**.
- **Monsters:** Slime, Mushroom, Sprout, Wolf. 5 clips × 8 frames = **20 strips**.
- **Combat effects:** **18 animated VFX strips** covering all 12 starting class skills, projectiles, basic attacks, and some hit impacts.
- `animation_manifest.json`: exact frame counts, file paths, fps suggestions and loop mode.
- `CODEX_HANDOFF.md`: detailed architecture-aware implementation instructions.
- `combat_animation_contact_sheet.png`: frame overview.
- `combat_animation_preview.gif`: illustrative animated preview (not a game capture).

Every sprite sheet is **768 × 96 RGBA**, divided into **8 contiguous 96 × 96 frames**, left to right; no padding between frames. Keep the full transparent 96px cell for stable sprite pivots and gameplay feet anchor.

**Quality note:** This is a prototype animation pack, combining generated action key poses with procedural animation of existing source artwork. Down-facing Warrior sword attacks, Mage casting, and Archer bow actions have distinct illustrated action key poses. Left/right/up action clips are basic visual motion placeholders. Full production-quality eight-direction art, individualized attack frame drawings, and monster limb animation are not yet finished. Monster movement uses bobs/hops rather than new full hand-drawn frames. VFX are pixel-art prototypes and intentionally separate from gameplay hitboxes.

## Recommended Codex workflow
1. Unzip it beside the existing project or into its asset tree.
2. Give Codex `CODEX_HANDOFF.md` together with `animation_manifest.json` and this pack.
3. Ask Codex to inspect the repository and integrate incrementally.
4. Review the Godot runtime visuals at your actual camera zoom before commissioning production animation replacements.

No C# code is included because all code changes should be done by Codex in the existing repository.
