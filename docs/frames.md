# Frame API standard library

The compiler embeds `standard-library/Frames.w3js`. Import `Frames` and `Warcraft3`:

```csharp
using Frames;
using Warcraft3;

public void OnInit()
{
    Frame root = Frame.FromOrigin(ORIGIN_FRAME_GAME_UI, 0);
    Frame panel = Frame.Create("Frame", root.Handle(), 0, 0);

    panel.SetSize(0.30, 0.12);
    panel.SetPoint(
        FRAMEPOINT_CENTER,
        root.Handle(),
        FRAMEPOINT_CENTER,
        0.0,
        0.0);
    panel.SetVisible(true);
    panel.SetText("Hello from JASS#!");

    // Explicitly destroy frames created by this code.
    panel.Dispose();

    // root is borrowed from the game; Dispose() only invalidates this wrapper.
    root.Dispose();
}
```

## Frame factories

| Factory | Behavior | Ownership |
|---|---|---|
| `Frame.Create(template, parent, priority, context)` | Calls `BlzCreateFrame` | Owned |
| `Frame.CreateSimple(template, parent, context)` | Calls `BlzCreateSimpleFrame` | Owned |
| `Frame.CreateByType(type, name, parent, inherits, context)` | Calls `BlzCreateFrameByType` | Owned |
| `Frame.FromOrigin(type, index)` | Wraps `BlzGetOriginFrame` | Borrowed |
| `Frame.FindByName(name, context)` | Wraps `BlzGetFrameByName` | Borrowed |

`Frame.LoadTOC(path)` forwards to `BlzLoadTOCFile`. Load the TOC before creating frames that depend on templates defined by it.

The public constructor `Frame(framehandle, ownsHandle)` is intended for advanced interop. Pass `true` only when this wrapper is the unique owner responsible for destroying the handle. Never create two owning wrappers for the same native frame.

## Operations

- Layout: `SetPoint`, `SetAbsolutePoint`, `ClearPoints`, `SetAllPoints`, `SetParent`, `Parent`.
- Visibility and input: `SetVisible`, `IsVisible`, `SetEnabled`, `IsEnabled`, `SetFocus`, `CageMouse`, `Click`.
- Text: `SetText`, `Text`, `AddText`, `SetTextSizeLimit`, `TextSizeLimit`, `SetTextColor`, `SetTextAlignment`, `SetFont`.
- Appearance: `SetTexture`, `SetModel`, `SetAlpha`, `Alpha`, `SetScale`, `SetVertexColor`, `SetSpriteAnimation`, `SetLevel`.
- Dimensions and controls: `SetSize`, `Width`, `Height`, `SetValue`, `Value`, `SetMinMaxValue`, `SetStepSize`, `SetAutoScroll`.
- Hierarchy: `ChildrenCount`, `Child`, `Name`, `Handle`.

`SetAlpha` clamps its input to the native 0–255 range. `SetTextSizeLimit`, `SetLevel`, and `SetStepSize` clamp negative inputs to zero. `SetMinMaxValue` raises the maximum to the minimum if the supplied range is inverted. Other values are forwarded to Warcraft III unchanged.

`Handle()`, `Parent()`, and `Child(index)` return borrowed native handles. They do not transfer ownership. `Child(index)` uses the native zero-based child index and returns null for a negative or out-of-range index.

## Events

Register a native frame event on a trigger, then configure the trigger's actions as usual:

```csharp
trigger clickTrigger = CreateTrigger();
event click = panel.RegisterEvent(clickTrigger, FRAMEEVENT_CONTROL_CLICK);
```

Inside the trigger action, read the current event context through `FrameEvents.CurrentFrame()`, `CurrentEvent()`, `CurrentValue()`, and `CurrentText()`. These values are meaningful while handling the corresponding frame event; do not cache them as a substitute for event-specific state.

## Coordinate conversion

`FrameCoordinates.PixelToFrameX/Y` and `FrameToPixelX/Y` expose Warcraft III's native conversion functions. The frame coordinate space is normalized and affected by the game UI scale and client dimensions; avoid assuming a fixed pixel-to-frame ratio.

## Ownership and runtime constraints

Each wrapper tracks whether it owns its native handle. `Dispose()` destroys the native frame only for owning wrappers. For borrowed wrappers it merely marks that wrapper as disposed. Disposal is idempotent; methods called after disposal become no-ops or return neutral values. The destructor is manual and is not invoked by Lua garbage collection.

Destroying a parent frame may invalidate child handles. Do not continue using wrappers for descendants after their native parent has been destroyed. Likewise, do not destroy a handle through both an owning wrapper and a direct native call.

Frame UI is client-local. Create and mutate frames only in the appropriate local-player context (normally guarded by `GetLocalPlayer() == GetTriggerPlayer()` or the corresponding local UI condition). Do not put gameplay state changes inside a local-only UI branch: that can desynchronize the game simulation.

This module requires Warcraft III's Frame API at runtime. Headless mode can check and translate it when `using Warcraft3;` is present, but the generated Lua still requires the game runtime.
