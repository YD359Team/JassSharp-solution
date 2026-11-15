# Warcraft III 3.0.1 API

This page records the Warcraft III 3.0.1 native/API additions and compatibility rules represented by JASS#.

## Native functions added in 3.0.1

| Native | JASS# signature | Purpose |
|---|---|---|
| `BlzResetUnitTalents` | `void BlzResetUnitTalents(unit)` | Resets a unit's talent allocation. |
| `ChooseRandomItemExWithFilterAndIncludes` | `int ChooseRandomItemExWithFilterAndIncludes(itemtype, int, equipmentType, itemTag, bool, bool)` | Selects a random item with equipment-type, tag and inclusion filters. |
| `BlzUnitHeal` | `float BlzUnitHeal(unit, unit, float, bool, bool)` | Applies healing and returns the actual healing amount. Parameters are target, source, requested life, item-origin flag and stat-bonus flag. |
| `BlzRemoveEffect` | `void BlzRemoveEffect(effect)` | Removes an effect handle. |
| `BlzSetCameraAllowsHotkeyTargetLock` | `void BlzSetCameraAllowsHotkeyTargetLock(bool)` | Enables or disables camera target locking through the hotkey. |
| `BlzGetCameraAllowsHotkeyTargetLock` | `bool BlzGetCameraAllowsHotkeyTargetLock()` | Reads the camera hotkey target-lock setting. |
| `SetThematicMusicAbsoluteVolume` | `void SetThematicMusicAbsoluteVolume(int)` | Sets absolute thematic music volume. |

## Blz-prefixed equipment API

Warcraft III 3.0.1 adds Blz-prefixed forms of the equipment natives introduced in 3.0. Prefer these names for new maps; legacy unprefixed variants remain in the JASS# catalog for compatibility with older runtime/API environments.

- `BlzGetEquippedItem()` and `BlzGetUnequippedItem()`
- `BlzIsItemEquipped(item)` and `BlzIsItemInBag(item)`
- `BlzGetItemEquipmentType(item)` and `BlzGetItemTag(item)`
- `BlzUnitEquipItem(unit, item)`
- `BlzUnitUnequipItem(unit, item)`
- `BlzUnitUnequipItemFromSlot(unit, loadoutslot)`
- `BlzUnitHasItemBagged(unit, item)`
- `BlzUnitExtendedInventorySize(unit)`
- `BlzUnitItemInBagSlot(unit, int)`
- `BlzUnitItemInEquipmentSlot(unit, loadoutslot)`
- `BlzUnitHasItemEquipped(unit, item)`
- `BlzUnitHasLoadoutSlotEmpty(unit, loadoutslot)`
- `BlzUnitHasItemEquipmentOfType(unit, equipmentType)`
- `BlzUnitCanEquipItemOfEquipmentType(unit, equipmentType)`
- `BlzSetItemColor(item, playercolor)`

The patch notes spell one native as `BlzUnitHasAnyItemEquiped(unit)` (one `p` in `Equiped`). JASS# registers that exact engine spelling. The more grammatically conventional `BlzUnitHasAnyItemEquipped(unit)` is also retained as an additional compatibility spelling, but should not be assumed to exist in every Warcraft III runtime.

## Gameplay behavior and constants

3.0.1 adds the gameplay constant `CanInterruptByZeroDurationBuffs`. Disabling it prevents buffs applied with zero duration from interrupting channeling spells.

The patch also adds gameplay settings controlling whether illusions can receive healing and magic-resistance bonuses, plus trigger constants for Forsaken Paladin ability fields. These are object/gameplay configuration values, not standalone native functions; they must be represented as constants/configuration symbols rather than fabricated function signatures.

Camera note: camera-lock natives now automatically disable hotkey target locking when the camera is locked. Use the getter/setter above when a map needs explicit control of that behavior.

## JASS# integration

A native must be registered consistently in all three compiler/editor catalogs:

1. `src/JassSharp.Compiler/NativeSigs.cs` — semantic signature and return/parameter types.
2. `src/JassSharp.Compiler/Runtime/NativeCatalog.cs` — recognition as a Warcraft III runtime symbol.
3. `src/jasssharp-vscode/src/completion.ts` — VS Code completion signature and documentation.

Do not infer signatures from function names. In particular, `BlzUnitHeal` returns the actual healed amount (`float`), and `BlzRemoveEffect` accepts an `effect` handle.

## Sources

- [Warcraft III 3.0.1 patch notes (wc3.dev)](https://wc3.dev/patch-notes/)
- [XGM: Warcraft III Reforged 3.0.1 — список изменений](https://xgm.guru/p/wc3/Warcraft-3-Reforged-301-spisok-izmeneniy-RQE)


The regression fixture `tests/fixtures/Warcraft301ApiSmoke.w3js` and stage `[22/24]` in `tests/run-smoke.ps1` compile representative 3.0.1 calls, including talent reset, random item filtering, healing, effect removal, camera target-lock settings, equipment queries and the exact `BlzUnitHasAnyItemEquiped` spelling.
