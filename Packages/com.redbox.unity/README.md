# REDbox Unity Integration

Minimal runtime package to integrate REDbox in a Unity project.

## What is included
- Serial bridge: `ArduinoBridge`
- Runtime settings UI: `RuntimeSettingsMenu` and `RuntimeSettingsCanvasMenu`
- Event bus: `EventManager`
- Thread dispatch helper: `MainThreadDispatcher`
- Card/Settings models: `Card`, `HardwareSettings`, `GameConfig`

## Quick Setup
1. Create a fresh scene.
2. Add an empty GameObject and attach `ArduinoBridge`.
3. Create `HardwareSettings` asset:
   - `Assets > Create > RK > Settings > Hardware Settings`
4. Assign the settings asset to `ArduinoBridge.settings`.
5. (Optional) Add `EventManager` in scene.
6. Enter Play Mode and test serial connection.

## Notes
- This package currently focuses on runtime stability.
- Editor helper scripts were removed to avoid import/compiler issues across projects.
