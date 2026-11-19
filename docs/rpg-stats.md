# RPG and combat statistics

Import `RpgStats` for a typed facade over Warcraft III's native unit and weapon statistics.

```csharp
using RpgStats;

UnitCombatStats stats = new UnitCombatStats(hero);

int strength = stats.Strength(true);
int agility = stats.Agility(true);
int intelligence = stats.Intelligence(true);

float armor = stats.Armor();
int damage = stats.BaseDamage(0);
float attackCooldown = stats.AttackCooldown(0);

stats.SetArmor(armor + 2.0);
stats.SetBaseDamage(damage + 10, 0);
stats.SetAttackCooldown(attackCooldown * 0.9, 0);
```

## Exposed engine-backed values

- Hero strength, agility and intelligence, with the native `includeBonuses` switch and corresponding setters.
- Unit armor.
- Base damage, damage dice count/sides and attack cooldown per weapon index.
- Movement speed.
- Maximum life/mana and current life/mana.
- Ability cooldown duration, remaining cooldown and cooldown percentage, including set/adjust operations.

The wrapper forwards weapon indices and ability level indices to Warcraft III. Ability cooldown level indices are zero-based. The meaning and valid range of weapon indices are determined by the native API and the unit's weapon data; use the indices configured for that unit.

## RPG-specific attributes

This module intentionally does not invent generic accessors for Ability Amp, Ability Vamp, Ability Speed, Mana Efficiency, Critical Chance, Critical Damage, Ability Critical Chance, Ability Critical Damage or Resolve.

These values may be implemented through object data, abilities, campaign-specific systems or engine internals. The currently verified native signatures do not establish a universal getter/setter pair for them. Model those stats in your own map data until a stable public native interface is confirmed.

For ability progression, level fields and per-ability cooldown operations, see `Abilities.UnitAbility` in `standard-library/Abilities.w3js`. For talent reset and the 3.0 inventory model, see [inventory and talents](inventory-rpg.md).
