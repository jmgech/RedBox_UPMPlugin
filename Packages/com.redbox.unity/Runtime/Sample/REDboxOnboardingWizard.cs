using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Four-screen onboarding and live-monitor wizard for REDbox.
/// Renders via OnGUI — no Canvas, no TextMeshPro, no prefab required.
///
/// Screen 0  Welcome        — what REDbox is and what it does
/// Screen 1  Hardware       — how to wire Arduino + PN532
/// Screen 2  Configuration  — how to set up HardwareSettings
/// Screen 3  Live Monitor   — real-time connection, last scan, serial log
///
/// Toggle visibility: Escape key, or uncheck showWizard in the Inspector.
/// Add the component to any GameObject; it self-subscribes to EventManager.
/// </summary>
[AddComponentMenu("REDbox/Onboarding Wizard")]
public class REDboxOnboardingWizard : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Wizard")]
    [Tooltip("Show the wizard overlay. Uncheck to hide, or press Escape.")]
    public bool showWizard = true;

    // ── Screen ────────────────────────────────────────────────────────────────
    private int           _screen;
    private const int     kScreenCount = 4;
    private string        _simId       = string.Empty;

    // ── Live data (screen 3) ─────────────────────────────────────────────────
    private string _cardName = "—";
    private string _cardId   = "—";
    private string _cardType = "—";
    private int    _cardHp,  _cardMp, _cardAt;
    private string _scanTime   = "—";
    private string _connLabel  = "○  Disconnected";
    private string _connPort   = string.Empty;
    private bool   _connected;
    private bool   _emFound;
    private readonly Queue<string> _log = new Queue<string>();
    private const int kMaxLog = 6;

    // ── Geometry ─────────────────────────────────────────────────────────────
    private const float PW     = 700f;
    private const float PH     = 540f;
    private const float PadH   = 44f;
    private const float PadV   = 28f;
    private const float NavH   = 52f;
    private const float AccH   = 4f;

    // ── Cached textures ───────────────────────────────────────────────────────
    private Texture2D   _txDim;
    private Texture2D   _txPanel;
    private Texture2D   _txAccent;
    private Texture2D   _txCard;
    private Texture2D   _txBtnRed;
    private Texture2D   _txBtnGhost;
    private Texture2D[] _txDots;   // [0] inactive  [1] active

    // ── Cached styles ─────────────────────────────────────────────────────────
    private bool      _stylesReady;
    private GUIStyle  _stBig;
    private GUIStyle  _stSub;
    private GUIStyle  _stBody;
    private GUIStyle  _stMeta;
    private GUIStyle  _stMono;
    private GUIStyle  _stBtnRed;
    private GUIStyle  _stBtnGhost;
    private GUIStyle  _stBtnLink;
    private GUIStyle  _stBox;
    private GUIStyle  _stConnOn;
    private GUIStyle  _stConnOff;
    private GUIStyle  _stField;

    // ═════════════════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ═════════════════════════════════════════════════════════════════════════

    private void Start()
    {
        EventManager em = EventManager.Instance ?? FindAnyObjectByType<EventManager>();
        _emFound = em != null;
        if (_emFound)
            em.OnCardScanned.AddListener(OnCardScanned);

        ArduinoBridge.OnConnectionStateChanged += OnStateChanged;
        ArduinoBridge.OnRawDataReceived        += OnRawLine;

        var b = ArduinoBridge.Instance;
        if (b != null) OnStateChanged(b.State);
    }

    private void OnDestroy()
    {
        EventManager em = EventManager.Instance;
        if (em != null)
            em.OnCardScanned.RemoveListener(OnCardScanned);

        ArduinoBridge.OnConnectionStateChanged -= OnStateChanged;
        ArduinoBridge.OnRawDataReceived        -= OnRawLine;
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            showWizard = !showWizard;
#else
        if (Input.GetKeyDown(KeyCode.Escape))
            showWizard = !showWizard;
#endif
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnCardScanned(Card card, bool isScanned)
    {
        if (!isScanned || card == null) return;
        _cardName = card.cardName ?? "—";
        _cardId   = card.cardId   ?? "—";
        _cardType = card.cardType ?? "—";
        _cardHp   = card.hp;
        _cardMp   = card.mp;
        _cardAt   = card.at;
        _scanTime = System.DateTime.Now.ToString("HH:mm:ss");
        _screen   = 3; // jump to live monitor on first scan
    }

    private void OnStateChanged(ArduinoBridge.ConnectionState state)
    {
        _connected = state == ArduinoBridge.ConnectionState.Connected;
        _connPort  = ArduinoBridge.Instance?.ActivePort ?? string.Empty;
        _connLabel = state switch
        {
            ArduinoBridge.ConnectionState.Connected    => "●  Connected",
            ArduinoBridge.ConnectionState.Connecting   => "◌  Connecting…",
            ArduinoBridge.ConnectionState.Reconnecting => "↻  Reconnecting…",
            _                                          => "○  Disconnected",
        };
    }

    private void OnRawLine(string line)
    {
        _log.Enqueue(line);
        while (_log.Count > kMaxLog) _log.Dequeue();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GUI — ENTRY
    // ═════════════════════════════════════════════════════════════════════════

    private void OnGUI()
    {
        if (!showWizard) return;
        InitStyles();

        float sw = Screen.width;
        float sh = Screen.height;
        float pw = Mathf.Min(PW, sw - 32f);
        float ph = Mathf.Min(PH, sh - 32f);
        float px = Mathf.Round((sw - pw) * 0.5f);
        float py = Mathf.Round((sh - ph) * 0.5f);

        // Background dim
        GUI.DrawTexture(new Rect(0, 0, sw, sh), _txDim);

        // Panel
        GUI.DrawTexture(new Rect(px, py, pw, ph), _txPanel);

        // Red accent bar at top
        GUI.DrawTexture(new Rect(px, py, pw, AccH), _txAccent);

        // Content area
        float cx = px + PadH;
        float cy = py + AccH + PadV;
        float cw = pw - PadH * 2f;
        float ch = ph - AccH - PadV - NavH;

        GUILayout.BeginArea(new Rect(cx, cy, cw, ch));
        switch (_screen)
        {
            case 0: ScreenWelcome(cw); break;
            case 1: ScreenWiring(cw);  break;
            case 2: ScreenSettings();  break;
            case 3: ScreenMonitor();   break;
        }
        GUILayout.EndArea();

        NavBar(px, py, pw, ph);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SCREEN 0 — WELCOME
    // ═════════════════════════════════════════════════════════════════════════

    private void ScreenWelcome(float cw)
    {
        GUILayout.Space(8f);
        GUILayout.Label("REDbox", _stBig);
        GUILayout.Label("Arduino NFC Bridge for Unity", _stSub);
        GUILayout.Space(12f);
        HRule(cw);
        GUILayout.Space(18f);
        GUILayout.Label(
            "Connect physical NFC card readers to your Unity project.\n" +
            "Tap a card — an event fires. That's it.",
            _stBody);
        GUILayout.Space(20f);

        // Feature pill row
        GUILayout.BeginHorizontal();
        Pill("Zero Config",    "Auto-detects port\non Mac and Windows", cw);
        GUILayout.Space(8f);
        Pill("Event-Driven",   "UnityEvent<Card>\nno polling required", cw);
        GUILayout.Space(8f);
        Pill("Debug Mode",     "Simulate scans\nwithout hardware",      cw);
        GUILayout.EndHorizontal();

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Get Started  ›", _stBtnRed, GUILayout.Height(46f)))
            _screen = 1;
        GUILayout.Space(6f);
        if (GUILayout.Button("Dismiss  (press Escape to toggle)", _stBtnLink, GUILayout.Height(22f)))
            showWizard = false;
        GUILayout.Space(4f);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SCREEN 1 — HARDWARE WIRING
    // ═════════════════════════════════════════════════════════════════════════

    private void ScreenWiring(float cw)
    {
        StepHeader("Step 1 of 3", "Connect Your Hardware", cw);

        BeginCard();
        GUILayout.Label(
            "  [ PN532 NFC Module ]\n" +
            "         |\n" +
            "  [ Arduino Uno / Mega ]  ───── USB ─────  [ Mac / Windows PC ]",
            _stMono);
        EndCard();

        GUILayout.Space(10f);
        GUILayout.Label("1.  Stack the PN532 NFC shield on your Arduino.", _stBody);
        GUILayout.Label("2.  Flash the REDbox firmware via Arduino IDE.", _stBody);
        GUILayout.Label("3.  Open the Serial Monitor at 9600 baud — you should see:", _stBody);
        GUILayout.Space(4f);
        BeginCard();
        GUILayout.Label("  V1|SYS|STATE=READY", _stMono);
        EndCard();
        GUILayout.Space(10f);
        BeginCard();
        GUILayout.Label("Firmware:  REDbox_Sketch / NFC_WRITER_READER / src / main.cpp", _stMeta);
        EndCard();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SCREEN 2 — HARDWARE SETTINGS
    // ═════════════════════════════════════════════════════════════════════════

    private void ScreenSettings()
    {
        StepHeader("Step 2 of 3", "Configure HardwareSettings", 0f);

        GUILayout.Label(
            "Assign a HardwareSettings ScriptableObject to the ArduinoBridge component.\n" +
            "Create one from the Project window:",
            _stBody);
        GUILayout.Space(8f);
        BeginCard();
        GUILayout.Label("Right-click in Project  →  Create  →  RK / Settings / Hardware Settings", _stMeta);
        EndCard();
        GUILayout.Space(12f);

        Row("Auto Detect Port", "✓  Recommended — finds the right port automatically");
        Row("Baud Rate",        "9600  (must match the Arduino sketch)");
        Row("Mac port",         "/dev/cu.usbserial-XXXX  or  /dev/cu.wchusbserial-XXXX");
        Row("Windows port",     "COM3,  COM4  — check Device Manager");
        Row("Debug Mode",       "Test without hardware using the Simulate field");
        GUILayout.Space(12f);

        BeginCard();
        GUILayout.Label(
            "This scene ships with a pre-configured HardwareSettings asset.\n" +
            "Debug Mode is ON — use the Simulate field on the next screen to try it now.",
            _stMeta);
        EndCard();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SCREEN 3 — LIVE MONITOR
    // ═════════════════════════════════════════════════════════════════════════

    private void ScreenMonitor()
    {
        // Header: title + connection pill
        GUILayout.BeginHorizontal();
        GUILayout.Label("Live Monitor", _stSub);
        GUILayout.FlexibleSpace();
        GUILayout.Label(_connLabel, _connected ? _stConnOn : _stConnOff);
        GUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(_connPort))
            GUILayout.Label(_connPort, _stMeta);
        GUILayout.Space(8f);

        // Last scan
        BeginCard();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Last Scan", _stMeta);
        GUILayout.FlexibleSpace();
        GUILayout.Label(_scanTime, _stMeta);
        GUILayout.EndHorizontal();
        GUILayout.Space(2f);
        GUILayout.Label(_cardName, _stBody);
        GUILayout.Label($"ID: {_cardId}   Type: {_cardType}", _stMeta);
        if (_cardHp > 0 || _cardMp > 0 || _cardAt > 0)
            GUILayout.Label($"HP {_cardHp}   MP {_cardMp}   AT {_cardAt}", _stConnOn);
        EndCard();
        GUILayout.Space(6f);

        // Serial log
        BeginCard();
        GUILayout.Label("Serial Log", _stMeta);
        GUILayout.Space(2f);
        if (_log.Count == 0)
            GUILayout.Label("  — no data yet —", _stMeta);
        else
            foreach (string line in _log)
                GUILayout.Label(line, _stMono);
        EndCard();
        GUILayout.Space(8f);

        // Simulate row
        GUILayout.BeginHorizontal();
        GUILayout.Label("Simulate ID:", _stMeta, GUILayout.Width(82f));
        _simId = GUILayout.TextField(_simId, _stField, GUILayout.ExpandWidth(true));
        GUI.enabled = !string.IsNullOrWhiteSpace(_simId);
        if (GUILayout.Button("▶ Scan", _stBtnRed, GUILayout.Width(72f), GUILayout.Height(22f)))
            ArduinoBridge.Instance?.SimulateScan(_simId.Trim());
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        if (!_emFound)
        {
            GUILayout.Space(4f);
            GUILayout.Label("⚠  EventManager not found — add it to the scene.", _stMeta);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // NAVIGATION BAR
    // ═════════════════════════════════════════════════════════════════════════

    private void NavBar(float px, float py, float pw, float ph)
    {
        const float dotSz  = 8f;
        const float dotGap = 6f;
        float navY  = py + ph - NavH;
        float btnW  = 110f;
        float btnH  = 32f;
        float btnY  = navY + (NavH - btnH) * 0.5f;

        // Progress dots (centered)
        float dotsW = kScreenCount * dotSz + (kScreenCount - 1) * dotGap;
        float dotsX = px + (pw - dotsW) * 0.5f;
        float dotsY = navY + (NavH - dotSz) * 0.5f;
        for (int i = 0; i < kScreenCount; i++)
            GUI.DrawTexture(new Rect(dotsX + i * (dotSz + dotGap), dotsY, dotSz, dotSz),
                            _txDots[i == _screen ? 1 : 0]);

        // Back
        if (_screen > 0)
            if (GUI.Button(new Rect(px + PadH, btnY, btnW, btnH), "←  Back", _stBtnGhost))
                _screen--;

        // Next / Close (not shown on screen 0 where the CTA button is in-content)
        if (_screen > 0 && _screen < kScreenCount - 1)
        {
            if (GUI.Button(new Rect(px + pw - PadH - btnW, btnY, btnW, btnH), "Next  →", _stBtnRed))
                _screen++;
        }
        else if (_screen == kScreenCount - 1)
        {
            if (GUI.Button(new Rect(px + pw - PadH - btnW, btnY, btnW, btnH), "✕  Close", _stBtnGhost))
                showWizard = false;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DRAW HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    private void HRule(float w) =>
        GUI.DrawTexture(GUILayoutUtility.GetRect(w, 1f), _txAccent);

    private void StepHeader(string step, string title, float cw)
    {
        GUILayout.Label(step, _stMeta);
        GUILayout.Space(4f);
        GUILayout.Label(title, _stSub);
        GUILayout.Space(8f);
        if (cw > 0f) HRule(cw);
        GUILayout.Space(12f);
    }

    private void BeginCard() => GUILayout.BeginVertical(_stBox);
    private void EndCard()   => GUILayout.EndVertical();

    private void Row(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _stMeta,  GUILayout.Width(148f));
        GUILayout.Label(value, _stBody,  GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();
    }

    private void Pill(string title, string sub, float totalW)
    {
        float w = (totalW - 16f) / 3f;
        GUILayout.BeginVertical(_stBox, GUILayout.Width(w));
        GUILayout.Label(title, _stBody);
        GUILayout.Label(sub,   _stMeta);
        GUILayout.EndVertical();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // STYLES + TEXTURES
    // ═════════════════════════════════════════════════════════════════════════

    private void InitStyles()
    {
        if (_stylesReady) return;
        _stylesReady = true;

        // Palette
        Color cWhite = new Color(0.93f, 0.94f, 0.97f);
        Color cGray  = new Color(0.50f, 0.56f, 0.66f);
        Color cGreen = new Color(0.18f, 0.80f, 0.44f);
        Color cRed   = new Color(0.87f, 0.24f, 0.21f);
        Color cRedDk = new Color(0.62f, 0.13f, 0.11f);

        // Textures
        _txDim      = Tex(new Color(0f, 0f, 0f, 0.74f));
        _txPanel    = Tex(new Color32(18, 22, 32, 252));
        _txAccent   = Tex(cRed);
        _txCard     = Tex(new Color32(28, 33, 48, 255));
        _txBtnRed   = Tex(cRedDk);
        _txBtnGhost = Tex(new Color32(38, 44, 60, 255));
        _txDots     = new[] { Dot(cGray), Dot(cRed) };

        // Style helpers
        _stBig = Style(GUI.skin.label, s =>
        {
            s.fontSize = 30; s.fontStyle = FontStyle.Bold;
            s.normal.textColor = cWhite;
        });
        _stSub = Style(GUI.skin.label, s =>
        {
            s.fontSize = 20; s.fontStyle = FontStyle.Bold;
            s.normal.textColor = cWhite;
        });
        _stBody = Style(GUI.skin.label, s =>
        {
            s.fontSize = 13; s.wordWrap = true;
            s.normal.textColor = cWhite;
        });
        _stMeta = Style(GUI.skin.label, s =>
        {
            s.fontSize = 11; s.wordWrap = true;
            s.normal.textColor = cGray;
        });
        _stMono = Style(GUI.skin.label, s =>
        {
            s.fontSize = 11;
            s.normal.textColor = new Color(0.38f, 0.88f, 0.52f);
        });
        _stBtnRed = Style(GUI.skin.button, s =>
        {
            s.fontSize  = 14; s.fontStyle = FontStyle.Bold;
            s.normal.background  = _txBtnRed;  s.normal.textColor  = Color.white;
            s.hover.background   = _txAccent;  s.hover.textColor   = Color.white;
            s.active.background  = _txAccent;  s.active.textColor  = Color.white;
        });
        _stBtnGhost = Style(GUI.skin.button, s =>
        {
            s.fontSize = 13;
            s.normal.background  = _txBtnGhost; s.normal.textColor  = cGray;
            s.hover.background   = _txBtnGhost; s.hover.textColor   = cWhite;
            s.active.background  = _txBtnGhost; s.active.textColor  = cWhite;
        });
        _stBtnLink = Style(GUI.skin.label, s =>
        {
            s.fontSize  = 11; s.alignment = TextAnchor.MiddleCenter;
            s.normal.textColor = cGray;
        });
        _stBox = Style(GUI.skin.box, s =>
        {
            s.padding = new RectOffset(12, 12, 8, 8);
            s.margin  = new RectOffset(0, 0, 2, 2);
            s.normal.background = _txCard;
            s.normal.textColor  = cGray;
        });
        _stConnOn = Style(GUI.skin.label, s =>
        {
            s.fontSize = 12; s.fontStyle = FontStyle.Bold;
            s.normal.textColor = cGreen;
        });
        _stConnOff = Style(GUI.skin.label, s =>
        {
            s.fontSize = 12; s.fontStyle = FontStyle.Bold;
            s.normal.textColor = cGray;
        });
        _stField = Style(GUI.skin.textField, s =>
        {
            s.fontSize = 13;
            s.normal.background  = Tex(new Color32(30, 36, 52, 255));
            s.normal.textColor   = cWhite;
            s.focused.background = Tex(new Color32(38, 44, 64, 255));
            s.focused.textColor  = cWhite;
        });
    }

    private static Texture2D Tex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    private static Texture2D Tex(Color32 c) => Tex((Color)c);

    private static Texture2D Dot(Color c)
    {
        const int N = 8;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        t.filterMode = FilterMode.Bilinear;
        float r = N * 0.5f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Sqrt((x - r + .5f) * (x - r + .5f) + (y - r + .5f) * (y - r + .5f));
                t.SetPixel(x, y, new Color(c.r, c.g, c.b, Mathf.Clamp01(r - d) * c.a));
            }
        t.Apply();
        return t;
    }

    private static GUIStyle Style(GUIStyle src, System.Action<GUIStyle> apply)
    {
        var s = new GUIStyle(src);
        apply(s);
        return s;
    }
}
