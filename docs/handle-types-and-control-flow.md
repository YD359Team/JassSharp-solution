# Warcraft III handle types and control flow

## Native handle type hierarchy

JASS# models the documented Warcraft III handle hierarchy for semantic type checking. The core chain is:

```text
handle
└── agent
    ├── event
    ├── player
    ├── widget
    │   ├── unit
    │   ├── destructable
    │   └── item
    ├── ability
    │   └── buff
    ├── force
    ├── group
    ├── trigger
    ├── triggercondition
    ├── timer
    ├── location
    ├── region
    ├── rect
    ├── boolexpr
    │   ├── conditionfunc
    │   └── filterfunc
    └── sound

handle
├── eventid
│   ├── gameevent
│   ├── playerevent
│   ├── playerunitevent
│   ├── unitevent
│   ├── limitop
│   ├── widgetevent
│   └── dialogevent
└── gamestate
    ├── igamestate
    └── fgamestate
```

Additional handle descendants from the Warcraft III API catalog are also accepted as handle values. Specialized parent relationships are recorded explicitly; remaining native descriptor/enum handle types are treated as direct descendants of `handle`.

This affects assignment and parameter compatibility. A child can be used where its parent is expected:

```csharp
unit hero = GetTriggerUnit();
widget target = hero;
agent object = hero;
handle reference = hero;

SetWidgetLife(target, 500.0);
int id = hero.HandleId;
```

The reverse conversion is not implicit: a `widget` cannot be assigned to a `unit` without a runtime type check and a deliberate conversion API. JASS# does not synthesize unsafe casts.

## HandleId property

Every Warcraft III native handle subtype exposes a read-only `HandleId` pseudo-property:

```csharp
int unitId = hero.HandleId;
int itemId = carriedItem.HandleId;
int frameId = frame.HandleId;
```

The property is compiler sugar for the real native call `GetHandleId(value)`; no wrapper object or Lua metatable is created. It is not available on `int`, `float`, `bool`, `string`, or `code`, because those are not Warcraft handle types. Direct calls to `GetHandleId(value)` remain supported.

## Loop control

`break` exits the innermost loop. `continue` skips the remainder of the current iteration and proceeds to the next iteration. Both are diagnosed when used outside a loop.

The Lua backend lowers `continue` to a unique `goto` label scoped to the generated loop body. For a `for` loop, the increment expression still executes after `continue`. For `do/while`, the condition is evaluated. For `foreach(group)`, a break flag stops executing the callback body for subsequent group members; the engine's `ForGroup` callback enumeration itself cannot be interrupted.

A bare `return;` exits a method/function with no return value:

```csharp
public void TryWork(bool ready)
{
    if (!ready)
    {
        return;
    }

    // Continue work
}
```

A value-returning method must return a compatible value; a bare return is a semantic error there.

## Sources

- [XGM: Передача и хранение данных в варкрафте](https://xgm.guru/p/wc3/w3datatypes) — handle semantics and `GetHandleId`.
- [XGM: Утекают ли не обнулённые переменные типа handle](https://xgm.guru/p/wc3/do-unnulled-handle-locals-leak) — native handle hierarchy and Reforged handle types.
- [W3CE common.j](https://github.com/Warcraft-III-Community-Edition/W3CE/blob/main/Scripts/common.j) — additional parent relationships such as `player : agent`, `gameevent : eventid` and `igamestate : gamestate`.
- Warcraft III native declarations in `common.j` — engine signatures remain authoritative for native parameter types.

The implementation is a compile-time model of Warcraft III's native types. It does not turn Warcraft handles into user-defined classes or add runtime casting.
