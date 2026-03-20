# Changelog

## 0.4.22 - 2026-03-20
- Synced `CardScanToast` from source package, including heartbeat visual modes (Arcade, ECG spike, concentric circles) and action/status improvements.
- Added `Editor/CardScanToastEditor.cs` custom inspector with one-click visual presets (Compact Line, Oversize ECG, Hero Concentric).

## 0.4.21 - 2026-03-20
- Restored `Runtime/Driver/IRedboxReader.cs` and `ReaderSource` after root-layout migration so `ArduinoBridge` compiles in fresh UPM installs.
- Added missing Unity `.meta` files for package root docs/tools and native plugin folders/files to prevent immutable-package asset import warnings.

## 0.4.20 - 2026-03-20
- Replaced hard compile-stop API compatibility guard with an editor startup enforcer in `REDbox.Compat`.
- Added a fail-fast prompt when API Compatibility is not `.NET Framework`, including a one-click automatic fix to `.NET_Unity_4_8`.
- Kept compatibility checks isolated in the always-compiling compat assembly so fresh installs can self-recover instead of failing with compiler cascades.

## 0.4.19 - 2026-03-20
- Migrated package layout to repository root so Unity UPM base Git URL installs are supported directly.
- Fixed `REDboxApiCompatibilityRequirement.cs` preprocessor error formatting that could trigger cascading compile failures.
- Updated installation guidance for base URL and pinned milestone installs.

## 0.4.18 - 2026-03-20
- Fixed Git UPM installation guidance for monorepo layout by standardizing on `?path=/Packages/com.redbox.unity` URLs.
- Reverted root-manifest install approach that caused Unity to import nested package content as a single root package, producing compile failures in fresh projects.

## 0.4.17 - 2026-03-19
- Added RBX v0.3 `card_present` handling in `ArduinoBridge` to keep runtime state warm while cards remain on-reader.
- Updated scanner/session bookkeeping on presence heartbeats so device runtime no longer appears idle between detect and remove cycles.
- Aligned package behavior with current REDBOX_Device firmware milestone (`milestone-2026-03-19-hal-led-stable`).

## 0.4.16 - 2026-03-14
- Added in-VN device status panel showing connection state, active port, and scanner on/off status.
- Added manual device controls in the VN overlay (Connect Device, Activate Scanner, Reconnect).
- Added automatic scanner activation retry loop in the VN overlay for connected-but-idle hardware sessions.

## 0.4.15 - 2026-03-14
- Fixed CS1503 compile error in VN overlay by mapping UnityEngine.KeyCode to UnityEngine.InputSystem.Key.
- Kept developer-mode toggle compatible across both Input System and legacy input backends.

## 0.4.14 - 2026-03-14
- Fixed VN overlay runtime exception when project input handling is set to the Input System package.
- Updated sample scene open flow to normalize VN HardwareSettings even when the scene already exists.
- Ensured existing VN sample setups are switched back to live-device defaults without recreating the scene.

## 0.4.13 - 2026-03-14
- Improved VN card matching to infer taxonomy/subtype from FoundersSet-style card IDs when metadata fields are incomplete.
- Updated guided card payloads to use requirement-compatible taxonomy/subtype values with real local card IDs.
- Changed generated VN sample HardwareSettings to live-device mode by default (debugMode=false, autoActivateOnStart=true).

## 0.4.12 - 2026-03-14
- Updated VN sample card assists to prioritize real local card assets discovered in Resources/registry.
- Added visible "Valid local cards" hints for required card steps.
- Kept synthetic card fallback only when no local matching cards are available.

## 0.4.11 - 2026-03-14
- Switched VN overlay to player mode by default and moved raw simulation fields behind Developer Mode.
- Added runtime Developer Mode toggle (F3) for accessing low-level simulation tools.
- Reframed default card assist action as a story-facing prompt in player mode.

## 0.4.10 - 2026-03-14
- Reworked the VN sample story into a more dialogue-first, character-driven flow.
- Added contextual "Use Recommended Card" action to guarantee progression for required card steps.
- Improved required-card failure feedback with clearer expected taxonomy/subtype messaging.
- Made sample card simulation route directly through the VN controller for deterministic no-hardware behavior.

## 0.4.9 - 2026-03-14
- Refined the visual novel sample into a coherent mission narrative with clearer educational progression.
- Added explicit learning-goal hints on story nodes and branch choices.
- Added Tools > REDbox > Samples > Reset Visual Novel Story Data to regenerate sample story content after updates.

## 0.4.8 - 2026-03-14
- Added a one-click visual novel sample scene generator at Tools > REDbox > Samples > Create Visual Novel Sample.
- Added a no-hardware visual novel runtime slice with branching story data, card-gated progression, and scan simulation controls.
- Added sample documentation for launching and validating the visual novel flow.

## 0.1.0 - 2026-03-10
- Initial Unity package extraction from REDbox project runtime code.
- Includes serial bridge, runtime settings UI, card/event pipeline, and editor menu tooling.
- Removed hard compile-time dependency on `FPSController` to improve portability.
