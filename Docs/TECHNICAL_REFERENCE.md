# REDbox Unity Integration — Technical Reference

**Package ID:** `com.redbox.unity`  
**Version:** 0.3.7  
**Unity minimum:** 2022.3 LTS  
**Dependencies:** `com.unity.textmeshpro` ≥ 3.0.6, `com.unity.inputsystem` ≥ 1.7.0

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Assembly Definitions](#2-assembly-definitions)
3. [Runtime Layer](#3-runtime-layer)
   - 3.1 [ArduinoBridge](#31-arduinobridge)
   - 3.2 [EventManager](#32-eventmanager)
   - 3.3 [MainThreadDispatcher](#33-mainthreaddispatcher)
   - 3.4 [Card System](#34-card-system)
   - 3.5 [Settings ScriptableObjects](#35-settings-scriptableobjects)
   - 3.6 [UI Components](#36-ui-components)
4. [Editor Layer](#4-editor-layer)
   - 4.1 [CardDatabaseEditor](#41-carddatabaseeditor)
   - 4.2 [ArduinoBridgeEditor](#42-arduinobridgeeditor)
   - 4.3 [REDboxWelcomeScene](#43-redboxwelcomescene)
   - 4.4 [RuntimeSettingsCanvasMenuEditor](#44-runtimesettingscanvasmenueditor)
5. [Serial Protocol V1](#5-serial-protocol-v1)
6. [Card ID Normalisation](#6-card-id-normalisation)
7. [Thread Safety Model](#7-thread-safety-model)
8. [Event Flow Reference](#8-event-flow-reference)
9. [Compat / Deprecated Layer](#9-compat--deprecated-layer)

---

## 1. Architecture Overview

```
┌──────────────────────────────────────────────────────────────────┐
│  Arduino / NFC Hardware                                          │
│  PN532 reader ─► Arduino sketch ─► USB/BT serial port           │
└──────────────────────────────────┬───────────────────────────────┘
                                   │ Serial bytes (9600 baud)
┌──────────────────────────────────▼───────────────────────────────┐
│  ArduinoBridge  (Runtime/Driver)                                 │
│                                                                  │
│  • Opens & owns the SerialPort                                   │
│  • Background Task reads raw lines                               │
│  • MainThreadDispatcher marshals callbacks to Main Thread        │
│  • Parses V1 protocol; resolves Card assets from registry        │
│  • Fires static C# events (OnConnectionStateChanged, etc.)       │
└──────────┬────────────────────────┬─────────────────────────────-┘
           │ static events          │ calls methods
┌──────────▼──────────┐   ┌─────────▼──────────────────────────────┐
│  ConnectionStatus   │   │  EventManager  (singleton)             │
│  Indicator          │   │                                        │
│  DebugOverlay       │   │  UnityEvent<Card>    OnCardPresented   │
│  REDboxOnboarding   │   │  UnityEvent<Card>    OnCardRemoved     │
│  Wizard             │   │  UnityEvent<Card,bool> OnCardScanned   │
└─────────────────────┘   │  UnityEvent<bool>    OnScannerMissing  │
                          └────────────────────────────────────────┘
                                        │
                          ┌─────────────▼──────────────────────────┐
                          │  Game code / UIDisplayManager /        │
                          │  CardScanToast / custom components     │
                          └────────────────────────────────────────┘
```

**Data flow summary:**

1. `ArduinoBridge` opens the serial port on a background `Task`.
2. Each raw line is dispatched to the Main Thread via `MainThreadDispatcher`.
3. If the line resolves to a known card, `ArduinoBridge` invokes the static `OnCardPresented`/`OnCardRemoved` events **and** calls `EventManager.CardPresented()` / `CardRemoved()`.
4. `EventManager` fires its `UnityEvent` callbacks (inspector-wired or code-subscribed).
5. Built-in UI components (`UIDisplayManager`, `CardScanToast`, etc.) and game code respond.

---

## 2. Assembly Definitions

| Assembly | Path | Use |
|---|---|---|
| `REDbox.Runtime` | `Runtime/REDbox.Runtime.asmdef` | All runtime scripts; references `Unity.TextMeshPro`, `Unity.InputSystem` |
| `REDbox.Editor` | `Editor/REDbox.Editor.asmdef` | Editor-only tools; references `REDbox.Runtime` + Unity Editor assemblies |
| `REDbox.Compat` | `Runtime/Compat/REDbox.Compat.asmdef` | Archive; holds deprecated `SerialHandler` |

---

## 3. Runtime Layer

### 3.1 ArduinoBridge

**File:** `Runtime/Driver/ArduinoBridge.cs`  
**Base class:** `MonoBehaviour` (singleton via `DontDestroyOnLoad`)

The central hub. Manages the serial connection lifecycle and produces all card events.

#### Inspector Fields

| Field | Type | Default | Description |
|---|---|---|---|
| `settings` | `HardwareSettings` | — | **Required.** Hardware configuration asset. |
| `cardDataArray` | `Card[]` | — | All Card ScriptableObjects to register. Populated automatically from the Welcome Scene; otherwise drag-assign in the Inspector. |

#### Static Events

```csharp
public static event Action<ConnectionState>  OnConnectionStateChanged;
public static event Action<string>           OnRawDataReceived;
public static event Action<bool>             OnDeviceReadyChanged;
public static event Action<Card>             OnCardPresented;
public static event Action<Card>             OnCardRemoved;
```

All static events are dispatched on the **Main Thread**.

| Event | Fires when |
|---|---|
| `OnConnectionStateChanged` | State machine transitions (Disconnected → Connecting → Connected → Reconnecting) |
| `OnRawDataReceived` | Every raw line from the serial port (debug use) |
| `OnDeviceReadyChanged` | `true` when `Connected` + firmware reports `STATE=READY`; `false` on disconnect or shutdown |
| `OnCardPresented` | A card is placed / tapped (EV=ENTER or EV=TAP in V1 protocol) |
| `OnCardRemoved` | A card is removed (EV=EXIT in V1 protocol) |

#### Public Properties (read-only)

| Property | Type | Description |
|---|---|---|
| `Instance` | `ArduinoBridge` | Singleton accessor |
| `State` | `ConnectionState` | Current connection state enum |
| `IsDeviceReady` | `bool` | `true` when `Connected` and `SysState == "READY"` |
| `ScannerEnabled` | `bool` | `true` after firmware ACKs `SCANNER_ON` |
| `ScannerRequested` | `bool` | `true` after `ActivateScanner()` until ACK |
| `PendingScannerEnable` | `bool` | `true` when waiting for scanner ACK timeout |
| `ActivePort` | `string` | Current open port name, or `"—"` |
| `ConfiguredPort` | `string` | Runtime port override if set, else HardwareSettings.serialPort |
| `LastScannedCardId` | `string` | Raw NFC ID of last scan |
| `LastRawData` | `string` | Last raw serial line |
| `LastScanTime` | `DateTime` | UTC timestamp of last scan |
| `CardRegistryCount` | `int` | Number of cards in the internal lookup dictionary |
| `DeviceFirmwareVersion` | `string` | Firmware version string from the device |
| `LastErrorCode` | `int` | Last error code received from firmware |
| `LastErrorMessage` | `string` | Last error message string |
| `LastI2cReport` | `string` | Last I2C diagnostic line |
| `LastTagUid` | `string` | Raw UID of last NFC tag |
| `LastCardSource` | `string` | Source field from last V1 message |

#### `ConnectionState` Enum

```csharp
public enum ConnectionState { Disconnected, Connecting, Connected, Reconnecting }
```

#### Public Methods

```csharp
// Start NFC scanning. Sends 0x01 to the firmware. Returns immediately;
// waits for SCANNER_ON ACK (timeout: HardwareSettings.scannerEnableTimeout).
public void ActivateScanner();

// Stop NFC scanning. Sends 0xFF 0x00 to the firmware.
public void DeactivateScanner();

// Fire a scan event for a card by ID — no hardware required.
// Useful for testing. If cardId is not in the registry, fires with null card.
public void SimulateScan(string cardId);

// Override the serial port at runtime (persisted in PlayerPrefs).
// Pass null or empty string to clear the override.
public void SetRuntimePortOverride(string port);

// Retrieve all currently available serial ports on this machine.
public static string[] GetAvailablePorts();

// Re-read cardDataArray and rebuild the internal dictionary.
public void RebuildCardRegistry();
```

#### Lifecycle

- **`Awake`**: Builds card registry; ensures `MainThreadDispatcher` exists.  
- **`Start`**: Validates `settings`; starts `ConnectionLoop` coroutine; subscribes `AutoActivateOnReady` if `settings.autoActivateOnStart` is true.  
- **`OnDestroy` / `OnApplicationQuit`**: Calls `Shutdown()` — sends `0xFF 0x00` to deactivate scanner, cancels background task, closes port.

#### Connection Loop

`ConnectionLoop()` is a coroutine that:
1. Throttles attempts using `reconnectThrottleMs` (anti-burst for USB micro-disconnects).
2. Calls `ResolvePort()` — respects runtime override, then `HardwareSettings.serialPort`, then auto-detect keywords.
3. On success, starts a background `Task` via `ReadLoopAsync()` that reads lines indefinitely.
4. On failure, waits `reconnectDelay` seconds then retries (exponential back-off up to `reconnectMaxDelay`).
5. Stops after `maxReconnectAttempts` consecutive failures (0 = infinite).

#### Card ID Look-up

All IDs are normalised before registry insertion and before look-up. See [Section 6](#6-card-id-normalisation).

---

### 3.2 EventManager

**File:** `Runtime/EventManager.cs`  
**Base class:** `MonoBehaviour` (singleton via `DontDestroyOnLoad`)

Inspector-friendly event bus. Exposes `UnityEvent` fields so game components can subscribe without code in the Inspector.

#### UnityEvents

```csharp
public UnityEvent<Card>       OnCardPresented;   // card placed on reader
public UnityEvent<Card>       OnCardRemoved;     // card removed from reader
public UnityEvent<Card, bool> OnCardScanned;     // legacy: bool = true→present, false→removed
public UnityEvent<bool>       OnScannerMissing;  // true when no port found
```

`OnCardPresented` and `OnCardRemoved` both also fire `OnCardScanned` for backward compatibility.

#### Public Methods

```csharp
public void CardPresented(Card cardData);     // fires OnCardPresented + OnCardScanned(card, true)
public void CardRemoved(Card cardData);       // fires OnCardRemoved  + OnCardScanned(card, false)
public void CardScanned(Card cardData, bool status);  // fires OnCardScanned only (legacy)
public void ScannerMissing(bool status);     // fires OnScannerMissing
```

---

### 3.3 MainThreadDispatcher

**File:** `Runtime/MainThreadDispatcher.cs`  
**Base class:** `MonoBehaviour` (singleton via `DontDestroyOnLoad`)

Thread-safe queue that bridges the serial reader background thread to the Unity Main Thread.

```csharp
// Thread-safe — call from any thread.
MainThreadDispatcher.Instance.Enqueue(() => { /* Main Thread code */ });
```

`ArduinoBridge` auto-creates a `MainThreadDispatcher` if none exists in the scene. You should not need to manage it manually.

---

### 3.4 Card System

#### `Card` (abstract)

**File:** `Runtime/Card/Card.cs`  
**Base class:** `ScriptableObject`

```csharp
public abstract class Card : ScriptableObject
{
    public string cardId;       // NFC UID string — must be unique across all cards
    public string cardName;
    public string cardType;
    public string description;
    public int    hp;
    public int    mp;
    public int    at;

    public abstract void Activate();
}
```

Create card assets via **right-click → Create → RK/…** or via the Card Database editor window.

#### `ICardBehavior`

**File:** `Runtime/Card/ICardBehavior.cs`

```csharp
public interface ICardBehavior { void Activate(); }
```

Implemented by all concrete card types. Also fulfilled by the abstract `Card.Activate()`.

#### `CharacterCard`

**File:** `Runtime/Card/Type/CharacterCard.cs`  
**Create menu:** `Create → RK/Character`

| Field | Type | Default | Description |
|---|---|---|---|
| `characterPrefab` | `GameObject` | null | Prefab spawned in front of the player on `Activate()` |
| `spawnOffset` | `Vector3` | `(0,0,2)` | Local-space offset from player/camera |
| `autoDestroyAfter` | `float` | 0 | Auto-despawn delay (0 = permanent) |
| `summonVFX` | `GameObject` | null | Particle prefab instantiated at spawn point |
| `summonSound` | `AudioClip` | null | Audio played on spawn |

`Activate()` resolves a spawn origin tagged `"Player"` in the scene; falls back to world origin if none is found.

#### `PowerCard`

**File:** `Runtime/Card/Type/PowerCard.cs`  
**Create menu:** `Create → RK/Power`

| Field | Type | Default | Description |
|---|---|---|---|
| `powerType` | `PowerType` enum | `Heal` | `Heal`, `Shield`, `Attack`, `SpeedBoost`, `Freeze`, `Reveal` |
| `powerValue` | `int` | 30 | Effect magnitude |
| `effectDuration` | `float` | 5 | Duration in seconds (0 = instant) |
| `targetTag` | `string` | `"Player"` | Scene object tag to apply the effect to |
| `effectVFX` | `GameObject` | null | VFX prefab on target |
| `activationSound` | `AudioClip` | null | |
| `screenFlashColor` | `Color` | yellow | Screen flash on activation |

#### `ToolCard`

**File:** `Runtime/Card/Type/ToolCard.cs`  
**Create menu:** `Create → RK/Tool`

| Field | Type | Default | Description |
|---|---|---|---|
| `toolPrefab` | `GameObject` | null | Physical tool object spawned in scene |
| `spawnOffset` | `Vector3` | `(0,0.5,2)` | Spawn offset relative to player |
| `targetTag` | `string` | `""` | Optional scene target |
| `activationVFX` | `GameObject` | null | |
| `activationSound` | `AudioClip` | null | |

---

### 3.5 Settings ScriptableObjects

#### `HardwareSettings`

**File:** `Runtime/Settings/HardwareSettings.cs`  
**Create menu:** `Create → RK/Settings/Hardware Settings`

| Field | Type | Default | Description |
|---|---|---|---|
| `serialPort` | `string` | `"COM3"` | Port when `autoDetectPort` is off |
| `baudRate` | `int` | 9600 | Must match sketch baud rate |
| `reconnectDelay` | `float` | 3 | Seconds between reconnect attempts |
| `reconnectMaxDelay` | `float` | 10 | Exponential back-off ceiling |
| `reconnectThrottleMs` | `int` | 500 | Minimum ms between attempts (anti-burst) |
| `maxReconnectAttempts` | `int` | 0 | 0 = infinite |
| `scannerEnableTimeout` | `float` | 3 | Seconds to wait for `SCANNER_ON` ACK |
| `autoDetectPort` | `bool` | false | Scan all ports for matching keywords |
| `autoDetectKeywords` | `string[]` | `["cu.usbserial", ...]` | Keyword priority list for auto-detect |
| `debugMode` | `bool` | false | Disables serial; enables `SimulateScan()` |
| `autoActivateOnStart` | `bool` | false | Calls `ActivateScanner()` when device reports `READY` |
| `autoDeactivateOnStop` | `bool` | false | Calls `DeactivateScanner()` on `OnDestroy` / `OnApplicationQuit` |
| `bluetoothMode` | `bool` | false | Serial over BT (set port to BT serial port) |
| `webServiceUrl` | `string` | `"https://api.redk.ch"` | Base URL for card data API |
| `networkTimeout` | `int` | 10 | API request timeout in seconds |

#### `GameConfig`

**File:** `Runtime/Settings/GameConfig.cs`  
**Create menu:** `Create → RK/Settings/Game Config`

Centralises gameplay tuning values. Used by `UIDisplayManager`, `DebugOverlay`, and optionally by game code directly.

| Field | Type | Default | Description |
|---|---|---|---|
| `walkSpeed` | `float` | 5 | Player walk speed m/s |
| `sprintSpeed` | `float` | 10 | Sprint speed m/s |
| `jumpHeight` | `float` | 2 | Jump height m |
| `mouseSensitivity` | `float` | 100 | |
| `boundaryNearDistance` | `float` | 2 | Slow-down zone near boundaries |
| `roverMoveSpeed` | `float` | 5 | Rover movement speed |
| `roverTurnSpeed` | `float` | 90 | Rover rotation speed °/s |
| `roverStepLength` | `float` | 1 | Rover step distance |
| `cardDisplayDuration` | `float` | 5 | Seconds UIDisplayManager shows card info |
| `connectedColor` | `Color` | green | DebugOverlay connected indicator |
| `disconnectedColor` | `Color` | red | DebugOverlay disconnected indicator |
| `reconnectingColor` | `Color` | amber | |
| `debugOverlayKey` | `KeyCode` | F1 | Toggle for DebugOverlay |
| `showDebugOverlayOnStart` | `bool` | false | |

---

### 3.6 UI Components

#### `ConnectionStatusIndicator`

**File:** `Runtime/UI/ConnectionStatusIndicator.cs`  
**Add component:** `REDbox/Connection Status Indicator`

Lightweight LED + label widget showing the current connection state. Requires no canvas by default; automatically switches to **uGUI mode** if `ledImage` is assigned.

| Inspector Field | Type | Default | Description |
|---|---|---|---|
| `ledImage` | `Image` | null | Assign to use uGUI mode instead of OnGUI |
| `labelText` | `TMP_Text` / `Text` | null | Label in uGUI mode |
| `anchor` | `Corner` enum | `BottomLeft` | OnGUI screen corner |
| `fontSize` | `float` | 12 | OnGUI font size |
| `margin` | `Vector2` | `(12,12)` | OnGUI edge margin in pixels |

**LED colours:**

| State | Colour |
|---|---|
| Disconnected | Grey |
| Connecting / Reconnecting | Amber |
| Connected | Green |
| Scanning (scanner active) | Green |

Subscribes to `ArduinoBridge.OnConnectionStateChanged` and `OnDeviceReadyChanged` in `Start()`.

---

#### `UIDisplayManager`

**File:** `Runtime/UI/UIDisplayManager.cs`  
**Singleton.** Displays card info and status messages via TMP text fields wired in the Inspector.

| Inspector Field | Description |
|---|---|
| `nameText` | TMP label — card name |
| `descriptionText` | TMP label — card description/lore |
| `cardIdText` | TMP label — raw NFC ID |
| `statusText` | TMP label — connection status messages |
| `gameConfig` | Optional; reads `cardDisplayDuration` |
| `cardDisplayDuration` | Fallback if GameConfig not assigned |

**Public API:**
```csharp
public void ShowCard(Card card);
public void ShowStatus(string message);
public void ShowTemporaryStatus(string message, float duration);
public void ClearAll();
```

Subscribes to `EventManager.OnCardScanned` and `OnScannerMissing`.

---

#### `CardScanToast`

**File:** `Runtime/UI/CardScanToast.cs`  
**Add component:** manual (auto-added by Welcome Scene builder)

No-Canvas notification overlay. Displays card name, type, stats and a brief animated "NEW" flash on each scan. Shows connection/scanner status banners.

| Inspector Field | Type | Default | Description |
|---|---|---|---|
| `displayDuration` | `float` | 6 | Seconds the panel remains visible |
| `anchor` | `ToastAnchor` enum | `BottomCenter` | On-screen position |

Subscribes to `EventManager.OnCardScanned` and `OnScannerMissing`; also listens to `ArduinoBridge.OnConnectionStateChanged` for status banners.

---

#### `DebugOverlay`

**File:** `Runtime/UI/DebugOverlay.cs`  
Toggle key: **F1** (configurable via `GameConfig.debugOverlayKey`)

Displays a real-time panel showing:
- Connection state with colour coding
- Active port and baud rate
- Card registry count
- Last scanned card + timestamp
- Raw serial log (last N lines, configurable)
- **Tab** toggles between "Info" and "Simulation" panels
- Simulation panel accepts a card ID and fires `ArduinoBridge.SimulateScan()`

---

#### `RuntimeSettingsMenu`

**File:** `Runtime/UI/RuntimeSettingsMenu.cs`  
Toggle key: **Tab** (or **F2** as fallback)

IMGUI in-game menu that allows:
- Port selection from a live list of available serial ports
- Connect / Disconnect
- Activate / Deactivate scanner
- Baud rate, reconnect delay, scanner timeout override

Disabled and replaced by `RuntimeSettingsCanvasMenu` when both are present in the scene. Auto-spawned by `RuntimeSettingsBootstrap` if neither menu type is found.

---

#### `RuntimeSettingsBootstrap`

**File:** `Runtime/UI/RuntimeSettingsBootstrap.cs`

Static runtime initialiser (`RuntimeInitializeOnLoadMethod`). After every scene load, ensures at least one settings menu is present:
1. If a `RuntimeSettingsCanvasMenu` already exists — no-op.
2. If a `RuntimeSettingsMenu` already exists — no-op.
3. Otherwise, creates a `[RuntimeSettingsMenu AutoBootstrap]` GameObject with `RuntimeSettingsMenu` in IMGUI-only mode.

---

#### `REDboxOnboardingWizard`

**File:** `Runtime/Sample/REDboxOnboardingWizard.cs`  
**Add component:** `REDbox/Onboarding Wizard`  
Toggle: **Escape** key

Four-screen onboarding overlay:

| Screen | Content |
|---|---|
| 0 — Welcome | Plugin overview |
| 1 — Hardware | Wiring guide (Arduino + PN532) |
| 2 — Configuration | HardwareSettings fields walkthrough |
| 3 — Live Monitor | Real-time connection state, last scan, raw serial log |

Auto-activates the scanner via `TickAutoScanner()` every 2 seconds until the firmware confirms `SCANNER_ON`. Requires no Canvas.

---

## 4. Editor Layer

### 4.1 CardDatabaseEditor

**File:** `Editor/CardDatabaseEditor.cs`  
**Menu:** `Tools → REDbox → Card Database`  
**Min size:** 700 × 480 px

IMGUI EditorWindow for browsing and editing all `Card` ScriptableObjects in the project.

**Left panel:**
- Search bar filters by `cardName`, `cardId`, and `cardType`.
- 52 px rows showing name (bold) + type · id.
- Auto-refreshes every 4 seconds (`EditorApplication.timeSinceStartup` delta).
- Click a row to select; saves dirty state of previous selection first.

**Right panel (detail view):**
- `SerializedObject`-backed field editing (Undo/Redo supported).
- Identity section: `cardId`, `cardName`, `cardType`.
- Description text area.
- HP / MP / AT stat row in a horizontal layout.
- `CharacterCard`-only section: `characterPrefab`, `spawnOffset`, `summonVFX`, `summonSound`.
- **Select in Project** — pings the asset in the Project window.
- **Delete** — destroys the asset via `AssetDatabase.DeleteAsset()`.

**Toolbar:**
- `+ Character` — creates a new `CharacterCard` in `Assets/REDbox/`.
- `↺ Refresh` — rebuilds the asset list immediately.

---

### 4.2 ArduinoBridgeEditor

**File:** `Editor/ArduinoBridgeEditor.cs`  
**Decorator:** `[CustomEditor(typeof(ArduinoBridge))]`

Extends the default `ArduinoBridge` inspector with:
- Colour-coded connection state banner (green/red/amber/orange).
- **Scan Simulator panel** (Play Mode only): text field + one button per registered card.
- **Registry panel**: shows all registered card IDs.
- **Commands panel**: `ActivateScanner`, `DeactivateScanner`, `Reconnect` buttons.

---

### 4.3 REDboxWelcomeScene

**File:** `Editor/Sample/REDboxWelcomeScene.cs`  
**Menu:** `Tools → REDbox → Welcome Scene` (priority −10)

Creates or opens `Assets/REDbox/REDbox_Welcome.unity`. On first run, the builder:

1. Creates `Assets/REDbox/` folder if absent.
2. Generates `REDbox_HardwareSettings.asset` (`autoDetectPort = true`, `baudRate = 9600`).
3. Generates `Demo_Alpha.asset` and `Demo_Beta.asset` (`CharacterCard`).
4. Builds a new empty scene with:
   - `Main Camera` (dark background, tagged `MainCamera`)
   - `EventManager`
   - `MainThreadDispatcher`
   - `ArduinoBridge` (wired with HardwareSettings + both demo cards)
   - `REDboxOnboardingWizard`
   - `CardScanToast`
5. Saves and opens the scene.

On subsequent calls it simply opens the existing scene.

---

### 4.4 RuntimeSettingsCanvasMenuEditor

**File:** `Editor/RuntimeSettingsCanvasMenuEditor.cs`

Programmatic Canvas builder. Creates a full Canvas hierarchy for `RuntimeSettingsCanvasMenu` in the active scene. Called via `Tools → REDbox → UI → Create Runtime Settings Canvas Menu` (menu item currently removed from the toolbar; call `CreateCanvasMenu()` directly if needed).

---

## 5. Serial Protocol V1

The plugin implements the REDbox V1 protocol specified in `Docs/REDBOX_SERIAL_PROTOCOL_V1.md`. Two message formats are recognised:

### Format A — Full tagged frame

```
prefix:cardId:payload
```

Example: `nfc:04A31F2B:data`

### Format B — Short ID frame

```
d:cardId
```

Example: `d:04A31F2B`

### V1 Key-Value Frame

The primary format used by the firmware is a space-separated key=value line:

```
NFC EV=ENTER ID=04A31F2B SRC=PN532 VER=1.2.0
```

Recognised keys:

| Key | Values | Description |
|---|---|---|
| `EV` | `ENTER`, `TAP`, `PRESENT`, `EXIT` | Card lifecycle event |
| `ID` | hex string | NFC tag UID |
| `SRC` | string | Source hardware (e.g. `PN532`) |
| `VER` | semver string | Firmware version |
| `STATE` | `READY`, `IDLE`, `ERROR` | Device state |
| `SCANNER` | `ON`, `OFF` | Scanner enable/disable ACK |
| `ERR` | int | Error code |
| `MSG` | string | Error message |
| `I2C` | string | I2C diagnostic string |

**Event mapping:**

| EV value | Action |
|---|---|
| `ENTER` | `HandleCardScan()` → `OnCardPresented` + `EventManager.CardPresented()` |
| `TAP` | Same as `ENTER` |
| `PRESENT` | Heartbeat — ignored (no event fired) |
| `EXIT` | `OnCardRemoved` + `EventManager.CardRemoved()` (resolves card from registry by last `_lastTagUid`) |

**Scanner handshake:**

| Message | Direction | Meaning |
|---|---|---|
| `0x01` (byte) | Unity → Arduino | Activate scanner |
| `0xFF 0x00` (bytes) | Unity → Arduino | Deactivate scanner |
| `SCANNER=ON` | Arduino → Unity | Activation confirmed |
| `SCANNER=OFF` | Arduino → Unity | Deactivation confirmed |

---

## 6. Card ID Normalisation

`ArduinoBridge` normalises all card IDs before insertion into the registry and before look-up, ensuring robust matching regardless of formatting differences between the NFC firmware and the Unity assets.

Normalisation steps (applied in order):
1. Trim whitespace.
2. Remove `0x` / `0X` prefixes from each byte group.
3. Remove separator characters: spaces, hyphens, colons.
4. Convert to uppercase.

Examples:
```
"04 A3 1F 2B"  →  "04A31F2B"
"04-a3-1f-2b"  →  "04A31F2B"
"0x04:0xA3"    →  "04A3"
"DEMO_ALPHA"   →  "DEMO_ALPHA"
```

The look-up uses `StringComparer.OrdinalIgnoreCase` for the dictionary, so card IDs in Card assets are case-insensitive.

---

## 7. Thread Safety Model

Serial data is read inside a `Task.Run()` background thread. Unity's API is **not** thread-safe, so no Unity calls can be made from that thread.

The safety model:
1. The background thread calls `MainThreadDispatcher.Instance.Enqueue(action)`.
2. `MainThreadDispatcher.Update()` drains the queue each frame on the Main Thread.
3. All `ArduinoBridge` static events are fired from within enqueued actions, so handlers always run on the Main Thread.

> **Important:** Never subscribe to `ArduinoBridge` static events from a background thread. All subscriptions should be made in `Start()`, `Awake()`, or `OnEnable()`.

---

## 8. Event Flow Reference

### Card presentation (hardware path)

```
Serial bytes received (background thread)
  └─► MainThreadDispatcher.Enqueue(ParseAndDispatch)
        └─► Main Thread: ParseLine()
              └─► HandleV1Card() or HandleFormatA/B()
                    └─► HandleCardScan(card)
                          ├─► ArduinoBridge.OnCardPresented?.Invoke(card)
                          └─► EventManager.Instance.CardPresented(card)
                                ├─► EventManager.OnCardPresented.Invoke(card)
                                └─► EventManager.OnCardScanned.Invoke(card, true)   [legacy]
```

### Card removal (hardware path)

```
Serial "NFC EV=EXIT …" received (background thread)
  └─► MainThreadDispatcher.Enqueue(...)
        └─► Main Thread: HandleV1Card() EV=EXIT branch
              └─► Resolve card from registry by last UID
                    ├─► ArduinoBridge.OnCardRemoved?.Invoke(card)
                    └─► EventManager.Instance.CardRemoved(card)
                          ├─► EventManager.OnCardRemoved.Invoke(card)
                          └─► EventManager.OnCardScanned.Invoke(card, false)   [legacy]
```

### SimulateScan (debug path)

```csharp
ArduinoBridge.Instance.SimulateScan("DEMO_ALPHA");
  └─► Look up card in registry
        └─► HandleCardScan(card)   ← same path as hardware
```

---

## 9. Compat / Deprecated Layer

**File:** `Runtime/Compat/REDboxApiCompatibilityRequirement.cs`  
**Assembly:** `REDbox.Compat`

Contains `REDboxApiCompatibilityRequirement` which enforces minimum Unity API compatibility level via `[assembly: UnityAPICompatibilityVersion]`.

**File:** `Runtime/Serial/SerialHandler.cs`  
**Status:** `[Obsolete]` — kept for reference only, do not instantiate.

The constructor immediately throws a `Debug.LogError`. All functionality has been superseded by `ArduinoBridge`.

---

*End of Technical Reference — v0.3.7 — 2026-03-11*
