# Unit group standard library

The compiler embeds `standard-library/Groups.w3js`. Import `Groups` and `Warcraft3` to use the wrapper:

```csharp
namespace Gameplay
{
    using Groups;
    using Warcraft3;

    public void OnInit()
    {
        UnitGroup nearby = new UnitGroup();
        nearby.Add(GetTriggerUnit());

        nearby.ForEach(() =>
        {
            unit current = GetEnumUnit();
            KillUnit(current);
        });

        nearby.Dispose();
    }
}
```

## UnitGroup

Each instance owns exactly one native `group` created by `CreateGroup()`.

| Method | Behavior |
|---|---|
| `Add(unit)` | Adds a unit and returns the native `GroupAddUnit` result |
| `Remove(unit)` | Removes a unit and returns the native `GroupRemoveUnit` result |
| `Clear()` | Removes all members without destroying the group handle |
| `Count()` | Returns `BlzGroupGetSize` |
| `First()` | Returns an arbitrary member, or null when empty |
| `ForEach(callback)` | Invokes a zero-argument callback for each member through `ForGroup` |
| `IsDisposed()` | Reports whether the native group handle was destroyed |
| `Dispose()` | Explicitly destroys the native handle; generated from `~UnitGroup()` |

Inside a `ForEach` callback, use Warcraft III's `GetEnumUnit()` to obtain the current member. The callback is passed directly to `ForGroup`; it is not invoked with a unit parameter.

Group iteration order is unspecified. Avoid changing group membership while a `ForEach` enumeration is in progress. If the algorithm must remove members as it processes them, use the native `FirstOfGroup` / `GroupRemoveUnit` pattern explicitly rather than mutating the collection from its enumeration callback.

The wrapper owns only the native group handle. It does not own, destroy, or otherwise manage the lifetime of units stored in the group. `Clear()` does not destroy the handle; `Dispose()` does.

After disposal, `Add` and `Remove` return false, `Count` returns zero, `First` returns null, and mutating/enumeration methods become no-ops. Disposal is idempotent. The destructor is manual and is not invoked by Lua garbage collection.

This module requires Warcraft III natives at runtime. Headless mode can validate and translate the source when `using Warcraft3;` is present, but it does not provide a Warcraft III runtime.
