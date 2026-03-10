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
    public string[] autoDetectKeywords = { "usbserial", "wchusbserial", "usbmodem", "Arduino", "CH340", "CP210", "REDbox" };

    [Header("── Modes ────────────────────────────────────────")]
    [Tooltip("Mode debug : aucune connexion série nécessaire. Permet de simuler des scans via l'Inspector ou le DebugOverlay.")]
    public bool debugMode = false;

    [Tooltip("Connexion via Bluetooth (comportement série identique, port BT à spécifier dans 'serialPort').")]
    public bool bluetoothMode = false;

    [Header("── API de Données ────────────────────────────────")]
    [Tooltip("URL de base de l'API REDbox (sans slash final).")]
    public string webServiceUrl = "https://api.redk.ch";

    [Tooltip("Timeout réseau en secondes pour le chargement des données de cartes.")]
    [Range(3, 30)]
    public int networkTimeout = 10;
}
