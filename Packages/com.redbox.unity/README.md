# REDbox Unity Integration

Minimal runtime package to integrate REDbox in a Unity project.

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

## Sample Visual Novel (No Hardware Required)
Use `Tools > REDbox > Samples > Create Visual Novel Sample` to generate a ready-to-play tutorial scene.

The generated sample demonstrates:
- plugin scope and scan-to-event flow,
- REDbox card taxonomy usage (Lore, World, Actor, Instruction),
- branching progression with card-gated steps,
- simulation controls for development without a physical scanner.

## Notes
- This package currently focuses on runtime stability.
- Editor helper scripts were removed to avoid import/compiler issues across projects.
- Current firmware compatibility target: REDBOX_Device RBX Protocol v0.3 (`reader_ready`, `card_detected`, `card_present`, `card_removed`, `heartbeat`, `error`).
