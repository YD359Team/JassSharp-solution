# Special effects

The compiler embeds `standard-library/Effects.w3js`. Import `Effects` and `Warcraft3`:

```csharp
using Effects;
using Warcraft3;

effect fx = AddSpecialEffect("Abilities/Spells/Other/TalkToMe/TalkToMe.mdl", x, y);
SpecialEffects.Remove(fx);
```

`SpecialEffects.Remove` forwards to the Warcraft III 3.0.1 `BlzRemoveEffect` native. It is an explicit handle-lifecycle operation; do not continue using the handle after removal. Use the appropriate creation native for the effect's intended attachment and positioning.
