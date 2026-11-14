# Timer standard library

The compiler embeds `standard-library/Timers.w3js`. This module wraps Warcraft III's native timer handle and therefore requires the Warcraft III API:

```csharp
namespace Gameplay
{
    using Warcraft3;
    using Timers;

    public void OnInit()
    {
        GameTimer cooldown = new GameTimer();
        cooldown.StartOnce(3.0, () =>
        {
            Print("Cooldown finished");
            cooldown.Dispose();
        });
    }
}
```

## GameTimer

Each `GameTimer` owns exactly one native `timer` created by `CreateTimer()`.

- `StartOnce(timeout, callback)` — starts a one-shot timer.
- `StartPeriodic(timeout, callback)` — starts a repeating timer.
- `Pause()` / `Resume()` — forwards to the corresponding Warcraft III natives.
- `Elapsed()`, `Remaining()`, `Timeout()` — reads the native timer state.
- `IsDisposed()` — reports whether the native handle has been destroyed.
- `Dispose()` — explicitly destroys the native handle. It is generated from `~GameTimer()`.

Callbacks are zero-argument JASS# lambdas. They are emitted as Lua callback functions and passed directly to `TimerStart`. A callback may capture local variables and the owning timer according to the compiler's current lambda-capture behavior.

Calling either start method on a disposed timer is a no-op. Starting an active timer replaces its previous schedule. Negative timeouts are clamped to zero; zero is passed to Warcraft III unchanged. Pausing and resuming a disposed timer are no-ops. Query methods return 0 after disposal.

The timer is an explicitly owned native resource: call `Dispose()` when it is no longer needed. The generated destructor is not invoked by Lua garbage collection. Disposal is idempotent; repeated calls do not destroy the handle more than once.

This module is Warcraft III-specific. It is not intended for headless execution without a Warcraft III runtime; headless compilation can still validate the source when `using Warcraft3;` is present, but the generated Lua requires the game natives at runtime.
