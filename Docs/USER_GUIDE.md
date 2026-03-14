# REDbox Unity Integration — User Guide

**Package:** `com.redbox.unity` v0.4.9  
**Supports:** Unity 2022.3 LTS and above

---

## Table of Contents

1. [What is REDbox Unity Integration?](#1-what-is-redbox-unity-integration)
2. [Requirements](#2-requirements)
3. [Installation](#3-installation)
4. [Quick Start — Welcome Scene](#4-quick-start--welcome-scene)
5. [Quick Start — Visual Novel Sample](#5-quick-start--visual-novel-sample)
6. [Hardware Setup](#6-hardware-setup)
7. [Configuring the Hardware Settings](#7-configuring-the-hardware-settings)
8. [Creating and Managing Cards](#8-creating-and-managing-cards)
9. [Registering Cards with ArduinoBridge](#9-registering-cards-with-arduinobridge)
10. [Responding to Card Events in Your Game](#10-responding-to-card-events-in-your-game)
11. [Connection Status Indicator](#11-connection-status-indicator)
12. [Runtime Settings Menu](#12-runtime-settings-menu)
13. [Debug Tools](#13-debug-tools)
14. [Testing Without Hardware](#14-testing-without-hardware)
15. [Troubleshooting](#15-troubleshooting)

---

## 1. What is REDbox Unity Integration?

The **REDbox Unity Integration** package connects the REDbox physical card scanner (Arduino + PN532 NFC reader) to your Unity game. When a player places an NFC card on the scanner, the plugin:

- Identifies the card against your card database.
- Fires Unity events your game code can listen to.
- Optionally shows on-screen feedback automatically.

The plugin handles the serial communication, automatic reconnection, port detection, and thread safety automatically. You focus on game logic.

---

## 2. Requirements

| Requirement | Minimum |
|---|---|
| Unity | 2022.3 LTS |
| TextMesh Pro | 3.0.6 (Unity's own package) |
| Input System | 1.7.0 (Unity's own package) |
| Operating system | Windows, macOS, or Linux |
| Hardware | REDbox scanner (Arduino + PN532), connected via USB or Bluetooth |

---

## 3. Installation

### Via Unity Package Manager — Git URL

1. Open **Window → Package Manager**.
2. Click the **+** button in the top-left corner.
3. Choose **Add package from git URL…**
4. Enter:
   ```
   https://github.com/jmgech/RedBox_UPMPlugin.git?path=Packages/com.redbox.unity
   ```
5. Click **Add**. Unity will import the package.

### Via local disk (for contributors)

1. Clone the repository.
2. In **Package Manager**, choose **Add package from disk…** and point to `Packages/com.redbox.unity/package.json`.

---

## 4. Quick Start — Welcome Scene

The fastest way to get started is the built-in Welcome Scene.

1. In Unity, open the menu: **Tools → REDbox → Welcome Scene**.
2. Unity creates the scene at `Assets/REDbox/REDbox_Welcome.unity` and opens it.
3. Press **Play**.

The scene contains a four-screen onboarding wizard that walks you through hardware wiring, configuration, and offers a live monitor showing real-time scan events.

> The Welcome Scene also creates two demo card assets (`Demo_Alpha` and `Demo_Beta`) and a pre-configured `HardwareSettings` asset with auto-port-detection enabled.

---

## 5. Quick Start — Visual Novel Sample

The package includes a Japanese-style visual novel sample that teaches REDbox scan flow and card taxonomy.

1. Open **Tools → REDbox → Samples → Create Visual Novel Sample**.
2. Unity creates and opens: `Assets/REDbox/VNSample/REDbox_VisualNovel_Sample.unity`.
3. Press **Play**.

By default the scene starts in no-hardware mode (`HardwareSettings.debugMode = true`).
Use the on-screen simulation buttons to trigger sample cards:
- Lore/Memory
- World/Location
- Actor/Ally
- Instruction (Attack/Effect)

The sample includes:
- card-gated progression nodes,
- branching routes with card subtype checks,
- fallback manual branch for non-hardware workflows.

---

## 6. Hardware Setup

### Wiring (USB)

1. Connect the REDbox scanner to your computer via USB.
2. Open **Device Manager** (Windows) or run `ls /dev/tty.*` (macOS/Linux) to find the port name.
3. Note the port for the next step.

### Wiring (Bluetooth)

1. Pair the REDbox Bluetooth module with your computer.
2. Find the resulting serial port name (on macOS: `/dev/cu.xxx`; on Windows: a COM port).
3. Enable `bluetoothMode` in HardwareSettings and set the port manually.

### Firmware Baud Rate

The plugin defaults to **9600 baud**. This must match the `Serial.begin()` call in your Arduino sketch. Change the `baudRate` field in `HardwareSettings` if you use a different speed.

---

## 7. Configuring the Hardware Settings

`HardwareSettings` is a ScriptableObject that centralises all hardware configuration. One asset is shared across your scenes.

### Creating the Asset

Right-click in the **Project** window → **Create → RK/Settings → Hardware Settings**.

### Key Settings

| Setting | What it does |
|---|---|
| **Serial Port** | The port to connect to (e.g. `COM3`, `/dev/tty.usbserial-1234`). Ignored when Auto Detect Port is on. |
| **Baud Rate** | Must match the Arduino sketch. Default: 9600. |
| **Auto Detect Port** | Scans all available ports and picks the first one whose name matches any keyword in the list. Recommended for most setups. |
| **Auto Detect Keywords** | Priority-ordered list of port name fragments. Defaults cover common Arduino/CH340/CP210 chips on all platforms. |
| **Debug Mode** | Disables all serial communication. Use for testing card logic without hardware. |
| **Auto Activate On Start** | Automatically enables NFC scanning as soon as the device reports "READY". Turn this on if scanning should always be active. |
| **Auto Deactivate On Stop** | Shuts down the scanner cleanly when you stop Play Mode. Prevents the REDbox hardware from staying in active state between sessions. |
| **Reconnect Delay** | Seconds to wait between reconnection attempts. |
| **Max Reconnect Attempts** | 0 = retry indefinitely. Set to e.g. 5 to give up after five failures. |

### Assigning the Asset to ArduinoBridge

Drag the `HardwareSettings` asset into the **Settings** field on the `ArduinoBridge` component in your scene.

---

## 8. Creating and Managing Cards

Each physical NFC card maps to a **Card** ScriptableObject in your Unity project. There are three built-in card types:

| Type | Create via | Best used for |
|---|---|---|
| **CharacterCard** | `Create → RK/Character` | Spawning a character or creature |
| **PowerCard** | `Create → RK/Power` | Applying game effects (heal, attack, buff) |
| **ToolCard** | `Create → RK/Tool` | Triggering tools or interactable objects |

### Using the Card Database Editor

Open **Tools → REDbox → Card Database** to manage all cards in one window.

- **Left panel** — Browse all cards. Use the search box to filter by name, ID, or type.
- **Right panel** — Edit the selected card's fields. Changes are saved automatically when you click another card or close the window.
- **+ Character** button (toolbar) — Creates a new CharacterCard asset in `Assets/REDbox/`.
- **↺ Refresh** button — Rescans the project for card assets.

### Card Fields

Every card has these base fields:

| Field | Description |
|---|---|
| **Card ID** | The NFC UID string that the scanner will transmit. This must match the UID printed on or programmed onto your physical card. The plugin is case-insensitive and ignores separators (spaces, dashes, colons). |
| **Card Name** | Display name shown in the game UI. |
| **Card Type** | Free-form type label (e.g. "Character", "Boss", "Rare"). |
| **Description** | Lore or gameplay text. |
| **HP / MP / AT** | Base stats. Your game code can read these from the `Card` object. |

### Getting the NFC UID from a Card

Physical NFC cards print their UID on the back, or you can read it using:
- The **DebugOverlay** (press **F1** in Play Mode) — shows `LastTagUid` in real time.
- The `ArduinoBridge.LastTagUid` property from code.
- The REDbox onboarding wizard **Live Monitor** screen (screen 4).

---

## 9. Registering Cards with ArduinoBridge

`ArduinoBridge` must know about your card assets to dispatch the right `Card` object when a scan happens.

### In the Inspector

1. Select the **ArduinoBridge** GameObject in your scene.
2. In the **Cartes NFC enregistrées** section, set the **Size** of `cardDataArray` to the number of cards you have.
3. Drag each Card ScriptableObject into the array slots.

### Programmatically

```csharp
// After modifying the cardDataArray at runtime:
ArduinoBridge.Instance.RebuildCardRegistry();
```

> If a scan returns an unknown ID, card events fire with `null` as the card argument. Your listeners should guard against this.

---

## 10. Responding to Card Events in Your Game

There are two ways to listen to card events — pick whichever fits your architecture.

### Option A — Inspector (UnityEvent)

1. Select the **EventManager** GameObject (in your scene or the Welcome Scene).
2. In the Inspector, expand `OnCardPresented`.
3. Click **+**, drag in your GameObject, and choose the method to call.

This is the simplest approach for designers and small projects.

### Option B — C# Event subscription

Subscribe to static events on `ArduinoBridge` for low-overhead, code-only listeners:

```csharp
using UnityEngine;

public class MyCardHandler : MonoBehaviour
{
    private void OnEnable()
    {
        ArduinoBridge.OnCardPresented += HandleCardPresented;
        ArduinoBridge.OnCardRemoved  += HandleCardRemoved;
    }

    private void OnDisable()
    {
        ArduinoBridge.OnCardPresented -= HandleCardPresented;
        ArduinoBridge.OnCardRemoved  -= HandleCardRemoved;
    }

    private void HandleCardPresented(Card card)
    {
        if (card == null) return;          // unknown card — NFC ID not in registry
        Debug.Log($"Card presented: {card.cardName}  HP:{card.hp}");

        // Call the card's built-in activation logic (spawn, effect, etc.)
        card.Activate();
    }

    private void HandleCardRemoved(Card card)
    {
        if (card == null) return;
        Debug.Log($"Card removed: {card.cardName}");
    }
}
```

### Option C — EventManager UnityEvent (code)

```csharp
void Start()
{
    EventManager.Instance.OnCardPresented.AddListener(OnCard);
}

void OnDestroy()
{
    EventManager.Instance?.OnCardPresented.RemoveListener(OnCard);
}

void OnCard(Card card) { /* ... */ }
```

### Backward Compatibility — `OnCardScanned`

The legacy `OnCardScanned(Card, bool)` event is still fired on every card present/removed action. `bool = true` means presented, `bool = false` means removed. Existing code using this event requires no changes.

---

## 11. Connection Status Indicator

The **ConnectionStatusIndicator** component shows the current hardware state as a small LED dot and text label.

### Adding to a Scene (OnGUI mode — no Canvas needed)

1. Create any empty GameObject in your scene.
2. **Add Component → REDbox → Connection Status Indicator**.
3. Press Play. A coloured pill widget appears in the corner of the screen.

### Customising the Anchor

In the Inspector, set **Anchor** to one of: `BottomLeft`, `BottomRight`, `TopLeft`, `TopRight`. Adjust **Margin** to fine-tune the position.

### Using with a uGUI Canvas

1. Create a `Canvas` in your scene with an `Image` (for the LED dot) and a `TextMeshPro - Text` (for the label).
2. Add the `ConnectionStatusIndicator` component to any GameObject.
3. Drag the `Image` into the **Led Image** field and the text into the **Label Text** field.
4. The widget will now update your existing UI elements instead of drawing its own overlay.

### LED Colour States

| Colour | Meaning |
|---|---|
| Grey | Disconnected |
| Amber | Connecting or Reconnecting |
| Green | Connected |
| Green (same) | Connected and actively scanning |

---

## 12. Runtime Settings Menu

The **Runtime Settings Menu** lets you change the port, baud rate, and scanner state while the game is running — no editor access required.

### Opening the Menu

Press **Tab** (or **F2**) during Play Mode. The menu is available in builds too.

### What You Can Do

- **Port selection** — choose from a live list of available serial ports on the machine.
- **Connect / Disconnect** — manually control the connection.
- **Activate / Deactivate Scanner** — toggle NFC reading.
- **Adjust baud rate, reconnect delay, scanner timeout** — overrides for the current session (not persisted to the asset).

> The preferred port selected at runtime is saved automatically in `PlayerPrefs` and restored on next launch.

---

## 13. Debug Tools

### DebugOverlay (F1)

Press **F1** in Play Mode to toggle a full debug panel showing:
- Connection state (colour-coded)
- Active port
- Card registry count
- Last scanned card name + timestamp
- Rolling raw serial log
- Simulation panel (**Tab** to switch) — enter any Card ID and fire a simulated scan

Requires the `DebugOverlay` component in your scene. The Welcome Scene includes it.

### ArduinoBridge Inspector (Play Mode)

Select the `ArduinoBridge` GameObject in Play Mode. The custom Inspector shows:
- Live connection state with colour coding
- One-click scan simulation buttons for every registered card
- `ActivateScanner`, `DeactivateScanner`, and `Reconnect` buttons

---

## 14. Testing Without Hardware

You can develop and test card interactions before the physical scanner arrives.

### Enable Debug Mode

In your `HardwareSettings` asset, check **Debug Mode**. The plugin will not open any serial port and will not attempt to connect.

### Simulate Scans from Inspector

In Play Mode, select the **ArduinoBridge** GameObject. The custom Inspector panel shows a **Scan Simulator** with one button per registered card. Click a button to fire that card's events.

### Simulate Scans from Code

```csharp
// Fires OnCardPresented + EventManager.CardPresented for "DEMO_ALPHA"
ArduinoBridge.Instance.SimulateScan("DEMO_ALPHA");
```

### Simulate Scans from DebugOverlay

Press **F1** to open the overlay → press **Tab** to switch to the Simulation panel → type a Card ID → click **Scan**.

---

## 15. Troubleshooting

### "No port found" in the Console

- Check the USB cable is connected.
- On macOS, confirm the port appears under `/dev/tty.*` (run `ls /dev/tty.*` in Terminal).
- Enable **Auto Detect Port** in `HardwareSettings`.
- Add your port's name fragment to the **Auto Detect Keywords** list.
- On Linux, you may need to add your user to the `dialout` group: `sudo usermod -a -G dialout $USER`.

### Scanner not activating after connection

- Enable **Auto Activate On Start** in `HardwareSettings`.
- The onboarding wizard and Welcome Scene automatically retry every 2 seconds — wait a moment after the device connects.
- If in `debugMode`, the scanner never activates automatically; use `SimulateScan()` instead.

### Card scan not recognised (unknown card / `null` in event)

- Verify the **Card ID** in your Card asset matches the UID on the physical card exactly (ignoring separators and case).
- Use the **DebugOverlay** or Live Monitor to read `LastTagUid` from the scanner and copy it into the Card ID field.
- Ensure the Card asset is in the `cardDataArray` on `ArduinoBridge`.
- Call `ArduinoBridge.Instance.RebuildCardRegistry()` if you added cards at runtime.

### Connection drops and reconnects repeatedly

- Reduce **Reconnect Throttle Ms** — increase to smooth out USB micro-disconnects (e.g. 1000 ms).
- Try a different USB cable or hub.
- On macOS, avoid hubs and connect directly to a USB-A port.

### Events not firing after scene reload

- `ArduinoBridge` and `EventManager` both persist across scenes via `DontDestroyOnLoad`.
- Re-subscribe to `EventManager.OnCardPresented.AddListener(...)` in `Start()` after a scene reload since `MonoBehaviour` callbacks are re-registered each time.
- Static C# events on `ArduinoBridge` (`OnCardPresented`, `OnCardRemoved`, etc.) persist across scenes; ensure you unsubscribe in `OnDisable` or `OnDestroy` to avoid duplicate handlers.

### TextMeshPro errors after import

- Open **Window → TextMeshPro → Import TMP Essential Resources**.
- The plugin requires TMP Essential Resources to be present in the project.

### Menu items missing from Tools → REDbox

- Confirm the `Editor/` folder in the package imported correctly.
- Check the Console for compile errors; the `Editor` assembly will silently skip compilation on error.

---

*End of User Guide — v0.3.7 — 2026-03-11*
