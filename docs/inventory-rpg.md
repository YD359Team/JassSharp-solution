# Inventory, talents and RPG integration

The compiler embeds `standard-library/Inventory.w3js`. This module targets the extended inventory/equipment and talent APIs introduced in Warcraft III 3.0.

## Extended inventory

```csharp
using Inventory;
using Warcraft3;

UnitInventory inventory = new UnitInventory(hero);
int bagSlots = inventory.BagSize();
item potion = inventory.BagItem(0);
loadoutslot weaponSlot = ConvertLoadoutSlot(0);
item weapon = inventory.EquipmentItem(weaponSlot);

if (inventory.IsEquipped(weapon))
{
    inventory.Unequip(weapon);
}

inventory.Equip(potion);
// UnitInventory is a non-owning view; no disposal is required.
```

`UnitInventory` is a non-owning view over a unit. It never creates, destroys, or transfers ownership of the unit or its items. Bag indices are zero-based and validated against `BlzUnitExtendedInventorySize`. Equipment slots use the native `loadoutslot` type. Equipment operations and queries forward to the Blz-prefixed 3.0 API; this avoids depending on the legacy unprefixed aliases that 3.0.1 announced would be removed in a future patch.

The wrapper exposes bag lookup, equipment lookup, equip/unequip, equipment-type checks, empty-slot checks, any-equipped checks, and item metadata. It does not assume a fixed number of equipment slots. The engine's 3.0 equipment UI is configured through the Object Editor abilities (Expanded Inventory and Equipment Slots); this wrapper does not grant those abilities or create the UI automatically.

## Talents

```csharp
Talents.Reset(hero);

// Talent choices represented by abilities:
int talentId = AbilityId("A0T1");
Talents.GrantAbility(hero, talentId);
Talents.SetAbilityRank(hero, talentId, 2);
Talents.SetAbilityPermanent(hero, talentId, true);
int rank = Talents.AbilityRank(hero, talentId);
```

This calls `BlzResetUnitTalents`. The engine owns the talent allocation and any talent UI. The talent tree is configured in Object Editor through the talent-controller ability and its per-tier ability lists. A selected talent grants an ability; JASS# helpers therefore expose ability-level operations rather than pretending that each talent is a standalone engine object. The helpers do not replace the game's talent UI or allocate talent points themselves. The documented 3.0 implementation has up to six tiers and up to three choices per tier; validate these limits against the target game build before relying on them. Morphing abilities can remove abilities granted by the talent system. The native `UnitMakeAbilityPermanent` is exposed as `Talents.SetAbilityPermanent`; use it for talent abilities that must survive form changes, and verify the behavior in the target game build. There is no dedicated verified `talent selected` event in the current native surface, so do not assume `EVENT_UNIT_HERO_SKILL` reports these selections.

## Ability progression and cooldown control

The existing `Abilities.UnitAbility` wrapper is useful alongside the 3.0 talent and RPG systems:

```csharp
using Abilities;

UnitAbility ability = new UnitAbility(hero, AbilityId("A001"));
int rank = ability.Level();
float baseCooldown = ability.Cooldown(rank - 1);
float remaining = ability.RemainingCooldown();

ability.StartCooldown(2.5);
ability.EndCooldown();
ability.SetEnabled(false, true);
```

It exposes ability rank, permanence, enabled state, level fields, mana cost and cooldown operations through Warcraft III natives. Treat level indices as zero-based when accessing per-level Object Editor fields.

## RPG stat-aware healing

```csharp
float healed = RpgCombat.Heal(target, source, 125.0, false, true);
```

The final argument forwards the native `applyStatBonuses` switch to `BlzUnitHeal`. When enabled, the game applies the supported stat bonuses during the heal calculation. The amount is passed through unchanged, including negative values; the engine may interpret negative healing as health loss. This is preferable to duplicating game formulas in user code.

Patch 3.0.0 introduced Ability Amp, Ability Vamp, Ability Speed, Mana Efficiency, Crit Chance/Damage, Ability Crit Chance/Damage and Resolve. The patch notes describe these gameplay stats, but the native API catalog currently available to JASS# does not expose a verified general-purpose getter/setter pair for those values. The library therefore does not fabricate accessors or assume undocumented field IDs. The module provides the verified inventory/talent integration and stat-aware heal entry point; direct stat inspection/modification should be added when the runtime exposes stable public natives or typed object fields.

## Filtered random item selection

```csharp
int rawcode = ItemSelection.ChooseRandomRawcode(
    ITEM_TYPE_PERMANENT,
    5,
    ConvertEquipmentType(0),
    ConvertItemTag(0),
    false,
    false);

if (rawcode != 0)
{
    item generated = CreateItem(rawcode, x, y);
}
```

The helper wraps the 3.0.1 `ChooseRandomItemExWithFilterAndIncludes` native. It returns an item rawcode (integer), not an item handle. The equipment type and item tag filters are passed to the engine; the two flags control inclusion of invalid morphs and objects whose `pickRandom` data is disabled. The engine's object data remains authoritative.

## Inventory event context

`InventoryEvents.EquippedItem()` and `UnequippedItem()` wrap the native event-response functions. Call them only inside their corresponding inventory event callbacks.
