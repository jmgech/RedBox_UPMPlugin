using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Overlay de debug en jeu pour le système REDbox.
///
/// AFFICHE EN TEMPS RÉEL :
///   - État de connexion Arduino (coloré)
///   - Port actif et baud rate
///   - Nombre de cartes chargées
///   - Dernière carte scannée + timestamp
///   - Log des 10 dernières données brutes reçues
///   - Boutons de simulation de scan rapide
///
/// CONTRÔLES :
///   - F1 (configurable dans GameConfig) → Afficher / Masquer
///   - Tab → Basculer entre panneau Info et panneau Simulation
///
/// SETUP :
///   Ajouter ce composant sur n'importe quel GameObject en scène.
///   Assigner GameConfig (optionnel) pour la touche de toggle.
///   Fonctionne avec OnGUI — aucun Canvas requis.
/// </summary>
public class DebugOverlay : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("Référence au GameConfig pour la touche de toggle et les couleurs.")]
    public GameConfig gameConfig;

    [Tooltip("Touche de toggle si GameConfig non assigné.")]
    public KeyCode toggleKey = KeyCode.F1;

    [Tooltip("Afficher au démarrage.")]
    public bool showOnStart = false;

    [Tooltip("Nombre max de lignes dans le log brut.")]
    [Range(5, 30)]
    public int maxLogLines = 10;

    // ─── Privé ────────────────────────────────────────────────────────────────
    private bool _visible;
    private int  _activeTab; // 0 = Info, 1 = Simulation

    private readonly Queue<string> _rawLog = new Queue<string>();
    private string _lastCardName = "—";
    private string _lastCardId   = "—";
    private string _lastScanTime = "—";
    private string _simCardId    = "";

    // Style GUI (initialisé une seule fois)
    private GUIStyle _boxStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _connectedStyle;
    private GUIStyle _disconnectedStyle;
    private GUIStyle _warningStyle;
    private bool     _stylesInitialized;

    // Dimensions
    private const float PanelWidth  = 380f;
    private const float PanelHeight = 400f;
    private const float Margin      = 10f;

    // ═════════════════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ═════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // S'abonner aux données brutes pour le log
        ArduinoBridge.OnRawDataReceived     += OnRawData;
        ArduinoBridge.OnConnectionStateChanged += OnStateChanged;
        EventManager.Instance?.OnCardScanned.AddListener(OnCardScanned);
    }

    private void Start()
    {
        _visible = showOnStart;

        if (gameConfig != null)
        {
            toggleKey  = gameConfig.debugOverlayKey;
            _visible   = gameConfig.showDebugOverlayOnStart;
        }
    }

    private void OnDestroy()
    {
        ArduinoBridge.OnRawDataReceived        -= OnRawData;
        ArduinoBridge.OnConnectionStateChanged -= OnStateChanged;
        EventManager.Instance?.OnCardScanned.RemoveListener(OnCardScanned);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ÉCOUTE DES ÉVÉNEMENTS
    // ═════════════════════════════════════════════════════════════════════════

    private void OnRawData(string data)
    {
        _rawLog.Enqueue($"[{System.DateTime.Now:HH:mm:ss.fff}] {data}");
        while (_rawLog.Count > maxLogLines)
            _rawLog.Dequeue();
    }

    private void OnStateChanged(ArduinoBridge.ConnectionState state)
    {
        // Rien à stocker ici, on lit l'état directement depuis ArduinoBridge.Instance
    }

    private void OnCardScanned(Card card, bool isScanned)
    {
        if (!isScanned || card == null) return;
        _lastCardName = card.cardName;
        _lastCardId   = card.cardId;
        _lastScanTime = System.DateTime.Now.ToString("HH:mm:ss");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // UPDATE — TOGGLE
    // ═════════════════════════════════════════════════════════════════════════

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            Key toggle = KeyCodeToInputSystemKey(toggleKey);
            if (toggle != Key.None && kb[toggle].wasPressedThisFrame)
                _visible = !_visible;

            if (_visible && kb.tabKey.wasPressedThisFrame)
                _activeTab = (_activeTab + 1) % 2;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(toggleKey))
            _visible = !_visible;
        if (_visible && Input.GetKeyDown(KeyCode.Tab))
            _activeTab = (_activeTab + 1) % 2;
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static Key KeyCodeToInputSystemKey(KeyCode keyCode)
    {
        return keyCode switch
        {
            KeyCode.F1 => Key.F1,
            KeyCode.F2 => Key.F2,
            KeyCode.Tab => Key.Tab,
            KeyCode.BackQuote => Key.Backquote,
            _ => Key.None
        };
    }
#endif

    // ═════════════════════════════════════════════════════════════════════════
    // GUI
    // ═════════════════════════════════════════════════════════════════════════

    private void OnGUI()
    {
        if (!_visible) return;

        InitStyles();

        float x = Screen.width - PanelWidth - Margin;
        float y = Margin;
        Rect  panelRect = new Rect(x, y, PanelWidth, PanelHeight);

        // Fond semi-transparent
        GUI.Box(panelRect, GUIContent.none, _boxStyle);

        GUILayout.BeginArea(new Rect(x + 8, y + 8, PanelWidth - 16, PanelHeight - 16));

        // ── Titre ──────────────────────────────────────────────────────────
        GUILayout.BeginHorizontal();
        GUILayout.Label("⬛ REDbox Debug", _titleStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label($"[{toggleKey}] toggle | [Tab] changer onglet", _labelStyle);
        GUILayout.EndHorizontal();

        DrawThinLine();

        // ── Onglets ────────────────────────────────────────────────────────
        GUILayout.BeginHorizontal();
        GUI.backgroundColor = _activeTab == 0 ? Color.white : new Color(0.5f, 0.5f, 0.5f);
        if (GUILayout.Button("📊 Info",       GUILayout.Height(24))) _activeTab = 0;
        GUI.backgroundColor = _activeTab == 1 ? Color.white : new Color(0.5f, 0.5f, 0.5f);
        if (GUILayout.Button("🎮 Simulation", GUILayout.Height(24))) _activeTab = 1;
        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();

        GUILayout.Space(4);

        // ── Contenu ────────────────────────────────────────────────────────
        if (_activeTab == 0) DrawInfoTab();
        else                 DrawSimulationTab();

        GUILayout.EndArea();
    }

    // ─── Onglet Info ──────────────────────────────────────────────────────────

    private void DrawInfoTab()
    {
        var bridge = ArduinoBridge.Instance;

        // ── Connexion ─────────────────────────────────────────────────────
        GUILayout.Label("CONNEXION", _titleStyle);

        if (bridge != null)
        {
            GUIStyle stateStyle = bridge.State == ArduinoBridge.ConnectionState.Connected
                ? _connectedStyle
                : (bridge.State == ArduinoBridge.ConnectionState.Disconnected
                    ? _disconnectedStyle
                    : _warningStyle);

            string stateIcon = bridge.State switch
            {
                ArduinoBridge.ConnectionState.Connected    => "● CONNECTÉ",
                ArduinoBridge.ConnectionState.Disconnected => "○ DÉCONNECTÉ",
                ArduinoBridge.ConnectionState.Connecting   => "◌ CONNEXION…",
                ArduinoBridge.ConnectionState.Reconnecting => "↻ RECONNEXION…",
                _ => "? INCONNU"
            };

            GUILayout.Label(stateIcon, stateStyle);
            Row("Port",    bridge.ActivePort);
            Row("Cartes",  $"{bridge.CardRegistryCount} enregistrées");
            Row("FW",      bridge.DeviceFirmwareVersion);
            Row("SYS",     bridge.LastSysState);
            Row("I2C",     bridge.LastI2cReport);
            Row("TAGUID",  bridge.LastTagUid);
            Row("SRC",     bridge.LastCardSource);
            Row("ERR",     $"{bridge.LastErrorCode}:{bridge.LastErrorMessage}");
            Row("Scanner", $"req={bridge.ScannerRequested} on={bridge.ScannerEnabled} pend={bridge.PendingScannerEnable}");
        }
        else
        {
            GUILayout.Label("○ ArduinoBridge introuvable", _disconnectedStyle);
        }

        GUILayout.Space(6);
        DrawThinLine();

        // ── Dernier scan ──────────────────────────────────────────────────
        GUILayout.Label("DERNIER SCAN", _titleStyle);
        Row("Carte",  _lastCardName);
        Row("ID",     _lastCardId);
        Row("Heure",  _lastScanTime);

        GUILayout.Space(6);
        DrawThinLine();

        // ── Log brut ──────────────────────────────────────────────────────
        GUILayout.Label("LOG DONNÉES BRUTES", _titleStyle);

        if (_rawLog.Count == 0)
        {
            GUILayout.Label("  (aucune donnée reçue)", _labelStyle);
        }
        else
        {
            foreach (string line in _rawLog)
                GUILayout.Label($"  {line}", _labelStyle);
        }
    }

    // ─── Onglet Simulation ────────────────────────────────────────────────────

    private void DrawSimulationTab()
    {
        var bridge = ArduinoBridge.Instance;

        GUILayout.Label("SIMULATION DE SCAN", _titleStyle);

        if (bridge == null)
        {
            GUILayout.Label("ArduinoBridge introuvable en scène.", _disconnectedStyle);
            return;
        }

        // Champ ID manuel
        GUILayout.BeginHorizontal();
        GUILayout.Label("Card ID : ", GUILayout.Width(65));
        _simCardId = GUILayout.TextField(_simCardId, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("▶ Scan", GUILayout.Width(65)) &&
            !string.IsNullOrWhiteSpace(_simCardId))
        {
            bridge.SimulateScan(_simCardId.Trim());
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        DrawThinLine();

        // Cartes rapides
        GUILayout.Label("ACCÈS RAPIDE", _titleStyle);

        if (bridge.cardDataArray == null || bridge.cardDataArray.Length == 0)
        {
            GUILayout.Label("Aucune carte dans le registre.", _labelStyle);
        }
        else
        {
            foreach (Card card in bridge.cardDataArray)
            {
                if (card == null) continue;

                GUILayout.BeginHorizontal();
                GUILayout.Label($"[{card.cardType}]", _labelStyle, GUILayout.Width(70));
                GUILayout.Label(card.cardName, _labelStyle, GUILayout.ExpandWidth(true));

                if (GUILayout.Button("▶", GUILayout.Width(28)))
                    bridge.SimulateScan(card.cardId);

                GUILayout.EndHorizontal();
            }
        }

        GUILayout.Space(6);
        DrawThinLine();

        // Commandes Arduino
        GUILayout.Label("COMMANDES ARDUINO", _titleStyle);
        GUILayout.BeginHorizontal();

        bool connected = bridge.State == ArduinoBridge.ConnectionState.Connected;
        GUI.enabled = connected;

        if (GUILayout.Button("🟢 Activer"))   bridge.ActivateScanner();
        if (GUILayout.Button("🔴 Désactiver")) bridge.DeactivateScanner();

        GUI.enabled = true;
        GUILayout.EndHorizontal();

        if (!connected)
            GUILayout.Label("⚠ Non connecté — commandes désactivées.", _warningStyle);

        GUILayout.Space(6);
        DrawThinLine();

        // Sélection port runtime
        GUILayout.Label("PORT SÉRIE (RUNTIME)", _titleStyle);
        Row("Config", bridge.ConfiguredPort);

        string[] ports = bridge.GetAvailablePorts();
        if (ports == null || ports.Length == 0)
        {
            GUILayout.Label("Aucun port détecté.", _warningStyle);
        }
        else
        {
            foreach (string port in ports)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(port, _labelStyle, GUILayout.Width(140));
                if (GUILayout.Button("Utiliser", GUILayout.Width(70)))
                {
                    bridge.SelectRuntimePort(port, reconnect: true);
                }
                GUILayout.EndHorizontal();
            }
        }

        if (GUILayout.Button("Activer auto-détection", GUILayout.Height(22)))
        {
            bridge.EnableAutoDetectMode(reconnect: true);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // UTILITAIRES GUI
    // ═════════════════════════════════════════════════════════════════════════

    private void Row(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"  {label} :", _labelStyle, GUILayout.Width(80));
        GUILayout.Label(value,          _labelStyle);
        GUILayout.EndHorizontal();
    }

    private void DrawThinLine()
    {
        GUILayout.Box(string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(1));
        GUILayout.Space(2);
    }

    private void InitStyles()
    {
        if (_stylesInitialized) return;
        _stylesInitialized = true;

        _boxStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = MakeTex(2, 2, new Color(0.05f, 0.05f, 0.1f, 0.88f)) }
        };

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize  = 11,
            normal    = { textColor = new Color(0.8f, 0.8f, 1f) }
        };

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            normal   = { textColor = new Color(0.85f, 0.85f, 0.85f) }
        };

        _connectedStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize  = 11,
            normal    = { textColor = new Color(0.2f, 0.9f, 0.2f) }
        };

        _disconnectedStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize  = 11,
            normal    = { textColor = new Color(0.9f, 0.2f, 0.2f) }
        };

        _warningStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize  = 10,
            normal    = { textColor = new Color(0.9f, 0.7f, 0.1f) }
        };
    }

    private static Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = col;
        Texture2D tex = new Texture2D(width, height);
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
