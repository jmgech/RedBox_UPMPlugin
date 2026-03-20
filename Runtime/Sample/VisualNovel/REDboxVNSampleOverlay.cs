using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[AddComponentMenu("REDbox/Sample/Visual Novel Overlay")]
public class REDboxVNSampleOverlay : MonoBehaviour
{
    [Header("Bindings")]
    public REDboxVNSampleController controller;

    [Header("Display")]
    public bool showOverlay = true;

    [Tooltip("When enabled, shows low-level simulation tools intended for development only.")]
    public bool developerMode;

    [Tooltip("Function key used to toggle developer mode at runtime.")]
    public KeyCode toggleDeveloperModeKey = KeyCode.F3;

    [Tooltip("When enabled, the overlay periodically requests scanner activation while connected.")]
    public bool autoRequestScannerWhenConnected = true;

    [Tooltip("Retry interval in seconds for scanner activation requests.")]
    public float scannerRetrySeconds = 2f;

    private GUIStyle _title;
    private GUIStyle _chapter;
    private GUIStyle _speaker;
    private GUIStyle _body;
    private GUIStyle _meta;
    private GUIStyle _button;
    private GUIStyle _buttonGhost;
    private GUIStyle _status;

    private Texture2D _panel;
    private Texture2D _petal;
    private Texture2D _accent;

    private string _simId = "instruction.attack.fireball";
    private string _simType = "INSTRUCTION";
    private RedboxCardType _simTaxonomy = RedboxCardType.Instruction;
    private string _simSubtype = "attack";
    private float _nextScannerRetryAt;

    private void OnEnable()
    {
        if (controller == null)
            controller = FindAnyObjectByType<REDboxVNSampleController>();

        if (controller != null)
            controller.OnStateChanged += OnStateChanged;
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && TryMapKeyCode(toggleDeveloperModeKey, out Key inputKey))
        {
            if (Keyboard.current[inputKey].wasPressedThisFrame)
                developerMode = !developerMode;
        }
#else
        if (Input.GetKeyDown(toggleDeveloperModeKey))
            developerMode = !developerMode;
#endif

    TickScannerActivation();
    }

#if ENABLE_INPUT_SYSTEM
    private static bool TryMapKeyCode(KeyCode keyCode, out Key inputKey)
    {
        switch (keyCode)
        {
            case KeyCode.F1: inputKey = Key.F1; return true;
            case KeyCode.F2: inputKey = Key.F2; return true;
            case KeyCode.F3: inputKey = Key.F3; return true;
            case KeyCode.F4: inputKey = Key.F4; return true;
            case KeyCode.F5: inputKey = Key.F5; return true;
            case KeyCode.F6: inputKey = Key.F6; return true;
            case KeyCode.F7: inputKey = Key.F7; return true;
            case KeyCode.F8: inputKey = Key.F8; return true;
            case KeyCode.F9: inputKey = Key.F9; return true;
            case KeyCode.F10: inputKey = Key.F10; return true;
            case KeyCode.F11: inputKey = Key.F11; return true;
            case KeyCode.F12: inputKey = Key.F12; return true;
            case KeyCode.Escape: inputKey = Key.Escape; return true;
            case KeyCode.Tab: inputKey = Key.Tab; return true;
            case KeyCode.BackQuote: inputKey = Key.Backquote; return true;
            default:
                inputKey = Key.None;
                return false;
        }
    }
#endif

    private void OnDisable()
    {
        if (controller != null)
            controller.OnStateChanged -= OnStateChanged;
    }

    private void OnStateChanged()
    {
        // No-op hook kept to allow future transitions without polling.
    }

    private void OnGUI()
    {
        if (!showOverlay) return;
        if (controller == null)
        {
            GUI.Label(new Rect(20f, 20f, 500f, 24f), "REDboxVNSampleOverlay: missing controller reference.");
            return;
        }

        EnsureGui();

        float w = Mathf.Min(980f, Screen.width - 32f);
        float h = Mathf.Min(620f, Screen.height - 32f);
        float x = (Screen.width - w) * 0.5f;
        float y = (Screen.height - h) * 0.5f;

        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _petal);
        GUI.DrawTexture(new Rect(x, y, w, h), _panel);
        GUI.DrawTexture(new Rect(x, y, w, 6f), _accent);

        float pad = 26f;
        GUILayout.BeginArea(new Rect(x + pad, y + pad, w - pad * 2f, h - pad * 2f));

        var node = controller.CurrentNode;
        string chapter = node != null && !string.IsNullOrWhiteSpace(node.chapter) ? node.chapter : "Prologue";
        string speaker = node != null && !string.IsNullOrWhiteSpace(node.speaker) ? node.speaker : "Narrator";
        string body = node != null ? node.text : "Press Begin to start the REDbox sample visual novel.";
        string hint = node != null ? node.learningHint : string.Empty;

        GUILayout.Label(controller.storyData != null ? controller.storyData.storyTitle : "REDbox VN", _title);
        GUILayout.Label(chapter, _chapter);
        GUILayout.Space(6f);
        GUILayout.Label(speaker, _speaker);
        GUILayout.Space(10f);
        GUILayout.Label(body, _body);

        if (!string.IsNullOrWhiteSpace(hint))
        {
            GUILayout.Space(8f);
            GUILayout.Label($"Learning Goal: {hint}", _meta);
        }

        GUILayout.FlexibleSpace();

        GUILayout.Label($"Status: {controller.StatusLabel}", _status);
        GUILayout.Label($"Last Card: {controller.LastCardDebug}", _meta);
        GUILayout.Space(10f);

        DrawDeviceSection();
        GUILayout.Space(10f);

        if (controller.CanSimulateRecommendedCard())
        {
            string recommended = controller.GetRecommendedCardLabel();
            string actionLabel = developerMode
                ? $"Use Recommended Card ({recommended})"
                : $"Present Story Card ({recommended})";

            if (GUILayout.Button(actionLabel, _buttonGhost, GUILayout.Height(30f)))
                controller.SimulateRecommendedCard();

            string options = controller.GetValidCardOptionsLabel();
            if (!string.IsNullOrWhiteSpace(options))
                GUILayout.Label(options, _meta);

            GUILayout.Space(8f);
        }

        GUILayout.BeginHorizontal();
        if (!controller.StoryStarted)
        {
            if (GUILayout.Button("Begin Story", _button, GUILayout.Height(36f)))
                controller.StartStory();
        }
        else
        {
            if (GUILayout.Button("Next", _button, GUILayout.Height(36f)))
                controller.Advance();

            if (GUILayout.Button("Restart", _buttonGhost, GUILayout.Height(36f)))
                controller.RestartStory();
        }
        GUILayout.EndHorizontal();

        if (node != null && node.HasChoices)
        {
            GUILayout.Space(12f);
            GUILayout.Label("Choices", _chapter);
            for (int i = 0; i < node.choices.Length; i++)
            {
                var choice = node.choices[i];
                string label = string.IsNullOrWhiteSpace(choice.label) ? $"Choice {i + 1}" : choice.label;
                if (!choice.requiresCard)
                {
                    if (GUILayout.Button(label, _buttonGhost, GUILayout.Height(30f)))
                        controller.SelectChoice(i);
                }
                else
                {
                    GUILayout.Label($"{label} (card required)", _meta);
                    if (!string.IsNullOrWhiteSpace(choice.learningHint))
                        GUILayout.Label($"  {choice.learningHint}", _meta);
                }
            }
        }

        GUILayout.Space(8f);
        GUILayout.Label($"Mode: {(developerMode ? "Developer" : "Player")}", _meta);
        GUILayout.Label($"Press {toggleDeveloperModeKey} to toggle developer tools.", _meta);

        if (developerMode)
        {
            GUILayout.Space(12f);
            GUILayout.Label("Developer Simulation Tools", _chapter);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Sim Lore/Memory", _buttonGhost, GUILayout.Height(28f)))
                SimulatePreset("lore.memory.founders", "LORE", RedboxCardType.Lore, "memory");
            if (GUILayout.Button("Sim World/Location", _buttonGhost, GUILayout.Height(28f)))
                SimulatePreset("world.location.temple", "WORLD", RedboxCardType.World, "location");
            if (GUILayout.Button("Sim Actor/Ally", _buttonGhost, GUILayout.Height(28f)))
                SimulatePreset("actor.ally.hikari", "ACTOR", RedboxCardType.Actor, "ally");
            if (GUILayout.Button("Sim Instruction", _buttonGhost, GUILayout.Height(28f)))
                SimulatePreset("instruction.attack.fireball", "INSTRUCTION", RedboxCardType.Instruction, "attack");
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("ID", _meta, GUILayout.Width(20f));
            _simId = GUILayout.TextField(_simId);
            GUILayout.Label("Type", _meta, GUILayout.Width(34f));
            _simType = GUILayout.TextField(_simType, GUILayout.Width(140f));
            GUILayout.Label("Subtype", _meta, GUILayout.Width(54f));
            _simSubtype = GUILayout.TextField(_simSubtype, GUILayout.Width(140f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Taxonomy", _meta, GUILayout.Width(66f));
            _simTaxonomy = (RedboxCardType)GUILayout.SelectionGrid((int)_simTaxonomy, new[] { "Unknown", "Actor", "Instruction", "Modifier", "Lore", "Cosmetic", "World", "System" }, 4);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Simulate Custom Card", _buttonGhost, GUILayout.Height(28f)))
                SimulatePreset(_simId, _simType, _simTaxonomy, _simSubtype);
        }

        GUILayout.EndArea();
    }

    private void SimulatePreset(string cardId, string legacyType, RedboxCardType taxonomy, string subtype)
    {
        var tagData = new CardTagData
        {
            Id = cardId,
            CardId = cardId,
            Type = legacyType,
            TaxonomyType = taxonomy,
            Subtype = subtype,
            Name = cardId,
        };

        // Route directly to the sample controller so in-editor VN testing stays deterministic
        // regardless of hardware bridge state.
        controller.SimulateTag(tagData);
    }

    private void DrawDeviceSection()
    {
        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (bridge == null)
        {
            GUILayout.Label("Device: ArduinoBridge not found in scene.", _meta);
            return;
        }

        string scanner = bridge.ScannerEnabled ? "ON" : (bridge.PendingScannerEnable ? "PENDING" : "OFF");
        GUILayout.Label($"Device: {bridge.State} | Port: {bridge.ActivePort} | Scanner: {scanner}", _meta);

        if (bridge.settings == null)
            GUILayout.Label("HardwareSettings missing on ArduinoBridge.", _meta);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Connect Device", _buttonGhost, GUILayout.Height(28f)))
            bridge.Connect();

        if (GUILayout.Button("Activate Scanner", _buttonGhost, GUILayout.Height(28f)))
            bridge.ActivateScanner();

        if (GUILayout.Button("Reconnect", _buttonGhost, GUILayout.Height(28f)))
        {
            bridge.Disconnect();
            bridge.Connect();
        }
        GUILayout.EndHorizontal();
    }

    private void TickScannerActivation()
    {
        if (!autoRequestScannerWhenConnected)
            return;

        ArduinoBridge bridge = ArduinoBridge.Instance;
        if (bridge == null)
            return;

        if (bridge.State != ArduinoBridge.ConnectionState.Connected)
            return;

        if (bridge.ScannerEnabled || bridge.PendingScannerEnable)
            return;

        if (Time.unscaledTime < _nextScannerRetryAt)
            return;

        bridge.ActivateScanner();
        _nextScannerRetryAt = Time.unscaledTime + Mathf.Max(0.5f, scannerRetrySeconds);
    }

    private void EnsureGui()
    {
        if (_title != null) return;

        _panel = MakeTex(new Color(0.07f, 0.05f, 0.08f, 0.92f));
        _petal = MakeTex(new Color(0.1f, 0.08f, 0.12f, 0.95f));
        _accent = MakeTex(new Color(0.92f, 0.24f, 0.34f, 1f));

        _title = new GUIStyle(GUI.skin.label)
        {
            fontSize = 28,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.95f, 0.9f, 0.84f) }
        };

        _chapter = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.89f, 0.49f, 0.47f) }
        };

        _speaker = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.98f, 0.87f, 0.75f) }
        };

        _body = new GUIStyle(GUI.skin.label)
        {
            wordWrap = true,
            fontSize = 18,
            normal = { textColor = new Color(0.95f, 0.95f, 0.95f) }
        };

        _meta = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = { textColor = new Color(0.75f, 0.75f, 0.77f) }
        };

        _status = new GUIStyle(_meta)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.8f, 0.95f, 0.8f) }
        };

        _button = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            normal =
            {
                textColor = Color.white,
                background = MakeTex(new Color(0.72f, 0.18f, 0.27f, 1f))
            },
            active =
            {
                textColor = Color.white,
                background = MakeTex(new Color(0.58f, 0.14f, 0.21f, 1f))
            }
        };

        _buttonGhost = new GUIStyle(_button)
        {
            normal =
            {
                textColor = new Color(0.95f, 0.95f, 0.95f),
                background = MakeTex(new Color(0.2f, 0.2f, 0.25f, 1f))
            },
            active =
            {
                textColor = Color.white,
                background = MakeTex(new Color(0.25f, 0.25f, 0.31f, 1f))
            }
        };
    }

    private static Texture2D MakeTex(Color color)
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }
}
