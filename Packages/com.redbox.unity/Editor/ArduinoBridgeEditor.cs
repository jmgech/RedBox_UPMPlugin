#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO.Ports;

/// <summary>
/// Inspector étendu pour ArduinoBridge.
///
/// Ajoute un panneau "🎮 Simulateur de Scan" en bas de l'Inspector :
///   - Champ texte pour entrer un Card ID manuellement
///   - Boutons rapides pour chaque carte enregistrée
///   - Statut de connexion en temps réel
///   - Log des données brutes reçues
///
/// Fonctionne uniquement en Play Mode.
/// Ne nécessite PAS de boîtier physique.
/// </summary>
[CustomEditor(typeof(ArduinoBridge))]
public class ArduinoBridgeEditor : Editor
{
    private string _simulateCardId = "";
    private Vector2 _scrollPos;

    // Couleurs d'état
    private static readonly Color ColorConnected    = new Color(0.2f, 0.85f, 0.2f);
    private static readonly Color ColorDisconnected = new Color(0.9f, 0.2f, 0.2f);
    private static readonly Color ColorConnecting   = new Color(0.9f, 0.7f, 0.1f);
    private static readonly Color ColorReconnecting = new Color(0.9f, 0.5f, 0.1f);

    public override void OnInspectorGUI()
    {
        // Dessine l'Inspector par défaut en premier
        DrawDefaultInspector();

        ArduinoBridge bridge = (ArduinoBridge)target;

        EditorGUILayout.Space(10);
        DrawHorizontalLine();

        // ── État de connexion ─────────────────────────────────────────────
        DrawConnectionStatus(bridge);

        EditorGUILayout.Space(6);

        // ── Simulateur (Play Mode uniquement) ─────────────────────────────
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "▶ Lance le jeu (Play Mode) pour utiliser le simulateur de scan.",
                MessageType.Info);
        }

        if (Application.isPlaying)
        {
            DrawSimulatorPanel(bridge);
            DrawRegistryPanel(bridge);
        }

        DrawCommandsPanel(bridge);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PANNEAU ÉTAT DE CONNEXION
    // ═════════════════════════════════════════════════════════════════════════

    private void DrawConnectionStatus(ArduinoBridge bridge)
    {
        EditorGUILayout.LabelField("📡 État de connexion", EditorStyles.boldLabel);

        Color stateColor = bridge.State switch
        {
            ArduinoBridge.ConnectionState.Connected    => ColorConnected,
            ArduinoBridge.ConnectionState.Disconnected => ColorDisconnected,
            ArduinoBridge.ConnectionState.Connecting   => ColorConnecting,
            ArduinoBridge.ConnectionState.Reconnecting => ColorReconnecting,
            _ => Color.gray
        };

        string stateEmoji = bridge.State switch
        {
            ArduinoBridge.ConnectionState.Connected    => "✓",
            ArduinoBridge.ConnectionState.Disconnected => "✗",
            ArduinoBridge.ConnectionState.Connecting   => "…",
            ArduinoBridge.ConnectionState.Reconnecting => "↻",
            _ => "?"
        };

        var prevColor = GUI.color;
        GUI.color = stateColor;

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label($"{stateEmoji} {bridge.State}", EditorStyles.boldLabel);
        EditorGUILayout.EndHorizontal();

        GUI.color = prevColor;

        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField($"Port actif : {bridge.ActivePort}",
                                       EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Cartes chargées : {bridge.CardRegistryCount}",
                                       EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Dernière trame brute : {bridge.LastRawData}",
                                       EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Dernière erreur connexion : {bridge.LastConnectionError}",
                                       EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Firmware device : {bridge.DeviceFirmwareVersion}",
                                       EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"État SYS : {bridge.LastSysState}",
                                       EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Dernier I2C : {bridge.LastI2cReport}",
                                       EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Dernier TAGUID : {bridge.LastTagUid}",
                                       EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Source carte : {bridge.LastCardSource}",
                                       EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Dernière erreur protocole : {bridge.LastErrorCode} / {bridge.LastErrorMessage}",
                                       EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Scanner demandé/actif/pending : {bridge.ScannerRequested}/{bridge.ScannerEnabled}/{bridge.PendingScannerEnable}",
                                       EditorStyles.miniLabel);

            if (bridge.LastScannedCardId != null)
            {
                EditorGUILayout.LabelField(
                    $"Dernier scan : {bridge.LastScannedCardId} @ {bridge.LastScanTime:HH:mm:ss}",
                    EditorStyles.miniLabel);
            }

            // Force le repaint pour que le statut se rafraîchisse
            Repaint();
        }

        if (bridge.settings != null)
        {
            EditorGUILayout.LabelField($"Config port : {bridge.settings.serialPort}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Auto-détection : {(bridge.settings.autoDetectPort ? "ON" : "OFF")}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Baud rate : {bridge.settings.baudRate}", EditorStyles.miniLabel);
            if (bridge.settings.debugMode)
            {
                EditorGUILayout.HelpBox("DEBUG MODE actif: la connexion série réelle est désactivée.", MessageType.Warning);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("HardwareSettings non assigné sur ArduinoBridge.", MessageType.Error);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PANNEAU SIMULATEUR
    // ═════════════════════════════════════════════════════════════════════════

    private void DrawSimulatorPanel(ArduinoBridge bridge)
    {
        EditorGUILayout.LabelField("🎮 Simulateur de Scan", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.BeginHorizontal();
        _simulateCardId = EditorGUILayout.TextField("Card ID", _simulateCardId);

        GUI.enabled = !string.IsNullOrWhiteSpace(_simulateCardId);
        if (GUILayout.Button("Simuler", GUILayout.Width(80)))
        {
            bridge.SimulateScan(_simulateCardId.Trim());
        }
        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox(
            "Simule le scan d'une carte NFC sans boîtier physique.",
            MessageType.None);

        EditorGUILayout.EndVertical();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PANNEAU REGISTRE DE CARTES
    // ═════════════════════════════════════════════════════════════════════════

    private void DrawRegistryPanel(ArduinoBridge bridge)
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("🃏 Cartes enregistrées (cliquer pour simuler)",
                                    EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        if (bridge.cardDataArray == null || bridge.cardDataArray.Length == 0)
        {
            EditorGUILayout.HelpBox("Aucune carte dans cardDataArray.", MessageType.Warning);
        }
        else
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos,
                                                          GUILayout.MaxHeight(150));
            foreach (Card card in bridge.cardDataArray)
            {
                if (card == null) continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[{card.cardId}]",
                                            GUILayout.Width(90));
                EditorGUILayout.LabelField(card.cardName,
                                            GUILayout.ExpandWidth(true));
                EditorGUILayout.LabelField(card.cardType,
                                            EditorStyles.miniLabel,
                                            GUILayout.Width(70));

                if (GUILayout.Button("▶", GUILayout.Width(28)))
                {
                    bridge.SimulateScan(card.cardId);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.EndVertical();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PANNEAU COMMANDES ARDUINO
    // ═════════════════════════════════════════════════════════════════════════

    private void DrawCommandsPanel(ArduinoBridge bridge)
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("⚙ Commandes Arduino", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Les boutons de connexion sont disponibles en Play Mode.",
                MessageType.None);
        }

        bool isConnected = bridge.State == ArduinoBridge.ConnectionState.Connected;
        bool canConnect = bridge.State == ArduinoBridge.ConnectionState.Disconnected;

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = Application.isPlaying && canConnect;
        if (GUILayout.Button("🔌 Connect"))
            bridge.Connect();

        GUI.enabled = Application.isPlaying && !canConnect;
        if (GUILayout.Button("⏹ Disconnect"))
            bridge.Disconnect();
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();

        GUI.enabled = Application.isPlaying && isConnected;
        if (GUILayout.Button("🟢 Activer Scanner"))
            bridge.ActivateScanner();

        if (GUILayout.Button("🔴 Désactiver Scanner"))
            bridge.DeactivateScanner();
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        GUI.enabled = Application.isPlaying;
        if (GUILayout.Button("📋 Lister les ports série"))
        {
            string[] ports = SerialPort.GetPortNames();
            if (ports == null || ports.Length == 0)
            {
                Debug.LogWarning("[ArduinoBridgeEditor] Aucun port série détecté.");
            }
            else
            {
                Debug.Log($"[ArduinoBridgeEditor] Ports détectés : {string.Join(", ", ports)}");
            }
        }
        GUI.enabled = true;

        EditorGUILayout.EndVertical();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // UTILITAIRES
    // ═════════════════════════════════════════════════════════════════════════

    private static void DrawHorizontalLine()
    {
        var rect = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        EditorGUILayout.Space(2);
    }
}
#endif
