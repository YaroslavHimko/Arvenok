# Localization

Keep source locale, translation catalogs and terminology here when the first localization feature is implemented. Select a format and locale fallback policy in its ADR. Stable keys must include context, typed placeholders and plural intent; changing English wording must not silently change an ID.

No localization runtime or catalogs are implemented yet. Runtime exports will live under the game project and must be checked for missing keys and placeholder mismatches.
