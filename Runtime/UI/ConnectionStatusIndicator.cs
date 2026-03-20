using UnityEngine;
using UnityEngine.UI;
#if TMP_PRESENT
using TMPro;
#endif

/// <summary>
/// Lightweight connection-status widget.
/// Drop on any GameObject with a Canvas in the scene — no prefab required.
///
/// Shows:
///   ● LED dot  — red/yellow/green driven by ArduinoBridge.ConnectionState
///   Label      — "Connected  /dev/cu.usbserial-1234" or "Disconnected" etc.
///
/// Works in two modes (auto-detected):
///   OnGUI mode   — if no Canvas/Image child is wired in the Inspector.
///                  Draws a small overlay in the corner; no Canvas needed.
///   uGUI mode    — if ledImage + labelText are assigned in the Inspector.
///
/// Anchor position (OnGUI mode): Bottom-Left, Bottom-Right, Top-Left, Top-Right.
/// </summary>
[AddComponentMenu("REDbox/Connection Status Indicator")]
public class ConnectionStatusIndicator : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("uGUI (optional — leave null to use built-in OnGUI mode)")]
    [Tooltip("Image component used as the LED dot. Assign to use uGUI mode.")]
    public Image ledImage;

#if TMP_PRESENT
    [Tooltip("TextMeshPro label. Assign together with ledImage for uGUI mode.")]
    public TMP_Text labelText;
#else
    [Tooltip("UI Text label. Assign together with ledImage for uGUI mode.")]
    public Text labelText;
#endif

    [Header("OnGUI Layout")]
    public Corner anchor = Corner.BottomLeft;
    [Range(8f, 32f)] public float fontSize = 12f;
    [Tooltip("Margin from the screen edge in pixels.")]
    public Vector2 margin = new Vector2(12f, 12f);

    // ── Runtime ───────────────────────────────────────────────────────────────
    private ArduinoBridge.ConnectionState _state = ArduinoBridge.ConnectionState.Disconnected;
    private string  _port      = string.Empty;
    private bool    _scanning;

    // OnGUI resources
    private GUIStyle  _style;
    private Texture2D _txLed;
    private bool      _guiInit;

    // Colors
    private static readonly Color ColDisconnected = new Color(0.55f, 0.55f, 0.55f);  // grey
    private static readonly Color ColConnecting   = new Color(0.95f, 0.75f, 0.10f);  // amber
    private static readonly Color ColConnected    = new Color(0.18f, 0.78f, 0.44f);  // green
    private static readonly Color ColScanning     = new Color(0.18f, 0.78f, 0.44f);  // green (same)
    private static readonly Color ColBg           = new Color(0.08f, 0.10f, 0.14f, 0.80f);

    public enum Corner { BottomLeft, BottomRight, TopLeft, TopRight }

    // ═════════════════════════════════════════════════════════════════════════

    private void Start()
    {
        ArduinoBridge.OnConnectionStateChanged += OnStateChanged;
        ArduinoBridge.OnDeviceReadyChanged     += OnDeviceReady;

        var b = ArduinoBridge.Instance;
        if (b != null)
        {
            OnStateChanged(b.State);
            _scanning = b.ScannerEnabled;
        }

        ApplyToUGUI();
    }

    private void OnDestroy()
    {
        ArduinoBridge.OnConnectionStateChanged -= OnStateChanged;
        ArduinoBridge.OnDeviceReadyChanged     -= OnDeviceReady;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnStateChanged(ArduinoBridge.ConnectionState state)
    {
        _state = state;
        _port  = (state == ArduinoBridge.ConnectionState.Connected)
            ? (ArduinoBridge.Instance?.ActivePort ?? string.Empty)
            : string.Empty;
        if (state != ArduinoBridge.ConnectionState.Connected)
            _scanning = false;
        ApplyToUGUI();
    }

    private void OnDeviceReady(bool ready)
    {
        _scanning = ready && ArduinoBridge.Instance != null && ArduinoBridge.Instance.ScannerEnabled;
        ApplyToUGUI();
    }

    // ── uGUI update ───────────────────────────────────────────────────────────

    private void ApplyToUGUI()
    {
        if (ledImage == null) return;   // OnGUI mode — nothing to do here

        ledImage.color = LedColor();

        if (labelText != null)
        {
#if TMP_PRESENT
            labelText.text = StatusText();
#else
            labelText.text = StatusText();
#endif
        }
    }

    // ── OnGUI (no Canvas mode) ────────────────────────────────────────────────

    private void OnGUI()
    {
        if (ledImage != null) return;   // uGUI mode — skip

        InitGui();

        string text  = StatusText();
        Vector2 size = _style.CalcSize(new GUIContent(text));
        float   ledSz = fontSize * 0.75f;
        float   totalW = ledSz + 6f + size.x + 16f;
        float   totalH = Mathf.Max(ledSz, size.y) + 10f;

        float sw = Screen.width, sh = Screen.height;
        float rx = anchor == Corner.BottomRight || anchor == Corner.TopRight
                 ? sw - totalW - margin.x
                 : margin.x;
        float ry = anchor == Corner.BottomLeft || anchor == Corner.BottomRight
                 ? sh - totalH - margin.y
                 : margin.y;

        Rect bg = new Rect(rx - 8f, ry - 4f, totalW + 4f, totalH + 2f);
        GUI.DrawTexture(bg, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
            ColBg, 0f, 4f);

        // LED
        Color prev = GUI.color;
        GUI.color = LedColor();
        GUI.DrawTexture(new Rect(rx, ry + (totalH - ledSz) * 0.5f, ledSz, ledSz), _txLed);
        GUI.color = prev;

        // Label
        GUI.Label(new Rect(rx + ledSz + 6f, ry, size.x + 4f, totalH), text, _style);
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private Color LedColor() => _state switch
    {
        ArduinoBridge.ConnectionState.Connected    => _scanning ? ColScanning : ColConnected,
        ArduinoBridge.ConnectionState.Connecting   => ColConnecting,
        ArduinoBridge.ConnectionState.Reconnecting => ColConnecting,
        _                                          => ColDisconnected,
    };

    private string StatusText()
    {
        return _state switch
        {
            ArduinoBridge.ConnectionState.Connected    =>
                _scanning
                    ? $"Scanning  {_port}".TrimEnd()
                    : $"Connected  {_port}".TrimEnd(),
            ArduinoBridge.ConnectionState.Connecting   => "Connecting…",
            ArduinoBridge.ConnectionState.Reconnecting => "Reconnecting…",
            _                                          => "Disconnected",
        };
    }

    private void InitGui()
    {
        if (_guiInit) return;
        _guiInit = true;

        // Circular LED texture
        const int N = 16;
        _txLed = new Texture2D(N, N, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear };
        float r = N * 0.5f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Sqrt((x - r + .5f) * (x - r + .5f) + (y - r + .5f) * (y - r + .5f));
                _txLed.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d)));
            }
        _txLed.Apply();

        _style = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(fontSize),
            normal   = { textColor = new Color(0.88f, 0.90f, 0.94f) },
        };
    }
}
