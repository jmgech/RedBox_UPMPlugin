using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using SysProcess = System.Diagnostics.Process;
using SysProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
internal class RbxV2Message
{
    public string event_type;
    public long timestamp_ms;
    public string device_id;
    public string firmware_version;
    public string reader_id;
    public string slot_id;
    public string uid;
    public string card_id;
    public string card_type;
    public string subtype;
    public int payload_version;
    public int sequence;
    public string error_code;
    public string message;
}

/// <summary>
/// Pont de communication entre le boîtier REDbox (Arduino + NFC) et le jeu Unity.
///
/// RESPONSABILITÉS :
///   - Ouvrir et maintenir la connexion série (USB ou Bluetooth)
///   - Lire et parser les données NFC reçues de l'Arduino
///   - Notifier le système de jeu via EventManager lors d'un scan
///   - Gérer la reconnexion automatique en cas de perte de connexion
///   - Exposer SimulateScan() pour les tests sans boîtier physique
///
/// DÉPENDANCES :
///   - MainThreadDispatcher   → sécurité thread (OBLIGATOIRE dans la scène)
///   - EventManager           → distribution des événements de scan
///   - UIDisplayManager       → feedback visuel
///   - HardwareSettings       → configuration matérielle (ScriptableObject)
///
/// PROTOCOLE ARDUINO SUPPORTÉ :
///   Format A → "prefix:cardId:payload"   (ex: "nfc:A3F2:data")
///   Format B → "d:cardId"                (ex: "d:A3F2")
///   Autre    → affiché comme message de statut brut
/// </summary>
public class ArduinoBridge : MonoBehaviour, IRedboxReader
{
    private const string PreferredPortPlayerPrefKey = "RKNFC.PreferredPort";

    // ─── Singleton ────────────────────────────────────────────────────────────
    public static ArduinoBridge Instance { get; private set; }

    // ─── Configuration ────────────────────────────────────────────────────────
    [Header("Configuration Matérielle")]
    [Tooltip("ScriptableObject de configuration. Créer via RK/Settings/Hardware Settings.")]
    public HardwareSettings settings;

    [Header("Cartes NFC enregistrées")]
    [Tooltip("Glisser ici tous les ScriptableObject Card du projet.")]
    public Card[] cardDataArray;

    // ─── État de connexion ─────────────────────────────────────────────────────
    public enum ConnectionState { Disconnected, Connecting, Connected, Reconnecting }
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    /// <summary>Déclenché sur le Main Thread à chaque changement d'état de connexion.</summary>
    public static event Action<ConnectionState> OnConnectionStateChanged;

    /// <summary>Déclenché sur le Main Thread à chaque ligne brute reçue (debug).</summary>
    public static event Action<string> OnRawDataReceived;

    /// <summary>Déclenché sur le Main Thread quand l'état prêt (Connected + READY) change.</summary>
    public static event Action<bool> OnDeviceReadyChanged;

    /// <summary>Déclenché sur le Main Thread à chaque heartbeat reçu du firmware.</summary>
    public static event Action OnHeartbeat;

    /// <summary>Déclenché quand une carte est posée sur le lecteur (NFC ENTER / TAP).</summary>
    public static event Action<Card> OnCardPresented;

    /// <summary>Déclenché quand une carte est retirée du lecteur (NFC EXIT).</summary>
    public static event Action<Card> OnCardRemoved;

    /// <summary>Fired when the scanner reads a card that has no matching ScriptableObject in the registry.
    /// Carries the full tag data (Id, Name, Type) decoded from the NDEF payload, so you can display
    /// feedback, call an API with tagData.Id, or dispatch instantly using tagData.Name / tagData.Type
    /// — all without needing a ScriptableObject for that card.</summary>
    public static event Action<CardTagData> OnUnknownCardScanned;

    /// <summary>Fired on every NFC ENTER event, before registry lookup.
    /// Always carries the structured tag data (Id, Name, Type, TagUid) when the firmware sends them.
    /// Name and Type may be empty on cache-hit frames (card re-placed in the same session).
    /// Subscribe here for raw access to every scan regardless of whether the card is registered.</summary>
    public static event Action<CardTagData> OnCardTagRead;

    /// <summary>
    /// Fired on every NFC ENTER, PRESENT, or EXIT event with full protocol metadata.
    /// Carries a <see cref="RedboxEvent"/> that includes taxonomy type, subtype, dot-notation
    /// card id, slot id, sequence number, and timestamp — available for both RBX1 and legacy cards.
    /// Subscribe here when you need protocol-level detail beyond OnCardTagRead.
    /// </summary>
    public static event Action<RedboxEvent> OnRedboxEvent;

    // Instance-level events for reader abstraction consumers.
    public event Action<ConnectionState> ConnectionStateChanged;
    public event Action<CardTagData> CardTagRead;

    // ─── Privé ────────────────────────────────────────────────────────────────
    private SerialPort            _serialPort;
    private CancellationTokenSource _cts;
    private Dictionary<string, Card> _cardRegistry;
    // Taxonomy cache: keyed by normalized UID; populated on full ENTER events (with CARDTYPE field),
    // merged back on cache-hit ENTER events (which only carry UID/TAGUID, no taxonomy).
    private Dictionary<string, (RedboxCardType taxonomyType, string subtype, string cardId)> _taxonomyCache;
    private Coroutine             _connectionLoopCoroutine;
    private int                   _reconnectAttempts;
    private string                _lastScannedId;
    private string                _lastRawData = "—";
    private string                _lastConnectionError = "—";
    private int                   _lastErrorCode;
    private string                _lastErrorMessage = "—";
    private string                _lastSysState = "—";
    private string                _deviceFirmwareVersion = "—";
    private string                _lastI2cReport = "—";
    private string                _lastTagUid = "—";
    private string                _lastCardSource = "—";
    private CardTagData           _lastCardTagData;
    private bool                  _scannerRequested;
    private bool                  _scannerEnabled;
    private bool                  _pendingScannerEnable;
    private bool                  _useRbxV2Protocol;
    private bool                  _manualDisconnectRequested;
    private bool                  _isReconnectInProgress;
    private DateTime              _scannerEnableRequestedAt;
    private DateTime              _lastConnectAttemptTime = DateTime.MinValue;
    private int                   _consecutiveFailures;
    private string                _runtimePortOverride;
    private DateTime              _lastScanTime;
    private int                   _redboxEventSequence;
    private readonly StringBuilder _bleLineBuffer = new StringBuilder(512);
    private bool                  _bleConnected;

    // Type-handler registry: keyed by card type (e.g. "DIRECTION", "POWER").
    // Handlers fire on every ENTER for matching cards, whether or not a ScriptableObject exists.
    // Register via RegisterTypeHandler(); unregister in OnDestroy to avoid leaks.
    private static readonly Dictionary<string, List<Action<CardTagData>>> _typeHandlers
        = new Dictionary<string, List<Action<CardTagData>>>(StringComparer.OrdinalIgnoreCase);

    // Regex compilée une seule fois (pas recréée à chaque ligne reçue)
    private static readonly Regex _fullFormatRegex = new Regex(
        @"^(?<prefix>[^:]+):(?<cardId>[^:]+):(?<payload>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    // UID brut (ex: "04 A3 1F 2B", "04-A3-1F-2B", "04A31F2B")
    private static readonly Regex _rawUidRegex = new Regex(
        @"^(?:0x)?[0-9A-Fa-f]{2}(?:[\s:-]?(?:0x)?[0-9A-Fa-f]{2}){1,15}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    // ─── Propriétés publiques (lecture) ──────────────────────────────────────
    public string LastScannedCardId  => _lastScannedId;
    public string LastRawData         => _lastRawData;
    public string LastConnectionError => _lastConnectionError;
    public DateTime LastScanTime     => _lastScanTime;
    public int CardRegistryCount     => _cardRegistry?.Count ?? 0;
    public string ActivePort         =>
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        _btBridgePortName ??
#endif
        _serialPort?.PortName ?? "—";
    public int LastErrorCode         => _lastErrorCode;
    public string LastErrorMessage   => _lastErrorMessage;
    public string LastSysState       => _lastSysState;
    public string DeviceFirmwareVersion => _deviceFirmwareVersion;
    public string LastI2cReport      => _lastI2cReport;
    public string LastTagUid         => _lastTagUid;
    public string LastCardSource     => _lastCardSource;
    /// <summary>Tag data from the most recent NFC ENTER event (Id, Name, Type, TagUid).</summary>
    public CardTagData LastCardTagData => _lastCardTagData;
    public bool ScannerRequested     => _scannerRequested;
    public bool ScannerEnabled       => _scannerEnabled;
    public bool PendingScannerEnable => _pendingScannerEnable;
    public bool IsDeviceReady        => State == ConnectionState.Connected
                                     && string.Equals(_lastSysState, "READY", StringComparison.OrdinalIgnoreCase);
    public string ConfiguredPort     => !string.IsNullOrWhiteSpace(_runtimePortOverride)
        ? _runtimePortOverride
        : (settings != null ? settings.serialPort : "—");
    public bool IsConnected          => State == ConnectionState.Connected;
    public ReaderSource Source       => settings != null && settings.bluetoothMode
        ? ReaderSource.ExternalBluetooth
        : ReaderSource.ExternalUsb;

    private bool _lastPublishedReadyState;

    // ═════════════════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ═════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        BuildCardRegistry();
        EnsureMainThreadDispatcher();
    }

    private void Start()
    {
        Debug.Log($"[ArduinoBridge] Runtime assembly='{GetType().Assembly.GetName().Name}' bluetoothMode={settings?.bluetoothMode} useBleTransport={settings?.useBleTransport} endpoint='{settings?.bleEndpoint}'");

        if (settings == null)
        {
            Debug.LogError("[ArduinoBridge] ⚠ HardwareSettings non assigné ! " +
                           "Créez un asset via Assets > RK/Settings > Hardware Settings " +
                           "et assignez-le dans l'Inspector.");
            return;
        }

        // Téléchargement asynchrone des données de cartes
        StartCoroutine(FetchCardDataFromApi());

        if (settings.debugMode)
        {
            Debug.Log("[ArduinoBridge] Mode DEBUG — connexion série désactivée. " +
                      "Utilisez SimulateScan() ou le DebugOverlay (F1) pour tester.");
            SetState(ConnectionState.Disconnected);
            return;
        }

        // Prefer last user-selected runtime port when available.
        // NOTE: we only store the override; we never mutate settings.autoDetectPort
        // so that auto-detect still runs as a fallback if the saved port is gone.
        if (PlayerPrefs.HasKey(PreferredPortPlayerPrefKey))
        {
            _runtimePortOverride = PlayerPrefs.GetString(PreferredPortPlayerPrefKey, string.Empty)?.Trim();
        }

        // Auto-activate scanner as soon as the device signals READY.
        if (settings.autoActivateOnStart)
            OnDeviceReadyChanged += AutoActivateOnReady;

        StartConnectionLoop();
    }

    private void OnDestroy()         => Shutdown();
    private void OnApplicationQuit() => Shutdown();

    // Called by the static OnDeviceReadyChanged event when autoActivateOnStart is on.
    private void AutoActivateOnReady(bool ready)
    {
        if (ready) ActivateScanner();
    }

    private void Shutdown()
    {
        // Unsubscribe auto-activate handler regardless of current settings value
        OnDeviceReadyChanged -= AutoActivateOnReady;

        // Send deactivation command before closing the port so the firmware
        // returns to idle state (scanner off, LED red) instead of staying active.
        bool isBluetooth = settings != null && settings.bluetoothMode;
        if (_serialPort != null && _serialPort.IsOpen && _scannerEnabled && !_useRbxV2Protocol && !isBluetooth)
        {
            try { _serialPort.Write(new byte[] { 0xFF, 0x00 }, 0, 2); }
            catch { /* best-effort — port may already be closing */ }
        }

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        StopBtBridge();
#endif
    CloseBleTransport();
        _cts?.Cancel();
        _cts = null;
        if (_connectionLoopCoroutine != null)
        {
            StopCoroutine(_connectionLoopCoroutine);
            _connectionLoopCoroutine = null;
        }
        SerialPort portToClose = _serialPort;
        CloseSerialPortAsync(portToClose);
        _serialPort = null;
        _lastSysState = "—";
        _pendingScannerEnable = false;
        _scannerEnabled = false;
        PublishDeviceReadyStateIfChanged();
    }

    private bool UseBleTransport => settings != null && settings.bluetoothMode && settings.useBleTransport;

    private string ResolveBleEndpoint()
    {
        if (!string.IsNullOrWhiteSpace(settings?.bleEndpoint))
            return settings.bleEndpoint.Trim();

        if (!string.IsNullOrWhiteSpace(settings?.serialPort))
            return settings.serialPort.Trim();

        return "REDBOX";
    }

    private bool TryOpenBleTransport(string endpoint)
    {
        _bleConnected = false;
        _bleLineBuffer.Clear();

        bool started = ArduinoBridgeBleNative.Start(gameObject.name, endpoint);
        if (!started)
        {
            _lastConnectionError = "BLE native bridge start failed";
            return false;
        }

        return true;
    }

    private void CloseBleTransport()
    {
        ArduinoBridgeBleNative.Stop();
        _bleConnected = false;
        _bleLineBuffer.Clear();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // REGISTRE DE CARTES
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Construit un dictionnaire cardId → Card pour une recherche O(1).
    /// Sources (par ordre de priorité décroissante) :
    ///   1. cardDataArray  — assignations explicites dans l'Inspector (priorité maximale).
    ///   2. Resources.LoadAll — tous les Card assets placés dans n'importe quel dossier
    ///      Resources/ du projet. Aucun drag requis ; ajoutez un asset et relancez.
    /// </summary>
    private void BuildCardRegistry()
    {
        _cardRegistry  = new Dictionary<string, Card>(StringComparer.OrdinalIgnoreCase);
        _taxonomyCache = new Dictionary<string, (RedboxCardType, string, string)>(StringComparer.OrdinalIgnoreCase);

        // ── Pass 1 : explicit Inspector assignments (highest priority) ────────
        if (cardDataArray != null)
        {
            foreach (Card card in cardDataArray)
                RegisterCard(card, "Inspector");
        }

        // ── Pass 2 : auto-load from all Resources folders ─────────────────────
        // Any Card asset inside any Assets/.../Resources/ folder is discovered
        // automatically. Inspector entries already registered are skipped (no override).
        Card[] resourceCards = Resources.LoadAll<Card>(string.Empty);
        int autoCount = 0;
        foreach (Card card in resourceCards)
        {
            string normalizedId = NormalizeCardId(card?.cardId);
            if (string.IsNullOrEmpty(normalizedId)) continue;
            // Skip if already registered by Inspector pass
            if (_cardRegistry.ContainsKey(normalizedId)) continue;
            if (RegisterCard(card, "Resources")) autoCount++;
        }

        int total = _cardRegistry.Count;
        if (total == 0)
            Debug.LogWarning("[ArduinoBridge] Aucune carte trouvée. " +
                "Assignez des Card assets dans cardDataArray ou placez-les dans un dossier Resources/.");
        else
            Debug.Log($"[ArduinoBridge] Registre : {total} entrée(s) " +
                $"({total - autoCount} Inspector, {autoCount} Resources).");
    }

    /// <summary>Registers one Card into the registry; returns true if added.</summary>
    private bool RegisterCard(Card card, string source)
    {
        if (card == null) return false;

        string normalizedId = NormalizeCardId(card.cardId);
        if (string.IsNullOrEmpty(normalizedId))
        {
            Debug.LogWarning($"[ArduinoBridge] Card ignorée (ID invalide) [{source}]: '{card.cardName}'");
            return false;
        }

        if (_cardRegistry.ContainsKey(normalizedId))
        {
            Debug.LogWarning($"[ArduinoBridge] ID en doublon ignoré [{source}]: '{normalizedId}' ({card.cardName})");
            return false;
        }

        _cardRegistry.Add(normalizedId, card);

        // Backward-compat alias: legacy "001:Greta:Student" cardId → also register "001"
        string aliasId = NormalizeFirstToken(card.cardId);
        if (!string.IsNullOrEmpty(aliasId)
            && !aliasId.Equals(normalizedId, StringComparison.OrdinalIgnoreCase)
            && !_cardRegistry.ContainsKey(aliasId))
        {
            _cardRegistry.Add(aliasId, card);
        }

        return true;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // BOUCLE DE CONNEXION
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Coroutine principale : gère connexion initiale + reconnexion automatique.
    /// Tourne indéfiniment (sauf maxReconnectAttempts atteint).
    /// </summary>
    private IEnumerator ConnectionLoop()
    {
        try
        {
            while (true)
            {
                if (_manualDisconnectRequested)
                {
                    SetState(ConnectionState.Disconnected);
                    yield break;
                }

                // Anti-burst: évite les boucles de connexion ultra-rapides lors des micro-coupures USB.
                double elapsedSinceLastAttemptMs = (DateTime.UtcNow - _lastConnectAttemptTime).TotalMilliseconds;
                if (elapsedSinceLastAttemptMs < settings.reconnectThrottleMs)
                {
                    float waitSeconds = (float)((settings.reconnectThrottleMs - elapsedSinceLastAttemptMs) / 1000d);
                    if (waitSeconds > 0f)
                        yield return new WaitForSeconds(waitSeconds);
                }
                _lastConnectAttemptTime = DateTime.UtcNow;

                SetState(ConnectionState.Connecting);

                string port = UseBleTransport ? ResolveBleEndpoint() : ResolvePort();
                if (port == null)
                {
                    string available = UseBleTransport ? "BLE endpoint missing" : string.Join(", ", GetAvailablePorts());
                    Debug.LogWarning($"[ArduinoBridge] Aucun port trouvé. " +
                                     $"Ports disponibles : [{available}]. " +
                                     $"Nouvelle tentative dans {settings.reconnectDelay}s.");
                    EventManager.Instance?.ScannerMissing(true);
                    SetState(ConnectionState.Disconnected);
                    yield return new WaitForSeconds(settings.reconnectDelay);
                    continue;
                }

                bool opened;
                if (UseBleTransport)
                {
                    opened = TryOpenBleTransport(port);
                }
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                else if (settings.bluetoothMode)
                {
                    opened = StartBtBridge(port);
                    if (opened)
                    {
                        // Give the bridge time to establish RFCOMM
                        Debug.Log("[ArduinoBridge] Waiting for BT bridge RFCOMM...");
                        yield return new WaitForSeconds(4f);
                    }
                }
                else
#endif
                {
                    opened = TryOpenPort(port);
                }

                if (!opened)
                {
                    _reconnectAttempts++;
                    _consecutiveFailures++;
                    SetState(ConnectionState.Reconnecting);

                    if (settings.maxReconnectAttempts > 0 &&
                        _reconnectAttempts >= settings.maxReconnectAttempts)
                    {
                        Debug.LogError($"[ArduinoBridge] {_reconnectAttempts} tentatives échouées. Abandon.");
                        EventManager.Instance?.ScannerMissing(true);
                        yield break;
                    }

                    yield return new WaitForSeconds(GetReconnectDelaySeconds());
                    continue;
                }

                // ── Connexion établie ─────────────────────────────────────────
                _reconnectAttempts = 0;
                _consecutiveFailures = 0;

                if (UseBleTransport)
                {
                    float timeout = settings != null ? settings.bleConnectTimeout : 10f;
                    float elapsed = 0f;
                    while (!_bleConnected && elapsed < timeout)
                    {
                        elapsed += Time.deltaTime;
                        yield return null;
                    }

                    if (!_bleConnected)
                    {
                        Debug.LogWarning("[ArduinoBridge] Timeout connexion BLE, nouvelle tentative.");
                        SetState(ConnectionState.Disconnected);
                        Shutdown();
                        yield return new WaitForSeconds(GetReconnectDelaySeconds());
                        continue;
                    }
                }

                // BT REDbox sessions run firmware v2 JSON in this project.
                // Force v2 mode immediately to avoid legacy scanner handshake timeout
                // (which would otherwise surface as "scanner introuvable" a few seconds later).
                bool forceV2ForRedboxBt = UseBleTransport ||
                    (settings.bluetoothMode && port.IndexOf("redbox", StringComparison.OrdinalIgnoreCase) >= 0);
                if (forceV2ForRedboxBt)
                {
                    _useRbxV2Protocol = true;
                    _pendingScannerEnable = false;
                    _scannerEnabled = true;
                    _scannerRequested = true;
                    _lastSysState = "READY";
                    PublishDeviceReadyStateIfChanged();
                }

                SetState(ConnectionState.Connected);
                EventManager.Instance?.ScannerMissing(false);

                Debug.Log($"[ArduinoBridge] ✓ Connecté sur {port}" +
                          (UseBleTransport ? " [Bluetooth LE]" : (settings.bluetoothMode ? " [Bluetooth]" : $" @ {settings.baudRate} baud [USB]")));

                if (!UseBleTransport)
                {
                    // Lancer la lecture série en background
                    _cts = new CancellationTokenSource();
                    _ = ReadSerialAsync(_cts.Token);
                }

                // Attendre la déconnexion (ReadSerialAsync changera l'état quand ça plante)
                while (State == ConnectionState.Connected)
                {
                    if (UseBleTransport && !_bleConnected)
                    {
                        SetState(ConnectionState.Disconnected);
                        break;
                    }
                    CheckPendingScannerEnableTimeout();
                    yield return null;
                }

                // ── Déconnexion détectée ──────────────────────────────────────
                Shutdown();

                if (_manualDisconnectRequested)
                {
                    SetState(ConnectionState.Disconnected);
                    yield break;
                }

                _reconnectAttempts++;
                _consecutiveFailures++;
                if (settings.maxReconnectAttempts > 0 &&
                    _reconnectAttempts >= settings.maxReconnectAttempts)
                {
                    Debug.LogError("[ArduinoBridge] Déconnexion définitive — max tentatives atteint.");
                    yield break;
                }

                Debug.Log($"[ArduinoBridge] Déconnecté. Reconnexion dans {settings.reconnectDelay}s… " +
                          $"(tentative {_reconnectAttempts})");
                SetState(ConnectionState.Reconnecting);
                yield return new WaitForSeconds(GetReconnectDelaySeconds());
            }
        }
        finally
        {
            _connectionLoopCoroutine = null;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // RÉSOLUTION DU PORT
    // ═════════════════════════════════════════════════════════════════════════

    private string ResolvePort()
    {
        string[] available = GetAvailablePorts();

        static string NormalizePortName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string port = raw.Trim();
            if (port.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase))
                return port;
            if (port.StartsWith("cu.", StringComparison.OrdinalIgnoreCase)
                || port.StartsWith("tty.", StringComparison.OrdinalIgnoreCase))
                return "/dev/" + port;
            return port;
        }

        // On macOS, cu.* is the "call-up" (outgoing) device: opening it initiates
        // the RFCOMM connection to the ESP32 BT server.
        // tty.* is the incoming device: it blocks waiting for the remote to call in,
        // which the ESP32 BluetoothSerial server never does.
        // Always prefer cu.* in Bluetooth mode.
        static string BtCuCounterpart(string port)
        {
            if (string.IsNullOrWhiteSpace(port)) return string.Empty;
            if (port.StartsWith("/dev/tty.", StringComparison.OrdinalIgnoreCase))
                return "/dev/cu." + port.Substring("/dev/tty.".Length);
            if (port.StartsWith("/dev/cu.", StringComparison.OrdinalIgnoreCase))
                return port; // already the outgoing form
            return string.Empty;
        }

        // Runtime-selected port has top priority if still present.
        if (!string.IsNullOrWhiteSpace(_runtimePortOverride))
        {
            string normalizedOverride = NormalizePortName(_runtimePortOverride);
            string btCuOverride = settings != null && settings.bluetoothMode
                ? BtCuCounterpart(normalizedOverride)
                : string.Empty;

            // Prefer cu.* in BT mode — it initiates the outgoing RFCOMM connection.
            if (!string.IsNullOrWhiteSpace(btCuOverride) && File.Exists(btCuOverride))
                return btCuOverride;

            foreach (string port in available)
            {
                if (port.Equals(_runtimePortOverride, StringComparison.OrdinalIgnoreCase)
                    || port.Equals(normalizedOverride, StringComparison.OrdinalIgnoreCase))
                    return port;
                if (!string.IsNullOrWhiteSpace(btCuOverride)
                    && port.Equals(btCuOverride, StringComparison.OrdinalIgnoreCase))
                    return port;
            }

            // If user supplied an explicit /dev path (or cu./tty. shorthand),
            // try it directly even when enumeration misses it on this frame.
            if (!string.IsNullOrWhiteSpace(normalizedOverride)
                && normalizedOverride.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase))
                return normalizedOverride;
        }

        if (settings.autoDetectPort)
        {
            // Cherche le premier port contenant un des mots-clés (ordre de priorité)
            foreach (string keyword in settings.autoDetectKeywords)
            {
                foreach (string port in available)
                {
                    if (port.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Debug.Log($"[ArduinoBridge] Auto-détection : '{port}' (mot-clé: '{keyword}')");
                        return port;
                    }
                }
            }
            // No keyword match — fall through to explicit port as last resort
        }

        // Port explicitement configuré
        if (!string.IsNullOrWhiteSpace(settings.serialPort))
        {
            string normalizedConfigured = NormalizePortName(settings.serialPort);
            string btCuConfigured = settings != null && settings.bluetoothMode
                ? BtCuCounterpart(normalizedConfigured)
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(btCuConfigured) && File.Exists(btCuConfigured))
                return btCuConfigured;

            foreach (string port in available)
            {
                if (port.Equals(settings.serialPort, StringComparison.OrdinalIgnoreCase)
                    || port.Equals(normalizedConfigured, StringComparison.OrdinalIgnoreCase))
                    return port;
                if (!string.IsNullOrWhiteSpace(btCuConfigured)
                    && port.Equals(btCuConfigured, StringComparison.OrdinalIgnoreCase))
                    return port;
            }

            if (!string.IsNullOrWhiteSpace(normalizedConfigured)
                && normalizedConfigured.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase))
                return normalizedConfigured;
        }

        return null;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // OUVERTURE / FERMETURE PORT
    // ═════════════════════════════════════════════════════════════════════════

    // ── macOS BT SPP: subprocess bridge ────────────────────────────────────
    // Mono's SerialPort cannot establish RFCOMM on macOS BT SPP virtual ports.
    // We launch a small Python script (proven to work) as a subprocess and
    // read JSON lines from its stdout.  Clean, no P/Invoke, no crashes.
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
    private SysProcess  _btBridge;
    private string   _btBridgePortName;

    private static readonly string BtBridgeScript = @"
import os, sys, termios, fcntl, select, signal, traceback
signal.signal(signal.SIGTERM, lambda *a: sys.exit(0))
try:
    port = sys.argv[1]
    sys.stderr.write(f'[bt_bridge] opening {port}\n')
    sys.stderr.flush()
    fd = os.open(port, os.O_RDWR | os.O_NONBLOCK | os.O_NOCTTY)
    fcntl.ioctl(fd, termios.TIOCEXCL)
    flags = fcntl.fcntl(fd, fcntl.F_GETFL)
    fcntl.fcntl(fd, fcntl.F_SETFL, flags & ~os.O_NONBLOCK)
    attrs = termios.tcgetattr(fd)
    attrs[0] = 0
    attrs[1] = 0
    attrs[2] = termios.CS8 | termios.CREAD | termios.CLOCAL
    attrs[3] = 0
    attrs[4] = termios.B9600
    attrs[5] = termios.B9600
    attrs[6][termios.VMIN] = 1
    attrs[6][termios.VTIME] = 0
    termios.tcsetattr(fd, termios.TCSANOW, attrs)
    os.write(fd, b'\n')
    sys.stderr.write('[bt_bridge] trigger sent, reading...\n')
    sys.stderr.flush()
    buf = b''
    idle = 0
    got_data = False
    while True:
        r, _, _ = select.select([fd], [], [], 1.0)
        if r:
            data = os.read(fd, 4096)
            if not data:
                sys.stderr.write('[bt_bridge] EOF\n')
                break
            idle = 0
            got_data = True
            buf += data
            while b'\n' in buf:
                line, buf = buf.split(b'\n', 1)
                text = line.decode('utf-8', errors='replace').strip()
                sys.stdout.write(text + '\n')
                sys.stdout.flush()
                sys.stderr.write(f'[bt_bridge] >> {text}\n')
                sys.stderr.flush()
        else:
            idle += 1
            timeout = 30 if got_data else 8
            if idle >= timeout:
                if not got_data:
                    sys.stderr.write('[bt_bridge] RFCOMM_STALE\n')
                    sys.stderr.flush()
                    os.close(fd)
                    fd = -1
                    import subprocess, re as _re, shutil
                    blueutil = shutil.which('blueutil')
                    if not blueutil:
                        for p in ['/opt/homebrew/bin/blueutil', '/usr/local/bin/blueutil']:
                            if os.path.isfile(p): blueutil = p; break
                    dev_name = port.rsplit('/', 1)[-1].replace('cu.', '').replace('tty.', '')
                    if blueutil:
                        try:
                            paired = subprocess.check_output([blueutil, '--paired'], text=True)
                            addr = None
                            for pline in paired.splitlines():
                                if dev_name.lower() in pline.lower():
                                    m = _re.search(r'address:\s*([\w-]+)', pline)
                                    if m: addr = m.group(1)
                            if addr:
                                sys.stderr.write(f'[bt_bridge] re-pairing {addr}...\n')
                                sys.stderr.flush()
                                subprocess.run([blueutil, '--unpair', addr], timeout=5)
                                import time as _time; _time.sleep(2)
                                subprocess.run([blueutil, '--pair', addr], timeout=10)
                                sys.stderr.write('[bt_bridge] re-paired OK\n')
                                sys.stderr.flush()
                            else:
                                sys.stderr.write(f'[bt_bridge] BT address not found for {dev_name}\n')
                                sys.stderr.flush()
                        except Exception as e:
                            sys.stderr.write(f'[bt_bridge] re-pair error: {e}\n')
                            sys.stderr.flush()
                    else:
                        sys.stderr.write('[bt_bridge] blueutil not found — install with: brew install blueutil\n')
                        sys.stderr.flush()
                else:
                    sys.stderr.write('[bt_bridge] no data for 30s, exiting for reconnect\n')
                    sys.stderr.flush()
                break
except Exception:
    traceback.print_exc(file=sys.stderr)
    sys.stderr.flush()
";

    private bool StartBtBridge(string port)
    {
        string scriptPath = Path.Combine(Application.temporaryCachePath, "bt_bridge.py");
        File.WriteAllText(scriptPath, BtBridgeScript);

        try
        {
            _btBridge = new SysProcess();
            _btBridge.StartInfo = new SysProcessStartInfo
            {
                FileName               = "/usr/bin/python3",
                Arguments              = $"-u \"{scriptPath}\" \"{port}\"",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            _btBridge.Start();
            _btBridgePortName = port;
            // Log stderr from bridge process; surface stale-BT warnings in UI
            _btBridge.ErrorDataReceived += (s, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                MainThreadDispatcher.Instance?.Enqueue(() =>
                {
                    if (e.Data.Contains("RFCOMM_STALE"))
                    {
                        Debug.LogWarning("[ArduinoBridge] Bluetooth connection stale — auto re-pairing...");
                        UIDisplayManager.instance?.ShowStatus("Bluetooth stale — reconnexion en cours...");
                    }
                    else if (e.Data.Contains("re-paired OK"))
                    {
                        Debug.Log("[ArduinoBridge] Bluetooth re-paired successfully");
                    }
                    else if (e.Data.Contains("blueutil not found"))
                    {
                        Debug.LogWarning("[ArduinoBridge] Bluetooth stale. Please forget and re-pair 'REDbox' in System Settings > Bluetooth. (Install blueutil for auto-repair: brew install blueutil)");
                        UIDisplayManager.instance?.ShowStatus("Bluetooth stale — re-pairer REDbox dans Reglages Bluetooth");
                    }
                    else
                    {
                        Debug.Log($"[ArduinoBridge] {e.Data}");
                    }
                });
            };
            _btBridge.BeginErrorReadLine();
            Debug.Log($"[ArduinoBridge] BT bridge started (pid {_btBridge.Id}) for {port}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ArduinoBridge] BT bridge failed to start: {ex.Message}");
            _btBridge = null;
            return false;
        }
    }

    private void StopBtBridge()
    {
        if (_btBridge == null) return;
        try
        {
            if (!_btBridge.HasExited)
            {
                _btBridge.Kill();
                _btBridge.WaitForExit(2000);
            }
            _btBridge.Dispose();
        }
        catch { /* best-effort */ }
        _btBridge = null;
        _btBridgePortName = null;
    }
#endif

    private bool TryOpenPort(string portName)
    {
        try
        {
            _serialPort = new SerialPort(portName, settings.baudRate)
            {
                ReadTimeout  = settings.bluetoothMode ? 500 : 5000,
                WriteTimeout = settings.bluetoothMode ? 5000 : 2000,
                DtrEnable    = true,  // Nécessaire sur certains Arduinos pour éviter le reset
                RtsEnable    = true
            };
            _serialPort.Open();
            return _serialPort.IsOpen;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ArduinoBridge] Impossible d'ouvrir '{portName}' : {ex.Message}");
            _lastConnectionError = ex.Message;
            _serialPort = null;
            return false;
        }
    }

    // Called by native BLE bridge via UnitySendMessage.
    public void OnBleConnected(string value)
    {
        bool connected = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        _bleConnected = connected;

        if (!connected && State == ConnectionState.Connected)
        {
            SetState(ConnectionState.Disconnected);
        }
    }

    // Called by native BLE bridge via UnitySendMessage.
    public void OnBleData(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return;

        try
        {
            byte[] bytes = Convert.FromBase64String(base64);
            string chunk = Encoding.UTF8.GetString(bytes);
            if (string.IsNullOrEmpty(chunk)) return;

            _bleLineBuffer.Append(chunk);
            ProcessBleBuffer();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ArduinoBridge] BLE data decode error: {ex.Message}");
        }
    }

    private void ProcessBleBuffer()
    {
        int safety = 0;
        while (_bleLineBuffer.Length > 0 && safety++ < 256)
        {
            string buffer = _bleLineBuffer.ToString();
            int jsonStart = buffer.IndexOf('{');

            // If no JSON start found, fall back to legacy newline framing.
            if (jsonStart < 0)
            {
                int newline = buffer.IndexOf('\n');
                if (newline < 0) return;

                string legacyLine = buffer.Substring(0, newline).Trim('\r', ' ', '\t');
                _bleLineBuffer.Remove(0, newline + 1);
                if (legacyLine.Length > 0)
                    DispatchBleFrame(legacyLine);
                continue;
            }

            // Drop any leading noise before first JSON object.
            if (jsonStart > 0)
            {
                _bleLineBuffer.Remove(0, jsonStart);
                buffer = _bleLineBuffer.ToString();
            }

            if (TryFindBalancedJsonEnd(buffer, out int jsonEnd))
            {
                string jsonFrame = buffer.Substring(0, jsonEnd + 1).Trim();
                _bleLineBuffer.Remove(0, jsonEnd + 1);
                if (jsonFrame.Length > 0)
                    DispatchBleFrame(jsonFrame);
                continue;
            }

            // Incomplete JSON object: try to resync if another object start appears.
            int nextStart = buffer.IndexOf("{\"event_type\"", 1, StringComparison.Ordinal);
            if (nextStart > 0)
            {
                Debug.LogWarning("[ArduinoBridge] BLE JSON incomplet detecte, resynchronisation du flux.");
                _bleLineBuffer.Remove(0, nextStart);
                continue;
            }

            // Wait for more BLE chunks.
            return;
        }
    }

    private static bool TryFindBalancedJsonEnd(string buffer, out int endIndex)
    {
        endIndex = -1;
        if (string.IsNullOrEmpty(buffer) || buffer[0] != '{') return false;

        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < buffer.Length; i++)
        {
            char c = buffer[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    endIndex = i;
                    return true;
                }
                if (depth < 0)
                {
                    return false;
                }
            }
        }

        return false;
    }

    private void DispatchBleFrame(string frame)
    {
        _lastRawData = frame;
        OnRawDataReceived?.Invoke(frame);
        ProcessReceivedData(frame);
    }

    // Called by native BLE bridge via UnitySendMessage.
    public void OnBleError(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            _lastConnectionError = message;
            Debug.LogWarning($"[ArduinoBridge] BLE error: {message}");
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LECTURE SÉRIE ASYNCHRONE (thread background)
    // ═════════════════════════════════════════════════════════════════════════

    private async Task ReadSerialAsync(CancellationToken token)
    {
        try
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            bool useBtBridge = _btBridge != null && !_btBridge.HasExited;
            Debug.Log($"[ArduinoBridge] ReadSerialAsync: useBtBridge={useBtBridge}");
#else
            bool useBtBridge = false;
#endif
            while (!token.IsCancellationRequested)
            {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                // Guard: for bridge path, check process is still alive
                if (useBtBridge && (_btBridge == null || _btBridge.HasExited)) break;
#endif
                // Guard: for SerialPort path, check port is still open
                if (!useBtBridge && (_serialPort == null || !_serialPort.IsOpen)) break;

                string line = await Task.Run(() =>
                {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                    if (useBtBridge)
                    {
                        try { return _btBridge?.StandardOutput.ReadLine(); }
                        catch { return null; }
                    }
#endif
                    try
                    {
                        return _serialPort?.ReadLine();
                    }
                    catch (TimeoutException)
                    {
                        return string.Empty; // Pas de données dans la fenêtre, on continue
                    }
                    catch
                    {
                        return null; // Erreur réelle → sortie de boucle
                    }
                }, token);

                if (line == null) break;           // Erreur → déconnexion
                if (token.IsCancellationRequested) break;

                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                Debug.Log($"[ArduinoBridge] RAW RX: {trimmed}"); // TODO: remove after BT debug

                // ⚠ On ne touche JAMAIS aux APIs Unity depuis ce thread background.
                //   Tout passe par MainThreadDispatcher.
                MainThreadDispatcher.Instance.Enqueue(() =>
                {
                    _lastRawData = trimmed;
                    OnRawDataReceived?.Invoke(trimmed);
                    ProcessReceivedData(trimmed);
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt propre demandé — pas une erreur
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ArduinoBridge] Erreur lecture série : {ex.Message}");
        }
        finally
        {
            // Notifie la déconnexion sur le Main Thread
            MainThreadDispatcher.Instance?.Enqueue(() =>
            {
                if (State == ConnectionState.Connected)
                    SetState(ConnectionState.Disconnected);
            });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PARSING DES DONNÉES
    // ═════════════════════════════════════════════════════════════════════════

    private void ProcessReceivedData(string data)
    {
        // Protocole RBX v2 JSON (ESP32 POC / firmware v2)
        if (TryProcessRbxV2Json(data)) return;

        // If a JSON-like frame failed v2 parsing, do not reinterpret it as legacy
        // colon-delimited data. That creates false unknown-card IDs such as
        // "CARDDETECTEDREADERID" from JSON field names.
        if (!string.IsNullOrEmpty(data) && data[0] == '{')
        {
            Debug.LogWarning($"[ArduinoBridge] Trame JSON ignorée (parse v2 échoué): {data}");
            return;
        }

        // Protocole v1 (nouveau format versionné)
        if (data.StartsWith("V1|", StringComparison.OrdinalIgnoreCase))
        {
            if (TryProcessV1Frame(data)) return;
        }

        // Format A : prefix:cardId:payload
        Match match = _fullFormatRegex.Match(data);
        if (match.Success)
        {
            HandleCardScan(match.Groups["cardId"].Value.Trim());
            return;
        }

        // Format B : d:cardId
        if (data.StartsWith("d:", StringComparison.OrdinalIgnoreCase))
        {
            string payload = data.Substring(2).Trim();

            // Codes de controle boitier (pas des cartes NFC)
            if (payload.Equals("C01", StringComparison.OrdinalIgnoreCase))
            {
                _lastSysState = "SCANNER_ON";
                _scannerEnabled = true;
                _pendingScannerEnable = false;
                PublishDeviceReadyStateIfChanged();
                UIDisplayManager.instance?.ShowStatus("Scanner actif");
                return;
            }

            if (payload.Equals("C00", StringComparison.OrdinalIgnoreCase))
            {
                _lastSysState = "SCANNER_OFF";
                _scannerEnabled = false;
                _pendingScannerEnable = false;
                PublishDeviceReadyStateIfChanged();
                UIDisplayManager.instance?.ShowStatus("Scanner inactif");
                return;
            }

            HandleCardScan(payload);
            return;
        }

        // Format C : UID brut hex (selon firmware Arduino/RFID)
        if (TryExtractRawCardId(data, out string rawCardId))
        {
            HandleCardScan(rawCardId);
            return;
        }

        // Autre : message de statut brut de l'Arduino (ex: "READY", "ERROR:...")
        if (data.Equals("READY", StringComparison.OrdinalIgnoreCase)
            || data.Equals("BOITIER_PRET", StringComparison.OrdinalIgnoreCase))
        {
            _lastSysState = "READY";
            PublishDeviceReadyStateIfChanged();
            UIDisplayManager.instance?.ShowStatus("Boitier pret");
            EventManager.Instance?.ScannerMissing(false);
            return;
        }

        UIDisplayManager.instance?.ShowStatus(data);
    }

    private bool TryProcessRbxV2Json(string data)
    {
        if (string.IsNullOrEmpty(data) || data[0] != '{') return false;

        RbxV2Message msg;
        try
        {
            msg = JsonUtility.FromJson<RbxV2Message>(data);
        }
        catch
        {
            return false;
        }

        if (msg == null || string.IsNullOrWhiteSpace(msg.event_type)) return false;

        string eventType = msg.event_type.Trim().ToLowerInvariant();
        _useRbxV2Protocol = true;

        switch (eventType)
        {
            case "reader_ready":
                _lastSysState = "READY";
                _scannerEnabled = true;
                _pendingScannerEnable = false;
                _deviceFirmwareVersion = string.IsNullOrWhiteSpace(msg.firmware_version)
                    ? _deviceFirmwareVersion
                    : msg.firmware_version;
                PublishDeviceReadyStateIfChanged();
                EventManager.Instance?.ScannerMissing(false);
                UIDisplayManager.instance?.ShowStatus(string.IsNullOrWhiteSpace(_deviceFirmwareVersion) || _deviceFirmwareVersion == "—"
                    ? "Boitier pret"
                    : $"Boitier pret (FW {_deviceFirmwareVersion})");
                return true;

            case "card_detected":
                _scannerEnabled = true;
                _pendingScannerEnable = false;

                CardTagData tagData = new CardTagData
                {
                    Id = NormalizeCardId(msg.uid),
                    Type = string.IsNullOrWhiteSpace(msg.card_type)
                        ? string.Empty
                        : msg.card_type.Trim().ToUpperInvariant(),
                    TagUid = NormalizeCardId(msg.uid),
                    CardId = string.IsNullOrWhiteSpace(msg.card_id) ? string.Empty : msg.card_id.Trim(),
                    SlotId = string.IsNullOrWhiteSpace(msg.slot_id) ? "center" : msg.slot_id.Trim().ToLowerInvariant(),
                    Subtype = string.IsNullOrWhiteSpace(msg.subtype) ? string.Empty : msg.subtype.Trim().ToLowerInvariant(),
                    TaxonomyType = ParseRedboxCardType(msg.card_type)
                };
                HandleCardTagData(tagData);
                EmitRbxV2LifecycleEvent(msg, RedboxEvent.EventType.CardEnter);
                return true;

            case "card_present":
                _scannerEnabled = true;
                _pendingScannerEnable = false;

                CardTagData presentData = new CardTagData
                {
                    Id = NormalizeCardId(msg.uid),
                    Type = string.IsNullOrWhiteSpace(msg.card_type)
                        ? string.Empty
                        : msg.card_type.Trim().ToUpperInvariant(),
                    TagUid = NormalizeCardId(msg.uid),
                    CardId = string.IsNullOrWhiteSpace(msg.card_id) ? string.Empty : msg.card_id.Trim(),
                    SlotId = string.IsNullOrWhiteSpace(msg.slot_id) ? "center" : msg.slot_id.Trim().ToLowerInvariant(),
                    Subtype = string.IsNullOrWhiteSpace(msg.subtype) ? string.Empty : msg.subtype.Trim().ToLowerInvariant(),
                    TaxonomyType = ParseRedboxCardType(msg.card_type)
                };

                // Presence heartbeat: expose the raw tag fact without retriggering
                // the full card_detected gameplay dispatch pipeline.
                _lastCardTagData = presentData;
                _lastScannedId = presentData.Id;
                _lastScanTime = DateTime.Now;
                OnCardTagRead?.Invoke(presentData);
                CardTagRead?.Invoke(presentData);
                EmitRbxV2LifecycleEvent(msg, RedboxEvent.EventType.CardPresent);
                return true;

            case "card_removed":
                UIDisplayManager.instance?.ClearAll();
                UIDisplayManager.instance?.ShowTemporaryStatus("Carte retirée", 1.2f);
                EmitRbxV2LifecycleEvent(msg, RedboxEvent.EventType.CardExit);
                return true;

            case "heartbeat":
                // v2 firmware sends heartbeat periodically even when idle.
                // Confirms the device is still alive — update READY state but
                // never override _scannerEnabled (respects user deactivation).
                OnHeartbeat?.Invoke();
                if (!string.Equals(_lastSysState, "READY", StringComparison.OrdinalIgnoreCase))
                {
                    _lastSysState = "READY";
                    PublishDeviceReadyStateIfChanged();
                }
                return true;

            case "error":
                _lastErrorMessage = string.IsNullOrWhiteSpace(msg.message) ? "UNKNOWN" : msg.message;
                UIDisplayManager.instance?.ShowStatus($"ERR: {_lastErrorMessage}");
                return true;

            default:
                return false;
        }
    }

    private void EmitRbxV2LifecycleEvent(RbxV2Message msg, RedboxEvent.EventType eventType)
    {
        string uid = NormalizeCardId(msg.uid);
        string tagUid = NormalizeCardId(msg.uid);
        string slotId = string.IsNullOrWhiteSpace(msg.slot_id) ? "center" : msg.slot_id.Trim().ToLowerInvariant();
        string readerId = string.IsNullOrWhiteSpace(msg.reader_id) ? "reader_1" : msg.reader_id.Trim();
        RedboxCardType cardType = ParseRedboxCardType(msg.card_type);
        string subtype = string.IsNullOrWhiteSpace(msg.subtype) ? string.Empty : msg.subtype.Trim().ToLowerInvariant();
        string cardId = string.IsNullOrWhiteSpace(msg.card_id) ? uid : msg.card_id.Trim();

        RedboxCard rbCard = eventType == RedboxEvent.EventType.CardExit
            ? null
            : new RedboxCard(
                uid: uid,
                cardType: cardType,
                subtype: subtype,
                cardId: string.IsNullOrEmpty(cardId) ? uid : cardId,
                physicalTagUid: tagUid,
                payloadVersion: msg.payload_version > 0 ? msg.payload_version : (cardType != RedboxCardType.Unknown ? 1 : 0)
            );

        int sequence = msg.sequence > 0 ? msg.sequence : ++_redboxEventSequence;

        OnRedboxEvent?.Invoke(new RedboxEvent(
            type: eventType,
            card: rbCard,
            readerId: readerId,
            slotId: slotId,
            sequence: sequence
        ));
    }

    private static RedboxCardType ParseRedboxCardType(string cardType)
    {
        string raw = string.IsNullOrWhiteSpace(cardType) ? string.Empty : cardType.Trim().ToLowerInvariant();
        return raw switch
        {
            "actor" => RedboxCardType.Actor,
            "instruction" => RedboxCardType.Instruction,
            "modifier" => RedboxCardType.Modifier,
            "lore" => RedboxCardType.Lore,
            "cosmetic" => RedboxCardType.Cosmetic,
            "world" => RedboxCardType.World,
            "system" => RedboxCardType.System,
            _ => RedboxCardType.Unknown,
        };
    }

    private bool TryProcessV1Frame(string frame)
    {
        // Format attendu: V1|TYPE|KEY=VALUE;KEY=VALUE
        string[] parts = frame.Split(new[] { '|' }, 3, StringSplitOptions.None);
        if (parts.Length < 2) return false;

        string type = parts[1].Trim().ToUpperInvariant();
        string payload = parts.Length == 3 ? parts[2] : string.Empty;
        Dictionary<string, string> fields = ParseV1Fields(payload);

        switch (type)
        {
            case "SYS":
                HandleV1Sys(fields);
                return true;

            case "ERR":
                HandleV1Error(fields);
                return true;

            case "CARD":
                return HandleV1Card(fields);

            case "PING":
            case "PONG":
                // Trames techniques (diagnostic/liveness)
                return true;

            default:
                return false;
        }
    }

    private void HandleV1Sys(Dictionary<string, string> fields)
    {
        string state = GetField(fields, "STATE", string.Empty).ToUpperInvariant();
        string fw = GetField(fields, "FW", string.Empty);
        if (!string.IsNullOrWhiteSpace(fw))
            _deviceFirmwareVersion = fw.Trim();

        string i2c = GetField(fields, "I2C", string.Empty);
        if (!string.IsNullOrWhiteSpace(i2c))
            _lastI2cReport = i2c.Trim();

        if (string.IsNullOrEmpty(state))
        {
            if (!string.IsNullOrWhiteSpace(_lastI2cReport))
                UIDisplayManager.instance?.ShowStatus($"I2C: {_lastI2cReport}");
            return;
        }

        _lastSysState = state;
        PublishDeviceReadyStateIfChanged();

        switch (state)
        {
            case "READY":
                UIDisplayManager.instance?.ShowStatus(string.IsNullOrWhiteSpace(_deviceFirmwareVersion) || _deviceFirmwareVersion == "—"
                    ? "Boitier pret"
                    : $"Boitier pret (FW {_deviceFirmwareVersion})");
                EventManager.Instance?.ScannerMissing(false);
                break;
            case "SCANNER_ON":
                _scannerEnabled = true;
                _pendingScannerEnable = false;
                UIDisplayManager.instance?.ShowStatus("Scanner actif");
                break;
            case "SCANNER_OFF":
                _scannerEnabled = false;
                UIDisplayManager.instance?.ShowStatus("Scanner inactif");
                break;
            case "WAIT_PN532":
                UIDisplayManager.instance?.ShowStatus("Recherche du lecteur NFC...");
                EventManager.Instance?.ScannerMissing(true);
                break;
            default:
                UIDisplayManager.instance?.ShowStatus($"SYS: {state}");
                break;
        }
    }

    private void HandleV1Error(Dictionary<string, string> fields)
    {
        string codeRaw = GetField(fields, "CODE", "0");
        string msg = GetField(fields, "MSG", "UNKNOWN");
        string i2c = GetField(fields, "I2C", string.Empty);

        if (!string.IsNullOrWhiteSpace(i2c))
            _lastI2cReport = i2c.Trim();

        if (!int.TryParse(codeRaw, out _lastErrorCode))
            _lastErrorCode = 0;

        _lastErrorMessage = msg;

        switch (_lastErrorCode)
        {
            case 201:
                _scannerEnabled = false;
                _pendingScannerEnable = false;
                UIDisplayManager.instance?.ShowStatus(string.IsNullOrWhiteSpace(_lastI2cReport) || _lastI2cReport == "—"
                    ? "Scanner introuvable (PN532)"
                    : $"Scanner introuvable (I2C: {_lastI2cReport})");
                EventManager.Instance?.ScannerMissing(true);
                break;
            case 202:
                _scannerEnabled = false;
                _pendingScannerEnable = false;
                UIDisplayManager.instance?.ShowStatus("Redemarrage scanner en cours...");
                EventManager.Instance?.ScannerMissing(true);
                break;
            default:
                UIDisplayManager.instance?.ShowStatus($"ERR {_lastErrorCode}: {_lastErrorMessage}");
                break;
        }
    }

    private bool HandleV1Card(Dictionary<string, string> fields)
    {
        string ev     = GetField(fields, "EV", "TAP").ToUpperInvariant();
        string uid    = NormalizeCardId(GetField(fields, "UID", string.Empty));
        string name   = GetField(fields, "NAME", string.Empty).Trim().ToUpperInvariant();
        string type   = GetField(fields, "TYPE", string.Empty).Trim().ToUpperInvariant();
        string tagUid = NormalizeCardId(GetField(fields, "TAGUID", string.Empty));
        string src    = GetField(fields, "SRC", string.Empty).ToUpperInvariant();

        // RBX1 taxonomy fields
        string cardTypeRaw = GetField(fields, "CARDTYPE", string.Empty).Trim().ToLowerInvariant();
        string subtype     = GetField(fields, "SUBTYPE",  string.Empty).Trim().ToLowerInvariant();
        string cardId      = GetField(fields, "CARDID",   string.Empty).Trim();
        string slotId      = GetField(fields, "SLOT",     string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(uid))
        {
            // Certains firmwares peuvent envoyer DATA au lieu de UID en phase de transition.
            uid = NormalizeCardId(GetField(fields, "DATA", string.Empty));
        }

        if (string.IsNullOrEmpty(uid)) return false;

        if (!string.IsNullOrEmpty(tagUid)) _lastTagUid = tagUid;
        if (!string.IsNullOrEmpty(src))    _lastCardSource = src;

        // Parse taxonomy type string to enum
        RedboxCardType taxonomyType = cardTypeRaw switch {
            "actor"       => RedboxCardType.Actor,
            "instruction" => RedboxCardType.Instruction,
            "modifier"    => RedboxCardType.Modifier,
            "lore"        => RedboxCardType.Lore,
            "cosmetic"    => RedboxCardType.Cosmetic,
            "world"       => RedboxCardType.World,
            "system"      => RedboxCardType.System,
            _             => RedboxCardType.Unknown,
        };

        // Taxonomy cache: on a full event (CARDTYPE present) populate the cache.
        // On a cache-hit event (only UID/TAGUID, no CARDTYPE), restore from cache so
        // TaxonomyType/Subtype/CardId are consistent across first and subsequent scans.
        if (taxonomyType != RedboxCardType.Unknown)
        {
            _taxonomyCache[uid] = (taxonomyType, subtype, cardId);
        }
        else if (_taxonomyCache.TryGetValue(uid, out var cached))
        {
            taxonomyType = cached.taxonomyType;
            subtype      = cached.subtype;
            cardId       = cached.cardId;
        }

        CardTagData tagData = new CardTagData
        {
            Id           = uid,
            Name         = name,
            Type         = type,
            TagUid       = tagUid,
            TaxonomyType = taxonomyType,
            Subtype      = subtype,
            CardId       = cardId,
            SlotId       = slotId,
        };

        // Build RedboxCard and RedboxEvent for high-fidelity subscribers
        var rbCard = new RedboxCard(
            uid:            uid,
            cardType:       taxonomyType,
            subtype:        subtype,
            cardId:         string.IsNullOrEmpty(cardId) ? uid : cardId,
            physicalTagUid: tagUid,
            payloadVersion: taxonomyType != RedboxCardType.Unknown ? 1 : 0
        );

        RedboxEvent.EventType rbEventType = ev switch {
            "ENTER"   => RedboxEvent.EventType.CardEnter,
            "PRESENT" => RedboxEvent.EventType.CardPresent,
            "EXIT"    => RedboxEvent.EventType.CardExit,
            _         => RedboxEvent.EventType.CardEnter,
        };

        var rbEvent = new RedboxEvent(
            type:     rbEventType,
            card:     rbEventType == RedboxEvent.EventType.CardExit ? null : rbCard,
            readerId: "reader_1",
            slotId:   string.IsNullOrEmpty(slotId) ? "center" : slotId,
            sequence: ++_redboxEventSequence
        );

        switch (ev)
        {
            case "TAP":
            case "ENTER":
                HandleCardTagData(tagData);
                OnRedboxEvent?.Invoke(rbEvent);
                return true;

            case "PRESENT":
                // Heartbeat de présence: pas de retrigger gameplay par défaut.
                OnRedboxEvent?.Invoke(rbEvent);
                return true;

            case "EXIT":
                UIDisplayManager.instance?.ClearAll();
                UIDisplayManager.instance?.ShowTemporaryStatus(
                    string.IsNullOrEmpty(name) ? $"Carte retirée: {uid}" : $"Carte retirée: {name}", 1.5f);
                if (TryResolveCard(uid, out _, out Card removedCard))
                {
                    OnCardRemoved?.Invoke(removedCard);
                    EventManager.Instance?.CardRemoved(removedCard);
                }
                OnRedboxEvent?.Invoke(rbEvent);
                return true;

            default:
                HandleCardTagData(tagData);
                OnRedboxEvent?.Invoke(rbEvent);
                return true;
        }
    }

    private static Dictionary<string, string> ParseV1Fields(string payload)
    {
        Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(payload)) return fields;

        string[] pairs = payload.Split(';');
        foreach (string pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair)) continue;

            int sep = pair.IndexOf('=');
            if (sep <= 0) continue;

            string key = pair.Substring(0, sep).Trim();
            string value = pair.Substring(sep + 1).Trim();
            if (key.Length == 0) continue;

            fields[key] = value;
        }

        return fields;
    }

    private static string GetField(Dictionary<string, string> fields, string key, string fallback)
    {
        if (fields != null && fields.TryGetValue(key, out string value))
            return value;
        return fallback;
    }

    // Central scan handler — all card ENTER events flow through here.
    private void HandleCardTagData(CardTagData tagData)
    {
        if (!tagData.IsValid)
        {
            Debug.LogWarning("[ArduinoBridge] Card ID vide reçu.");
            return;
        }

        _lastCardTagData = tagData;
        _lastScannedId   = tagData.Id;
        _lastScanTime    = DateTime.Now;

        // Always fire raw tag event and type handlers before registry lookup.
        OnCardTagRead?.Invoke(tagData);
        CardTagRead?.Invoke(tagData);
        FireTypeHandlers(tagData);

        if (!TryResolveCard(tagData.Id, out string resolvedCardId, out Card card))
        {
            string display = !string.IsNullOrEmpty(tagData.Name) ? tagData.Name : tagData.Id;
            string typeHint = !string.IsNullOrEmpty(tagData.Type) ? $" [{tagData.Type}]" : string.Empty;
            Debug.LogWarning($"[ArduinoBridge] Carte inconnue scannée : '{tagData.Id}'{typeHint} ({display})");
            UIDisplayManager.instance?.ShowStatus($"Carte inconnue : {display}");
            OnUnknownCardScanned?.Invoke(tagData);
            EventManager.Instance?.UnknownCardScanned(tagData.Id);
            return;
        }

        if (!resolvedCardId.Equals(tagData.Id, StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"[ArduinoBridge] Alias ID résolu : '{tagData.Id}' -> '{resolvedCardId}'");
            _lastScannedId = resolvedCardId;
        }

        card.RegisterScan();
        Debug.Log($"[ArduinoBridge] ✓ Carte : {card.cardName} (ID: {card.cardId})");
        OnCardPresented?.Invoke(card);
        EventManager.Instance?.CardPresented(card);
        UIDisplayManager.instance?.ShowCard(card);
    }

    // Legacy wrapper: legacy serial formats (A/B/C) don't carry Name/Type.
    // They build a minimal CardTagData with Id only and route through the same pipeline.
    private void HandleCardScan(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId)) return;
        HandleCardTagData(new CardTagData { Id = NormalizeCardId(cardId) });
    }

    private static void FireTypeHandlers(CardTagData tagData)
    {
        if (string.IsNullOrEmpty(tagData.Type)) return;
        if (!_typeHandlers.TryGetValue(tagData.Type, out var list)) return;
        // Iterate a snapshot to tolerate handlers that unregister during invocation.
        var snapshot = new List<Action<CardTagData>>(list);
        foreach (var h in snapshot)
            h?.Invoke(tagData);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CHARGEMENT DES DONNÉES EN LIGNE
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator FetchCardDataFromApi()
    {
        if (settings == null) yield break;

        string cacheFile = Path.Combine(Application.persistentDataPath, "Data.json");
        string url       = settings.webServiceUrl.TrimEnd('/') + "/data.php";

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = settings.networkTimeout;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[ArduinoBridge] API inaccessible ({req.error}). " +
                                 $"Utilisation du cache : {cacheFile}");
                yield break;
            }

            string json = req.downloadHandler.text;

            // Mise à jour du cache uniquement si le contenu a changé
            bool needsUpdate = !File.Exists(cacheFile) ||
                               File.ReadAllText(cacheFile) != json;

            if (needsUpdate)
            {
                File.WriteAllText(cacheFile, json);
                Debug.Log("[ArduinoBridge] Cache de données mis à jour depuis l'API.");
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // API PUBLIQUE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Simulates the scan of a card by its raw ID string.
    /// Fonctionne même sans boîtier physique (debugMode ou en développement).
    /// Utilisé par le DebugOverlay et l'ArduinoBridgeEditor.
    /// </summary>
    public void SimulateScan(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            Debug.LogWarning("[ArduinoBridge] SimulateScan : cardId vide.");
            return;
        }

        Debug.Log($"[ArduinoBridge] ► SIMULATION SCAN : '{cardId}'");
        HandleCardScan(cardId);
    }

    /// <summary>
    /// Simulates a structured card scan with full tag data (Id, Name, Type).
    /// Use this overload to test type-handler routing and unknown-card flows
    /// without a physical scanner.
    /// </summary>
    public void SimulateScan(CardTagData tagData)
    {
        if (!tagData.IsValid)
        {
            Debug.LogWarning("[ArduinoBridge] SimulateScan : tagData invalide (Id vide).");
            return;
        }

        Debug.Log($"[ArduinoBridge] ► SIMULATION SCAN : {tagData}");
        HandleCardTagData(tagData);
    }

    /// <summary>
    /// Registers a handler that fires whenever a card with the given type is scanned,
    /// regardless of whether a ScriptableObject exists for the card.
    /// Type is matched case-insensitively ("DIRECTION", "direction", "Direction" all match).
    ///
    /// Use for instant-action cards (Direction, Power) that need zero-latency dispatch:
    ///   ArduinoBridge.RegisterTypeHandler("DIRECTION", data => MovePlayer(data.Name));
    ///   ArduinoBridge.RegisterTypeHandler("POWER",     data => TriggerPower(data.Name));
    ///
    /// Always pair with UnregisterTypeHandler() in OnDestroy to avoid stale references.
    /// </summary>
    public static void RegisterTypeHandler(string type, Action<CardTagData> handler)
    {
        if (string.IsNullOrEmpty(type) || handler == null) return;
        string key = type.Trim().ToUpperInvariant();
        if (!_typeHandlers.TryGetValue(key, out var list))
        {
            list = new List<Action<CardTagData>>();
            _typeHandlers[key] = list;
        }
        if (!list.Contains(handler)) list.Add(handler);
    }

    /// <summary>Removes a previously registered type handler.</summary>
    public static void UnregisterTypeHandler(string type, Action<CardTagData> handler)
    {
        if (string.IsNullOrEmpty(type) || handler == null) return;
        string key = type.Trim().ToUpperInvariant();
        if (_typeHandlers.TryGetValue(key, out var list))
            list.Remove(handler);
    }

    /// <summary>
    /// Lance (ou relance) la boucle de connexion série.
    /// Exposé pour les boutons UI UnityEvent (compatibilité avec anciennes scènes).
    /// </summary>
    [ContextMenu("Connect")]
    public void Connect()
    {
        if (settings == null)
        {
            Debug.LogError("[ArduinoBridge] Connect() impossible : HardwareSettings non assigné.");
            return;
        }

        if (settings.debugMode)
        {
            Debug.Log("[ArduinoBridge] Connect() ignoré en mode DEBUG.");
            return;
        }

        if (_connectionLoopCoroutine != null)
        {
            Debug.Log("[ArduinoBridge] Connect() ignoré : boucle de connexion déjà active.");
            return;
        }

        _manualDisconnectRequested = false;
        StartConnectionLoop();
    }

    [ContextMenu("Reconnect")]
    public void Reconnect()
    {
        if (!Application.isPlaying) return;
        if (_isReconnectInProgress) return;
        StartCoroutine(ReconnectRoutine());
    }

    private IEnumerator ReconnectRoutine()
    {
        _isReconnectInProgress = true;
        try
        {
            Disconnect();
            // Let coroutine stop + BT stack close propagate before reconnecting.
            // A tiny settle delay avoids transient beachball/lockups on macOS SPP.
            yield return null;
            yield return new WaitForSeconds(0.35f);
            Connect();
        }
        finally
        {
            _isReconnectInProgress = false;
        }
    }

    /// <summary>
    /// Coupe la connexion série et stoppe la boucle de reconnexion.
    /// Exposé pour les boutons UI UnityEvent (compatibilité avec anciennes scènes).
    /// </summary>
    [ContextMenu("Disconnect")]
    public void Disconnect()
    {
        _manualDisconnectRequested = true;
        _cts?.Cancel();
        _cts = null;

        if (_connectionLoopCoroutine != null)
        {
            StopCoroutine(_connectionLoopCoroutine);
            _connectionLoopCoroutine = null;
        }

        SerialPort portToClose = _serialPort;
        CloseSerialPortAsync(portToClose);
        _serialPort = null;
        CloseBleTransport();
        _lastSysState = "—";
        _pendingScannerEnable = false;
        _scannerEnabled = false;
        _scannerRequested = false;

        EventManager.Instance?.ScannerMissing(true);
        SetState(ConnectionState.Disconnected);
        PublishDeviceReadyStateIfChanged();
        Debug.Log("[ArduinoBridge] Déconnexion manuelle effectuée.");
    }

    /// <summary>Envoie une commande brute (bytes) à l'Arduino.</summary>
    public void SendCommand(byte[] command)
    {
        if (_serialPort == null || !_serialPort.IsOpen)
        {
            Debug.LogWarning("[ArduinoBridge] SendCommand : port non connecté.");
            _lastConnectionError = "Port non connecte";
            return;
        }
        try
        {
            _serialPort.Write(command, 0, command.Length);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ArduinoBridge] Erreur écriture port série : {ex.Message}");
            _lastConnectionError = ex.Message;
        }
    }

    /// <summary>Active le mode lecture NFC (LED verte sur le boîtier).</summary>
    public void ActivateScanner()
    {
        if (_useRbxV2Protocol)
        {
            _scannerRequested = true;
            _pendingScannerEnable = false;
            _scannerEnabled = true;
            UIDisplayManager.instance?.ShowStatus("Scanner actif (v2)");
            EventManager.Instance?.ScannerMissing(false);
            return;
        }

        _scannerRequested = true;
        _pendingScannerEnable = true;
        _scannerEnableRequestedAt = DateTime.UtcNow;
        SendCommand(new byte[] { 0xFF, 0x01 });
    }

    /// <summary>Désactive le mode lecture NFC (LED rouge sur le boîtier).</summary>
    public void DeactivateScanner()
    {
        _scannerRequested = false;
        _pendingScannerEnable = false;
        _scannerEnabled = false;

        if (_useRbxV2Protocol)
        {
            UIDisplayManager.instance?.ShowStatus("Scanner inactif (v2)");
            return;
        }

        SendCommand(new byte[] { 0xFF, 0x00 });
    }

    /// <summary>
    /// Retourne tous les ports série disponibles.
    /// Sur macOS/Linux, SerialPort.GetPortNames() ne retourne que /dev/tty.* —
    /// on y ajoute manuellement /dev/cu.* qui est le bon sens (outgoing) pour Arduino.
    /// Les entrées cu.* sont triées en premier pour être préférées lors du matching.
    /// </summary>
    public string[] GetAvailablePorts()
    {
        var portSet = new System.Collections.Generic.HashSet<string>(
            SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        // /dev/cu.* = call-up device = correct for outgoing connections (Arduino).
        // Mono's GetPortNames() only enumerates tty.* — supplement manually.
        try
        {
            foreach (string f in System.IO.Directory.GetFiles("/dev", "cu.*"))
                portSet.Add(f);
        }
        catch { /* /dev not accessible */ }
#endif

        var list = new System.Collections.Generic.List<string>(portSet);
        // Sort: cu.* before tty.* so keyword matching prefers the outgoing device.
        list.Sort((a, b) =>
        {
            bool aCu = a.IndexOf("/dev/cu.", StringComparison.OrdinalIgnoreCase) == 0;
            bool bCu = b.IndexOf("/dev/cu.", StringComparison.OrdinalIgnoreCase) == 0;
            if (aCu != bCu) return aCu ? -1 : 1;
            return StringComparer.OrdinalIgnoreCase.Compare(a, b);
        });
        return list.ToArray();
    }

    /// <summary>
    /// Sélectionne un port série explicitement pour l'exécution courante (et persistance locale).
    /// </summary>
    public bool SelectRuntimePort(string portName, bool reconnect = true)
    {
        if (settings == null || string.IsNullOrWhiteSpace(portName)) return false;

        string[] ports = GetAvailablePorts();
        bool exists = false;
        foreach (string p in ports)
        {
            if (p.Equals(portName, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }

        if (!exists)
        {
            UIDisplayManager.instance?.ShowStatus($"Port introuvable: {portName}");
            return false;
        }

        _runtimePortOverride = portName.Trim();
        settings.autoDetectPort = false;
        settings.serialPort = _runtimePortOverride;

        PlayerPrefs.SetString(PreferredPortPlayerPrefKey, _runtimePortOverride);
        PlayerPrefs.Save();

        UIDisplayManager.instance?.ShowStatus($"Port selectionne: {_runtimePortOverride}");

        if (reconnect && !settings.debugMode)
        {
            Disconnect();
            Connect();
        }

        return true;
    }

    /// <summary>Réactive l'auto-détection des ports et efface la préférence manuelle.</summary>
    public void EnableAutoDetectMode(bool reconnect = true)
    {
        if (settings == null) return;

        _runtimePortOverride = string.Empty;
        settings.autoDetectPort = true;
        PlayerPrefs.DeleteKey(PreferredPortPlayerPrefKey);
        PlayerPrefs.Save();

        UIDisplayManager.instance?.ShowStatus("Auto-detection port activee");

        if (reconnect && !settings.debugMode)
        {
            Disconnect();
            Connect();
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // UTILITAIRES
    // ═════════════════════════════════════════════════════════════════════════

    private float GetReconnectDelaySeconds()
    {
        if (_consecutiveFailures <= 1) return settings.reconnectDelay;

        float baseDelay = Mathf.Max(0.5f, settings.reconnectDelay);
        float maxDelay = Mathf.Max(baseDelay, settings.reconnectMaxDelay);

        // 1, 2, 4, 8... plafonné
        float multiplier = Mathf.Pow(2f, Mathf.Min(6, _consecutiveFailures - 1));
        return Mathf.Min(maxDelay, baseDelay * multiplier);
    }

    private void CheckPendingScannerEnableTimeout()
    {
        if (!_pendingScannerEnable) return;
        if (State != ConnectionState.Connected) return;
        if (_useRbxV2Protocol) return;
        if (settings != null && settings.bluetoothMode) return;

        double elapsed = (DateTime.UtcNow - _scannerEnableRequestedAt).TotalSeconds;
        // On Bluetooth links, first useful v2 frame can arrive after startup/handshake.
        // Use a larger minimum window to avoid false timeout -> "scanner introuvable".
        float timeoutSeconds = settings.scannerEnableTimeout;
        if (settings != null && settings.bluetoothMode)
            timeoutSeconds = Mathf.Max(timeoutSeconds, 8f);
        if (elapsed < timeoutSeconds) return;

        _pendingScannerEnable = false;
        _scannerEnabled = false;
        _lastErrorCode = 901;
        _lastErrorMessage = "SCANNER_ENABLE_TIMEOUT";
        UIDisplayManager.instance?.ShowStatus("Timeout activation scanner");
        EventManager.Instance?.ScannerMissing(true);
    }

    private void SetState(ConnectionState newState)
    {
        if (State == newState) return;
        State = newState;
        Debug.Log($"[ArduinoBridge] État connexion → {newState}");
        OnConnectionStateChanged?.Invoke(newState);
        ConnectionStateChanged?.Invoke(newState);
        PublishDeviceReadyStateIfChanged();
    }

    private void PublishDeviceReadyStateIfChanged()
    {
        bool ready = IsDeviceReady;
        if (_lastPublishedReadyState == ready) return;

        _lastPublishedReadyState = ready;
        OnDeviceReadyChanged?.Invoke(ready);
    }

    private static void EnsureMainThreadDispatcher()
    {
        if (MainThreadDispatcher.Instance == null)
        {
            new GameObject("[MainThreadDispatcher]")
                .AddComponent<MainThreadDispatcher>();
        }
    }

    private static bool TryExtractRawCardId(string data, out string cardId)
    {
        cardId = null;
        if (!_rawUidRegex.IsMatch(data)) return false;

        string normalized = NormalizeCardId(data);
        if (string.IsNullOrEmpty(normalized) || normalized.Length < 4) return false;

        cardId = normalized;
        return true;
    }

    private static string NormalizeCardId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        string noPrefix = value.Replace("0x", string.Empty)
                               .Replace("0X", string.Empty);
        string compact = Regex.Replace(noPrefix, @"[^0-9A-Za-z]", string.Empty);
        return compact.Trim().ToUpperInvariant();
    }

    // Returns the normalised first colon-delimited token of a card ID.
    // Used to register backward-compat aliases for legacy "id:name:type" format assets.
    // e.g. "001:Greta:Student" → "001",  "001" → "001"
    private static string NormalizeFirstToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        int colon = value.IndexOf(':');
        return NormalizeCardId(colon > 0 ? value.Substring(0, colon) : value);
    }

    private bool TryResolveCard(string normalizedInput, out string resolvedCardId, out Card card)
    {
        resolvedCardId = normalizedInput;
        card = null;

        if (string.IsNullOrEmpty(normalizedInput)) return false;

        // 1) Match exact (chemin principal)
        if (_cardRegistry.TryGetValue(normalizedInput, out card))
        {
            return true;
        }

        // 2) Match par préfixe forward (ex: T001M -> T001)
        // Input starts with a registry key — handles legacy normalised IDs sent by old firmware.
        string bestKey = null;
        foreach (KeyValuePair<string, Card> kvp in _cardRegistry)
        {
            string key = kvp.Key;
            if (!normalizedInput.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                continue;

            if (bestKey == null || key.Length > bestKey.Length)
                bestKey = key;
        }

        if (bestKey != null && _cardRegistry.TryGetValue(bestKey, out card))
        {
            resolvedCardId = bestKey;
            return true;
        }

        // 3) Reverse prefix match: registry key starts with the input.
        // Handles new firmware (UID=001) against old assets whose cardId was saved as
        // "001GRETASTUDENT" (no colon) — i.e. the alias registration path didn't fire.
        // Picks the shortest matching key to prefer the most specific asset.
        string bestReverseKey = null;
        foreach (KeyValuePair<string, Card> kvp in _cardRegistry)
        {
            string key = kvp.Key;
            if (!key.StartsWith(normalizedInput, StringComparison.OrdinalIgnoreCase))
                continue;

            if (bestReverseKey == null || key.Length < bestReverseKey.Length)
                bestReverseKey = key;
        }

        if (bestReverseKey != null && _cardRegistry.TryGetValue(bestReverseKey, out card))
        {
            resolvedCardId = bestReverseKey;
            return true;
        }

        return false;
    }

    private void StartConnectionLoop()
    {
        if (_connectionLoopCoroutine != null) return;
        _connectionLoopCoroutine = StartCoroutine(ConnectionLoop());
    }

    private static void CloseSerialPortAsync(SerialPort port)
    {
        if (port == null) return;
        _ = Task.Run(() =>
        {
            try { port.DiscardInBuffer(); } catch { }
            try { port.DiscardOutBuffer(); } catch { }
            try { port.Close(); } catch { }
            try { port.Dispose(); } catch { }
        });
    }
}
