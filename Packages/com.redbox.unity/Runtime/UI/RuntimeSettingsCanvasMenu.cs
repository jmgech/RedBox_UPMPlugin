using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Menu runtime REDbox basé sur Canvas/TMP (sans IMGUI) pour les builds.
///
/// Setup :
///   Tools > REDbox > UI > Create Runtime Settings Canvas Menu
///   Ce menu génère automatiquement le Canvas et câble toutes les références.
///
/// Comportement :
///   - Toggle via l'action InputSystem "UI/ToggleMenu" (Tab, F2, Start gamepad)
///   - Met en pause le jeu (Time.timeScale = 0) mais PAS les animations (UnscaledTime)
///   - Désactive les contrôleurs joueur renseignés dans pauseTargets
///   - N'affiche que les ports série pertinents (USB/serial) dans le dropdown
///   - Grise/dégrise les boutons selon l'état de connexion et du scanner
/// </summary>
public class RuntimeSettingsCanvasMenu : MonoBehaviour
{
    private static RuntimeSettingsCanvasMenu _instance;
    private static readonly string[] BackdropObjectNames = { "BackdropDim", "BackdropStreakA", "BackdropStreakB" };

    // ─── Propriété statique ────────────────────────────────────────────────────
    /// <summary>Vrai lorsque le menu settings est actuellement ouvert.</summary>
    public static bool IsMenuOpen { get; private set; }

    // ─── Display ───────────────────────────────────────────────────────────────
    [Header("Display")]
    [Tooltip("Racine du panneau runtime à afficher/masquer.")]
    public GameObject panelRoot;

    [Tooltip("Afficher le panneau au démarrage.")]
    public bool showOnStart;

    [Tooltip("Quand le panneau est visible, déverrouille le curseur.")]
    public bool unlockCursorWhenVisible = true;

    // ─── Pause ─────────────────────────────────────────────────────────────────
    [Header("Pause")]
    [Tooltip("Met en pause le gameplay (Time.timeScale=0) quand le menu est ouvert.")]
    public bool pauseGameplayWhenVisible = true;

    [Tooltip("Composants à désactiver quand le menu est ouvert (ex: RoverController, PlayerController…).")]
    public Behaviour[] pauseTargets = Array.Empty<Behaviour>();

    // ─── Ports ─────────────────────────────────────────────────────────────────
    [Header("Ports")]
    [Tooltip("N'affiche que les ports potentiellement liés au boîtier (USB/serial/keywords).")]
    public bool showOnlyRelevantPorts = true;

    [Tooltip("Si aucun port pertinent n'est trouvé, ré-affiche tous les ports pour dépannage.")]
    public bool showAllPortsIfNoRelevant = false;

    // ─── Recovery ──────────────────────────────────────────────────────────────
    [Header("Recovery")]
    [Tooltip("Force l'ouverture via menu IMGUI de secours (recommandé si le layout Canvas est cassé).")]
    public bool safeModeForceImGuiFallback = false;

    // ─── Status Labels ─────────────────────────────────────────────────────────
    [Header("Status Labels")]
    public TMP_Text connectionStateText;
    public TMP_Text activePortText;
    public TMP_Text configuredPortText;
    public TMP_Text lastErrorText;
    public TMP_Text scannerStateText;
    public TMP_Text feedbackText;

    // ─── Port Controls ─────────────────────────────────────────────────────────
    [Header("Port Controls")]
    public TMP_Dropdown portDropdown;
    public Button refreshPortsButton;
    public Button useSelectedPortButton;
    public Button autoDetectButton;

    // ─── Connection Controls ───────────────────────────────────────────────────
    [Header("Connection Controls")]
    public Button connectButton;
    public Button disconnectButton;
    public Button scannerOnButton;
    public Button scannerOffButton;
    public Button autoScannerModeButton;

    // ─── Tuning Controls ───────────────────────────────────────────────────────
    [Header("Tuning Controls")]
    public TMP_InputField baudRateInput;
    public TMP_InputField reconnectDelayInput;
    public TMP_InputField scannerTimeoutInput;
    public Button applySettingsButton;
    public Button resetSettingsButton;

    // ─── Panel Controls ────────────────────────────────────────────────────────
    [Header("Panel Controls")]
    public Button closeButton;

    // ─── Privé ─────────────────────────────────────────────────────────────────
    private bool _visible;
    private string[] _ports = Array.Empty<string>();
    private readonly Dictionary<Button, Color> _buttonBaseColors = new Dictionary<Button, Color>();
    private readonly Dictionary<Button, Color> _buttonBaseTextColors = new Dictionary<Button, Color>();

    private bool _cursorStateCaptured;
    private CursorLockMode _previousLockMode;
    private bool _previousCursorVisible;
    private bool _recoveryTriggered;
    private bool _timeScaleCaptured;
    private float _previousTimeScale = 1f;
    private float _toggleInputEnabledAt;
    private float _nextStatusRefreshAt;
    private float _nextPortsRefreshAt;
    private float _nextAutoScannerAttemptAt;
    private bool _autoScannerEnabled;

    private const float StatusRefreshInterval = 0.25f;
    private const float PortsRefreshInterval = 1.5f;
    private const float AutoScannerRetryInterval = 1.25f;

    [Header("Accessibility")]
    [Tooltip("Couleur appliquée aux boutons désactivés pour un contraste clair.")]
    public Color disabledButtonColor = new Color(0.09f, 0.10f, 0.13f, 1f);

    [Tooltip("Couleur du texte des boutons désactivés.")]
    public Color disabledButtonTextColor = new Color(0.45f, 0.48f, 0.56f, 1f);

    // Animators pour UnscaledTime pendant la pause
    private Animator[] _sceneAnimators = Array.Empty<Animator>();
    private AnimatorUpdateMode[] _originalUpdateModes = Array.Empty<AnimatorUpdateMode>();

#if ENABLE_INPUT_SYSTEM
    // InputAction standalone (indépendant du code généré par PlayerInputActions.inputactions)
    // — fonctionne sans regénération de PlayerInputActions.cs
    private InputAction _toggleAction;
#endif

    // ─── Unity lifecycle ───────────────────────────────────────────────────────
    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            if (panelRoot != null)
                panelRoot.SetActive(false);
            enabled = false;
            return;
        }

        _instance = this;
        WireButtons();
        ArduinoBridge.OnConnectionStateChanged += OnConnectionStateChanged;
        ArduinoBridge.OnDeviceReadyChanged += OnDeviceReadyChanged;

        ResolvePanelRootReference();

        // Cacher dès l'Awake pour éviter un flash d'une image avant Start.
        if (!showOnStart)
            SetOverlayVisualsActive(false);

#if ENABLE_INPUT_SYSTEM
        // InputAction standalone — pas de dépendance au fichier généré PlayerInputActions.cs.
        // Les bindings correspondent à ceux déclarés dans PlayerInputActions.inputactions (UI/ToggleMenu).
        _toggleAction = new InputAction("REDbox/ToggleMenu", InputActionType.Button);
        _toggleAction.AddBinding("<Keyboard>/tab");
        _toggleAction.AddBinding("<Keyboard>/f2");
        _toggleAction.AddBinding("<Gamepad>/start");
        _toggleAction.started += OnToggleMenuInput;
        _toggleAction.Enable();
#endif
    }

    private void Start()
    {
        if (safeModeForceImGuiFallback)
        {
            ActivateImGuiRecovery();
            safeModeForceImGuiFallback = false;
            return;
        }

        ResolvePanelRootReference();
        if (panelRoot == null)
        {
            ActivateImGuiRecovery();
            return;
        }

        SanitizeTitleGlyphs();
        EnsureAutoScannerModeButton();

        UpgradeEventSystemInputModuleIfNeeded();

        EnsureDedicatedOverlayCanvas();
        ReclaimBackdropOrphans();
        RepairCanvasHierarchy();
        EnsureReadableUiLayout();

        DisableLegacyImGuiMenus();

        // Snapshot des Animators pour pouvoir basculer en UnscaledTime pendant la pause
        CaptureSceneAnimators();
        CaptureButtonVisualBaselines();

        _visible = showOnStart;
        ApplyPanelState();
        RefreshAll();

        // Ignorer les éventuelles pressions de touche au démarrage (focus Editor)
        _toggleInputEnabledAt = Time.unscaledTime + 0.35f;

        StartCoroutine(RepairAfterLayoutPass());
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;

        ArduinoBridge.OnConnectionStateChanged -= OnConnectionStateChanged;
        ArduinoBridge.OnDeviceReadyChanged -= OnDeviceReadyChanged;
        RestoreCursorIfCaptured();
        RestoreTimeScaleIfCaptured();
        RestoreAnimatorUpdateModes();

#if ENABLE_INPUT_SYSTEM
        if (_toggleAction != null)
        {
            _toggleAction.started -= OnToggleMenuInput;
            _toggleAction.Disable();
            _toggleAction.Dispose();
            _toggleAction = null;
        }
#endif
    }

    // ─── Toggle via InputAction ────────────────────────────────────────────────
#if ENABLE_INPUT_SYSTEM
    private void OnToggleMenuInput(InputAction.CallbackContext _)
    {
        // Guard pour ignorer les pressions parasites au démarrage
        if (Time.unscaledTime < _toggleInputEnabledAt)
            return;

        _visible = !_visible;
        ApplyPanelState();

        if (_visible)
            RefreshAll();
    }
#endif

    // ─── Event ArduinoBridge ───────────────────────────────────────────────────
    private void OnConnectionStateChanged(ArduinoBridge.ConnectionState _)
    {
        RefreshStatusOnly();
    }

    private void OnDeviceReadyChanged(bool _)
    {
        RefreshStatusOnly();
    }

    private void Update()
    {
        TickAutoScanner();

        if (!_visible) return;

        float now = Time.unscaledTime;

        if (now >= _nextStatusRefreshAt)
        {
            _nextStatusRefreshAt = now + StatusRefreshInterval;
            RefreshStatusOnly();
        }

        if (now >= _nextPortsRefreshAt)
        {
            _nextPortsRefreshAt = now + PortsRefreshInterval;
            RefreshPorts();
        }
    }

    // ─── Câblage des boutons ───────────────────────────────────────────────────
    private void WireButtons()
    {
        if (refreshPortsButton  != null) refreshPortsButton.onClick.AddListener(OnRefreshPortsClicked);
        if (useSelectedPortButton != null) useSelectedPortButton.onClick.AddListener(OnUseSelectedPortClicked);
        if (autoDetectButton    != null) autoDetectButton.onClick.AddListener(OnAutoDetectClicked);

        if (connectButton       != null) connectButton.onClick.AddListener(OnConnectClicked);
        if (disconnectButton    != null) disconnectButton.onClick.AddListener(OnDisconnectClicked);
        if (scannerOnButton     != null) scannerOnButton.onClick.AddListener(OnScannerOnClicked);
        if (scannerOffButton    != null) scannerOffButton.onClick.AddListener(OnScannerOffClicked);
        if (autoScannerModeButton != null) autoScannerModeButton.onClick.AddListener(OnAutoScannerModeClicked);

        if (applySettingsButton != null) applySettingsButton.onClick.AddListener(OnApplySettingsClicked);
        if (resetSettingsButton != null) resetSettingsButton.onClick.AddListener(OnResetSettingsClicked);
        if (closeButton         != null) closeButton.onClick.AddListener(OnCloseClicked);
    }

    // ─── Handlers boutons ──────────────────────────────────────────────────────
    private void OnRefreshPortsClicked()
    {
        RefreshPorts();
        RefreshStatusOnly();
        SetFeedback("Ports refreshed.");
    }

    private void OnUseSelectedPortClicked()
    {
        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (!TryGetBridgeWithSettings(bridge)) return;

        if (_ports.Length == 0 || portDropdown == null
            || portDropdown.value < 0 || portDropdown.value >= _ports.Length)
        {
            SetFeedback("No valid port selected.");
            return;
        }

        bool ok = bridge.SelectRuntimePort(_ports[portDropdown.value], reconnect: true);
        SetFeedback(ok ? "Port selected and reconnect requested." : "Unable to select port.");
        RefreshAll();
    }

    private void OnAutoDetectClicked()
    {
        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (!TryGetBridgeWithSettings(bridge)) return;

        bridge.EnableAutoDetectMode(reconnect: true);
        SetFeedback("Auto-detect enabled and reconnect requested.");
        RefreshAll();
    }

    private void OnConnectClicked()
    {
        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (!TryGetBridgeWithSettings(bridge)) return;

        bridge.Connect();
        SetFeedback("Connect requested.");
        RefreshStatusOnly();
    }

    private void OnDisconnectClicked()
    {
        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (bridge == null) { SetFeedback("ArduinoBridge not found in scene."); return; }

        bridge.Disconnect();
        SetFeedback("Disconnected.");
        RefreshStatusOnly();
    }

    private void OnScannerOnClicked()
    {
        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (bridge == null) { SetFeedback("ArduinoBridge not found in scene."); return; }

        if (bridge.State != ArduinoBridge.ConnectionState.Connected)
        {
            SetFeedback("Device is not connected.");
            return;
        }

        bridge.ActivateScanner();
        SetFeedback(IsBridgeReady(bridge)
            ? "Scanner activation requested."
            : "Scanner activation requested (waiting for READY confirmation).");
        RefreshStatusOnly();
    }

    private void OnScannerOffClicked()
    {
        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (bridge == null) { SetFeedback("ArduinoBridge not found in scene."); return; }

        if (bridge.State != ArduinoBridge.ConnectionState.Connected)
        {
            SetFeedback("Device is not connected.");
            return;
        }

        bridge.DeactivateScanner();
        SetFeedback("Scanner deactivation requested.");
        RefreshStatusOnly();
    }

    private void OnAutoScannerModeClicked()
    {
        _autoScannerEnabled = !_autoScannerEnabled;
        UpdateAutoScannerButtonVisual();
        SetFeedback(_autoScannerEnabled
            ? "Auto Scanner enabled. Scanner will auto-activate when connected."
            : "Auto Scanner disabled. Manual scanner control only.");
        RefreshStatusOnly();
    }

    private void OnApplySettingsClicked()
    {
        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (!TryGetBridgeWithSettings(bridge)) return;

        HardwareSettings settings = bridge.settings;
        bool changed = false;

        if (TryReadPositiveInt(baudRateInput, out int baudRate) && baudRate != settings.baudRate)
        {
            settings.baudRate = baudRate;
            changed = true;
        }

        if (TryReadPositiveFloat(reconnectDelayInput, out float reconnectDelay))
        {
            reconnectDelay = Mathf.Clamp(reconnectDelay, 1f, 30f);
            if (!Mathf.Approximately(reconnectDelay, settings.reconnectDelay))
            {
                settings.reconnectDelay = reconnectDelay;
                changed = true;
            }
        }

        if (TryReadPositiveFloat(scannerTimeoutInput, out float scannerTimeout))
        {
            scannerTimeout = Mathf.Clamp(scannerTimeout, 0.5f, 10f);
            if (!Mathf.Approximately(scannerTimeout, settings.scannerEnableTimeout))
            {
                settings.scannerEnableTimeout = scannerTimeout;
                changed = true;
            }
        }

        if (!changed)
        {
            SetFeedback("No settings changes detected.");
            return;
        }

        if (!settings.debugMode)
        {
            bridge.Disconnect();
            bridge.Connect();
        }

        SetFeedback("Runtime settings applied.");
        RefreshAll();
    }

    private void OnResetSettingsClicked()
    {
        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (!TryGetBridgeWithSettings(bridge)) return;

        LoadSettingsInputs(bridge.settings);
        SetFeedback("Input fields reset from current HardwareSettings values.");
    }

    private void OnCloseClicked()
    {
        _visible = false;
        ApplyPanelState();
    }

    // ─── Refresh ───────────────────────────────────────────────────────────────
    private void RefreshAll()
    {
        RefreshPorts();
        RefreshStatusOnly();

        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (bridge != null && bridge.settings != null)
            LoadSettingsInputs(bridge.settings);
    }

    private void RefreshPorts()
    {
        ArduinoBridge bridge = ArduinoBridge.Instance;
        _ports = bridge != null ? bridge.GetAvailablePorts() : Array.Empty<string>();
        _ports = FilterPorts(_ports, bridge);

        if (portDropdown == null) return;

        portDropdown.ClearOptions();
        if (_ports.Length == 0)
        {
            portDropdown.AddOptions(new List<string> { "(no ports)" });
            portDropdown.value = 0;
            portDropdown.RefreshShownValue();
            return;
        }

        portDropdown.AddOptions(new List<string>(_ports));

        int selectedIndex = 0;
        if (bridge != null)
        {
            string configured = bridge.ConfiguredPort;
            for (int i = 0; i < _ports.Length; i++)
            {
                if (_ports[i].Equals(configured, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }
            }
        }

        portDropdown.value = selectedIndex;
        portDropdown.RefreshShownValue();
    }

    private void RefreshStatusOnly()
    {
        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (bridge == null)
        {
            SetLabel(connectionStateText, "State: bridge missing");
            SetLabel(activePortText,      "Active Port: —");
            SetLabel(configuredPortText,  "Configured Port: —");
            SetLabel(lastErrorText,       "Last Error: —");
            SetLabel(scannerStateText,    "Scanner: —");
            ApplyButtonStatesNoBridge();
            return;
        }

        bool ready = IsBridgeReady(bridge);
        SetLabel(connectionStateText, ready
            ? $"State: {bridge.State}  |  READY ✓"
            : $"State: {bridge.State}  |  SYS={bridge.LastSysState}");
        SetLabel(activePortText,     $"Active Port: {bridge.ActivePort}");
        SetLabel(configuredPortText, $"Configured Port: {bridge.ConfiguredPort}");
        SetLabel(lastErrorText,      $"Last Error: {bridge.LastConnectionError}");
        SetLabel(scannerStateText,
            $"Scanner  req={bridge.ScannerRequested}  on={bridge.ScannerEnabled}  pending={bridge.PendingScannerEnable}  auto={(_autoScannerEnabled ? "ON" : "OFF")}");

        bool connected    = bridge.State == ArduinoBridge.ConnectionState.Connected;
        bool connecting   = bridge.State == ArduinoBridge.ConnectionState.Connecting
                         || bridge.State == ArduinoBridge.ConnectionState.Reconnecting;
        bool disconnected = bridge.State == ArduinoBridge.ConnectionState.Disconnected;
        bool hasPort      = _ports != null && _ports.Length > 0;

        SetInteractable(connectButton,         disconnected && !connecting);
        SetInteractable(disconnectButton,      !disconnected);
        SetInteractable(useSelectedPortButton, hasPort && !connecting);
        SetInteractable(autoDetectButton,      !connecting);

        // Scanner ON : autorisé dès que connecté (certains firmwares legacy n'envoient pas READY explicite)
        SetInteractable(scannerOnButton,
            connected && !bridge.ScannerEnabled && !bridge.PendingScannerEnable);

        // Scanner OFF : si scanner actif ou en cours d'activation
        SetInteractable(scannerOffButton,
            connected && (bridge.ScannerEnabled || bridge.ScannerRequested || bridge.PendingScannerEnable));

        SetInteractable(autoScannerModeButton, true);
        UpdateAutoScannerButtonVisual();
    }

    private void LoadSettingsInputs(HardwareSettings settings)
    {
        if (baudRateInput      != null) baudRateInput.text      = settings.baudRate.ToString();
        if (reconnectDelayInput != null) reconnectDelayInput.text = settings.reconnectDelay.ToString("0.###");
        if (scannerTimeoutInput != null) scannerTimeoutInput.text = settings.scannerEnableTimeout.ToString("0.###");
    }

    // ─── Panel state ───────────────────────────────────────────────────────────
    private void ApplyPanelState()
    {
        IsMenuOpen = _visible;
        SetOverlayVisualsActive(_visible);
        ApplyPauseState();
        ApplyCursorState();

        if (_visible)
        {
            _nextStatusRefreshAt = 0f;
            _nextPortsRefreshAt = 0f;
        }
    }

    // ─── Pause gameplay ────────────────────────────────────────────────────────
    private void ApplyPauseState()
    {
        if (!pauseGameplayWhenVisible)
        {
            RestoreTimeScaleIfCaptured();
            RestoreAnimatorUpdateModes();
            SetPauseTargetsEnabled(true);
            return;
        }

        if (_visible)
        {
            // ── TimeScale ──
            if (!_timeScaleCaptured)
            {
                _previousTimeScale = Time.timeScale;
                _timeScaleCaptured = true;
            }
            Time.timeScale = 0f;

            // ── Animators → UnscaledTime pour que les animations continuent ──
            SwitchAnimatorsToUnscaledTime();

            // ── Désactiver les contrôleurs joueur ──
            SetPauseTargetsEnabled(false);
        }
        else
        {
            RestoreTimeScaleIfCaptured();
            RestoreAnimatorUpdateModes();
            SetPauseTargetsEnabled(true);
        }
    }

    private void RestoreTimeScaleIfCaptured()
    {
        if (!_timeScaleCaptured) return;
        Time.timeScale = _previousTimeScale;
        _timeScaleCaptured = false;
    }

    // ─── Contrôleurs joueur ────────────────────────────────────────────────────
    private void SetPauseTargetsEnabled(bool enabled)
    {
        if (pauseTargets == null) return;
        foreach (Behaviour b in pauseTargets)
            if (b != null) b.enabled = enabled;
    }

    // ─── Animators : UnscaledTime ──────────────────────────────────────────────
    /// <summary>
    /// Capture tous les Animator de la scène et leur mode de mise à jour original.
    /// À appeler une fois dans Start().
    /// </summary>
    private void CaptureSceneAnimators()
    {
        _sceneAnimators    = FindObjectsByType<Animator>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        _originalUpdateModes = new AnimatorUpdateMode[_sceneAnimators.Length];

        for (int i = 0; i < _sceneAnimators.Length; i++)
        {
            if (_sceneAnimators[i] != null)
                _originalUpdateModes[i] = _sceneAnimators[i].updateMode;
        }
    }

    /// <summary>
    /// Passe tous les Animator en UnscaledTime pour qu'ils continuent d'animer
    /// même quand Time.timeScale == 0.
    /// </summary>
    private void SwitchAnimatorsToUnscaledTime()
    {
        // Re-capture to include animators spawned after Start().
        CaptureSceneAnimators();

        for (int i = 0; i < _sceneAnimators.Length; i++)
        {
            if (_sceneAnimators[i] != null)
                _sceneAnimators[i].updateMode = AnimatorUpdateMode.UnscaledTime;
        }
    }

    /// <summary>
    /// Restaure le mode de mise à jour original de chaque Animator.
    /// </summary>
    private void RestoreAnimatorUpdateModes()
    {
        for (int i = 0; i < _sceneAnimators.Length; i++)
        {
            if (_sceneAnimators[i] != null && i < _originalUpdateModes.Length)
                _sceneAnimators[i].updateMode = _originalUpdateModes[i];
        }
    }

    // ─── Curseur ───────────────────────────────────────────────────────────────
    private void ApplyCursorState()
    {
        if (!unlockCursorWhenVisible) return;

        if (_visible)
        {
            if (!_cursorStateCaptured)
            {
                _previousLockMode    = Cursor.lockState;
                _previousCursorVisible = Cursor.visible;
                _cursorStateCaptured = true;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            return;
        }

        RestoreCursorIfCaptured();
    }

    private void RestoreCursorIfCaptured()
    {
        if (!_cursorStateCaptured) return;

        Cursor.lockState = _previousLockMode;
        Cursor.visible   = _previousCursorVisible;
        _cursorStateCaptured = false;
    }

    // ─── Filtrage des ports ────────────────────────────────────────────────────
    private string[] FilterPorts(string[] ports, ArduinoBridge bridge)
    {
        if (ports == null || ports.Length == 0)
            return ports ?? Array.Empty<string>();

        if (!showOnlyRelevantPorts)
            return ports;

        List<string> filtered = new List<string>();

        string configured = bridge != null ? bridge.ConfiguredPort : string.Empty;
        string active     = bridge != null ? bridge.ActivePort     : string.Empty;

        foreach (string port in ports)
        {
            if (IsRelevantPort(port, bridge, configured, active))
                filtered.Add(port);
        }

        return filtered.ToArray();
    }

    private static bool IsBridgeReady(ArduinoBridge bridge)
    {
        return bridge != null && bridge.IsDeviceReady;
    }

    private static bool IsRelevantPort(string port, ArduinoBridge bridge, string configured, string active)
    {
        if (string.IsNullOrWhiteSpace(port)) return false;

        // Toujours garder le port configuré et le port actif
        if (!string.IsNullOrWhiteSpace(configured) &&
            port.Equals(configured, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(active) &&
            port.Equals(active, StringComparison.OrdinalIgnoreCase))
            return true;

        string p = port.ToLowerInvariant();

        // Patterns typiques : USB serial (macOS/Windows/Linux)
        if (p.Contains("usb"))    return true;  // cu.usbserial-*, ttyUSB*
        if (p.Contains("serial")) return true;  // cu.usbserial-*, /dev/serial*
        if (p.Contains("modem"))  return true;  // cu.usbmodem*
        if (p.Contains("wch"))    return true;  // CH340 sur certains systèmes
        if (p.Contains("acm"))    return true;  // ttyACM* (Linux Arduino)
        if (p.StartsWith("com"))  return true;  // COM1..COM99 (Windows)

        // Keywords personnalisés depuis HardwareSettings
        if (bridge != null && bridge.settings != null && bridge.settings.autoDetectKeywords != null)
        {
            foreach (string keyword in bridge.settings.autoDetectKeywords)
            {
                if (string.IsNullOrWhiteSpace(keyword)) continue;
                if (p.Contains(keyword.Trim().ToLowerInvariant())) return true;
            }
        }

        return false;
    }

    // ─── Interactabilité boutons ───────────────────────────────────────────────
    private void ApplyButtonStatesNoBridge()
    {
        SetInteractable(connectButton,          false);
        SetInteractable(disconnectButton,       false);
        SetInteractable(scannerOnButton,        false);
        SetInteractable(scannerOffButton,       false);
        SetInteractable(autoScannerModeButton,  true);
        SetInteractable(useSelectedPortButton,  false);
        SetInteractable(autoDetectButton,       false);
        UpdateAutoScannerButtonVisual();
    }

    private void SetInteractable(Button btn, bool value)
    {
        if (btn == null) return;

        btn.interactable = value;

        if (!_buttonBaseColors.TryGetValue(btn, out Color baseColor))
            baseColor = btn.GetComponent<Image>() != null ? btn.GetComponent<Image>().color : Color.white;

        if (!_buttonBaseTextColors.TryGetValue(btn, out Color baseTextColor))
            baseTextColor = Color.white;

        Image image = btn.GetComponent<Image>();
        if (image != null)
            image.color = value ? Color.Lerp(baseColor, Color.white, 0.12f) : disabledButtonColor;

        TMP_Text label = btn.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.color = value ? Color.white : disabledButtonTextColor;
            label.fontStyle = value ? FontStyles.Bold : FontStyles.Normal;
            label.fontSize = Mathf.Max(16f, label.fontSize);
        }
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────
    private bool TryGetBridgeWithSettings(ArduinoBridge bridge)
    {
        if (bridge == null)
        {
            SetFeedback("ArduinoBridge not found in scene.");
            return false;
        }

        if (bridge.settings == null)
        {
            SetFeedback("HardwareSettings not assigned on ArduinoBridge.");
            return false;
        }

        return true;
    }

    private void SetFeedback(string message)
    {
        SetLabel(feedbackText, message);
        UIDisplayManager.instance?.ShowStatus(message);
    }

    private static void SetLabel(TMP_Text label, string value)
    {
        if (label != null) label.text = value;
    }

    private static bool TryReadPositiveInt(TMP_InputField field, out int value)
    {
        value = 0;
        return field != null && int.TryParse(field.text, out value) && value > 0;
    }

    private static bool TryReadPositiveFloat(TMP_InputField field, out float value)
    {
        value = 0f;
        return field != null && float.TryParse(field.text, out value) && value > 0f;
    }

    // ─── Canvas Setup / Repair ────────────────────────────────────────────────
    private void ResolvePanelRootReference()
    {
        if (panelRoot != null) return;

        Transform panel = transform.Find("RuntimeSettingsPanel");
        if (panel == null)
            panel = FindDeepChild(transform, "RuntimeSettingsPanel");

        if (panel != null)
            panelRoot = panel.gameObject;
    }

    private void SetOverlayVisualsActive(bool visible)
    {
        if (panelRoot == null) return;

        Transform container = panelRoot.transform.parent;
        if (container == null)
        {
            panelRoot.SetActive(visible);
            return;
        }

        for (int i = 0; i < container.childCount; i++)
        {
            Transform child = container.GetChild(i);
            if (child != null) child.gameObject.SetActive(visible);
        }

        ToggleNamedBackdrops(visible);
    }

    private static void ToggleNamedBackdrops(bool visible)
    {
        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Transform t in all)
        {
            if (t == null) continue;
            foreach (string name in BackdropObjectNames)
            {
                if (string.Equals(t.name, name, StringComparison.Ordinal))
                {
                    t.gameObject.SetActive(visible);
                    break;
                }
            }
        }
    }

    private IEnumerator RepairAfterLayoutPass()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        RepairCanvasHierarchy();
        RepairPanelRect();
        EnsureReadableUiLayout();
        RefreshAll();
        Canvas.ForceUpdateCanvases();
    }

    [ContextMenu("Repair Canvas Hierarchy")]
    private void RepairCanvasHierarchy()
    {
        if (panelRoot == null) return;

        NormalizeTransformTree(panelRoot.transform);
        RepairPanelRect();

        VerticalLayoutGroup panelLayout = panelRoot.GetComponent<VerticalLayoutGroup>();
        if (panelLayout != null)
        {
            panelLayout.padding                = new RectOffset(24, 24, 22, 22);
            panelLayout.spacing                = 14f;
            panelLayout.childControlWidth      = true;
            panelLayout.childControlHeight     = true;
            panelLayout.childForceExpandWidth  = true;
            panelLayout.childForceExpandHeight = false;
        }

        NormalizeSection("StatusSection");
        NormalizeSection("TuningSection");
        NormalizeRow("PortRow");
        NormalizeRow("ConnectionRow");
        NormalizeRow("CloseRow");
    }

    private void RepairPanelRect()
    {
        if (panelRoot == null) return;

        RectTransform rt = panelRoot.GetComponent<RectTransform>();
        if (rt == null) return;

        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(980f, 700f);
        rt.anchoredPosition = Vector2.zero;
    }

    private void EnsureDedicatedOverlayCanvas()
    {
        if (panelRoot == null) return;

        Transform existingHost = GameObject.Find("RuntimeSettingsCanvasHost")?.transform;
        if (existingHost != null)
        {
            MoveOverlaySiblingsTo(existingHost);
            MoveBackdropOrphansTo(existingHost);
            return;
        }

        Canvas currentCanvas = panelRoot.GetComponentInParent<Canvas>();
        if (currentCanvas == null) return;

        bool needsDedicatedHost = currentCanvas.transform.localScale != Vector3.one
                               || currentCanvas.renderMode != RenderMode.ScreenSpaceOverlay;
        if (!needsDedicatedHost) return;

        GameObject host = new GameObject("RuntimeSettingsCanvasHost",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        Canvas hostCanvas = host.GetComponent<Canvas>();
        hostCanvas.renderMode    = RenderMode.ScreenSpaceOverlay;
        hostCanvas.sortingOrder  = currentCanvas.sortingOrder + 50;

        CanvasScaler scaler = host.GetComponent<CanvasScaler>();
        scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        MoveOverlaySiblingsTo(host.transform);
        MoveBackdropOrphansTo(host.transform);
    }

    private void ReclaimBackdropOrphans()
    {
        if (panelRoot == null) return;
        Transform parent = panelRoot.transform.parent;
        if (parent == null) return;
        MoveBackdropOrphansTo(parent);
    }

    private static void MoveBackdropOrphansTo(Transform targetParent)
    {
        if (targetParent == null) return;

        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Transform t in all)
        {
            if (t == null) continue;

            bool isBackdrop = false;
            foreach (string name in BackdropObjectNames)
            {
                if (string.Equals(t.name, name, StringComparison.Ordinal))
                {
                    isBackdrop = true;
                    break;
                }
            }

            if (!isBackdrop || t.parent == targetParent) continue;
            t.SetParent(targetParent, false);
        }
    }

    private void MoveOverlaySiblingsTo(Transform targetParent)
    {
        if (panelRoot == null || targetParent == null) return;

        Transform sourceParent = panelRoot.transform.parent;
        if (sourceParent == null || sourceParent == targetParent) return;

        List<Transform> children = new List<Transform>(sourceParent.childCount);
        for (int i = 0; i < sourceParent.childCount; i++)
            children.Add(sourceParent.GetChild(i));

        foreach (Transform child in children)
        {
            if (child != null)
                child.SetParent(targetParent, false);
        }
    }

    private static void UpgradeEventSystemInputModuleIfNeeded()
    {
#if ENABLE_INPUT_SYSTEM
        EventSystem es = FindAnyObjectByType<EventSystem>();
        if (es == null) return;

        if (es.GetComponent<InputSystemUIInputModule>() != null) return;

        StandaloneInputModule old = es.GetComponent<StandaloneInputModule>();
        if (old != null) Destroy(old);

        es.gameObject.AddComponent<InputSystemUIInputModule>();
#endif
    }

    private void DisableLegacyImGuiMenus()
    {
        RuntimeSettingsMenu[] legacyMenus = FindObjectsByType<RuntimeSettingsMenu>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (RuntimeSettingsMenu m in legacyMenus)
        {
            if (m == null) continue;
            m.enableLegacyImGuiMenu = false;
            m.showOnStart           = false;
            m.enabled               = false;
        }
    }

    private void NormalizeSection(string sectionName)
    {
        if (panelRoot == null) return;

        Transform section = FindDeepChild(panelRoot.transform, sectionName);
        if (section == null) return;

        VerticalLayoutGroup layout = section.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
        {
            layout.padding                = new RectOffset(14, 14, 12, 12);
            layout.spacing                = 8f;
            layout.childControlWidth      = true;
            layout.childControlHeight     = true;
            layout.childForceExpandWidth  = true;
            layout.childForceExpandHeight = false;
        }

        RectTransform rt = section as RectTransform ?? section.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.localScale    = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.sizeDelta     = new Vector2(rt.sizeDelta.x, Mathf.Max(210f, rt.sizeDelta.y));
        }
    }

    private void NormalizeRow(string rowName)
    {
        if (panelRoot == null) return;

        Transform row = FindDeepChild(panelRoot.transform, rowName);
        if (row == null) return;

        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
        {
            layout.spacing                = 10f;
            layout.childControlWidth      = true;
            layout.childControlHeight     = true;
            layout.childForceExpandWidth  = false;
            layout.childForceExpandHeight = false;
        }

        RectTransform rt = row as RectTransform ?? row.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.localScale    = Vector3.one;
            rt.localRotation = Quaternion.identity;
            if (rt.sizeDelta.y < 56f)
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, 56f);
        }
    }

    private static Transform FindDeepChild(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), name);
            if (found != null) return found;
        }

        return null;
    }

    private void SanitizeTitleGlyphs()
    {
        if (panelRoot == null) return;

        Transform titleTransform = FindDeepChild(panelRoot.transform, "Title");
        if (titleTransform == null) return;

        TMP_Text title = titleTransform.GetComponent<TMP_Text>();
        if (title == null || string.IsNullOrEmpty(title.text)) return;

        if (title.text.IndexOf('\u2699') >= 0)
            title.text = title.text.Replace("⚙", string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(title.text))
            title.text = "REDbox Settings";
    }

    private static void NormalizeTransformTree(Transform root)
    {
        if (root == null) return;

        root.localScale    = Vector3.one;
        root.localRotation = Quaternion.identity;

        for (int i = 0; i < root.childCount; i++)
            NormalizeTransformTree(root.GetChild(i));
    }

    private void EnsureReadableUiLayout()
    {
        TMP_Text[] labels =
        {
            connectionStateText,
            activePortText,
            configuredPortText,
            lastErrorText,
            scannerStateText,
            feedbackText
        };

        foreach (TMP_Text label in labels)
        {
            if (label == null) continue;

            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode     = TextOverflowModes.Overflow;
            label.enableAutoSizing = false;
            label.fontSize = Mathf.Max(15f, label.fontSize);
            label.color = new Color(0.92f, 0.94f, 0.98f, 1f);

            RectTransform rt = label.rectTransform;
            rt.anchorMin  = new Vector2(0f, rt.anchorMin.y);
            rt.anchorMax  = new Vector2(1f, rt.anchorMax.y);
            rt.offsetMin  = new Vector2(0f, rt.offsetMin.y);
            rt.offsetMax  = new Vector2(0f, rt.offsetMax.y);

            LayoutElement le = label.GetComponent<LayoutElement>();
            if (le == null) le = label.gameObject.AddComponent<LayoutElement>();
            if (le.preferredHeight < 30f) le.preferredHeight = 34f;
        }

        if (connectionStateText != null)
            connectionStateText.fontStyle = FontStyles.Bold;

        if (feedbackText != null)
            feedbackText.color = new Color(0.98f, 0.86f, 0.52f, 1f);

        if (portDropdown != null && portDropdown.captionText != null)
        {
            portDropdown.captionText.textWrappingMode = TextWrappingModes.NoWrap;
            portDropdown.captionText.overflowMode     = TextOverflowModes.Ellipsis;
            portDropdown.captionText.enableAutoSizing = false;
        }

        TMP_InputField[] inputs = { baudRateInput, reconnectDelayInput, scannerTimeoutInput };
        foreach (TMP_InputField input in inputs)
        {
            if (input == null || input.textComponent == null) continue;
            input.textComponent.textWrappingMode = TextWrappingModes.NoWrap;
            input.textComponent.overflowMode     = TextOverflowModes.Overflow;
            input.textComponent.enableAutoSizing = false;
            input.textComponent.fontSize         = Mathf.Max(14f, input.textComponent.fontSize);
            input.textComponent.color            = new Color(0.95f, 0.96f, 0.98f, 1f);
        }

        Button[] buttons =
        {
            refreshPortsButton,
            useSelectedPortButton,
            autoDetectButton,
            connectButton,
            disconnectButton,
            scannerOnButton,
            scannerOffButton,
            autoScannerModeButton,
            applySettingsButton,
            resetSettingsButton,
            closeButton
        };

        foreach (Button btn in buttons)
        {
            if (btn == null) continue;
            TMP_Text label = btn.GetComponentInChildren<TMP_Text>(true);
            if (label == null) continue;
            label.fontSize = Mathf.Max(15f, label.fontSize);
            label.enableAutoSizing = false;
        }
    }

    private void CaptureButtonVisualBaselines()
    {
        Button[] buttons =
        {
            refreshPortsButton,
            useSelectedPortButton,
            autoDetectButton,
            connectButton,
            disconnectButton,
            scannerOnButton,
            scannerOffButton,
            autoScannerModeButton,
            applySettingsButton,
            resetSettingsButton,
            closeButton
        };

        foreach (Button btn in buttons)
        {
            if (btn == null) continue;
            if (!_buttonBaseColors.ContainsKey(btn))
            {
                Image image = btn.GetComponent<Image>();
                _buttonBaseColors[btn] = image != null ? image.color : Color.white;
            }

            if (!_buttonBaseTextColors.ContainsKey(btn))
            {
                TMP_Text label = btn.GetComponentInChildren<TMP_Text>(true);
                _buttonBaseTextColors[btn] = label != null ? label.color : Color.white;
            }
        }
    }

    private void TickAutoScanner()
    {
        if (!_autoScannerEnabled) return;

        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (bridge == null) return;
        if (bridge.State != ArduinoBridge.ConnectionState.Connected) return;
        if (bridge.ScannerEnabled || bridge.PendingScannerEnable) return;

        float now = Time.unscaledTime;
        if (now < _nextAutoScannerAttemptAt) return;

        _nextAutoScannerAttemptAt = now + AutoScannerRetryInterval;
        bridge.ActivateScanner();
    }

    private void UpdateAutoScannerButtonVisual()
    {
        if (autoScannerModeButton == null) return;

        TMP_Text label = autoScannerModeButton.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.text = _autoScannerEnabled ? "AUTO SCAN: ON" : "AUTO SCAN: OFF";
    }

    private void EnsureAutoScannerModeButton()
    {
        if (autoScannerModeButton != null || panelRoot == null) return;

        Transform row = FindDeepChild(panelRoot.transform, "ConnectionRow");
        if (row == null) return;

        Button template = scannerOffButton != null ? scannerOffButton
            : (scannerOnButton != null ? scannerOnButton
            : (connectButton != null ? connectButton : disconnectButton));
        if (template == null) return;

        GameObject clone = Instantiate(template.gameObject, row);
        clone.name = "AutoScannerModeBtn";

        autoScannerModeButton = clone.GetComponent<Button>();
        if (autoScannerModeButton == null) return;

        autoScannerModeButton.onClick.RemoveAllListeners();
        autoScannerModeButton.onClick.AddListener(OnAutoScannerModeClicked);

        LayoutElement le = clone.GetComponent<LayoutElement>();
        if (le != null)
            le.preferredWidth = 180f;

        UpdateAutoScannerButtonVisual();
    }

    private void ActivateImGuiRecovery()
    {
        if (_recoveryTriggered) return;
        _recoveryTriggered = true;

        RuntimeSettingsMenu fallback = FindAnyObjectByType<RuntimeSettingsMenu>();
        if (fallback == null)
        {
            GameObject go = new GameObject("[RuntimeSettingsMenu Recovery]");
            fallback = go.AddComponent<RuntimeSettingsMenu>();
            DontDestroyOnLoad(go);
        }

        fallback.showOnStart          = false;
        fallback.toggleKey            = KeyCode.Tab;
        fallback.alternateToggleKey   = KeyCode.F2;
        fallback.enableLegacyImGuiMenu = true;
        fallback.enabled = true;

        _visible = false;
        if (panelRoot != null)
            panelRoot.SetActive(false);

        Debug.LogError("[RuntimeSettingsCanvasMenu] Canvas layout détecté comme invalide. "
                     + "Bascule automatique vers le menu recovery IMGUI (Tab). "
                     + "Régénérez le Canvas via Tools > REDbox > UI > Create Runtime Settings Canvas Menu.");
    }
}
