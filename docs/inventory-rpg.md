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

The wrapper exposes bag lookup, equipment lookup, equip/unequip, equipment-type checks, empty-slot checks, and item metadata. It does not assume a fixed number of equipment slots.

## Talents

```csharp
Talents.Reset(hero);
```

This calls `BlzResetUnitTalents`. The engine owns the talent allocation and any talent UI; JASS# does not invent a separate talent-point model.

## RPG stat-aware healing

```csharp
float healed = RpgCombat.Heal(target, source, 125.0, false, true);
```

The final argument forwards the native `applyStatBonuses` switch to `BlzUnitHeal`. When enabled, the game applies the supported stat bonuses during the heal calculation. This is preferable to duplicating game formulas in user code.

Patch 3.0.0 introduced Ability Amp, Ability Vamp, Ability Speed, Mana Efficiency, Crit Chance/Damage, Ability Crit Chance/Damage and Resolve. The patch notes describe these gameplay stats, but the native API catalog currently available to JASS# does not expose a verified general-purpose getter/setter pair for those values. The library therefore does not fabricate accessors or assume undocumented field IDs. The module provides the verified inventory/talent integration and stat-aware heal entry point; direct stat inspection/modification should be added when the runtime exposes stable public natives or typed object fields.

## Inventory event context

`InventoryEvents.EquippedItem()` and `UnequippedItem()` wrap the native event-response functions. Call them only inside their corresponding inventory event callbacks.
