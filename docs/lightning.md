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

The wrapper owns the native lightning handle created by `AddLightningEx`. Coordinates are Warcraft world coordinates; Z values are passed through unchanged. `Move` updates both endpoints using `MoveLightningEx` and sets the visibility-check flag for that move. `SetColor` uses normalized RGBA channels, matching `SetLightningColor`, rather than 0..255 integers.

Call `Dispose()` exactly once per owning wrapper. The native destroy result is intentionally not exposed as a gameplay success guarantee.
