# Woodvale v0.1 runtime collection

The 27 supplied PNGs are retained byte-for-byte in their original `assets/` subpaths. The source archive is `Woodvale_OpenWorld_MMO_ArtPack_v01.zip`, supplied by the user. Original manifest, handoff, README, references, source art and provenance are in [the source collection](../../../../../content/art-source/art2d/woodvale/).

- Twelve 96x96 directional player idle poses: Warrior, Mage and Archer, facing down/left/right/up.
- One 192x128 terrain atlas containing 24 tiles, each 32x32.
- Four static monsters: Slime, Mushroom, Wolf and Sprout.
- Ten props: green/gold/pink trees, pine, large/small rocks, bush, flowers, fence and barrel.

Keep the Godot `.import` sidecars: textures use lossless compression with mipmaps disabled and preserve alpha. `ForestWorldView` and `WorldEntityView` use nearest-neighbor filtering, also selected as the project's default 2D texture filter. Never rescale all images to the tile dimensions; the prop/monster sizes differ.

Character roots are at feet (pixel 48,91 on their canvas), with sprite center offset (0,-43). Other sprites use bottom-center roots. Entities and props share a Y-sorted parent, so characters can pass behind tree canopies while their feet respect the small trunk footprint.

This base pack supplies no walk, attack, cast, hurt or death frames. The separate [combat animation extension](../woodvale-combat/README.md) now supplies those clips and VFX; these original static sprites remain reference skins and size metadata. There are no building or skill-icon PNGs in either collection.

See [world authoring](../../../../../content/worlds/README.md) for terrain, footprint and monster placement data. `tools/check-woodvale-assets.ps1` validates the supplied paths, dimensions, alpha and import settings without requiring Godot.
