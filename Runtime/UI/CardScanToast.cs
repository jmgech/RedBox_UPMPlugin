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
///   - Animated "NEW" flash on first session scan per card
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
    public enum HeartbeatAnchor
    {
        TopLeft,
        TopCenter,
        TopRight,
        LeftCenter,
        Center,
        RightCenter,
        BottomLeft,
        BottomCenter,
        BottomRight
    }
    public enum HeartbeatVisualStyle { ArcadePulse, EcgSpike, ConcentricCircles }

    [Header("Heartbeat Visual")]
    [Tooltip("Shows a line beat when device heartbeat frames are received.")]
    public bool showHeartbeatLine = true;

    [Tooltip("Visual style for heartbeat rendering.")]
    public HeartbeatVisualStyle heartbeatStyle = HeartbeatVisualStyle.ArcadePulse;

    [Header("Heartbeat Placement")]
    [Tooltip("Anchor for heartbeat visual, independent from toast anchor.")]
    public HeartbeatAnchor heartbeatAnchor = HeartbeatAnchor.TopCenter;

    [Range(100f, 3000f)]
    [Tooltip("Arcade line layout width in pixels.")]
    public float heartbeatWidth = 420f;

    [Tooltip("Extra screen-space offset from anchored position.")]
    public Vector2 heartbeatOffset = Vector2.zero;

    [Range(0f, 80f)]
    [Tooltip("Distance from screen edges for anchored heartbeat.")]
    public float heartbeatScreenMargin = 20f;

    [Range(0.4f, 4f)]
    [Tooltip("How long each beat animation lasts.")]
    public float heartbeatPulseDuration = 1.2f;

    [Range(2f, 20f)]
    [Tooltip("How long to keep heartbeat visible after the last beat.")]
    public float heartbeatVisibleWindow = 8f;

    [Range(1f, 220f)]
    [Tooltip("Arcade line height in pixels.")]
    public float heartbeatLineHeight = 8f;

    [Header("ECG Layout")]
    [Range(100f, 5000f)]
    [Tooltip("ECG layout width in pixels (can be larger than screen).")]
    public float ecgWidth = 680f;

    [Range(8f, 1200f)]
    [Tooltip("ECG layout height in pixels.")]
    public float ecgHeight = 64f;

    [Header("Concentric Layout")]
    [Range(40f, 2400f)]
    [Tooltip("Concentric layout size (square) in pixels.")]
    public float concentricSize = 220f;

    [Tooltip("If enabled, concentric layout auto-expands to fit circles and glyph.")]
    public bool concentricAutoFit = true;

    [Range(0.4f, 2.2f)]
    [Tooltip("Additional thickness multiplier for ECG segments.")]
    public float ecgThickness = 1f;

    [Range(0.5f, 2.5f)]
    [Tooltip("Global visual intensity.")]
    public float heartbeatIntensity = 1.15f;

    [Range(10f, 60f)]
    [Tooltip("Arcade style marker width.")]
    public float arcadeSweepWidth = 26f;

    [Range(0.1f, 0.9f)]
    [Tooltip("ECG style spike width as a fraction of panel width.")]
    public float ecgSpikeWidth = 0.32f;

    [Range(8f, 420f)]
    [Tooltip("Concentric circles max radius in pixels.")]
    public float circlesMaxRadius = 28f;

    [Range(1, 5)]
    [Tooltip("Number of concentric circles to draw.")]
    public int circlesCount = 3;

    [Range(1f, 20f)]
    [Tooltip("Concentric circles line thickness in pixels.")]
    public float circlesThickness = 2f;

    [Tooltip("Fill the concentric layout background rectangle.")]
    public bool concentricFillBackground = false;

    [Range(0f, 1f)]
    [Tooltip("Opacity used when concentric background fill is enabled.")]
    public float concentricFillOpacity = 0.16f;

    [Range(24, 240)]
    [Tooltip("Ring smoothness (higher = cleaner circles, heavier draw cost).")]
    public int concentricRingSegments = 96;

    [Header("Heartbeat Glyph")]
    [Tooltip("Show a center glyph for concentric heartbeat style.")]
    public bool showHeartbeatGlyph = true;

    [Tooltip("Allow glyph size to influence concentric layout auto-fit.")]
    public bool glyphAffectsLayout = false;

    [Tooltip("Glyph shown at the center of the concentric heartbeat.")]
    public string heartbeatGlyph = "♥";

    [Range(10f, 180f)]
    [Tooltip("Base glyph size in pixels.")]
    public float heartbeatGlyphSize = 20f;

    public Color heartbeatGlyphColor = new Color(1f, 0.58f, 0.68f, 0.95f);

    [Tooltip("Show circular pulse dot at center of concentric style.")]
    public bool showConcentricCenterDot = true;

    [Range(0f, 32f)]
    [Tooltip("Base radius of the concentric center dot.")]
    public float concentricCenterDotRadius = 4f;

    [Header("Heartbeat Colors")]
    public Color arcadeBaseColor = new Color(0.06f, 0.18f, 0.12f, 0.68f);
    public Color arcadeBeatColor = new Color(0.26f, 0.98f, 0.56f, 0.96f);
    public Color arcadeMarkerColor = new Color(0.86f, 1f, 0.92f, 0.95f);
    public Color ecgBaseColor = new Color(0.06f, 0.16f, 0.1f, 0.62f);
    public Color ecgTraceColor = new Color(0.22f, 0.74f, 0.42f, 0.8f);
    public Color ecgPeakColor = new Color(0.88f, 1f, 0.94f, 0.9f);
    public Color circlesBaseColor = new Color(0.08f, 0.18f, 0.14f, 0.55f);
    public Color circlesPulseColor = new Color(0.74f, 1f, 0.9f, 0.92f);

    // ── State ─────────────────────────────────────────────────────────────────
    private Card   _card;
    private CardTagData _tagData;
    private bool   _hasTagData;
    private float  _hideAt   = -1f;
    private bool   _flash;
    private float  _flashUntil;
    private string _statusLine = string.Empty;
    private float  _statusHideAt = -1f;
    private string _actionLine = string.Empty;
    private float  _actionHideAt = -1f;
    private Color  _actionColor = new Color(0.12f, 0.55f, 0.2f, 0.92f);
    private float  _lastHeartbeatAt = -100f;
    private float  _heartbeatPulseAt = -100f;

    // ── Styles (lazy init) ────────────────────────────────────────────────────
    private GUIStyle _panelStyle;
    private GUIStyle _bigLabel;
    private GUIStyle _smallLabel;
    private GUIStyle _statLabel;
    private GUIStyle _statusStyle;
    private GUIStyle _actionStyle;
    private GUIStyle _heartbeatStyle;
    private GUIStyle _heartbeatGlyphStyle;
    private GUIStyle _flashStyle;
    private bool     _stylesReady;

    private const float PanelW   = 420f;
    private const float PanelH   = 140f;
    private const float Margin   = 20f;
    private const float StatusH  = 32f;
    private const float ActionH  = 28f;

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
        ArduinoBridge.OnCardTagRead += OnCardTagRead;
        ArduinoBridge.OnUnknownCardScanned += OnUnknownCardScanned;
        ArduinoBridge.OnRedboxEvent += OnRedboxEvent;
        ArduinoBridge.OnHeartbeat += OnHeartbeat;
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
        ArduinoBridge.OnCardTagRead -= OnCardTagRead;
        ArduinoBridge.OnUnknownCardScanned -= OnUnknownCardScanned;
        ArduinoBridge.OnRedboxEvent -= OnRedboxEvent;
        ArduinoBridge.OnHeartbeat -= OnHeartbeat;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // EVENTS
    // ═════════════════════════════════════════════════════════════════════════

    private void OnCardScanned(Card card, bool isScanned)
    {
        if (!isScanned || card == null) return;
        _card    = card;
        _hasTagData = false;
        _hideAt  = displayDuration > 0f ? Time.unscaledTime + displayDuration : float.MaxValue;
        _flash   = card.ContextScanCount <= 1;
        _flashUntil = _flash ? Time.unscaledTime + 0.8f : -1f;
    }

    private void OnCardTagRead(CardTagData tagData)
    {
        if (!tagData.IsValid) return;

        _tagData = tagData;
        _hasTagData = true;
        _hideAt  = displayDuration > 0f ? Time.unscaledTime + displayDuration : float.MaxValue;
        _flash   = true;
        _flashUntil = Time.unscaledTime + 0.8f;
    }

    private void OnUnknownCardScanned(CardTagData tagData)
    {
        if (!tagData.IsValid) return;

        _tagData = tagData;
        _hasTagData = true;
        _card = null;
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

    private void OnRedboxEvent(RedboxEvent redboxEvent)
    {
        switch (redboxEvent.Type)
        {
            case RedboxEvent.EventType.CardEnter:
                _actionLine = "CARD ON";
                _actionColor = new Color(0.12f, 0.55f, 0.2f, 0.92f);
                break;
            case RedboxEvent.EventType.CardPresent:
                _actionLine = "CARD STAY";
                _actionColor = new Color(0.63f, 0.48f, 0.08f, 0.92f);
                break;
            case RedboxEvent.EventType.CardExit:
                _actionLine = "CARD AWAY";
                _actionColor = new Color(0.62f, 0.2f, 0.12f, 0.92f);
                break;
            default:
                return;
        }

        _actionHideAt = Time.unscaledTime + 1.8f;
    }

    private void OnHeartbeat()
    {
        _lastHeartbeatAt = Time.unscaledTime;
        _heartbeatPulseAt = Time.unscaledTime;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GUI
    // ═════════════════════════════════════════════════════════════════════════

    private void OnGUI()
    {
        InitStyles();

        bool showCard   = (_card != null || _hasTagData) && Time.unscaledTime < _hideAt;
        bool showStatus = !string.IsNullOrEmpty(_statusLine) && Time.unscaledTime < _statusHideAt;
        bool showAction = !string.IsNullOrEmpty(_actionLine) && Time.unscaledTime < _actionHideAt;
        bool showHeartbeat = showHeartbeatLine && (Time.unscaledTime - _lastHeartbeatAt < heartbeatVisibleWindow);
        bool showPanel = showCard || showStatus || showAction;

        if (!showPanel && !showHeartbeat) return;

        float heartbeatW;
        float heartbeatH;
        GetHeartbeatLayoutSize(out heartbeatW, out heartbeatH);

        if (showHeartbeat)
        {
            DrawHeartbeatLine(CalcHeartbeatRect(heartbeatW, heartbeatH));
        }

        if (!showPanel) return;

        float totalH = (showCard ? PanelH : 0f)
                 + (showStatus ? StatusH + 4f : 0f)
             + (showAction ? ActionH + 4f : 0f);

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

        // ── Action banner (card on / stay / away) ────────────────────────
        if (showAction)
        {
            _actionStyle.normal.background = MakePixel(_actionColor);

            Rect ar = new Rect(panelRect.x, panelRect.y, panelRect.width, ActionH);
            GUI.Box(ar, GUIContent.none, _actionStyle);

            Rect aLabel = new Rect(ar.x + 12, ar.y, ar.width - 12, ar.height);
            GUI.Label(aLabel, _actionLine, _smallLabel);

            panelRect.y += ActionH + 4f;
            panelRect.height -= ActionH + 4f;
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

        if (_card != null)
        {
            // Card name
            GUI.Label(new Rect(px, py, pw - 56, 34), _card.cardName ?? "—", _bigLabel);
            py += 32;

            // Type + ID row
            string typeId = $"[{_card.cardType}]   ID : {_card.cardId}";
            GUI.Label(new Rect(px, py, pw, 22), typeId, _smallLabel);
            py += 22;

            // Stats row
            string scopeLabel = _card.StatsPersistent ? $"CTX({_card.StatsContextId})" : "SESSION";
            string stats = $"HP {_card.hp}   MP {_card.mp}   AT {_card.at}   {scopeLabel} {_card.ContextScanCount}";
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
        else if (_hasTagData)
        {
            string title = !string.IsNullOrWhiteSpace(_tagData.Name) ? _tagData.Name : _tagData.Id;
            GUI.Label(new Rect(px, py, pw - 56, 34), title ?? "—", _bigLabel);
            py += 32;

            string type = !string.IsNullOrWhiteSpace(_tagData.Type)
                ? _tagData.Type
                : _tagData.TaxonomyType.ToString();
            string cardId = !string.IsNullOrWhiteSpace(_tagData.CardId) ? _tagData.CardId : _tagData.Id;
            string typeId = $"[{type}]   ID : {cardId}";
            GUI.Label(new Rect(px, py, pw, 22), typeId, _smallLabel);
            py += 22;

            string uid = string.IsNullOrWhiteSpace(_tagData.TagUid) ? "—" : _tagData.TagUid;
            string slot = string.IsNullOrWhiteSpace(_tagData.SlotId) ? "—" : _tagData.SlotId;
            GUI.Label(new Rect(px, py, pw, 22), $"UID {uid}   SLOT {slot}", _statLabel);
            py += 22;

            GUI.Label(new Rect(px, py, pw, 20), "Carte non résolue dans la base locale.", _smallLabel);
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

    private void GetHeartbeatLayoutSize(out float heartbeatW, out float heartbeatH)
    {
        switch (heartbeatStyle)
        {
            case HeartbeatVisualStyle.EcgSpike:
                heartbeatW = Mathf.Max(100f, ecgWidth);
                heartbeatH = Mathf.Max(8f, ecgHeight);
                return;
            case HeartbeatVisualStyle.ConcentricCircles:
                float glyphDriven = (showHeartbeatGlyph && glyphAffectsLayout)
                    ? Mathf.Max(40f, heartbeatGlyphSize * 3f)
                    : 40f;
                float circlesDriven = Mathf.Max(16f, circlesMaxRadius * 2f + circlesThickness * 2f + 8f);
                float size = Mathf.Max(40f, concentricSize);
                if (concentricAutoFit)
                    size = Mathf.Max(size, Mathf.Max(glyphDriven, circlesDriven));

                heartbeatW = size;
                heartbeatH = size;
                return;
            default:
                heartbeatW = Mathf.Max(100f, heartbeatWidth);
                heartbeatH = Mathf.Max(4f, heartbeatLineHeight);
                return;
        }
    }

    private Rect CalcHeartbeatRect(float heartbeatW, float heartbeatH)
    {
        float sw = Screen.width;
        float sh = Screen.height;
        float margin = Mathf.Max(0f, heartbeatScreenMargin);
        float width = Mathf.Max(100f, heartbeatW);

        float x;
        float y;

        switch (heartbeatAnchor)
        {
            case HeartbeatAnchor.TopLeft:
                x = margin;
                y = margin;
                break;
            case HeartbeatAnchor.TopCenter:
                x = (sw - width) * 0.5f;
                y = margin;
                break;
            case HeartbeatAnchor.TopRight:
                x = sw - width - margin;
                y = margin;
                break;
            case HeartbeatAnchor.LeftCenter:
                x = margin;
                y = (sh - heartbeatH) * 0.5f;
                break;
            case HeartbeatAnchor.Center:
                x = (sw - width) * 0.5f;
                y = (sh - heartbeatH) * 0.5f;
                break;
            case HeartbeatAnchor.RightCenter:
                x = sw - width - margin;
                y = (sh - heartbeatH) * 0.5f;
                break;
            case HeartbeatAnchor.BottomLeft:
                x = margin;
                y = sh - heartbeatH - margin;
                break;
            case HeartbeatAnchor.BottomRight:
                x = sw - width - margin;
                y = sh - heartbeatH - margin;
                break;
            default:
                x = (sw - width) * 0.5f;
                y = sh - heartbeatH - margin;
                break;
        }

        x += heartbeatOffset.x;
        y += heartbeatOffset.y;
        return new Rect(x, y, width, Mathf.Max(4f, heartbeatH));
    }

    private void DrawHeartbeatLine(Rect rect)
    {
        float duration = Mathf.Max(0.05f, heartbeatPulseDuration);
        float elapsed = Time.unscaledTime - _heartbeatPulseAt;
        float pulse = 1f - Mathf.Clamp01(elapsed / duration);

        if (heartbeatStyle == HeartbeatVisualStyle.ConcentricCircles)
        {
            DrawConcentricHeartbeat(rect, pulse);
            return;
        }

        if (heartbeatStyle == HeartbeatVisualStyle.EcgSpike)
        {
            DrawEcgHeartbeat(rect, pulse);
            return;
        }

        DrawArcadeHeartbeat(rect, pulse);
    }

    private void DrawArcadeHeartbeat(Rect rect, float pulse)
    {
        float intensity = Mathf.Max(0.1f, heartbeatIntensity);
        Color baseColor = arcadeBaseColor;
        baseColor.a *= intensity;
        Color beatColor = arcadeBeatColor;
        Color lineColor = Color.Lerp(baseColor, beatColor, Mathf.Clamp01(pulse * intensity));

        _heartbeatStyle.normal.background = MakePixel(lineColor);
        GUI.Box(rect, GUIContent.none, _heartbeatStyle);

        if (pulse <= 0.01f) return;

        float sweep = Mathf.Clamp01(1f - pulse);
        float markerW = Mathf.Clamp(arcadeSweepWidth, 8f, rect.width * 0.45f);
        float markerX = Mathf.Lerp(rect.x, rect.xMax - markerW, sweep);
        Color markerColor = arcadeMarkerColor;
        markerColor.a = Mathf.Clamp01(markerColor.a * (0.95f * pulse + 0.12f));
        _heartbeatStyle.normal.background = MakePixel(markerColor);
        GUI.Box(new Rect(markerX, rect.y, markerW, rect.height), GUIContent.none, _heartbeatStyle);
    }

    private void DrawEcgHeartbeat(Rect rect, float pulse)
    {
        float intensity = Mathf.Max(0.1f, heartbeatIntensity);
        Color baseColor = ecgBaseColor;
        baseColor.a *= intensity;
        _heartbeatStyle.normal.background = MakePixel(baseColor);
        GUI.Box(rect, GUIContent.none, _heartbeatStyle);

        float spikeTravel = 1f - Mathf.Clamp01(pulse);
        float spikeCenterX = Mathf.Lerp(rect.x + rect.width * 0.12f, rect.x + rect.width * 0.88f, spikeTravel);
        float spikeW = Mathf.Clamp(rect.width * ecgSpikeWidth * Mathf.Clamp01(pulse * 1.25f), 4f, rect.width * 0.9f);

        float segH = Mathf.Clamp(rect.height * Mathf.Max(0.4f, ecgThickness), 1f, 220f);
        float segY = rect.y + (rect.height - segH) * 0.5f;

        Rect left = new Rect(spikeCenterX - spikeW * 0.5f, segY, spikeW * 0.33f, segH);
        Rect mid = new Rect(left.xMax, segY, spikeW * 0.24f, segH);
        Rect right = new Rect(mid.xMax, segY, spikeW * 0.43f, segH);

        _heartbeatStyle.normal.background = MakePixel(ecgTraceColor);
        GUI.Box(left, GUIContent.none, _heartbeatStyle);

        Color peakColor = ecgPeakColor;
        peakColor.a = Mathf.Clamp01(peakColor.a * (0.35f + pulse * intensity));
        _heartbeatStyle.normal.background = MakePixel(peakColor);
        GUI.Box(mid, GUIContent.none, _heartbeatStyle);

        _heartbeatStyle.normal.background = MakePixel(ecgTraceColor);
        GUI.Box(right, GUIContent.none, _heartbeatStyle);
    }

    private void DrawConcentricHeartbeat(Rect rect, float pulse)
    {
        float intensity = Mathf.Max(0.1f, heartbeatIntensity);
        if (concentricFillBackground)
        {
            Color bg = circlesBaseColor;
            bg.a = Mathf.Clamp01(concentricFillOpacity * (0.7f + 0.3f * intensity));
            _heartbeatStyle.normal.background = MakePixel(bg);
            GUI.Box(rect, GUIContent.none, _heartbeatStyle);
        }

        Vector2 center = new Vector2(rect.x + rect.width * 0.5f, rect.y + rect.height * 0.5f);
        int ringCount = Mathf.Clamp(circlesCount, 1, 5);
        float maxRadius = Mathf.Clamp(circlesMaxRadius, 4f, rect.height * 0.5f - 1f);
        float thickness = Mathf.Clamp(circlesThickness, 1f, 28f);
        int baseSegments = Mathf.Clamp(concentricRingSegments, 24, 240);

        for (int i = 0; i < ringCount; i++)
        {
            float phase = i / (float)ringCount;
            float animated = Mathf.Repeat((1f - pulse) + phase, 1f);
            float radius = Mathf.Max(2f, animated * maxRadius);
            float alphaCurve = 1f - animated;

            Color ringColor = Color.Lerp(circlesBaseColor, circlesPulseColor, Mathf.Clamp01(pulse * intensity));
            ringColor.a = Mathf.Clamp01(ringColor.a * alphaCurve * (0.7f + 0.5f * pulse));

            int segments = Mathf.Clamp(Mathf.RoundToInt(baseSegments * (0.55f + (radius / Mathf.Max(1f, maxRadius)) * 0.85f)), 24, 360);
            DrawRing(center, radius, thickness, ringColor, segments);
        }

        // Central pulse marker (circular, not square).
        if (showConcentricCenterDot)
        {
            float dotRadius = Mathf.Clamp(concentricCenterDotRadius + pulse * 6f * intensity, 1f, 42f);
            Color dotColor = circlesPulseColor;
            dotColor.a = Mathf.Clamp01(0.4f + 0.5f * pulse);
            DrawFilledDisc(center, dotRadius, dotColor);
        }

        if (showHeartbeatGlyph && !string.IsNullOrEmpty(heartbeatGlyph))
        {
            float glyphPulseScale = Mathf.Lerp(0.92f, 1.12f, pulse);
            int glyphSize = Mathf.RoundToInt(Mathf.Clamp(heartbeatGlyphSize * glyphPulseScale, 8f, 220f));

            Color glyphColor = heartbeatGlyphColor;
            glyphColor.a = Mathf.Clamp01(glyphColor.a * (0.6f + 0.4f * pulse));

            _heartbeatGlyphStyle.fontSize = glyphSize;
            _heartbeatGlyphStyle.normal.textColor = glyphColor;

            float glyphBox = Mathf.Clamp(glyphSize * 2.2f, 24f, 600f);
            Rect glyphRect = new Rect(center.x - glyphBox * 0.5f, center.y - glyphBox * 0.5f, glyphBox, glyphBox);
            GUI.Label(glyphRect, heartbeatGlyph, _heartbeatGlyphStyle);
        }
    }

    private void DrawRing(Vector2 center, float radius, float thickness, Color color, int segments)
    {
        int segmentCount = Mathf.Clamp(segments, 16, 360);
        float half = Mathf.Max(0.5f, thickness * 0.5f);
        _heartbeatStyle.normal.background = MakePixel(color);

        for (int i = 0; i < segmentCount; i++)
        {
            float angle = (i / (float)segmentCount) * Mathf.PI * 2f;
            float x = center.x + Mathf.Cos(angle) * radius;
            float y = center.y + Mathf.Sin(angle) * radius;
            GUI.Box(new Rect(x - half, y - half, thickness, thickness), GUIContent.none, _heartbeatStyle);
        }
    }

    private void DrawFilledDisc(Vector2 center, float radius, Color color)
    {
        float r = Mathf.Clamp(radius, 1f, 64f);
        int minX = Mathf.FloorToInt(center.x - r);
        int maxX = Mathf.CeilToInt(center.x + r);
        int minY = Mathf.FloorToInt(center.y - r);
        int maxY = Mathf.CeilToInt(center.y + r);
        float r2 = r * r;

        _heartbeatStyle.normal.background = MakePixel(color);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - center.x;
                float dy = y - center.y;
                if (dx * dx + dy * dy <= r2)
                {
                    GUI.Box(new Rect(x, y, 1f, 1f), GUIContent.none, _heartbeatStyle);
                }
            }
        }
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

        _actionStyle = new GUIStyle(GUI.skin.box);

        _heartbeatStyle = new GUIStyle(GUI.skin.box);

        _heartbeatGlyphStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 20,
            normal = { textColor = new Color(1f, 0.58f, 0.68f, 0.95f) }
        };

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
