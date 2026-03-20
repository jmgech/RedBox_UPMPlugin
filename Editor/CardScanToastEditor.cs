#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CardScanToast))]
public class CardScanToastEditor : Editor
{
    private enum Preset
    {
        CompactLine,
        OversizeEcg,
        HeroConcentric,
    }

    private SerializedProperty _displayDuration;
    private SerializedProperty _anchor;

    private SerializedProperty _showHeartbeatLine;
    private SerializedProperty _heartbeatStyle;
    private SerializedProperty _heartbeatAnchor;
    private SerializedProperty _heartbeatOffset;
    private SerializedProperty _heartbeatScreenMargin;
    private SerializedProperty _heartbeatPulseDuration;
    private SerializedProperty _heartbeatVisibleWindow;
    private SerializedProperty _heartbeatIntensity;

    private SerializedProperty _heartbeatWidth;
    private SerializedProperty _heartbeatLineHeight;
    private SerializedProperty _arcadeSweepWidth;
    private SerializedProperty _arcadeBaseColor;
    private SerializedProperty _arcadeBeatColor;
    private SerializedProperty _arcadeMarkerColor;

    private SerializedProperty _ecgWidth;
    private SerializedProperty _ecgHeight;
    private SerializedProperty _ecgThickness;
    private SerializedProperty _ecgSpikeWidth;
    private SerializedProperty _ecgBaseColor;
    private SerializedProperty _ecgTraceColor;
    private SerializedProperty _ecgPeakColor;

    private SerializedProperty _concentricSize;
    private SerializedProperty _concentricAutoFit;
    private SerializedProperty _circlesMaxRadius;
    private SerializedProperty _circlesCount;
    private SerializedProperty _circlesThickness;
    private SerializedProperty _concentricFillBackground;
    private SerializedProperty _concentricFillOpacity;
    private SerializedProperty _concentricRingSegments;
    private SerializedProperty _circlesBaseColor;
    private SerializedProperty _circlesPulseColor;

    private SerializedProperty _showHeartbeatGlyph;
    private SerializedProperty _glyphAffectsLayout;
    private SerializedProperty _heartbeatGlyph;
    private SerializedProperty _heartbeatGlyphSize;
    private SerializedProperty _heartbeatGlyphColor;
    private SerializedProperty _showConcentricCenterDot;
    private SerializedProperty _concentricCenterDotRadius;

    private enum HeartbeatVisualStyle
    {
        ArcadePulse = 0,
        EcgSpike = 1,
        ConcentricCircles = 2,
    }

    private void OnEnable()
    {
        _displayDuration = serializedObject.FindProperty("displayDuration");
        _anchor = serializedObject.FindProperty("anchor");

        _showHeartbeatLine = serializedObject.FindProperty("showHeartbeatLine");
        _heartbeatStyle = serializedObject.FindProperty("heartbeatStyle");
        _heartbeatAnchor = serializedObject.FindProperty("heartbeatAnchor");
        _heartbeatOffset = serializedObject.FindProperty("heartbeatOffset");
        _heartbeatScreenMargin = serializedObject.FindProperty("heartbeatScreenMargin");
        _heartbeatPulseDuration = serializedObject.FindProperty("heartbeatPulseDuration");
        _heartbeatVisibleWindow = serializedObject.FindProperty("heartbeatVisibleWindow");
        _heartbeatIntensity = serializedObject.FindProperty("heartbeatIntensity");

        _heartbeatWidth = serializedObject.FindProperty("heartbeatWidth");
        _heartbeatLineHeight = serializedObject.FindProperty("heartbeatLineHeight");
        _arcadeSweepWidth = serializedObject.FindProperty("arcadeSweepWidth");
        _arcadeBaseColor = serializedObject.FindProperty("arcadeBaseColor");
        _arcadeBeatColor = serializedObject.FindProperty("arcadeBeatColor");
        _arcadeMarkerColor = serializedObject.FindProperty("arcadeMarkerColor");

        _ecgWidth = serializedObject.FindProperty("ecgWidth");
        _ecgHeight = serializedObject.FindProperty("ecgHeight");
        _ecgThickness = serializedObject.FindProperty("ecgThickness");
        _ecgSpikeWidth = serializedObject.FindProperty("ecgSpikeWidth");
        _ecgBaseColor = serializedObject.FindProperty("ecgBaseColor");
        _ecgTraceColor = serializedObject.FindProperty("ecgTraceColor");
        _ecgPeakColor = serializedObject.FindProperty("ecgPeakColor");

        _concentricSize = serializedObject.FindProperty("concentricSize");
        _concentricAutoFit = serializedObject.FindProperty("concentricAutoFit");
        _circlesMaxRadius = serializedObject.FindProperty("circlesMaxRadius");
        _circlesCount = serializedObject.FindProperty("circlesCount");
        _circlesThickness = serializedObject.FindProperty("circlesThickness");
        _concentricFillBackground = serializedObject.FindProperty("concentricFillBackground");
        _concentricFillOpacity = serializedObject.FindProperty("concentricFillOpacity");
        _concentricRingSegments = serializedObject.FindProperty("concentricRingSegments");
        _circlesBaseColor = serializedObject.FindProperty("circlesBaseColor");
        _circlesPulseColor = serializedObject.FindProperty("circlesPulseColor");

        _showHeartbeatGlyph = serializedObject.FindProperty("showHeartbeatGlyph");
        _glyphAffectsLayout = serializedObject.FindProperty("glyphAffectsLayout");
        _heartbeatGlyph = serializedObject.FindProperty("heartbeatGlyph");
        _heartbeatGlyphSize = serializedObject.FindProperty("heartbeatGlyphSize");
        _heartbeatGlyphColor = serializedObject.FindProperty("heartbeatGlyphColor");
        _showConcentricCenterDot = serializedObject.FindProperty("showConcentricCenterDot");
        _concentricCenterDotRadius = serializedObject.FindProperty("concentricCenterDotRadius");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Toast", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_displayDuration);
        EditorGUILayout.PropertyField(_anchor);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Heartbeat", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_showHeartbeatLine);

        using (new EditorGUI.DisabledScope(!_showHeartbeatLine.boolValue))
        {
            EditorGUILayout.PropertyField(_heartbeatStyle);
            DrawPresetButtons();
            EditorGUILayout.PropertyField(_heartbeatAnchor);
            EditorGUILayout.PropertyField(_heartbeatOffset);
            EditorGUILayout.PropertyField(_heartbeatScreenMargin);
            EditorGUILayout.PropertyField(_heartbeatPulseDuration);
            EditorGUILayout.PropertyField(_heartbeatVisibleWindow);
            EditorGUILayout.PropertyField(_heartbeatIntensity);

            EditorGUILayout.Space(6);
            DrawStyleSpecificSettings();
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawPresetButtons()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("One-Click Presets", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Compact Line"))
            ApplyPreset(Preset.CompactLine);

        if (GUILayout.Button("Oversize ECG"))
            ApplyPreset(Preset.OversizeEcg);

        if (GUILayout.Button("Hero Concentric"))
            ApplyPreset(Preset.HeroConcentric);

        EditorGUILayout.EndHorizontal();
    }

    private void ApplyPreset(Preset preset)
    {
        Undo.RecordObjects(targets, "Apply Heartbeat Preset");

        switch (preset)
        {
            case Preset.CompactLine:
                _heartbeatStyle.enumValueIndex = (int)HeartbeatVisualStyle.ArcadePulse;
                _heartbeatAnchor.enumValueIndex = 1; // TopCenter
                _heartbeatOffset.vector2Value = new Vector2(0f, 0f);
                _heartbeatScreenMargin.floatValue = 20f;
                _heartbeatPulseDuration.floatValue = 1.0f;
                _heartbeatVisibleWindow.floatValue = 8f;
                _heartbeatIntensity.floatValue = 1.1f;

                _heartbeatWidth.floatValue = 420f;
                _heartbeatLineHeight.floatValue = 10f;
                _arcadeSweepWidth.floatValue = 24f;
                _arcadeBaseColor.colorValue = new Color(0.08f, 0.18f, 0.14f, 0.35f);
                _arcadeBeatColor.colorValue = new Color(0.22f, 0.92f, 0.58f, 0.95f);
                _arcadeMarkerColor.colorValue = new Color(0.9f, 1f, 0.95f, 0.95f);
                break;

            case Preset.OversizeEcg:
                _heartbeatStyle.enumValueIndex = (int)HeartbeatVisualStyle.EcgSpike;
                _heartbeatAnchor.enumValueIndex = 4; // Center
                _heartbeatOffset.vector2Value = new Vector2(0f, 0f);
                _heartbeatScreenMargin.floatValue = 8f;
                _heartbeatPulseDuration.floatValue = 1.2f;
                _heartbeatVisibleWindow.floatValue = 10f;
                _heartbeatIntensity.floatValue = 1.35f;

                _ecgWidth.floatValue = 1800f;
                _ecgHeight.floatValue = 180f;
                _ecgThickness.floatValue = 1.35f;
                _ecgSpikeWidth.floatValue = 0.35f;
                _ecgBaseColor.colorValue = new Color(0.03f, 0.08f, 0.06f, 0.22f);
                _ecgTraceColor.colorValue = new Color(0.2f, 0.85f, 0.5f, 0.88f);
                _ecgPeakColor.colorValue = new Color(0.9f, 1f, 0.92f, 0.95f);
                break;

            case Preset.HeroConcentric:
                _heartbeatStyle.enumValueIndex = (int)HeartbeatVisualStyle.ConcentricCircles;
                _heartbeatAnchor.enumValueIndex = 4; // Center
                _heartbeatOffset.vector2Value = new Vector2(0f, 0f);
                _heartbeatScreenMargin.floatValue = 12f;
                _heartbeatPulseDuration.floatValue = 1.4f;
                _heartbeatVisibleWindow.floatValue = 10f;
                _heartbeatIntensity.floatValue = 1.2f;

                _concentricSize.floatValue = 380f;
                _concentricAutoFit.boolValue = true;
                _circlesMaxRadius.floatValue = 130f;
                _circlesCount.intValue = 4;
                _circlesThickness.floatValue = 3f;
                _concentricRingSegments.intValue = 144;
                _concentricFillBackground.boolValue = false;
                _concentricFillOpacity.floatValue = 0.14f;

                _circlesBaseColor.colorValue = new Color(0.2f, 0.55f, 0.42f, 0.35f);
                _circlesPulseColor.colorValue = new Color(0.82f, 1f, 0.92f, 0.92f);

                _showConcentricCenterDot.boolValue = true;
                _concentricCenterDotRadius.floatValue = 5f;
                _showHeartbeatGlyph.boolValue = true;
                _glyphAffectsLayout.boolValue = false;
                _heartbeatGlyph.stringValue = "♥";
                _heartbeatGlyphSize.floatValue = 72f;
                _heartbeatGlyphColor.colorValue = new Color(1f, 0.58f, 0.68f, 0.96f);
                break;
        }

        serializedObject.ApplyModifiedProperties();
        foreach (Object t in targets)
            EditorUtility.SetDirty(t);
    }

    private void DrawStyleSpecificSettings()
    {
        HeartbeatVisualStyle style = (HeartbeatVisualStyle)_heartbeatStyle.enumValueIndex;

        switch (style)
        {
            case HeartbeatVisualStyle.EcgSpike:
                DrawEcgSettings();
                break;
            case HeartbeatVisualStyle.ConcentricCircles:
                DrawConcentricSettings();
                break;
            default:
                DrawArcadeSettings();
                break;
        }
    }

    private void DrawArcadeSettings()
    {
        EditorGUILayout.LabelField("Arcade Layout", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_heartbeatWidth);
        EditorGUILayout.PropertyField(_heartbeatLineHeight);
        EditorGUILayout.PropertyField(_arcadeSweepWidth);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Arcade Colors", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_arcadeBaseColor);
        EditorGUILayout.PropertyField(_arcadeBeatColor);
        EditorGUILayout.PropertyField(_arcadeMarkerColor);
    }

    private void DrawEcgSettings()
    {
        EditorGUILayout.LabelField("ECG Layout", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_ecgWidth);
        EditorGUILayout.PropertyField(_ecgHeight);
        EditorGUILayout.PropertyField(_ecgThickness);
        EditorGUILayout.PropertyField(_ecgSpikeWidth);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("ECG Colors", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_ecgBaseColor);
        EditorGUILayout.PropertyField(_ecgTraceColor);
        EditorGUILayout.PropertyField(_ecgPeakColor);
    }

    private void DrawConcentricSettings()
    {
        EditorGUILayout.LabelField("Concentric Layout", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_concentricSize);
        EditorGUILayout.PropertyField(_concentricAutoFit);
        EditorGUILayout.PropertyField(_circlesMaxRadius);
        EditorGUILayout.PropertyField(_circlesCount);
        EditorGUILayout.PropertyField(_circlesThickness);
        EditorGUILayout.PropertyField(_concentricRingSegments);
        EditorGUILayout.PropertyField(_concentricFillBackground);
        if (_concentricFillBackground.boolValue)
            EditorGUILayout.PropertyField(_concentricFillOpacity);
        EditorGUILayout.PropertyField(_showConcentricCenterDot);
        if (_showConcentricCenterDot.boolValue)
            EditorGUILayout.PropertyField(_concentricCenterDotRadius);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Concentric Colors", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_circlesBaseColor);
        EditorGUILayout.PropertyField(_circlesPulseColor);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Glyph", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_showHeartbeatGlyph);
        if (_showHeartbeatGlyph.boolValue)
        {
            EditorGUILayout.PropertyField(_glyphAffectsLayout);
            EditorGUILayout.PropertyField(_heartbeatGlyph);
            EditorGUILayout.PropertyField(_heartbeatGlyphSize);
            EditorGUILayout.PropertyField(_heartbeatGlyphColor);
        }
    }
}
#endif
