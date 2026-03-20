using System.Collections;
using UnityEngine;

/// <summary>
/// Fallback test overlay: displays the last scanned card prominently on-screen
/// for a configurable duration. No Canvas, no TMP, no setup required.
///
/// Add to any GameObject in the scene (the test scene setup does this
/// automatically). Works independently of DebugOverlay.
///
/// Shows:
///   - Card name + type in large text
///   - cardId, HP/MP/AT stats
///   - Connection / scanner status banner
///   - Animated "NEW" flash on each new scan
/// </summary>
public class CardScanToast : MonoBehaviour
{
    [Header("Display")]
    [Tooltip("Seconds the card panel stays on screen after a scan. 0 = permanent.")]
    [Range(0f, 30f)]
    public float displayDuration = 6f;

    [Tooltip("Position on screen.")]
    public ToastAnchor anchor = ToastAnchor.BottomCenter;

    public enum ToastAnchor { BottomCenter, TopCenter, BottomLeft, BottomRight }

    // ── State ─────────────────────────────────────────────────────────────────
    private Card   _card;
    private float  _hideAt   = -1f;
    private bool   _flash;
    private float  _flashUntil;
    private string _statusLine = string.Empty;
    private float  _statusHideAt = -1f;

    // ── Styles (lazy init) ────────────────────────────────────────────────────
    private GUIStyle _panelStyle;
    private GUIStyle _bigLabel;
    private GUIStyle _smallLabel;
    private GUIStyle _statLabel;
    private GUIStyle _statusStyle;
    private GUIStyle _flashStyle;
    private bool     _stylesReady;

    private const float PanelW   = 420f;
    private const float PanelH   = 140f;
    private const float Margin   = 20f;
    private const float StatusH  = 32f;

    // ═════════════════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ═════════════════════════════════════════════════════════════════════════

    private void Start()
    {
        // Start() runs after all Awake() calls, so EventManager.Instance is guaranteed set.
        EventManager em = EventManager.Instance
                          ?? FindAnyObjectByType<EventManager>();
        if (em != null)
        {
            em.OnCardScanned.AddListener(OnCardScanned);
            em.OnScannerMissing.AddListener(OnScannerMissing);
        }
        else
        {
            Debug.LogWarning("[CardScanToast] EventManager not found in scene. Card events won't show.");
        }

        ArduinoBridge.OnConnectionStateChanged += OnConnectionStateChanged;
    }

    private void OnDisable()
    {
        EventManager em = EventManager.Instance;
        if (em != null)
        {
            em.OnCardScanned.RemoveListener(OnCardScanned);
            em.OnScannerMissing.RemoveListener(OnScannerMissing);
        }
        ArduinoBridge.OnConnectionStateChanged -= OnConnectionStateChanged;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // EVENTS
    // ═════════════════════════════════════════════════════════════════════════

    private void OnCardScanned(Card card, bool isScanned)
    {
        if (!isScanned || card == null) return;
        _card    = card;
        _hideAt  = displayDuration > 0f ? Time.unscaledTime + displayDuration : float.MaxValue;
        _flash   = true;
        _flashUntil = Time.unscaledTime + 0.8f;
    }

    private void OnScannerMissing(bool missing)
    {
        _statusLine    = missing ? "⚠  Scanner REDbox introuvable" : "✓  Scanner REDbox connecté";
        _statusHideAt  = missing ? float.MaxValue : Time.unscaledTime + 3f;
    }

    private void OnConnectionStateChanged(ArduinoBridge.ConnectionState state)
    {
        string msg = state switch
        {
            ArduinoBridge.ConnectionState.Connected    => "✓  Arduino connecté",
            ArduinoBridge.ConnectionState.Disconnected => "✗  Arduino déconnecté",
            ArduinoBridge.ConnectionState.Connecting   => "…  Connexion en cours",
            ArduinoBridge.ConnectionState.Reconnecting => "↻  Reconnexion…",
            _                                          => string.Empty
        };
        if (string.IsNullOrEmpty(msg)) return;
        _statusLine   = msg;
        _statusHideAt = state == ArduinoBridge.ConnectionState.Connected
            ? Time.unscaledTime + 3f
            : float.MaxValue;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GUI
    // ═════════════════════════════════════════════════════════════════════════

    private void OnGUI()
    {
        InitStyles();

        bool showCard   = _card != null && Time.unscaledTime < _hideAt;
        bool showStatus = !string.IsNullOrEmpty(_statusLine) && Time.unscaledTime < _statusHideAt;

        if (!showCard && !showStatus) return;

        float totalH = (showCard ? PanelH : 0f) + (showStatus ? StatusH + 4f : 0f);

        Rect panelRect = CalcRect(totalH);

        // ── Status banner (connection / scanner) ──────────────────────────
        if (showStatus)
        {
            bool isOk = _statusLine.StartsWith("✓");
            _statusStyle.normal.background = MakePixel(isOk
                ? new Color(0.1f, 0.5f, 0.1f, 0.85f)
                : new Color(0.6f, 0.3f, 0.05f, 0.85f));

            Rect sr = new Rect(panelRect.x, panelRect.y, panelRect.width, StatusH);
            GUI.Box(sr, GUIContent.none, _statusStyle);

            Rect sLabel = new Rect(sr.x + 12, sr.y, sr.width - 12, sr.height);
            GUI.Label(sLabel, _statusLine, _smallLabel);

            panelRect.y  += StatusH + 4f;
            panelRect.height -= StatusH + 4f;
        }

        if (!showCard) return;

        // ── Card panel ────────────────────────────────────────────────────
        GUI.Box(panelRect, GUIContent.none, _panelStyle);

        float py = panelRect.y + 10;
        float px = panelRect.x + 14;
        float pw = panelRect.width - 28;

        // Flash "NEW" badge
        if (_flash && Time.unscaledTime < _flashUntil)
        {
            Rect badge = new Rect(panelRect.x + panelRect.width - 56, panelRect.y + 8, 46, 22);
            GUI.Label(badge, "NEW", _flashStyle);
        }

        // Card name
        GUI.Label(new Rect(px, py, pw - 56, 34), _card.cardName ?? "—", _bigLabel);
        py += 32;

        // Type + ID row
        string typeId = $"[{_card.cardType}]   ID : {_card.cardId}";
        GUI.Label(new Rect(px, py, pw, 22), typeId, _smallLabel);
        py += 22;

        // Stats row
        string stats = $"HP {_card.hp}   MP {_card.mp}   AT {_card.at}";
        GUI.Label(new Rect(px, py, pw, 22), stats, _statLabel);
        py += 22;

        // Description (truncated)
        if (!string.IsNullOrEmpty(_card.description))
        {
            string desc = _card.description.Length > 80
                ? _card.description.Substring(0, 77) + "…"
                : _card.description;
            GUI.Label(new Rect(px, py, pw, 20), desc, _smallLabel);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    private Rect CalcRect(float totalH)
    {
        float sw = Screen.width;
        float sh = Screen.height;
        return anchor switch
        {
            ToastAnchor.TopCenter    => new Rect((sw - PanelW) * 0.5f, Margin,           PanelW, totalH),
            ToastAnchor.BottomLeft   => new Rect(Margin,                sh - totalH - Margin, PanelW, totalH),
            ToastAnchor.BottomRight  => new Rect(sw - PanelW - Margin,  sh - totalH - Margin, PanelW, totalH),
            _                        => new Rect((sw - PanelW) * 0.5f,  sh - totalH - Margin, PanelW, totalH),
        };
    }

    private void InitStyles()
    {
        if (_stylesReady) return;
        _stylesReady = true;

        _panelStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = MakePixel(new Color(0.05f, 0.05f, 0.05f, 0.88f)) }
        };

        _bigLabel = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 22,
            fontStyle = FontStyle.Bold,
            normal    = { textColor = Color.white }
        };

        _smallLabel = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            normal   = { textColor = new Color(0.85f, 0.85f, 0.85f) }
        };

        _statLabel = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 13,
            fontStyle = FontStyle.Bold,
            normal    = { textColor = new Color(0.4f, 0.9f, 0.4f) }
        };

        _statusStyle = new GUIStyle(GUI.skin.box);

        _flashStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = Color.yellow }
        };
    }

    private static Texture2D MakePixel(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}
