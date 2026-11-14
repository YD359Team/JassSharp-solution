# Color standard library

The compiler embeds `standard-library/Colors.w3js`. Import `Colors`:

```csharp
using Colors;

Color accent = new Color(0.2, 0.6, 1.0, 1.0);
Color opaque = Color.FromRgb(40, 120, 255);
Color translucent = Color.FromBytes(40, 120, 255, 160);
Color fromFrameColor = Color.FromArgb(0xA02878FF);
Color percentage = Color.FromPercent(20.0, 60.0, 100.0, 100.0);
string colored = accent.WrapText("|cffffcc00JASS#|r");
```

## Canonical representation

`Color` stores normalized floating-point RGBA channels in the inclusive range 0..1. Constructor inputs are clamped. This is the canonical representation; do not pass these normalized values directly to APIs that expect byte channels or packed integers.

| Warcraft III API | Expected representation | Adapter |
|---|---|---|
| `SetLightningColor` | normalized real RGBA, 0..1 | `ApplyToLightning` |
| `SetUnitVertexColor` | integer RGBA, 0..255 | `ApplyToUnitVertexColor` |
| `SetTextTagColor` | integer RGBA, 0..255 | `ApplyToTextTag` |
| `BlzSetSpecialEffectColor` + alpha | integer RGB/A, 0..255 | `ApplyToEffect` |
| `BlzFrameSetTextColor` / vertex color | packed integer `0xAARRGGBB` | `ApplyToFrameText` / `ApplyToFrameVertex` |
| In-game colored text | markup `|cAARRGGBB...|r` | `ToMarkup` / `WrapText` |

`FromBytes` accepts integer channels; `FromPercent` accepts percentage channels in 0..100. Both normalize into the same internal representation. Byte conversion rounds to the nearest integer.

## Player colors are a separate type

Warcraft's `playercolor` is a native handle, not an RGB structure. It must not be confused with `Color`. Use the dedicated helpers when assigning a player's team color:

```csharp
Color.ApplyPlayerColorToUnit(myUnit, GetPlayerColor(owner));
Color.ApplyPlayerColorToItem(myItem, GetPlayerColor(owner));
Color.ApplyPlayerColorToDestructable(myDestructable, GetPlayerColor(owner));
Color.ApplyPlayerColorToEffect(myEffect, owner);
```

The effect helper takes a `player` because `BlzSetSpecialEffectColorByPlayer` expects a player handle. The unit and item helpers take `playercolor`.

## Text formatting

`ToMarkup()` returns the Warcraft color prefix `|cAARRGGBB`; `WrapText(value)` appends the reset code `|r`. This composes with existing markup, so nested color segments can be authored explicitly:

```csharp
string message = Color.Red().WrapText("Critical ") +
    Color.White().WrapText("hit for ") +
    Color.FromBytes(255, 210, 80, 255).WrapText("250");
```

This module does not parse existing markup or infer colors from player handles. It only creates color values and converts them to documented native representations.
