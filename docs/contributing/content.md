# Content workflow

Content production has its own ownership and review. Writers and artists should not need to modify combat or deployment code to contribute.

## Sources and runtime resources

Keep editable source files in `content/art-source/<discipline>/<collection>/<asset-id>/`. Export runtime-ready resources to `apps/game/Assets/<Category>/<collection>/<asset-id>/`, or keep feature-only resources beside their scene. Categories are `Art2D`, `Art3D`, `Animation`, `Audio` and `UI`. Godot only imports files under `apps/game`, so source documents, build tools and infrastructure never enter the resource database.

Use lowercase ASCII kebab-case asset IDs such as `prop-harbor-crate-01`; retain IDs when display names change. Preserve committed `.uid` files when moving scripts/resources. Use descriptive local names within each asset (`model.glb`, `albedo.png`, `material.tres`, `preview.png`); include an `asset.json` following [the example](../../content/art-source/asset.example.json). The manifest is a convention at this stage, not an implemented importer/schema validator. The submitting owner must review its fields.

For each asset record provenance, license or internal rights reference, creator/owner, source/export paths, source tool version, export recipe, scale/origin conventions, dependencies and intended runtime budget. Store test renders or review captures alongside the PR or artifact. Never use a machine-specific absolute path in a manifest.

1. Reserve the stable asset ID and coordinate with the collection owner. Lock nonmergeable sources when supported by the Git LFS remote.
2. Edit the source and export with the agreed tool version/settings. Treat generated exports as reproducible outputs; change the source rather than hand-editing the binary export.
3. Import into Godot, inspect the feature or isolated preview scene, and check materials, texture memory, collision, skeleton/animation bindings and platform constraints.
4. Review source, export and manifest together. Commit text `.tscn`, `.tres` and `.uid` files normally; binary formats are tracked by `.gitattributes` through LFS.
5. If exports move to an artifact store later, pin immutable content hashes in the manifest and fetch them before engine import. Never depend on a floating `latest` asset bundle.

Large scenes should compose owned child scenes. Shared materials, skeletons and animation libraries have named owners; modifying them requires affected consumers to validate. Animation events may trigger presentation effects, but damage and authoritative timing remain server decisions. Collision meshes must be reviewed separately from visual meshes. Keep 3D scale at one Godot unit per meter. The 2D world uses 32 pixels per tile, defined in CombatBalance; authored map geometry must follow that convention. Define per-platform triangle/texture/bone/audio budgets with technical art before a collection is accepted; none are measured yet.

## Story and localization

Use `content/narrative` for canon, character sheets, quest design and dialogue sources. Separate approved canon from proposals with document status and owner fields. Use stable character/quest/dialogue IDs and explicit references; do not couple a story ID to a localized name or source-file path. Writers own intent and text; gameplay owners review rewards, conditions, progression and state transitions. Future quests require a versioned schema, reference validation and content compilation before they can execute on the server.

Use `content/localization` for locale catalogs and terminology. Localizable runtime text uses stable keys, explicit placeholder descriptions and context. Translators must preserve placeholder types and plural semantics. Add missing-key, placeholder and locale-fallback validation when the first catalog is implemented; no localization runtime exists yet. Prototype UI strings remain in the view until that pipeline is implemented.

Raw authoring documents are not executable content. Approve and compile them into validated runtime data before a game build consumes them. Keep story spoilers in the appropriate access-controlled source repository when access restrictions matter; folder names inside one Git repository do not provide permissions.

Godot's resource organization and import exclusions are described in the [official project organization guide](https://docs.godotengine.org/en/stable/tutorials/best_practices/project_organization.html).
