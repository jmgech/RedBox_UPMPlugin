using UnityEngine;
using UnityEngine.UI;
#if TMP_PRESENT
using TMPro;
#endif

/// <summary>
/// Displays the last scanned card as a compact badge widget.
/// Sister component to ConnectionStatusIndicator — drop it on any GameObject.
///
/// Shows:
///   ● Card art thumbnail  (if Card.cardArt is set)
///   ● Card name + type pill
///   ● HP / MP / AT stats row
///   ● Brief "NEW" flash animation on each new scan
///
/// Two modes (auto-detected):
///   OnGUI mode  — no Canvas needed; draws a pill overlay in the configured corner.
///   uGUI mode   — assign cardNameText / statsText / artImage in the Inspector.
///
/// The badge hides itself after displayDuration seconds (0 = permanent while present).
/// It clears when the card is removed (OnCardRemoved event).
/// </summary>
[AddComponentMenu("REDbox/Last Scan Badge")]
public class LastScanBadge : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("uGUI (leave null to use built-in OnGUI mode)")]
    [Tooltip("Image to display card art. Assign to enable uGUI mode.")]
    public Image artImage;

#if TMP_PRESENT
    [Tooltip("TextMeshPro label for the card name + type.")]
    public TMP_Text cardNameText;

    [Tooltip("TextMeshPro label for HP / MP / AT stats.")]
    public TMP_Text statsText;
#else
    [Tooltip("UI Text for the card name + type.")]
    public Text cardNameText;

    [Tooltip("UI Text for HP / MP / AT stats.")]
    public Text statsText;
#endif

    [Header("Display")]
    [Tooltip("Seconds the badge stays visible after a scan. 0 = until card is removed.")]
    [Range(0f, 30f)]
    public float displayDuration = 6f;

    [Header("OnGUI Layout")]
    public BadgeCorner anchor = BadgeCorner.BottomRight;
    [Range(8f, 24f)] public float fontSize = 12f;
    [Tooltip("Margin from screen edge (pixels).")]
    public Vector2 margin = new Vector2(12f, 12f);

    public enum BadgeCorner { BottomLeft, BottomRight, TopLeft, TopRight }

    // ── Runtime state ─────────────────────────────────────────────────────────
    private Card        _card;
    private CardTagData _tagData;    // fallback when card has no ScriptableObject
    private float       _hideAt  = -1f;
    private bool        _flash;
    private float       _flashUntil;
    private bool        _visible;

    // OnGUI resources (lazy init)
    private GUIStyle  _styleLabel;
    private GUIStyle  _styleName;
    private GUIStyle  _styleStat;
    private GUIStyle  _styleNew;
    private bool      _guiInit;

    private static readonly Color ColBg      = new Color(0.08f, 0.10f, 0.14f, 0.88f);
    private static readonly Color ColAccent  = new Color(0.87f, 0.24f, 0.21f);
    private static readonly Color ColNew     = new Color(1.00f, 0.85f, 0.10f);
    private static readonly Color ColStat    = new Color(0.70f, 0.85f, 1.00f);

    private Texture2D _txBg;
    private Texture2D _txAccent;

    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        ArduinoBridge.OnCardPresented      += OnCardPresented;
        ArduinoBridge.OnCardRemoved        += OnCardRemoved;
        ArduinoBridge.OnUnknownCardScanned += OnUnknownCardScanned;
    }

    private void OnDestroy()
    {
        ArduinoBridge.OnCardPresented      -= OnCardPresented;
        ArduinoBridge.OnCardRemoved        -= OnCardRemoved;
        ArduinoBridge.OnUnknownCardScanned -= OnUnknownCardScanned;
    }

    private void Update()
    {
        if (!_visible) return;

        if (displayDuration > 0f && Time.unscaledTime > _hideAt)
        {
            _visible = false;
            ApplyToUGUI();
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnCardPresented(Card card)
    {
        _card    = card;
        _tagData = CardTagData.Empty;
        _visible = true;
        _hideAt  = displayDuration > 0f ? Time.unscaledTime + displayDuration : float.MaxValue;
        _flash   = true;
        _flashUntil = Time.unscaledTime + 0.9f;
        ApplyToUGUI();
    }

    private void OnCardRemoved(Card card)
    {
        if (_card != card && card != null) return;
        _visible = false;
        ApplyToUGUI();
    }

    // Shows a fallback badge for cards that have no ScriptableObject asset.
    // Uses Name and Type directly from the NDEF tag data so action cards
    // (Direction, Power) and any unregistered card still get a visible response.
    private void OnUnknownCardScanned(CardTagData tagData)
    {
        _card    = null;
        _tagData = tagData;
        _visible = true;
        _hideAt  = displayDuration > 0f ? Time.unscaledTime + displayDuration : float.MaxValue;
        _flash   = true;
        _flashUntil = Time.unscaledTime + 0.9f;
        ApplyToUGUI();
    }

    // ── uGUI update ───────────────────────────────────────────────────────────

    private void ApplyToUGUI()
    {
        // Only run when a uGUI field is explicitly assigned.
        if (artImage == null && cardNameText == null) return;

        bool hasCard    = _card != null;
        bool hasTagData = _tagData.IsValid;

        if (!_visible || (!hasCard && !hasTagData))
        {
            if (artImage      != null) artImage.enabled      = false;
            if (cardNameText  != null) cardNameText.text      = string.Empty;
            if (statsText     != null) statsText.text         = string.Empty;
            return;
        }

        if (artImage != null)
        {
            if (hasCard && _card.cardArt != null)
            {
                artImage.sprite  = Sprite.Create(_card.cardArt,
                    new Rect(0, 0, _card.cardArt.width, _card.cardArt.height),
                    new Vector2(0.5f, 0.5f));
                artImage.enabled = true;
            }
            else
            {
                artImage.enabled = false;
            }
        }

        string displayName = hasCard ? _card.cardName : _tagData.Name;
        string displayType = hasCard ? _card.cardType : _tagData.Type;

        if (cardNameText != null)
            cardNameText.text = $"[{displayType ?? "—"}]  {displayName ?? "—"}";

        if (statsText != null)
        {
            if (hasCard && (_card.hp > 0 || _card.mp > 0 || _card.at > 0))
                statsText.text = $"HP {_card.hp}   MP {_card.mp}   AT {_card.at}";
            else
                statsText.text = string.Empty;
        }
    }

    // ── OnGUI (no-Canvas mode) ────────────────────────────────────────────────

    private void OnGUI()
    {
        // Skip if uGUI is wired
        if (artImage != null || cardNameText != null) return;

        bool hasCard    = _card != null;
        bool hasTagData = _tagData.IsValid;
        if (!_visible || (!hasCard && !hasTagData)) return;

        InitGui();

        string displayName = hasCard ? _card.cardName : _tagData.Name;
        string displayType = hasCard ? _card.cardType : _tagData.Type;

        bool   showFlash  = _flash && Time.unscaledTime < _flashUntil;
        bool   showArt    = hasCard && _card.cardArt != null;
        float  artSz      = showArt ? 56f : 0f;
        float  artPad     = showArt ? 8f  : 0f;

        string nameLine  = $"[{displayType ?? "—"}]  {displayName ?? "—"}";
        string statsLine = (hasCard && (_card.hp > 0 || _card.mp > 0 || _card.at > 0))
            ? $"HP {_card.hp}   MP {_card.mp}   AT {_card.at}"
            : string.Empty;

        Vector2 nameSize  = _styleName.CalcSize(new GUIContent(nameLine));
        Vector2 statsSize = string.IsNullOrEmpty(statsLine)
            ? Vector2.zero
            : _styleStat.CalcSize(new GUIContent(statsLine));

        float innerW = Mathf.Max(nameSize.x, statsSize.x);
        float totalW = artSz + artPad + innerW + 20f;
        float totalH = Mathf.Max(artSz, nameSize.y + (statsSize.y > 0 ? statsSize.y + 4f : 0f)) + 16f;

        float sw = Screen.width, sh = Screen.height;
        float rx = (anchor == BadgeCorner.BottomRight || anchor == BadgeCorner.TopRight)
                 ? sw - totalW - margin.x : margin.x;
        float ry = (anchor == BadgeCorner.BottomLeft  || anchor == BadgeCorner.BottomRight)
                 ? sh - totalH - margin.y : margin.y;

        // Background pill
        GUI.DrawTexture(new Rect(rx - 8f, ry - 4f, totalW + 4f, totalH + 4f),
            _txBg, ScaleMode.StretchToFill, true, 0f, ColBg, 0f, 6f);

        // Accent bar on the left edge
        GUI.DrawTexture(new Rect(rx - 8f, ry - 4f, 3f, totalH + 4f),
            _txAccent, ScaleMode.StretchToFill, false, 0f, ColAccent, 0f, 0f);

        float cx = rx;

        // Art thumbnail
        if (showArt)
        {
            GUI.DrawTexture(new Rect(cx, ry + (totalH - artSz) * 0.5f, artSz, artSz),
                _card.cardArt, ScaleMode.ScaleToFit, true);
            cx += artSz + artPad;
        }

        // Name
        GUI.Label(new Rect(cx, ry + 6f, innerW + 4f, nameSize.y), nameLine, _styleName);

        // Stats
        if (!string.IsNullOrEmpty(statsLine))
            GUI.Label(new Rect(cx, ry + 6f + nameSize.y + 4f, innerW + 4f, statsSize.y),
                statsLine, _styleStat);

        // "NEW" flash
        if (showFlash)
        {
            float alpha = Mathf.Lerp(1f, 0f,
                (Time.unscaledTime - (_flashUntil - 0.9f)) / 0.9f);
            Color prev = GUI.color;
            GUI.color = new Color(ColNew.r, ColNew.g, ColNew.b, alpha);
            GUI.Label(new Rect(rx - 8f + totalW - 30f, ry + 4f, 32f, 18f), "NEW", _styleNew);
            GUI.color = prev;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void InitGui()
    {
        if (_guiInit) return;
        _guiInit = true;

        _txBg     = new Texture2D(1, 1);
        _txBg.SetPixel(0, 0, Color.white);
        _txBg.Apply();

        _txAccent = new Texture2D(1, 1);
        _txAccent.SetPixel(0, 0, Color.white);
        _txAccent.Apply();

        _styleName = new GUIStyle(GUI.skin.label)
        {
            fontSize  = (int)(fontSize * 1.1f),
            fontStyle = FontStyle.Bold,
            normal    = { textColor = Color.white },
        };

        _styleStat = new GUIStyle(GUI.skin.label)
        {
            fontSize = (int)fontSize,
            normal   = { textColor = ColStat },
        };

        _styleNew = new GUIStyle(GUI.skin.label)
        {
            fontSize  = (int)(fontSize * 0.85f),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperRight,
        };
    }
}
