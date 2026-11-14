# Lightning standard library

The compiler embeds `standard-library/Lightning.w3js`. Import `Lightning`, `Math`, `Colors`, and `Warcraft3`:

```csharp
using Lightning;
using Math;
using Colors;
using Warcraft3;

LightningEffect bolt = new LightningEffect(
    "CLPB", true,
    new Vector3(sourceX, sourceY, sourceZ),
    new Vector3(targetX, targetY, targetZ));

bolt.SetColor(Color.FromBytes(80, 160, 255, 220));
bolt.Move(
    new Vector3(sourceX, sourceY, sourceZ),
    new Vector3(targetX, targetY, targetZ),
    true);

bolt.Dispose();
```

The wrapper owns the native lightning handle created by `AddLightningEx`. Coordinates are Warcraft world coordinates; Z values are passed through unchanged. `Move(start, end, checkVisibility)` updates both endpoints using `MoveLightningEx` and stores the visibility-check flag. The two-argument `Move(start, end)` overload reuses the most recently selected flag (initially the constructor value). `SetColor` uses normalized RGBA channels, matching `SetLightningColor`, rather than 0..255 integers.

Call `Dispose()` when the effect is no longer needed. Disposal is idempotent: subsequent calls are safe no-ops. The native destroy result is intentionally not exposed as a gameplay success guarantee.
