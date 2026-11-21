# Woodvale progression presentation

The seven PNG decorations are byte-for-byte copies from `Woodvale_XP_Skill_Progression_UI_v01.zip`. Their manifest and original handoff are retained under `content/art-source/ui/woodvale-progression`. Images use transparent, lossless RGBA imports, no mipmaps and nearest filtering. `tools/check-progression-assets.ps1` validates dimensions, format and import settings.

`ProgressionHud` supplies native labels, clipping, buttons and the four skill comparisons. The artwork contains approximate cutouts, not nine-patch skins; changing controls never changes character data. The EXP fill clips left-to-right inside the frame. Level badge and skill-point icon have native text. Sparkles mark awarded EXP, and the level-up image briefly fades above the authoritative character after a server event. The preview mockup is not used as a functional HUD.

Replace PNGs and update `asset_manifest.json` plus the small layout bindings in `ProgressionHud` when dimensions or decorative margins change. Run editor import and validation. Keep authoritative definitions in `content/progression`; no EXP calculations, rank effects or upgrade rules belong in this presentation component.

Press **K** or the points button to open Skills; **Escape** closes it. The window compares current and next effects, displays required levels, and disables unaffordable, locked, maximum-rank or pending upgrades. Native controls consume clicks while the window is open. The existing 1000×650 design viewport scales with preserved aspect ratio at 1280×720 and 1920×1080; black side margins may be present.
