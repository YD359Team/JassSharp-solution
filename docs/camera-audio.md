# Camera and thematic audio settings

The compiler embeds `standard-library/Camera.w3js` and `standard-library/Audio.w3js`.

## Camera hotkey target lock

```csharp
using CameraControl;

CameraSettings.SetHotkeyTargetLockAllowed(false);
bool allowed = CameraSettings.IsHotkeyTargetLockAllowed();
```

These methods wrap the Warcraft III 3.0.1 `BlzSetCameraAllowsHotkeyTargetLock` and `BlzGetCameraAllowsHotkeyTargetLock` natives. This setting controls the player's hotkey-based camera target lock; it is distinct from the map's scripted camera follow/lock behavior.

## Thematic music volume

```csharp
using Audio;

AudioSettings.SetThematicMusicVolume(96);
```

The method wraps `BlzSetThematicMusicAbsoluteVolume` and clamps the input to the engine's 0..127 volume scale.
