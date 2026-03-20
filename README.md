# REDbox Unity Integration

Minimal runtime package to integrate REDbox in a Unity project.

## Install (UPM Git URL)

Base URL (latest):
- `https://github.com/jmgech/RedBox_UPMPlugin.git`

Pinned milestone:
- `https://github.com/jmgech/RedBox_UPMPlugin.git#milestone-2026-03-20-plugin-base-url-root-layout`

## What is included
- Serial bridge: `ArduinoBridge`
- Runtime settings UI: `RuntimeSettingsMenu` and `RuntimeSettingsCanvasMenu`
- Event bus: `EventManager`
- Thread dispatch helper: `MainThreadDispatcher`
- Card/Settings models: `Card`, `HardwareSettings`, `GameConfig`
- Sample tooling: Welcome Scene and Visual Novel Sample scene generator

## Quick Setup
1. Create a fresh scene.
2. Add an empty GameObject and attach `ArduinoBridge`.
3. Create `HardwareSettings` asset:
	- `Assets > Create > RK > Settings > Hardware Settings`
4. Assign the settings asset to `ArduinoBridge.settings`.
5. (Optional) Add `EventManager` in scene.
6. Enter Play Mode and test serial connection.

## Notes
- Current firmware compatibility target: REDBOX_Device RBX Protocol v0.3 (`reader_ready`, `card_detected`, `card_present`, `card_removed`, `heartbeat`, `error`).
