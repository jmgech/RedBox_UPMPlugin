# REDbox Unity Integration

Unity package to add REDbox support quickly in any project.

## Features
- Serial REDbox bridge with reconnect and port auto-detection.
- Card scan event pipeline (`EventManager`).
- Runtime settings menu (Canvas + IMGUI fallback).
- Editor tools to generate settings menu UI.

## Requirements
- Unity 2022.3+
- Input System package
- TextMeshPro package

## Install (local path)
1. Open Package Manager.
2. Click `+` > `Add package from disk...` and select:
   `REDbox_Plugin/Packages/com.redbox.unity/package.json`

## Quick Setup
1. Create a `HardwareSettings` asset:
   - `Assets > Create > RK > Settings > Hardware Settings`
2. Add `ArduinoBridge` on a GameObject in your scene.
3. Assign your `HardwareSettings` asset to `ArduinoBridge.settings`.
4. (Optional) Assign your card assets in `ArduinoBridge.cardDataArray`.
5. Add required service objects if missing:
   - `EventManager`
   - `MainThreadDispatcher` (auto-created at runtime by `ArduinoBridge` if absent)
6. Create runtime settings UI:
   - `Tools > REDbox > UI > Create Runtime Settings Canvas Menu`

## Runtime Controls
- Toggle menu: `Tab` (or fallback key)
- Port selection, connect/disconnect, scanner on/off, runtime tuning

## Notes
- The package avoids compile-time dependency on project-specific classes like `FPSController`.
- If your game has a custom controller to pause, assign it in `RuntimeSettingsCanvasMenu.pauseTargets`.
