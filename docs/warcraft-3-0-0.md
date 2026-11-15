# Warcraft III Reforged 3.0.0 API

This page records the gameplay-facing native additions introduced or highlighted by the Reforged 3.0.0 release. All signatures below were checked against the JASS# native signature catalog and the community-maintained Warcraft III `common.j` reference.

## Attack cooldown

| Native | JASS# signature | Purpose |
|---|---|---|
| `BlzResetUnitAttack` | `void BlzResetUnitAttack(unit, int)` | Resets the selected unit attack's cooldown. The second argument is the weapon index. |

## Ability cooldown control

| Native | JASS# signature | Purpose |
|---|---|---|
| `BlzGetUnitAbilityCooldownPercent` | `float BlzGetUnitAbilityCooldownPercent(unit, int)` | Gets the current cooldown as a percentage. |
| `BlzGetUnitAbilityCooldownRemaining` | `float BlzGetUnitAbilityCooldownRemaining(unit, int)` | Gets remaining cooldown duration. |
| `BlzGetUnitAbilityCooldown` | `float BlzGetUnitAbilityCooldown(unit, int, int)` | Gets the configured cooldown for an ability and level. |
| `BlzSetUnitAbilityCooldownRemaining` | `void BlzSetUnitAbilityCooldownRemaining(unit, int, float)` | Sets remaining cooldown duration. |
| `BlzSetUnitAbilityCooldownPercent` | `void BlzSetUnitAbilityCooldownPercent(unit, int, float)` | Sets remaining cooldown by percentage. |
| `BlzAdjustUnitAbilityCooldownRemaining` | `void BlzAdjustUnitAbilityCooldownRemaining(unit, int, float)` | Adjusts remaining cooldown duration. |
| `BlzAdjustUnitAbilityCooldownPercent` | `void BlzAdjustUnitAbilityCooldownPercent(unit, int, float)` | Adjusts remaining cooldown percentage. |
| `BlzStartUnitAbilityCooldown` | `void BlzStartUnitAbilityCooldown(unit, int, float)` | Starts an ability cooldown with an explicit duration. |
| `BlzEndUnitAbilityCooldown` | `void BlzEndUnitAbilityCooldown(unit, int)` | Ends an ability cooldown. |

The integer ability argument is the ability rawcode/ID, not a level index. Where a native accepts an additional integer level, the level argument is separate.

## Aura control

| Native | JASS# signature | Purpose |
|---|---|---|
| `BlzUnitEnableAuras` | `void BlzUnitEnableAuras(unit, bool, bool)` | Enables or disables a unit's aura abilities. The final flag controls whether the change affects the UI. |

## Special-effect animation

| Native | JASS# signature | Purpose |
|---|---|---|
| `BlzSetSpecialEffectAnimationBlendTime` | `void BlzSetSpecialEffectAnimationBlendTime(effect, float)` | Sets the animation blend time for an effect. |
| `BlzQueueSpecialEffectAnimation` | `void BlzQueueSpecialEffectAnimation(effect, string)` | Queues an animation on an effect. |
| `BlzSetSpecialEffectAnimation` | `void BlzSetSpecialEffectAnimation(effect, string)` | Sets the current animation on an effect. |

## New gameplay statistics

The 3.0.0 release introduced gameplay statistics including Ability Amp, Ability Vamp, Ability Speed, Mana Efficiency, Crit Chance, Crit Damage, Ability Crit Chance, Ability Crit Damage and Resolve.

These are gameplay/data-system statistics, not standalone native functions. They should not be represented as invented `BlzGet...` or `BlzSet...` native declarations. Access must be added only when the target runtime exposes a documented field/native or the map's data system provides a supported interface.

## JASS# coverage and regression test

The native declarations are registered in all three required places:

- `src/JassSharp.Compiler/NativeSigs.cs`
- `src/JassSharp.Compiler/Runtime/NativeCatalog.cs`
- `src/jasssharp-vscode/src/completion.ts`

`tests/fixtures/Warcraft300ApiSmoke.w3js` and stage `[21]` in `tests/run-smoke.ps1` compile and verify representative calls for the 3.0.0 attack, aura, cooldown and special-effect APIs. The shared smoke script runs this after the Frame API stage.

## Sources

- [Warcraft III 3.0.0 patch notes](https://warcraft.wiki.gg/wiki/Warcraft_III/Patch_3.0.0)
- [Warcraft III: Reforged — Forsaken Kingdom patch notes](https://us.forums.blizzard.com/en/warcraft3/t/warcraft-iii-reforged-forsaken-kingdom-patch-notes/38400/)
- [XGM: complete API change list](https://xgm.guru/p/wc3/Polnyy-spisok-izmeneniy-v-API-Puw)
- [JASS# common.j signature reference](https://github.com/Warcraft-III-Community-Edition/W3CE/blob/main/Scripts/common.j)
