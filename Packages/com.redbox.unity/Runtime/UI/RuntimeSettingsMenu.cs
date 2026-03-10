using System;
using System.Collections.Generic;
using System.IO.Ports;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Menu runtime en jeu pour configurer la connexion REDbox sans passer par l'Editor.
///
/// Utilisation:
/// - Ajouter ce composant dans la scene (ex: sur un GameObject "UI").
/// - Ouvrir/fermer avec la touche toggle (Tab par defaut) ou une touche alternative.
/// - Permet de selectionner un port, connecter/deconnecter, activer le scanner,
///   et ajuster quelques parametres hardware pendant l'execution.
/// </summary>
public class RuntimeSettingsMenu : MonoBehaviour
{
    private const string PreferredPortPlayerPrefKey = "RKNFC.PreferredPort";

    [Header("Display")]
    [Tooltip("Touche pour afficher/masquer le menu runtime.")]
    public KeyCode toggleKey = KeyCode.Tab;

    [Tooltip("Touche alternative pour afficher/masquer (fallback).")]
    public KeyCode alternateToggleKey = KeyCode.F2;

    [Tooltip("Afficher le menu des le demarrage.")]
    public bool showOnStart = false;

    [Header("Legacy IMGUI")]
    [Tooltip("Menu IMGUI legacy desactive par defaut. Activer uniquement pour debug local.")]
    public bool enableLegacyImGuiMenu = false;

    [Tooltip("Largeur du panneau en pixels.")]
    [Range(300f, 700f)]
    public float panelWidth = 520f;

    [Tooltip("Hauteur du panneau en pixels.")]
    [Range(260f, 820f)]
    public float panelHeight = 620f;

    [Header("Input")]
    [Tooltip("Si actif, le curseur est debloque quand le menu est visible.")]
    public bool unlockCursorWhenVisible = true;

    private bool _visible;
    private Vector2 _scroll;

    private string[] _ports = Array.Empty<string>();
    private int _selectedPortIndex = -1;
    private string _selectedPort = string.Empty;

    private string _baudRateText = string.Empty;
    private string _reconnectDelayText = string.Empty;
    private string _scannerTimeoutText = string.Empty;

    private bool _cursorStateCaptured;
    private CursorLockMode _previousLockMode;
    private bool _previousCursorVisible;

    private void Awake()
    {
        if (!enableLegacyImGuiMenu)
        {
            _visible = false;
            enabled = false;
            return;
        }

        // The Canvas runtime menu is the primary UI. If it exists, disable IMGUI menu.
        if (FindAnyObjectByType<RuntimeSettingsCanvasMenu>() != null)
        {
            _visible = false;
            enabled = false;
        }
    }

    private void Start()
    {
        if (!isActiveAndEnabled)
            return;

        _visible = showOnStart;
        RefreshFromBridge();
        ApplyCursorState();
    }

    private void Update()
    {
        if (!enableLegacyImGuiMenu)
            return;

        if (WasTogglePressed())
        {
            _visible = !_visible;
            ApplyCursorState();
        }
    }

    private bool WasTogglePressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.tabKey.wasPressedThisFrame) return true;
            if (KeyCodeToInputSystemKey(toggleKey, out Key keyA) && kb[keyA].wasPressedThisFrame) return true;
            if (KeyCodeToInputSystemKey(alternateToggleKey, out Key keyB) && kb[keyB].wasPressedThisFrame) return true;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Tab)) return true;
        if (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(alternateToggleKey)) return true;
#endif

        return false;
    }

#if ENABLE_INPUT_SYSTEM
    private static bool KeyCodeToInputSystemKey(KeyCode keyCode, out Key key)
    {
        switch (keyCode)
        {
            case KeyCode.Tab: key = Key.Tab; return true;
            case KeyCode.F2: key = Key.F2; return true;
            case KeyCode.BackQuote: key = Key.Backquote; return true;
            default:
                key = Key.None;
                return false;
        }
    }
#endif

    private void OnDestroy()
    {
        if (_cursorStateCaptured)
        {
            Cursor.lockState = _previousLockMode;
            Cursor.visible = _previousCursorVisible;
            _cursorStateCaptured = false;
        }
    }

    private void OnGUI()
    {
        if (!enableLegacyImGuiMenu) return;
        if (!_visible) return;

        float x = Mathf.Max(12f, (Screen.width - panelWidth) * 0.5f);
        float y = Mathf.Max(12f, (Screen.height - panelHeight) * 0.5f);
        Rect panelRect = new Rect(x, y, panelWidth, panelHeight);

        GUILayout.BeginArea(panelRect, "REDbox Runtime Settings", GUI.skin.window);

        _scroll = GUILayout.BeginScrollView(_scroll);

        DrawConnectionSection();
        SpaceLine();
        DrawPortSection();
        SpaceLine();
        DrawScannerSection();
        SpaceLine();
        DrawTuningSection();

        GUILayout.EndScrollView();

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh", GUILayout.Height(28)))
            RefreshFromBridge();

        if (GUILayout.Button("Close", GUILayout.Height(28)))
        {
            _visible = false;
            ApplyCursorState();
        }
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    private void DrawConnectionSection()
    {
        GUILayout.Label("Connection", GUI.skin.box);

        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (bridge == null)
        {
            GUILayout.Label("ArduinoBridge not found in scene.");
            return;
        }

        GUILayout.Label($"State: {bridge.State}");
        GUILayout.Label($"Active Port: {bridge.ActivePort}");
        GUILayout.Label($"Configured Port: {GetConfiguredPort(bridge)}");
        GUILayout.Label($"Last Error: {bridge.LastConnectionError}");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Connect", GUILayout.Height(26)))
            bridge.Connect();

        if (GUILayout.Button("Disconnect", GUILayout.Height(26)))
            bridge.Disconnect();
        GUILayout.EndHorizontal();
    }

    private void DrawPortSection()
    {
        GUILayout.Label("Port Selection", GUI.skin.box);

        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (bridge == null)
        {
            GUILayout.Label("Bridge unavailable.");
            return;
        }

        if (_ports.Length == 0)
        {
            GUILayout.Label("No serial ports detected.");
        }
        else
        {
            EnsureSelectionIsValid();

            string[] display = BuildPortDisplay(_ports);
            int newIndex = GUILayout.SelectionGrid(_selectedPortIndex, display, 2);
            if (newIndex != _selectedPortIndex && newIndex >= 0 && newIndex < _ports.Length)
            {
                _selectedPortIndex = newIndex;
                _selectedPort = _ports[newIndex];
            }

            GUILayout.BeginHorizontal();
            GUI.enabled = _selectedPortIndex >= 0 && _selectedPortIndex < _ports.Length;
            if (GUILayout.Button("Use Selected Port", GUILayout.Height(26)))
            {
                ApplySelectedPort(bridge, _selectedPort, reconnect: true);
                RefreshFromBridge();
            }
            GUI.enabled = true;

            if (GUILayout.Button("Enable Auto Detect", GUILayout.Height(26)))
            {
                EnableAutoDetect(bridge, reconnect: true);
                RefreshFromBridge();
            }
            GUILayout.EndHorizontal();
        }
    }

    private void DrawScannerSection()
    {
        GUILayout.Label("Scanner Controls", GUI.skin.box);

        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (bridge == null)
        {
            GUILayout.Label("Bridge unavailable.");
            return;
        }

        GUILayout.Label($"Requested: {bridge.ScannerRequested}");
        GUILayout.Label($"Enabled: {bridge.ScannerEnabled}");
        GUILayout.Label($"Pending Enable: {bridge.PendingScannerEnable}");

        GUILayout.BeginHorizontal();
        bool connected = bridge.State == ArduinoBridge.ConnectionState.Connected;
        GUI.enabled = connected;
        if (GUILayout.Button("Activate Scanner", GUILayout.Height(26)))
            bridge.ActivateScanner();

        if (GUILayout.Button("Deactivate Scanner", GUILayout.Height(26)))
            bridge.DeactivateScanner();
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        if (!connected)
            GUILayout.Label("Scanner commands are available when connected.");
    }

    private void DrawTuningSection()
    {
        GUILayout.Label("Runtime Tuning", GUI.skin.box);

        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (bridge == null || bridge.settings == null)
        {
            GUILayout.Label("HardwareSettings not available.");
            return;
        }

        HardwareSettings settings = bridge.settings;

        LabeledTextField("Baud Rate", ref _baudRateText);
        LabeledTextField("Reconnect Delay (s)", ref _reconnectDelayText);
        LabeledTextField("Scanner Enable Timeout (s)", ref _scannerTimeoutText);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset Fields", GUILayout.Height(24)))
            LoadSettingsFields(settings);

        if (GUILayout.Button("Apply Values", GUILayout.Height(24)))
            ApplyRuntimeSettings(settings, bridge);
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        GUILayout.Label($"Current baudRate: {settings.baudRate}");
        GUILayout.Label($"Current reconnectDelay: {settings.reconnectDelay:0.###} s");
        GUILayout.Label($"Current scannerEnableTimeout: {settings.scannerEnableTimeout:0.###} s");
    }

    private static string[] BuildPortDisplay(IReadOnlyList<string> ports)
    {
        string[] display = new string[ports.Count];
        for (int i = 0; i < ports.Count; i++)
            display[i] = ports[i];
        return display;
    }

    private void LabeledTextField(string label, ref string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(180));
        value = GUILayout.TextField(value, GUILayout.Height(22));
        GUILayout.EndHorizontal();
    }

    private void ApplyRuntimeSettings(HardwareSettings settings, ArduinoBridge bridge)
    {
        bool changed = false;

        if (int.TryParse(_baudRateText, out int baudRate) && baudRate > 0 && baudRate != settings.baudRate)
        {
            settings.baudRate = baudRate;
            changed = true;
        }

        if (float.TryParse(_reconnectDelayText, out float reconnectDelay) && reconnectDelay > 0f)
        {
            reconnectDelay = Mathf.Clamp(reconnectDelay, 1f, 30f);
            if (!Mathf.Approximately(reconnectDelay, settings.reconnectDelay))
            {
                settings.reconnectDelay = reconnectDelay;
                changed = true;
            }
        }

        if (float.TryParse(_scannerTimeoutText, out float scannerTimeout) && scannerTimeout > 0f)
        {
            scannerTimeout = Mathf.Clamp(scannerTimeout, 0.5f, 10f);
            if (!Mathf.Approximately(scannerTimeout, settings.scannerEnableTimeout))
            {
                settings.scannerEnableTimeout = scannerTimeout;
                changed = true;
            }
        }

        if (!changed) return;

        if (!settings.debugMode)
        {
            bridge.Disconnect();
            bridge.Connect();
        }

        RefreshFromBridge();
    }

    private void RefreshFromBridge()
    {
        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (bridge == null)
        {
            _ports = Array.Empty<string>();
            _selectedPortIndex = -1;
            _selectedPort = string.Empty;
            return;
        }

        _ports = GetAvailablePorts();
        _selectedPort = GetConfiguredPort(bridge);

        _selectedPortIndex = -1;
        for (int i = 0; i < _ports.Length; i++)
        {
            if (_ports[i].Equals(_selectedPort, StringComparison.OrdinalIgnoreCase))
            {
                _selectedPortIndex = i;
                break;
            }
        }

        if (bridge.settings != null)
            LoadSettingsFields(bridge.settings);
    }

    private static string[] GetAvailablePorts()
    {
        string[] ports = SerialPort.GetPortNames();
        Array.Sort(ports, StringComparer.OrdinalIgnoreCase);
        return ports;
    }

    private static string GetConfiguredPort(ArduinoBridge bridge)
    {
        if (bridge == null || bridge.settings == null) return "—";
        return bridge.settings.serialPort;
    }

    private static void ApplySelectedPort(ArduinoBridge bridge, string portName, bool reconnect)
    {
        if (bridge == null || bridge.settings == null || string.IsNullOrWhiteSpace(portName)) return;

        bridge.settings.autoDetectPort = false;
        bridge.settings.serialPort = portName.Trim();
        PlayerPrefs.SetString(PreferredPortPlayerPrefKey, bridge.settings.serialPort);
        PlayerPrefs.Save();

        if (reconnect && !bridge.settings.debugMode)
        {
            bridge.Disconnect();
            bridge.Connect();
        }
    }

    private static void EnableAutoDetect(ArduinoBridge bridge, bool reconnect)
    {
        if (bridge == null || bridge.settings == null) return;

        bridge.settings.autoDetectPort = true;
        PlayerPrefs.DeleteKey(PreferredPortPlayerPrefKey);
        PlayerPrefs.Save();

        if (reconnect && !bridge.settings.debugMode)
        {
            bridge.Disconnect();
            bridge.Connect();
        }
    }

    private void LoadSettingsFields(HardwareSettings settings)
    {
        _baudRateText = settings.baudRate.ToString();
        _reconnectDelayText = settings.reconnectDelay.ToString("0.###");
        _scannerTimeoutText = settings.scannerEnableTimeout.ToString("0.###");
    }

    private void EnsureSelectionIsValid()
    {
        if (_ports.Length == 0)
        {
            _selectedPortIndex = -1;
            return;
        }

        if (_selectedPortIndex >= 0 && _selectedPortIndex < _ports.Length)
            return;

        _selectedPortIndex = 0;
        _selectedPort = _ports[0];
    }

    private void ApplyCursorState()
    {
        if (!unlockCursorWhenVisible) return;

        if (_visible)
        {
            if (!_cursorStateCaptured)
            {
                _previousLockMode = Cursor.lockState;
                _previousCursorVisible = Cursor.visible;
                _cursorStateCaptured = true;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (_cursorStateCaptured)
        {
            Cursor.lockState = _previousLockMode;
            Cursor.visible = _previousCursorVisible;
            _cursorStateCaptured = false;
        }
    }

    private static void SpaceLine()
    {
        GUILayout.Space(8);
        Rect r = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
        Color prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.2f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
        GUILayout.Space(8);
    }
}
