# Woodvale / Arvenok Experience UI v0.1

EXP bar and skill-point UI art concepts plus a complete server-authoritative EXP and skill progression specification for Codex.

- `CODEX_HANDOFF.md`: implementation contract, test cases, and RPG progression values.
- `asset_manifest.json`: imported asset dimensions and recommended use.
- `UI_CONCEPT_PREVIEW.png`: composition mockup, not a functional HUD.
- `ui/`: individual pixel-art PNG decorations, with an approximated transparent background keyed from a generated sprite sheet.

**Caveat:** These images are concept/prototype UI art. Their transparency edges and white glow centers have been cleaned/retouched manually; inspect in Godot and refine as needed. The true EXP bar and numbers must be live Godot Control nodes with server-owned values. Do not bake a percentage into the art.
