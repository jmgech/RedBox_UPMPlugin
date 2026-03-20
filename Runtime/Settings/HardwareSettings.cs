using UnityEngine;

/// <summary>
/// Configuration matérielle du boîtier REDbox.
///
/// COMMENT CRÉER : clic droit dans le Project → Create → RK/Settings → Hardware Settings
/// Assigner l'asset créé dans le champ "Settings" de l'ArduinoBridge dans la scène.
/// </summary>
[CreateAssetMenu(fileName = "HardwareSettings", menuName = "RK/Settings/Hardware Settings")]
public class HardwareSettings : ScriptableObject
{
    public enum ScanStatsMode
    {
        SessionOnly,
        PersistentByContext
    }

    [Header("── Connexion Série ──────────────────────────────")]
    [Tooltip("Port COM à utiliser.\n• Windows : COM3, COM4…\n• Mac : /dev/tty.usbserial-xxxx\n• Linux : /dev/ttyUSB0")]
    public string serialPort = "COM3";

    [Tooltip("Baud rate du port série. Doit être identique à la valeur dans le sketch Arduino.")]
    public int baudRate = 9600;

    [Header("── Reconnexion Automatique ──────────────────────")]
    [Tooltip("Délai (secondes) entre deux tentatives de reconnexion.")]
    [Range(1f, 30f)]
    public float reconnectDelay = 3f;

    [Tooltip("Délai maximal (secondes) utilisé par le backoff exponentiel de reconnexion.")]
    [Range(1f, 30f)]
    public float reconnectMaxDelay = 10f;

    [Tooltip("Intervalle minimal entre deux tentatives de connexion (anti-burst USB), en millisecondes.")]
    [Range(100, 5000)]
    public int reconnectThrottleMs = 500;

    [Tooltip("0 = tentatives infinies. > 0 = abandon après N échecs.")]
    public int maxReconnectAttempts = 0;

    [Header("── Commandes Scanner ─────────────────────────────")]
    [Tooltip("Timeout (secondes) pour recevoir SCANNER_ON après ActivateScanner().")]
    [Range(0.5f, 10f)]
    public float scannerEnableTimeout = 3f;

    [Header("── Auto-détection de Port ──────────────────────")]
    [Tooltip("Si activé, ignore 'serialPort' et cherche automatiquement un port USB/BT correspondant aux mots-clés.")]
    public bool autoDetectPort = false;

    [Tooltip("Mots-clés pour identifier le bon port. L'ordre détermine la priorité.")]
    public string[] autoDetectKeywords = { "cu.usbserial", "cu.wchusbserial", "cu.usbmodem", "usbserial", "wchusbserial", "usbmodem", "Arduino", "CH340", "CP210", "REDbox" };

    [Header("── Modes ────────────────────────────────────────")]
    [Tooltip("Mode debug : aucune connexion série nécessaire. Permet de simuler des scans via l'Inspector ou le DebugOverlay.")]
    public bool debugMode = false;

    [Tooltip("Active automatiquement le scanner dès que le boîtier est prêt (STATE=READY).\nUtile pour les jeux où le scanner doit être actif en permanence. Peut être surchargé à l'exécution.")]
    public bool autoActivateOnStart = false;

    [Tooltip("Désactive le scanner et ferme la connexion proprement quand le jeu s'arrête (OnApplicationQuit / OnDestroy).\nRecommandé pour éviter que le boîtier reste en état actif entre deux sessions.")]
    public bool autoDeactivateOnStop = false;

    [Tooltip("Connexion via Bluetooth (comportement série identique, port BT à spécifier dans 'serialPort').")]
    public bool bluetoothMode = false;

    [Header("── Bluetooth Low Energy (BLE) ───────────────────")]
    [Tooltip("Active la connexion BLE native (CoreBluetooth/WinRT) au lieu du port série Bluetooth classique.")]
    public bool useBleTransport = false;

    [Tooltip("Nom partiel du périphérique BLE à rechercher (ex: REDBOX).")]
    public string bleEndpoint = "REDBOX";

    [Tooltip("Timeout (secondes) pour établir la connexion BLE avant retry.")]
    [Range(2f, 30f)]
    public float bleConnectTimeout = 10f;

    [Header("── API de Données ────────────────────────────────")]
    [Tooltip("URL de base de l'API REDbox (sans slash final).")]
    public string webServiceUrl = "https://api.redk.ch";

    [Tooltip("Timeout réseau en secondes pour le chargement des données de cartes.")]
    [Range(3, 30)]
    public int networkTimeout = 10;

    [Header("── Stats de Scan ─────────────────────────────────")]
    [Tooltip("Mode de conservation des stats de scan: session courante uniquement, ou persistance par contexte.")]
    public ScanStatsMode scanStatsMode = ScanStatsMode.SessionOnly;

    [Tooltip("Contexte logique des stats persistantes (ex: profile_a, partie_1, campaign).")]
    public string scanStatsContextId = "default";

    [Tooltip("Efface les stats persistantes du contexte au démarrage.")]
    public bool resetPersistentScanStatsOnStart = false;
}
