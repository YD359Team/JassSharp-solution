# Frame API standard library

The compiler embeds `standard-library/Frames.w3js`. Import `Frames` and `Warcraft3`:

```csharp
using Frames;
using Warcraft3;

public void ShowPlayerUI(player targetPlayer)
{
    // Frame UI is local to the client that owns targetPlayer.
    if (GetLocalPlayer() != targetPlayer) { return; }

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

`Frame.LoadTOC(path)` forwards to `BlzLoadTOCFile`; `Frame.HideOriginFrames(hide)` forwards to `BlzHideOriginFrames`.

`FrameCoordinates.HudScale()`, `ClientWidth()`, and `ClientHeight()` expose the current client's HUD scale and dimensions. Load the TOC before creating frames that depend on templates defined by it.

The public constructor `Frame(framehandle, ownsHandle)` is intended for advanced interop. Pass `true` only when this wrapper is the unique owner responsible for destroying the handle. Never create two owning wrappers for the same native frame.

## Operations

- Layout: `SetPoint`, `SetAbsolutePoint`, `ClearPoints`, `SetAllPoints`, `SetParent`, `Parent`.
- Visibility and input: `SetVisible`, `IsVisible`, `SetEnabled`, `IsEnabled`, `SetFocus`, `CageMouse`, `Click`.
- Text: `SetText`, `Text`, `AddText`, `SetTextSizeLimit`, `TextSizeLimit`, `SetTextColor`, `SetTextAlignment`, `SetFont`.
- Appearance: `SetTexture`, `SetModel`, `SetAlpha`, `Alpha`, `SetScale`, `SetVertexColor`, `SetSpriteAnimation`, `SetLevel`.
- Dimensions and controls: `SetSize`, `Width`, `Height`, `SetValue`, `Value`, `SetMinMaxValue`, `SetStepSize`, `SetAutoScroll`.
- Hierarchy: `ChildrenCount`, `Child`, `Name`, `Handle`.

`SetAlpha` clamps its input to the native 0–255 range. `SetTextSizeLimit`, `SetLevel`, and `SetStepSize` clamp negative inputs to zero. `SetMinMaxValue` raises the maximum to the minimum if the supplied range is inverted. Other values are forwarded to Warcraft III unchanged. Control-specific methods must be used only with compatible frame types: for example, `SetValue` and `SetMinMaxValue` are intended for value controls, while `SetAutoScroll` is intended for text-area frames. The wrapper does not query or validate the native frame type.

`Handle()`, `Parent()`, and `Child(index)` return borrowed native handles. They do not transfer ownership. `Child(index)` uses the native zero-based child index and returns null for a negative or out-of-range index.

## Typed controls

The generic `Frame` wrapper exposes the complete low-level API. The following wrappers provide smaller APIs for common frame types:

| Wrapper | Native type | Main operations |
|---|---|---|
| `ButtonFrame` | `BUTTON` | Text, enabled/visible state, click, events |
| `TextFrame` | `TEXT` | Text, font, color, alignment, size |
| `SliderFrame` | `SLIDER` | Value, range, step size, events |
| `EditBoxFrame` | `EDITBOX` | Text, focus, text-size limit, events |
| `CheckBoxFrame` | `CHECKBOX` | Text, enabled/visible state, checked/unchecked events |
| `PopupFrame` | `POPUPMENU` | Enabled/visible state, selection-changed event |
| `MenuFrame` | `MENU` | Enabled/visible state, frame events |

Each `Create` factory creates an owned frame. The public constructor accepts a native handle and an explicit ownership flag for interop. Do not create multiple owning wrappers for the same handle. As with `Frame`, call `Dispose()` explicitly; there is no Lua-GC finalization.

Example:

```csharp
ButtonFrame button = ButtonFrame.Create(
    "MyButton",
    parent,
    "ScriptDialogButton",
    0);

button.SetText("Apply");
FrameEventSubscription click = button.Subscribe(
    FRAMEEVENT_CONTROL_CLICK,
    () =>
    {
        // Read frame-event context here.
        framehandle source = FrameEvents.CurrentFrame();
    });

// Release the event subscription before destroying its UI.
click.Dispose();
button.Dispose();
```

## Events

For low-level integration, register a native frame event on a trigger, then configure the trigger's actions as usual. For a self-contained subscription, use `Frame.Subscribe` or a typed wrapper's `Subscribe` method. It creates and owns a trigger and trigger action, invoking the supplied `code` callback for the event:

```csharp
trigger clickTrigger = CreateTrigger();
event click = panel.RegisterEvent(clickTrigger, FRAMEEVENT_CONTROL_CLICK);
```

Inside the trigger action or subscription callback, read the current event context through `FrameEvents.CurrentFrame()`, `CurrentEvent()`, `CurrentValue()`, and `CurrentText()`. These values are meaningful while handling the corresponding frame event; do not cache them as a substitute for event-specific state.

All typed controls expose `Subscribe(frameeventtype, code)`. `TextFrame` now supports subscriptions as well as the interactive controls. `CheckBoxFrame.SubscribeChecked` and `SubscribeUnchecked` are shortcuts for the corresponding checkbox events. Warcraft III exposes checkbox state changes as events, but the native API used by this library does not expose a checked-state getter or setter; the wrapper therefore does not invent `IsChecked` or `SetChecked`.

Use `PopupFrame.SubscribeSelectionChanged` for `FRAMEEVENT_POPUPMENU_ITEM_CHANGED`. Read the selected item index through `FrameEvents.CurrentValue()` inside that callback. `MenuFrame` wraps a native `MENU` frame; menu item construction remains the responsibility of its FDF/template because Warcraft III does not provide a dedicated native item-collection API for it.

Frame event types are not universally valid for every frame type. For example, checkbox events belong to checkbox frames, popup selection changes belong to popup-menu frames, edit-box events belong to edit boxes, and slider value changes belong to sliders. Select events supported by the concrete native frame type.

## Coordinate conversion

`FrameCoordinates.PixelToFrameX/Y` and `FrameToPixelX/Y` expose Warcraft III's native conversion functions. The frame coordinate space is normalized and affected by the game UI scale and client dimensions; avoid assuming a fixed pixel-to-frame ratio.

## Ownership and runtime constraints

Each wrapper tracks whether it owns its native handle. `Dispose()` destroys the native frame only for owning wrappers. For borrowed wrappers it merely marks that wrapper as disposed. Disposal is idempotent; methods called after disposal become no-ops or return neutral values. The destructor is manual and is not invoked by Lua garbage collection.

Destroying a parent frame may invalidate child handles. Dispose event subscriptions before destroying their associated frames so no trigger action remains registered for a UI object that no longer exists. Do not continue using wrappers for descendants after their native parent has been destroyed. Likewise, do not destroy a handle through both an owning wrapper and a direct native call.

Frame UI is client-local. Create and mutate frames only in the appropriate local-player context (normally guarded by `GetLocalPlayer() == GetTriggerPlayer()` or the corresponding local UI condition). Do not put gameplay state changes inside a local-only UI branch: that can desynchronize the game simulation.

This module requires Warcraft III's Frame API at runtime. Headless mode can check and translate it when `using Warcraft3;` is present, but the generated Lua still requires the game runtime.
