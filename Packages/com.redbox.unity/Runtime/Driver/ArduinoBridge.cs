using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

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
public class ArduinoBridge : MonoBehaviour
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

    // ─── Privé ────────────────────────────────────────────────────────────────
    private SerialPort            _serialPort;
    private CancellationTokenSource _cts;
    private Dictionary<string, Card> _cardRegistry;
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
    private bool                  _scannerRequested;
    private bool                  _scannerEnabled;
    private bool                  _pendingScannerEnable;
    private DateTime              _scannerEnableRequestedAt;
    private DateTime              _lastConnectAttemptTime = DateTime.MinValue;
    private int                   _consecutiveFailures;
    private string                _runtimePortOverride;
    private DateTime              _lastScanTime;

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
    public string ActivePort         => _serialPort?.PortName ?? "—";
    public int LastErrorCode         => _lastErrorCode;
    public string LastErrorMessage   => _lastErrorMessage;
    public string LastSysState       => _lastSysState;
    public string DeviceFirmwareVersion => _deviceFirmwareVersion;
    public string LastI2cReport      => _lastI2cReport;
    public string LastTagUid         => _lastTagUid;
    public string LastCardSource     => _lastCardSource;
    public bool ScannerRequested     => _scannerRequested;
    public bool ScannerEnabled       => _scannerEnabled;
    public bool PendingScannerEnable => _pendingScannerEnable;
    public bool IsDeviceReady        => State == ConnectionState.Connected
                                     && string.Equals(_lastSysState, "READY", StringComparison.OrdinalIgnoreCase);
    public string ConfiguredPort     => !string.IsNullOrWhiteSpace(_runtimePortOverride)
        ? _runtimePortOverride
        : (settings != null ? settings.serialPort : "—");

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
        if (_serialPort != null && _serialPort.IsOpen && _scannerEnabled)
        {
            try { _serialPort.Write(new byte[] { 0xFF, 0x00 }, 0, 2); }
            catch { /* best-effort — port may already be closing */ }
        }

        _cts?.Cancel();
        _cts = null;
        if (_connectionLoopCoroutine != null)
        {
            StopCoroutine(_connectionLoopCoroutine);
            _connectionLoopCoroutine = null;
        }
        try { _serialPort?.Close(); } catch { /* déjà fermé */ }
        _serialPort = null;
        _lastSysState = "—";
        _pendingScannerEnable = false;
        _scannerEnabled = false;
        PublishDeviceReadyStateIfChanged();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // REGISTRE DE CARTES
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Construit un dictionnaire cardId → Card pour une recherche O(1).
    /// Bien plus performant qu'une boucle foreach sur tableau.
    /// </summary>
    private void BuildCardRegistry()
    {
        _cardRegistry = new Dictionary<string, Card>(StringComparer.OrdinalIgnoreCase);

        if (cardDataArray == null || cardDataArray.Length == 0)
        {
            Debug.LogWarning("[ArduinoBridge] Aucune carte assignée dans cardDataArray.");
            return;
        }

        foreach (Card card in cardDataArray)
        {
            if (card == null) continue;

            string normalizedId = NormalizeCardId(card.cardId);
            if (string.IsNullOrEmpty(normalizedId))
            {
                Debug.LogWarning($"[ArduinoBridge] Card ignorée (ID invalide): '{card.cardName}'");
                continue;
            }

            if (_cardRegistry.ContainsKey(normalizedId))
            {
                Debug.LogWarning($"[ArduinoBridge] ID en doublon ignoré : '{normalizedId}' ({card.cardName})");
                continue;
            }

            _cardRegistry.Add(normalizedId, card);
        }

        Debug.Log($"[ArduinoBridge] Registre : {_cardRegistry.Count} carte(s) chargée(s).");
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

                string port = ResolvePort();
                if (port == null)
                {
                    string available = string.Join(", ", GetAvailablePorts());
                    Debug.LogWarning($"[ArduinoBridge] Aucun port trouvé. " +
                                     $"Ports disponibles : [{available}]. " +
                                     $"Nouvelle tentative dans {settings.reconnectDelay}s.");
                    EventManager.Instance.ScannerMissing(true);
                    SetState(ConnectionState.Disconnected);
                    yield return new WaitForSeconds(settings.reconnectDelay);
                    continue;
                }

                bool opened = TryOpenPort(port);

                if (!opened)
                {
                    _reconnectAttempts++;
                    _consecutiveFailures++;
                    SetState(ConnectionState.Reconnecting);

                    if (settings.maxReconnectAttempts > 0 &&
                        _reconnectAttempts >= settings.maxReconnectAttempts)
                    {
                        Debug.LogError($"[ArduinoBridge] {_reconnectAttempts} tentatives échouées. Abandon.");
                        EventManager.Instance.ScannerMissing(true);
                        yield break;
                    }

                    yield return new WaitForSeconds(GetReconnectDelaySeconds());
                    continue;
                }

                // ── Connexion établie ─────────────────────────────────────────
                _reconnectAttempts = 0;
                _consecutiveFailures = 0;
                SetState(ConnectionState.Connected);
                EventManager.Instance.ScannerMissing(false);

                Debug.Log($"[ArduinoBridge] ✓ Connecté sur {port} @ {settings.baudRate} baud" +
                          (settings.bluetoothMode ? " [Bluetooth]" : " [USB]"));

                // Lancer la lecture série en background
                _cts = new CancellationTokenSource();
                _ = ReadSerialAsync(_cts.Token);

                // Attendre la déconnexion (ReadSerialAsync changera l'état quand ça plante)
                while (State == ConnectionState.Connected)
                {
                    CheckPendingScannerEnableTimeout();
                    yield return null;
                }

                // ── Déconnexion détectée ──────────────────────────────────────
                Shutdown();

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

        // Runtime-selected port has top priority if still present.
        if (!string.IsNullOrWhiteSpace(_runtimePortOverride))
        {
            foreach (string port in available)
            {
                if (port.Equals(_runtimePortOverride, StringComparison.OrdinalIgnoreCase))
                    return port;
            }
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
            foreach (string port in available)
            {
                if (port.Equals(settings.serialPort, StringComparison.OrdinalIgnoreCase))
                    return port;
            }
        }

        return null;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // OUVERTURE / FERMETURE PORT
    // ═════════════════════════════════════════════════════════════════════════

    private bool TryOpenPort(string portName)
    {
        try
        {
            _serialPort = new SerialPort(portName, settings.baudRate)
            {
                ReadTimeout  = 5000,
                WriteTimeout = 2000,
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

    // ═════════════════════════════════════════════════════════════════════════
    // LECTURE SÉRIE ASYNCHRONE (thread background)
    // ═════════════════════════════════════════════════════════════════════════

    private async Task ReadSerialAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && _serialPort != null && _serialPort.IsOpen)
            {
                // ReadLine bloque jusqu'à réception d'une ligne — thread background OK
                string line = await Task.Run(() =>
                {
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
        string ev = GetField(fields, "EV", "TAP").ToUpperInvariant();
        string uid = NormalizeCardId(GetField(fields, "UID", string.Empty));
        string tagUid = NormalizeCardId(GetField(fields, "TAGUID", string.Empty));
        string src = GetField(fields, "SRC", string.Empty).ToUpperInvariant();

        if (string.IsNullOrEmpty(uid))
        {
            // Certains firmwares peuvent envoyer DATA au lieu de UID en phase de transition.
            uid = NormalizeCardId(GetField(fields, "DATA", string.Empty));
        }

        if (string.IsNullOrEmpty(uid)) return false;

        if (!string.IsNullOrEmpty(tagUid)) _lastTagUid = tagUid;
        if (!string.IsNullOrEmpty(src)) _lastCardSource = src;

        switch (ev)
        {
            case "TAP":
            case "ENTER":
                HandleCardScan(uid);
                return true;

            case "PRESENT":
                // Heartbeat de présence: pas de retrigger gameplay par défaut.
                return true;

            case "EXIT":
                UIDisplayManager.instance?.ClearAll();
                UIDisplayManager.instance?.ShowTemporaryStatus($"Carte retirée: {uid}", 1.5f);
                return true;

            default:
                HandleCardScan(uid);
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

    private void HandleCardScan(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            Debug.LogWarning("[ArduinoBridge] Card ID vide reçu.");
            return;
        }

        string normalizedInput = NormalizeCardId(cardId);
        _lastScannedId = normalizedInput;
        _lastScanTime  = DateTime.Now;

        if (!TryResolveCard(normalizedInput, out string resolvedCardId, out Card card))
        {
            Debug.LogWarning($"[ArduinoBridge] Carte inconnue scannée : '{normalizedInput}'");
            UIDisplayManager.instance?.ShowStatus($"Carte inconnue : {normalizedInput}");
            return;
        }

        if (!resolvedCardId.Equals(normalizedInput, StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"[ArduinoBridge] Alias ID résolu : '{normalizedInput}' -> '{resolvedCardId}'");
            _lastScannedId = resolvedCardId;
        }

        Debug.Log($"[ArduinoBridge] ✓ Carte : {card.cardName} (ID: {card.cardId})");

        // Déclenche l'événement global (EventActivator l'écoute)
        EventManager.Instance.CardScanned(card, true);

        // Met à jour l'UI
        UIDisplayManager.instance?.ShowCard(card);
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
    /// Simule le scan d'une carte par son ID.
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

        StartConnectionLoop();
    }

    /// <summary>
    /// Coupe la connexion série et stoppe la boucle de reconnexion.
    /// Exposé pour les boutons UI UnityEvent (compatibilité avec anciennes scènes).
    /// </summary>
    [ContextMenu("Disconnect")]
    public void Disconnect()
    {
        _cts?.Cancel();
        _cts = null;

        if (_connectionLoopCoroutine != null)
        {
            StopCoroutine(_connectionLoopCoroutine);
            _connectionLoopCoroutine = null;
        }

        try { _serialPort?.Close(); } catch { /* déjà fermé */ }
        _serialPort = null;
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

        double elapsed = (DateTime.UtcNow - _scannerEnableRequestedAt).TotalSeconds;
        if (elapsed < settings.scannerEnableTimeout) return;

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

        // 2) Match par préfixe (ex: T001M -> T001)
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

        return false;
    }

    private void StartConnectionLoop()
    {
        if (_connectionLoopCoroutine != null) return;
        _connectionLoopCoroutine = StartCoroutine(ConnectionLoop());
    }
}
