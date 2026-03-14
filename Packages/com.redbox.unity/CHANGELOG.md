# Changelog

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
