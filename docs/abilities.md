# Ability standard library

The compiler embeds `standard-library/Abilities.w3js`. The module provides `Abilities.UnitAbility`, a non-owning wrapper around one ability identified by its rawcode on a specific unit.

```csharp
using Abilities;
using Warcraft3;

public void OnInit()
{
    UnitAbility firebolt = new UnitAbility(GetTriggerUnit(), AbilityId("AHbz"));

    if (!firebolt.IsPresent())
    {
        firebolt.Add();
    }

    firebolt.SetLevel(2);
    firebolt.SetEnabled(false, false);
    firebolt.StartCooldown(3.0);
}
```

## UnitAbility

The constructor accepts the owning `unit` and an integer ability rawcode. Use Warcraft III's `AbilityId("AHbz")` to convert a four-character rawcode to the integer expected by the natives.

| Method | Behavior |
|---|---|
| `IsPresent()` | Checks whether the unit currently has a positive ability level |
| `Add()` / `Remove()` | Adds or removes the ability; returns the native result |
| `Level()` | Reads the current ability level |
| `SetLevel(level)` | Sets the level; negative input is clamped to zero |
| `IncreaseLevel()` / `DecreaseLevel()` | Uses the native level adjustment functions |
| `SetPermanent(permanent)` | Controls whether the ability is permanent |
| `SetEnabled(enabled, hideUI)` | Enables/disables the ability; `hideUI` is forwarded to Warcraft III |
| `Cooldown(level)` | Reads the cooldown duration for a zero-based ability level |
| `RemainingCooldown()` | Reads the current remaining cooldown |
| `StartCooldown(seconds)` / `EndCooldown()` | Starts or ends this ability's cooldown |
| `ManaCost(level)` | Reads mana cost for a zero-based ability level |
| `BooleanField(field)` / `SetBooleanField(field, value)` | Reads or writes a boolean ability field using `abilitybooleanfield` |
| `IntegerField(field)` / `SetIntegerField(field, value)` | Reads or writes an integer ability field using `abilityintegerfield` |
| `RealField(field)` / `SetRealField(field, value)` | Reads or writes a real ability field using `abilityrealfield` |
| `StringField(field)` / `SetStringField(field, value)` | Reads or writes a string ability field using `abilitystringfield` |
| `BooleanLevelField(field, level)` / `SetBooleanLevelField(field, level, value)` | Reads or writes a typed per-level ability field using `abilitybooleanlevelfield` |
| `BooleanLevelArrayField(field, level, index)` / `SetBooleanLevelArrayField(field, level, index, value)` | Reads or writes a typed per-level array element using `abilitybooleanlevelarrayfield` |
| `AddBooleanLevelArrayField(field, level, value)` / `RemoveBooleanLevelArrayField(field, level, value)` | Adds or removes a value from a typed per-level array field |
| `IntegerLevelField(field, level)` / `SetIntegerLevelField(field, level, value)` | Reads or writes a typed per-level ability field using `abilityintegerlevelfield` |
| `IntegerLevelArrayField(field, level, index)` / `SetIntegerLevelArrayField(field, level, index, value)` | Reads or writes a typed per-level array element using `abilityintegerlevelarrayfield` |
| `AddIntegerLevelArrayField(field, level, value)` / `RemoveIntegerLevelArrayField(field, level, value)` | Adds or removes a value from a typed per-level array field |
| `RealLevelField(field, level)` / `SetRealLevelField(field, level, value)` | Reads or writes a typed per-level ability field using `abilityreallevelfield` |
| `RealLevelArrayField(field, level, index)` / `SetRealLevelArrayField(field, level, index, value)` | Reads or writes a typed per-level array element using `abilityreallevelarrayfield` |
| `AddRealLevelArrayField(field, level, value)` / `RemoveRealLevelArrayField(field, level, value)` | Adds or removes a value from a typed per-level array field |
| `StringLevelField(field, level)` / `SetStringLevelField(field, level, value)` | Reads or writes a typed per-level ability field using `abilitystringlevelfield` |
| `StringLevelArrayField(field, level, index)` / `SetStringLevelArrayField(field, level, index, value)` | Reads or writes a typed per-level array element using `abilitystringlevelarrayfield` |
| `AddStringLevelArrayField(field, level, value)` / `RemoveStringLevelArrayField(field, level, value)` | Adds or removes a value from a typed per-level array field |
| `Handle()` | Returns the borrowed native `ability` handle for advanced native API calls |

Level-field and level-array-field indices are zero-based, matching the native API. Array accessors take both a level and an array index; add/remove operations take a level and a value. The wrapper forwards these indices unchanged and does not clamp or validate them. The caller must use a field handle of the matching type and valid indices.

Cooldown duration is expressed in game seconds. Negative cooldown durations are clamped to zero. The level arguments to `Cooldown` and `ManaCost` use Warcraft III's zero-based indexing and are clamped to at least zero. This differs from `GetUnitAbilityLevel`, which reports the unit's ability level starting at one.

## Ownership and lifetime

`UnitAbility` is a lightweight, non-owning view. It does not create or destroy the unit or ability, and it does not own the handle returned by `Handle()`. No destructor or `Dispose()` is needed. The view remains associated with its original unit and rawcode; if the ability is removed and later re-added, the same wrapper addresses that ability again.

The wrapper does not validate that the unit is non-null, alive, or currently owns the ability before forwarding calls to Warcraft III. Check `IsPresent()` when that distinction matters. Field access also assumes that the ability handle exists and that the supplied field belongs to the corresponding field type. The native setters return Warcraft III's success result; the wrapper does not impose additional validation. Gameplay-specific preconditions remain the caller's responsibility.

This module uses Warcraft III natives at runtime. In headless mode it can be checked and translated with `using Warcraft3;`, but the generated Lua still requires the Warcraft III runtime for these calls.
