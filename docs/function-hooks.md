# Function hooks

JASS# supports hooks for global functions, including Warcraft III natives:

```csharp
using Warcraft3;

hook int GetUnitAbilityLevel(unit whichUnit, int abilityId)
{
    if (whichUnit == null)
        return 0;

    return original(whichUnit, abilityId);
}
```

A hook uses the target function's name and must match its complete signature: return type, parameter count, and parameter types. Native hooks require `using Warcraft3`. Hooks are global declarations; class methods and constructors cannot be hooked.

## Calling the original implementation

Inside a hook, `original(...)` calls the implementation that was wrapped by that hook. Its arguments are checked against the target signature, and its return type is the target function's return type.

A hook can bypass the original implementation by returning directly:

```csharp
hook bool CanUseAbility(unit whichUnit, int abilityId)
{
    if (whichUnit == null)
        return false;

    return original(whichUnit, abilityId);
}
```

## Multiple hooks

When multiple hooks target the same function, they execute in source declaration order. Each hook's `original(...)` continues to the next hook; the final hook's `original(...)` invokes the original function.

For example, with hooks A and B declared in that order, the call chain is:

```text
A -> B -> original function
```

The compiler emits wrappers in reverse order to preserve this runtime call order. Hook priorities are not used.

The compiler rejects unknown targets, signature mismatches, and invalid `original(...)` arguments. `original` is reserved within a hook body.
