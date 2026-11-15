# Warcraft III object-data field constants

The generic object-data API uses typed field identifiers. They are not native function names; each identifier is passed to the matching getter/setter for an ability, item, unit or unit weapon.

JASS# already exposes the corresponding generic native families (for example `BlzGetAbilityIntegerField`, `BlzGetItemIntegerField`, `BlzGetUnitRealField` and `BlzGetUnitWeaponRealField`). The compiler and VS Code constant completion now also include the following standard field identifiers that were missing from the generated catalog snapshot.

## Ability fields

- Integer: `ABILITY_IF_ALLOWED_TARGETS`
- Real: `ABILITY_RF_CAST_POINT`, `ABILITY_RF_CAST_BACK_SWING`, `ABILITY_RF_CASTING_TIME`
- Integer level: `ABILITY_ILF_BUFFID`, `ABILITY_ILF_EFFECTID`, `ABILITY_ILF_ALLOWED_TARGETS`, `ABILITY_ILF_SPAWN_UNIT_ID_NSY2`, `ABILITY_ILF_REQUIRED_UNIT_TYPE`, `ABILITY_ILF_CONVERTED_UNIT_TYPE`, `ABILITY_ILF_PARTNER_UNIT_TYPE`, `ABILITY_ILF_PARTNER_UNIT_TYPE_ONE`, `ABILITY_ILF_PARTNER_UNIT_TYPE_TWO`, `ABILITY_ILF_UNIT_TYPE_ONE`, `ABILITY_ILF_UNIT_TYPE_TWO`, `ABILITY_ILF_UNIT_TYPE_SOD2`, `ABILITY_ILF_SUMMON_1_UNIT_TYPE`, `ABILITY_ILF_SUMMON_2_UNIT_TYPE`, `ABILITY_ILF_RACE_TO_CONVERT`, `ABILITY_ILF_NORMAL_FORM_UNIT_EME1`, `ABILITY_ILF_NORMAL_FORM_UNIT_MIL1`, `ABILITY_ILF_ALTERNATE_FORM_UNIT_MIL2`
- Integer level-array: `ABILITY_ILAF_BUFFID`

## Item fields

- Integer: `ITEM_IF_GOLD_COST`, `ITEM_IF_LUMBER_COST`, `ITEM_IF_OLD_LEVEL`, `ITEM_IF_STOCK_MAX`, `ITEM_IF_STOCK_REG`, `ITEM_IF_STOCK_DELAY`
- Boolean: `ITEM_BF_IGNORE_COOLDOWN`

## Unit fields

- Integer: `UNIT_IF_GOLD_COST`, `UNIT_IF_LUMBER_COST`, `UNIT_IF_GOLD_REPAIR_COST`, `UNIT_IF_LUMBER_REPAIR_COST`, `UNIT_IF_CARGO_SIZE`, `UNIT_IF_REPULSE_GROUP`, `UNIT_IF_REPULSE_PARAM`, `UNIT_IF_REPULSE_PRIO`, `UNIT_IF_BUILD_TIME`, `UNIT_IF_STOCK_MAX`, `UNIT_IF_POINT_VALUE`, `UNIT_IF_RACE`, `UNIT_IF_HIT_POINTS_BASE`, `UNIT_IF_MANA_BASE`, `UNIT_IF_MANA_START`, `UNIT_IF_REPAIR_TIME`, `UNIT_IF_DEFENSE_UPGRADE_BONUS`
- Real: `UNIT_RF_SPEED_MAX`, `UNIT_RF_SPEED_MIN`, `UNIT_RF_SPEED_BASE`
- Boolean: `UNIT_BF_REPULSE`, `UNIT_BF_CAN_FLEE`, `UNIT_BF_CAN_SLEEP`
- Weapon real: `UNIT_WEAPON_RF_MOTION_BUFFER`

## Example

```csharp
using Warcraft3;

unit target = GetTriggerUnit();
ability firebolt = BlzGetUnitAbility(target, AbilityId("AHbz"));

int allowedTargets = BlzGetAbilityIntegerField(
    firebolt,
    ABILITY_IF_ALLOWED_TARGETS
);

float castPoint = BlzGetAbilityRealField(
    firebolt,
    ABILITY_RF_CAST_POINT
);

int buffId = BlzGetAbilityIntegerLevelField(
    firebolt,
    ABILITY_ILF_BUFFID,
    0
);
```

The level index in level-field APIs is zero-based in JASS# examples. Use the field identifier type expected by the corresponding native; do not pass an ability field identifier to an item or unit getter.

## Implementation and regression coverage

The additional constants are kept in `src/JassSharp.Compiler/Runtime/Warcraft3AdditionalConstants.cs` rather than editing the generated `NativeCatalog.cs` snapshot by hand. `Checker.cs` recognizes this supplemental set, and `src/jasssharp-vscode/src/constants.ts` exposes the same names to completion.

The fixture `tests/fixtures/WarcraftObjectDataFieldsSmoke.w3js` compiles representative ability, item, unit and weapon field lookups.
