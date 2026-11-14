# Floating text standard library

The compiler embeds `standard-library/FloatingText.w3js`. Import `FloatingText`, `Colors`, and `Warcraft3`:

```csharp
using FloatingText;
using Colors;
using Warcraft3;

FloatingTextTag tag = new FloatingTextTag(
    "+250", GetUnitX(target), GetUnitY(target), 40.0,
    0.023, 2.0, 1.5);

tag.SetColor(Color.FromBytes(255, 210, 80, 255));
tag.SetVelocity(0.0, 0.035);
tag.AttachToUnit(target, 50.0);

// Explicitly destroy the owned native handle.
tag.Dispose();
```

The constructor creates a non-permanent text tag, positions it in world coordinates, sets its size, lifespan and fade point, and makes it visible. The fade point is clamped to 0..lifespan. `AttachToUnit` switches positioning to the native unit-relative placement function. `SetPosition` returns to explicit world coordinates.

`SetVelocity` forwards native X/Y velocity values without reinterpretation. `SetAge` changes the native age. `SetSuspended` pauses/resumes native text-tag motion and aging; it does not pause the whole game.

A finite lifespan is handled by Warcraft III itself. The wrapper still owns the handle and exposes explicit `Dispose()`; do not rely on Lua garbage collection to destroy it. The game increased the floating-text cap to 10,000 in patch 3.0.0, but that is a hard engine cap, not a recommended target for simultaneous effects.
