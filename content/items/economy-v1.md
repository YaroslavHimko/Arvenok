# Loot, equipment and one-use recipes

The source pack's handoff and four JSON files are preserved in [arvenok-loot-v1](../art-source/ui/arvenok-loot-v1). `catalog-v1.json` holds 39 definitions: ten retained legacy entries plus twelve equipment items, eleven materials and six scrolls. Original package IDs and bonuses are unchanged. Runtime PNGs share `apps/game/Assets/UI/items` with the legacy SVGs; `icon` plus `iconFormat` selects the file. PNG import is lossless, has no mipmaps and uses nearest filtering in the native UI/world nodes. The pack's reference mockups are not runtime screens.

| Class | Tier I weapon / body | Tier II weapon / body |
|---|---|---|
| Warrior | Rustleaf Sword / Padded Guard Coat | Ironbark Sword / Warden Mail |
| Mage | Willow Staff / Apprentice Robe | Runestone Staff / Runewoven Robe |
| Archer | Elm Shortbow / Scout Vest | Ashwood Recurve / Ranger Jerkin |

Tier I requires level 1 and grants +1 weapon damage or +5 body maximum HP. Tier II requires level 5 and grants +2 or +10. Only the matching class can equip; anyone can collect/store/craft. Equipping never heals. Swap after stopping attacks and waiting five seconds since damage. Two separate equipment slots preserve the 24-slot default backpack.

Slime drops gel and occasional stone/swords; Sprout drops fiber, stone and archer-related crafting inputs; Mushroom drops spores, gel and mage-related inputs; Wolf drops pelts, fangs, stone and warrior/archer-related inputs. Each table also defines its equipment, scroll and key-piece probabilities, including rare direct Tier II gear. In v0.1 both materials were reserved. In v0.2 Wolves drop 1–2 `material.iron_ore` on a separate 15% roll for the Forged Greatsword recipe. `material.wood_log` remains reserved. The legacy `resource.*`, `monster.wolf_pelt` and training items remain separate IDs; they are not substituted for recipe materials.

Every material/key-piece entry rolls independently. A successful group roll chooses exactly one entry by positive weight. A failed group produces nothing. The server rolls only at an eligible monster death. The highest eligible contributor owns pickup for 20 seconds, then anybody may collect for 100 seconds. Uncollected loot expires and is cleared on server restart; collected loot persists. Click a bag or press F within two tiles on reachable land. A full backpack leaves the bag intact.

Select a scroll for output information and owned/required ingredient counts. Double-click it or press **Craft / Use scroll** to craft immediately. Success consumes one selected scroll and all listed ingredients; failure consumes nothing. The six recipes have no class restriction, station, skill, learning ledger, duration or RNG. Crafting output carries a unique ID and recipe provenance. Metadata remains available for later item systems without adding affixes now.

Authoring changes require a rebuild and restarting clients/server together. Update normalized item definitions and source recipe/loot JSON consistently, then run `tools/validate.ps1` and the native economy smoke. Validation rejects dangling IDs, incompatible tiers, invalid probabilities/weights and unsupported rule changes. Preserve stable IDs or provide an explicit migration; never delete an unknown saved item as a fallback. See [ADR 0007](../../docs/architecture/decisions/0007-loot-equipment-crafting.md) for ownership, replay and persistence boundaries.
