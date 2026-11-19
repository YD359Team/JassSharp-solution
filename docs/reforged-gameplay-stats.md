# Warcraft III Reforged 3.0.x gameplay stats

Warcraft III Reforged 3.0.0 introduced the following gameplay statistics:

- Ability Amp
- Ability Vamp
- Ability Speed
- Mana Efficiency
- Critical Strike Chance and Damage
- Ability Critical Strike Chance and Damage
- Resolve

These are gameplay/stat-system features, not automatically JASS native functions or object-data field constants.

## Script API audit

The JASS# native catalog and the community-maintained Warcraft III `common.j` reference were checked for public declarations or field identifiers for the statistics above.

As of the 3.0.1 API surface audited for this release:

- No dedicated getter/setter native for these statistics is declared in `common.j`.
- No corresponding typed ability/unit/item field constants were found in the audited declarations.
- The 3.0.1 patch notes mention a behavioral fix for Resolve applying to Aerial Shackles and Banish, but do not define a script accessor for Resolve.
- Existing generic object-data field natives do not imply that arbitrary internal stat properties can be queried. They require a valid field identifier accepted by the engine.

Consequently, JASS# does not invent APIs such as `GetUnitAbilityAmp`, `SetUnitResolve`, or fabricated `UNIT_RF_ABILITY_AMP` constants. Such declarations would compile but could fail at runtime because no matching engine native or documented field ID has been established.

## What can be done today

For custom maps, these mechanics can be represented by map-owned data and systems: store the values in structs/classes or hashtables, then apply their effects in the map's damage, healing, cooldown, and mana-cost logic. This is a custom implementation and must not be confused with reading or modifying the engine's built-in Reforged campaign stat block.

## Revisit criteria

This audit should be extended only when at least one of the following becomes available:

1. An official native declaration or patch note documenting a script getter/setter.
2. A verified `common.j` declaration with the exact native name and signature.
3. A verified object-data field ID and its matching conversion function.
4. A reproducible runtime test against Warcraft III 3.0.x proving the call and its semantics.

When such evidence appears, add the declaration to the compiler native catalog, VS Code completion, a focused fixture, and this document in the same change.

## Sources

- [Warcraft III 3.0.0 patch notes](https://warcraft.wiki.gg/wiki/Warcraft_III/Patch_3.0.0) — lists the new gameplay statistics and the editor/native additions.
- [Official Warcraft III 3.0.1 patch notes](https://us.forums.blizzard.com/en/warcraft3/t/warcraft-iii-reforged-forsaken-kingdom-patch-notes/38400/) — documents the Resolve behavior fix and the 3.0.1 native additions.
- [W3CE common.j reference](https://github.com/Warcraft-III-Community-Edition/W3CE/blob/main/Scripts/common.j) — community-maintained JASS declaration reference used for the native/field audit.

This page records a negative API finding intentionally: absence of a verified script interface is preferable to shipping guessed native declarations.
