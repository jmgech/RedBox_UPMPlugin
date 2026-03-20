#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Outil éditeur pour générer automatiquement le Canvas UI du RuntimeSettingsCanvasMenu.
///
/// Usage :
///   Tools > REDbox > UI > Create Runtime Settings Canvas Menu
///
/// Ce script crée la hiérarchie complète dans la scène active et câble toutes
/// les références du composant RuntimeSettingsCanvasMenu en une seule opération.
/// Si un RuntimeSettingsCanvasHost existe déjà, il est détruit et recréé.
/// </summary>
public static class RuntimeSettingsCanvasMenuEditor
{
    // ─── Palette de couleurs ──────────────────────────────────────────────────
    private static readonly Color ColPanelBg    = HexColor("1A1A2E");
    private static readonly Color ColSectionBg  = HexColor("0D1117");
    private static readonly Color ColRowBg      = HexColor("16213E");
    private static readonly Color ColAccent     = HexColor("E63946");
    private static readonly Color ColTextPrim   = HexColor("E8E8E8");
    private static readonly Color ColTextSecond = HexColor("A0A8B8");
    private static readonly Color ColBtnNormal  = HexColor("1F3A5F");
    private static readonly Color ColBtnHover   = HexColor("2A5080");
    private static readonly Color ColBtnPress   = HexColor("0E2540");
    private static readonly Color ColBtnDisabled= HexColor("2A2A3A");
    private static readonly Color ColInputBg    = HexColor("0A0F1A");
    private static readonly Color ColDropdownBg = HexColor("0F1E33");

    // ─── Menu item removed — call CreateCanvasMenu() directly if needed ─────
    private static void CreateCanvasMenu()
    {
        // ── Nettoyage d'un éventuel hôte existant ──
        GameObject existing = GameObject.Find("RuntimeSettingsCanvasHost");
        if (existing != null)
        {
            bool confirm = EditorUtility.DisplayDialog(
                "REDbox – Recréer le Canvas",
                "Un RuntimeSettingsCanvasHost existe déjà dans la scène.\n\nVoulez-vous le supprimer et le recréer ?",
                "Recréer", "Annuler");

            if (!confirm) return;
            Undo.DestroyObjectImmediate(existing);
        }

        // ── Canvas racine ──
        GameObject host = CreateCanvas();
        Undo.RegisterCreatedObjectUndo(host, "Create REDbox Settings Canvas");

        // ── EventSystem ──
        EnsureEventSystem(host.transform);

        // ── Backdrop (fond semi-transparent) ──
        GameObject backdrop = CreateBackdrop(host.transform);

        // ── Panneau principal ──
        GameObject panel = CreatePanel(host.transform);

        // ── Contenu du panneau ──
        var refs = PopulatePanel(panel.transform);

        // ── Composant menu ──
        RuntimeSettingsCanvasMenu menu = host.AddComponent<RuntimeSettingsCanvasMenu>();
        WireReferences(menu, panel, refs);

        // ── Sélection & dirty ──
        Selection.activeGameObject = host;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("[REDbox] Canvas Settings Menu créé avec succès. "
                + "Assignez votre ArduinoBridge si besoin, puis lancez le jeu et appuyez sur Tab.");
    }

    // ─── Canvas racine ────────────────────────────────────────────────────────
    private static GameObject CreateCanvas()
    {
        GameObject go = new GameObject("RuntimeSettingsCanvasHost",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        return go;
    }

    // ─── EventSystem ─────────────────────────────────────────────────────────
    private static void EnsureEventSystem(Transform parent)
    {
        EventSystem existingEs = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
        if (existingEs != null)
        {
#if ENABLE_INPUT_SYSTEM
            // S'assurer que le bon Input Module est présent
            if (existingEs.GetComponent<InputSystemUIInputModule>() == null)
            {
                StandaloneInputModule old = existingEs.GetComponent<StandaloneInputModule>();
                if (old != null) Undo.DestroyObjectImmediate(old);
                Undo.AddComponent<InputSystemUIInputModule>(existingEs.gameObject);
            }
#endif
            return;
        }

        GameObject esGo = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        esGo.AddComponent<InputSystemUIInputModule>();
#else
        esGo.AddComponent<StandaloneInputModule>();
#endif
        Undo.RegisterCreatedObjectUndo(esGo, "Create EventSystem");
    }

    // ─── Backdrop ─────────────────────────────────────────────────────────────
    private static GameObject CreateBackdrop(Transform parent)
    {
        GameObject go = new GameObject("BackdropDim", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.one;
        rt.offsetMin        = Vector2.zero;
        rt.offsetMax        = Vector2.zero;

        Image img = go.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.75f);

        return go;
    }

    // ─── Panneau principal ────────────────────────────────────────────────────
    private static GameObject CreatePanel(Transform parent)
    {
        GameObject go = new GameObject("RuntimeSettingsPanel",
            typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(980f, 700f);
        rt.anchoredPosition = Vector2.zero;

        Image img = go.GetComponent<Image>();
        img.color = ColPanelBg;

        VerticalLayoutGroup vlg = go.GetComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(24, 24, 20, 20);
        vlg.spacing                = 12f;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        ContentSizeFitter csf = go.GetComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return go;
    }

    // ─── Remplissage du panneau ───────────────────────────────────────────────
    /// <summary>Retourne les références à câbler sur RuntimeSettingsCanvasMenu.</summary>
    private static MenuRefs PopulatePanel(Transform panel)
    {
        MenuRefs r = new MenuRefs();

        // TitleRow
        r.closeButton = CreateTitleRow(panel);

        // StatusSection
        CreateStatusSection(panel, ref r);

        // Separateur
        CreateSeparator(panel);

        // PortRow
        CreatePortRow(panel, ref r);

        // ConnectionRow
        CreateConnectionRow(panel, ref r);

        // Separateur
        CreateSeparator(panel);

        // TuningSection
        CreateTuningSection(panel, ref r);

        // FeedbackRow
        r.feedbackText = CreateFeedbackRow(panel);

        return r;
    }

    // ─── Titre ────────────────────────────────────────────────────────────────
    private static Button CreateTitleRow(Transform parent)
    {
        GameObject row = CreateHorizRow("TitleRow", parent, ColPanelBg, 56f);

        // Titre
        GameObject titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
        titleGo.transform.SetParent(row.transform, false);

        TMP_Text title = titleGo.GetComponent<TMP_Text>();
        title.text      = "REDbox Settings";
        title.fontSize  = 22f;
        title.fontStyle = FontStyles.Bold;
        title.color     = ColAccent;
        title.alignment = TextAlignmentOptions.MidlineLeft;

        LayoutElement titleLe = titleGo.AddComponent<LayoutElement>();
        titleLe.flexibleWidth = 1f;
        titleLe.preferredHeight = 48f;

        // Bouton Close
        Button closeBtn = CreateButton(row.transform, "[ X ]", ColBtnNormal, 80f, 40f);
        closeBtn.GetComponentInChildren<TMP_Text>().color = ColAccent;

        return closeBtn;
    }

    // ─── Status ───────────────────────────────────────────────────────────────
    private static void CreateStatusSection(Transform parent, ref MenuRefs r)
    {
        GameObject section = CreateVertSection("StatusSection", parent, ColSectionBg);

        TMP_Text header = CreateSectionHeader(section.transform, "Connexion");

        r.connectionStateText = CreateStatusLabel(section.transform, "State: —");
        r.activePortText      = CreateStatusLabel(section.transform, "Active Port: —");
        r.configuredPortText  = CreateStatusLabel(section.transform, "Configured Port: —");
        r.lastErrorText       = CreateStatusLabel(section.transform, "Last Error: —");
        r.scannerStateText    = CreateStatusLabel(section.transform, "Scanner: —");
    }

    // ─── Port Row ─────────────────────────────────────────────────────────────
    private static void CreatePortRow(Transform parent, ref MenuRefs r)
    {
        GameObject header = CreateSectionHeaderRow(parent, "Ports série");
        GameObject row    = CreateHorizRow("PortRow", parent, ColRowBg, 52f);

        // Dropdown
        r.portDropdown = CreateDropdown(row.transform);
        LayoutElement ddLe = r.portDropdown.gameObject.GetComponent<LayoutElement>()
                          ?? r.portDropdown.gameObject.AddComponent<LayoutElement>();
        ddLe.flexibleWidth  = 1f;
        ddLe.preferredHeight = 44f;

        // Boutons
        r.refreshPortsButton    = CreateButton(row.transform, "↺  Refresh",     ColBtnNormal, 120f, 44f);
        r.useSelectedPortButton = CreateButton(row.transform, "✔  Use Port",     ColBtnNormal, 130f, 44f);
        r.autoDetectButton      = CreateButton(row.transform, "⚡ Auto Detect",  ColBtnNormal, 140f, 44f);
    }

    // ─── Connection Row ───────────────────────────────────────────────────────
    private static void CreateConnectionRow(Transform parent, ref MenuRefs r)
    {
        GameObject header = CreateSectionHeaderRow(parent, "Contrôles");
        GameObject row    = CreateHorizRow("ConnectionRow", parent, ColRowBg, 52f);

        r.connectButton    = CreateButton(row.transform, "Connect",       ColBtnNormal,  130f, 44f);
        r.disconnectButton = CreateButton(row.transform, "Disconnect",    ColBtnNormal,  130f, 44f);
        r.scannerOnButton  = CreateButton(row.transform, "Scanner  ON",   HexColor("1A5940"), 140f, 44f);
        r.scannerOffButton = CreateButton(row.transform, "Scanner  OFF",  HexColor("591A1A"), 140f, 44f);
        r.autoScannerModeButton = CreateButton(row.transform, "AUTO SCAN: OFF", HexColor("2D3B1A"), 180f, 44f);
    }

    // ─── Tuning Section ───────────────────────────────────────────────────────
    private static void CreateTuningSection(Transform parent, ref MenuRefs r)
    {
        GameObject section = CreateVertSection("TuningSection", parent, ColSectionBg);
        CreateSectionHeader(section.transform, "Paramètres avancés");

        r.baudRateInput      = CreateLabeledInput(section.transform, "Baud Rate",                 "9600");
        r.reconnectDelayInput = CreateLabeledInput(section.transform, "Reconnect Delay (s)",       "3");
        r.scannerTimeoutInput = CreateLabeledInput(section.transform, "Scanner Enable Timeout (s)", "3");

        GameObject btnRow = CreateHorizRow("TuningBtnRow", section.transform, ColSectionBg, 44f);
        r.resetSettingsButton = CreateButton(btnRow.transform, "↺  Reset",  ColBtnNormal, 130f, 40f);
        r.applySettingsButton = CreateButton(btnRow.transform, "✔  Apply",  HexColor("1A5940"), 130f, 40f);
    }

    // ─── Feedback Row ─────────────────────────────────────────────────────────
    private static TMP_Text CreateFeedbackRow(Transform parent)
    {
        GameObject go = new GameObject("FeedbackRow", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        TMP_Text t = go.GetComponent<TMP_Text>();
        t.text      = "";
        t.fontSize  = 13f;
        t.fontStyle = FontStyles.Italic;
        t.color     = ColTextSecond;
        t.alignment = TextAlignmentOptions.Center;

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 32f;

        return t;
    }

    // ─── Helpers de construction ──────────────────────────────────────────────
    private static GameObject CreateHorizRow(string name, Transform parent, Color bg, float height)
    {
        GameObject go = new GameObject(name,
            typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
        go.transform.SetParent(parent, false);

        go.GetComponent<Image>().color = bg;

        HorizontalLayoutGroup hlg = go.GetComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(10, 10, 6, 6);
        hlg.spacing                = 10f;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = true;
        hlg.childAlignment         = TextAnchor.MiddleLeft;

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;

        return go;
    }

    private static GameObject CreateVertSection(string name, Transform parent, Color bg)
    {
        GameObject go = new GameObject(name,
            typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        go.transform.SetParent(parent, false);

        go.GetComponent<Image>().color = bg;

        VerticalLayoutGroup vlg = go.GetComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(12, 12, 10, 10);
        vlg.spacing                = 6f;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        return go;
    }

    private static GameObject CreateSectionHeaderRow(Transform parent, string text)
    {
        GameObject go = new GameObject("SectionHeader_" + text, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        TMP_Text t = go.GetComponent<TMP_Text>();
        t.text      = text.ToUpperInvariant();
        t.fontSize  = 11f;
        t.fontStyle = FontStyles.Bold;
        t.color     = ColTextSecond;
        t.alignment = TextAlignmentOptions.MidlineLeft;

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 22f;

        return go;
    }

    private static TMP_Text CreateSectionHeader(Transform parent, string text)
    {
        GameObject go = new GameObject("SectionTitle", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        TMP_Text t = go.GetComponent<TMP_Text>();
        t.text      = text.ToUpperInvariant();
        t.fontSize  = 11f;
        t.fontStyle = FontStyles.Bold;
        t.color     = ColTextSecond;
        t.alignment = TextAlignmentOptions.MidlineLeft;

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 22f;

        return t;
    }

    private static TMP_Text CreateStatusLabel(Transform parent, string defaultText)
    {
        GameObject go = new GameObject("StatusLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        TMP_Text t = go.GetComponent<TMP_Text>();
        t.text              = defaultText;
        t.fontSize          = 14f;
        t.color             = ColTextPrim;
        t.alignment         = TextAlignmentOptions.MidlineLeft;
        t.textWrappingMode  = TextWrappingModes.NoWrap;
        t.overflowMode      = TextOverflowModes.Overflow;
        t.enableAutoSizing  = false;

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 28f;

        return t;
    }

    private static Button CreateButton(Transform parent, string label, Color bgColor, float width, float height)
    {
        // Racine
        GameObject go = new GameObject(label.Replace(" ", "") + "Btn",
            typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.color = bgColor;

        Button btn = go.GetComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor      = bgColor;
        cb.highlightedColor = Color.Lerp(bgColor, Color.white, 0.15f);
        cb.pressedColor     = Color.Lerp(bgColor, Color.black, 0.2f);
        cb.disabledColor    = ColBtnDisabled;
        cb.colorMultiplier  = 1f;
        btn.colors          = cb;

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredWidth  = width;
        le.preferredHeight = height;

        // Label
        GameObject textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);

        RectTransform textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin        = Vector2.zero;
        textRt.anchorMax        = Vector2.one;
        textRt.offsetMin        = new Vector2(4f, 2f);
        textRt.offsetMax        = new Vector2(-4f, -2f);

        TMP_Text tmp = textGo.GetComponent<TMP_Text>();
        tmp.text      = label;
        tmp.fontSize  = 13f;
        tmp.color     = ColTextPrim;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;

        return btn;
    }

    private static TMP_Dropdown CreateDropdown(Transform parent)
    {
        // Racine dropdown
        GameObject go = new GameObject("PortDropdown", typeof(RectTransform), typeof(Image), typeof(TMP_Dropdown));
        go.transform.SetParent(parent, false);

        go.GetComponent<Image>().color = ColDropdownBg;

        TMP_Dropdown dd = go.GetComponent<TMP_Dropdown>();

        // Caption
        GameObject captionGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        captionGo.transform.SetParent(go.transform, false);

        RectTransform captionRt = captionGo.GetComponent<RectTransform>();
        captionRt.anchorMin  = new Vector2(0f, 0f);
        captionRt.anchorMax  = new Vector2(1f, 1f);
        captionRt.offsetMin  = new Vector2(10f, 2f);
        captionRt.offsetMax  = new Vector2(-30f, -2f);

        TMP_Text captionText = captionGo.GetComponent<TMP_Text>();
        captionText.text             = "(select port)";
        captionText.fontSize         = 14f;
        captionText.color            = ColTextPrim;
        captionText.alignment        = TextAlignmentOptions.MidlineLeft;
        captionText.textWrappingMode = TextWrappingModes.NoWrap;
        captionText.overflowMode     = TextOverflowModes.Ellipsis;

        dd.captionText = captionText;

        // Arrow
        GameObject arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(TextMeshProUGUI));
        arrowGo.transform.SetParent(go.transform, false);

        RectTransform arrowRt = arrowGo.GetComponent<RectTransform>();
        arrowRt.anchorMin  = new Vector2(1f, 0f);
        arrowRt.anchorMax  = new Vector2(1f, 1f);
        arrowRt.pivot      = new Vector2(1f, 0.5f);
        arrowRt.offsetMin  = new Vector2(-30f, 0f);
        arrowRt.offsetMax  = new Vector2(0f, 0f);

        TMP_Text arrowText = arrowGo.GetComponent<TMP_Text>();
        arrowText.text      = "▼";
        arrowText.fontSize  = 12f;
        arrowText.color     = ColTextSecond;
        arrowText.alignment = TextAlignmentOptions.Center;

        // Template (minimal — Unity requiert un Template pour TMP_Dropdown)
        GameObject templateGo = new GameObject("Template",
            typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(Mask));
        templateGo.transform.SetParent(go.transform, false);
        templateGo.SetActive(false);

        RectTransform templateRt = templateGo.GetComponent<RectTransform>();
        templateRt.anchorMin  = new Vector2(0f, 0f);
        templateRt.anchorMax  = new Vector2(1f, 0f);
        templateRt.pivot      = new Vector2(0.5f, 1f);
        templateRt.sizeDelta  = new Vector2(0f, 160f);

        templateGo.GetComponent<Image>().color = ColDropdownBg;

        Mask mask = templateGo.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        // Viewport
        GameObject viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image));
        viewportGo.transform.SetParent(templateGo.transform, false);

        RectTransform viewportRt = viewportGo.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = Vector2.zero;
        viewportGo.GetComponent<Image>().color = ColDropdownBg;

        templateGo.GetComponent<ScrollRect>().viewport = viewportRt;

        // Content
        GameObject contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        RectTransform contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot     = new Vector2(0.5f, 1f);
        contentRt.sizeDelta = new Vector2(0f, 0f);

        templateGo.GetComponent<ScrollRect>().content = contentRt;

        // Item (nécessaire pour TMP_Dropdown)
        GameObject itemGo = new GameObject("Item",
            typeof(RectTransform), typeof(Toggle), typeof(Image));
        itemGo.transform.SetParent(contentGo.transform, false);

        RectTransform itemRt = itemGo.GetComponent<RectTransform>();
        itemRt.anchorMin  = new Vector2(0f, 0.5f);
        itemRt.anchorMax  = new Vector2(1f, 0.5f);
        itemRt.sizeDelta  = new Vector2(0f, 36f);

        itemGo.GetComponent<Image>().color = ColDropdownBg;

        Toggle itemToggle = itemGo.GetComponent<Toggle>();

        // Item checkmark
        GameObject checkGo = new GameObject("Item Checkmark", typeof(RectTransform), typeof(TextMeshProUGUI));
        checkGo.transform.SetParent(itemGo.transform, false);
        RectTransform checkRt = checkGo.GetComponent<RectTransform>();
        checkRt.anchorMin  = new Vector2(0f, 0f);
        checkRt.anchorMax  = new Vector2(0f, 1f);
        checkRt.sizeDelta  = new Vector2(30f, 0f);

        TMP_Text checkText = checkGo.GetComponent<TMP_Text>();
        checkText.text      = "✓";
        checkText.fontSize  = 14f;
        checkText.color     = ColAccent;
        checkText.alignment = TextAlignmentOptions.Center;

        // Item label
        GameObject itemLabelGo = new GameObject("Item Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        itemLabelGo.transform.SetParent(itemGo.transform, false);

        RectTransform itemLabelRt = itemLabelGo.GetComponent<RectTransform>();
        itemLabelRt.anchorMin  = new Vector2(0f, 0f);
        itemLabelRt.anchorMax  = new Vector2(1f, 1f);
        itemLabelRt.offsetMin  = new Vector2(30f, 2f);
        itemLabelRt.offsetMax  = new Vector2(-5f, -2f);

        TMP_Text itemLabel = itemLabelGo.GetComponent<TMP_Text>();
        itemLabel.text      = "Option";
        itemLabel.fontSize  = 14f;
        itemLabel.color     = ColTextPrim;
        itemLabel.alignment = TextAlignmentOptions.MidlineLeft;

        dd.itemText     = itemLabel;
        dd.template     = templateRt;

        return dd;
    }

    private static TMP_InputField CreateLabeledInput(Transform parent, string labelText, string defaultValue)
    {
        // Ligne label + input
        GameObject row = new GameObject("InputRow_" + labelText,
            typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);

        HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing                = 8f;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        hlg.childAlignment         = TextAnchor.MiddleLeft;

        LayoutElement rowLe = row.AddComponent<LayoutElement>();
        rowLe.preferredHeight = 38f;

        // Label
        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(row.transform, false);

        TMP_Text label = labelGo.GetComponent<TMP_Text>();
        label.text      = labelText;
        label.fontSize  = 13f;
        label.color     = ColTextPrim;
        label.alignment = TextAlignmentOptions.MidlineLeft;

        LayoutElement labelLe = labelGo.AddComponent<LayoutElement>();
        labelLe.preferredWidth  = 220f;
        labelLe.preferredHeight = 34f;

        // Input field
        GameObject inputGo = new GameObject("InputField",
            typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        inputGo.transform.SetParent(row.transform, false);

        inputGo.GetComponent<Image>().color = ColInputBg;

        LayoutElement inputLe = inputGo.AddComponent<LayoutElement>();
        inputLe.preferredWidth  = 120f;
        inputLe.preferredHeight = 34f;

        TMP_InputField inputField = inputGo.GetComponent<TMP_InputField>();
        inputField.contentType = TMP_InputField.ContentType.DecimalNumber;

        // Text area
        GameObject textAreaGo = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textAreaGo.transform.SetParent(inputGo.transform, false);
        RectTransform textAreaRt = textAreaGo.GetComponent<RectTransform>();
        textAreaRt.anchorMin  = Vector2.zero;
        textAreaRt.anchorMax  = Vector2.one;
        textAreaRt.offsetMin  = new Vector2(6f, 2f);
        textAreaRt.offsetMax  = new Vector2(-6f, -2f);

        // Placeholder
        GameObject phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
        phGo.transform.SetParent(textAreaGo.transform, false);
        RectTransform phRt = phGo.GetComponent<RectTransform>();
        phRt.anchorMin = Vector2.zero;
        phRt.anchorMax = Vector2.one;
        phRt.offsetMin = Vector2.zero;
        phRt.offsetMax = Vector2.zero;

        TMP_Text ph = phGo.GetComponent<TMP_Text>();
        ph.text      = defaultValue;
        ph.fontSize  = 13f;
        ph.color     = ColTextSecond;
        ph.alignment = TextAlignmentOptions.MidlineLeft;

        // Text
        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(textAreaGo.transform, false);
        RectTransform textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        TMP_Text inputText = textGo.GetComponent<TMP_Text>();
        inputText.text      = defaultValue;
        inputText.fontSize  = 13f;
        inputText.color     = ColTextPrim;
        inputText.alignment = TextAlignmentOptions.MidlineLeft;

        inputField.textViewport   = textAreaRt;
        inputField.textComponent  = inputText;
        inputField.placeholder    = ph;
        inputField.text           = defaultValue;

        return inputField;
    }

    private static GameObject CreateSeparator(Transform parent)
    {
        GameObject go = new GameObject("Separator", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 1f;

        return go;
    }

    // ─── Câblage des références ───────────────────────────────────────────────
    private static void WireReferences(RuntimeSettingsCanvasMenu menu, GameObject panel, MenuRefs r)
    {
        menu.panelRoot = panel;

        // Status labels
        menu.connectionStateText = r.connectionStateText;
        menu.activePortText      = r.activePortText;
        menu.configuredPortText  = r.configuredPortText;
        menu.lastErrorText       = r.lastErrorText;
        menu.scannerStateText    = r.scannerStateText;
        menu.feedbackText        = r.feedbackText;

        // Port controls
        menu.portDropdown        = r.portDropdown;
        menu.refreshPortsButton  = r.refreshPortsButton;
        menu.useSelectedPortButton = r.useSelectedPortButton;
        menu.autoDetectButton    = r.autoDetectButton;

        // Connection controls
        menu.connectButton    = r.connectButton;
        menu.disconnectButton = r.disconnectButton;
        menu.scannerOnButton  = r.scannerOnButton;
        menu.scannerOffButton = r.scannerOffButton;
        menu.autoScannerModeButton = r.autoScannerModeButton;

        // Tuning controls
        menu.baudRateInput       = r.baudRateInput;
        menu.reconnectDelayInput = r.reconnectDelayInput;
        menu.scannerTimeoutInput = r.scannerTimeoutInput;
        menu.applySettingsButton = r.applySettingsButton;
        menu.resetSettingsButton = r.resetSettingsButton;

        // Close
        menu.closeButton = r.closeButton;
    }

    // ─── Struct de références ─────────────────────────────────────────────────
    private struct MenuRefs
    {
        // Labels
        public TMP_Text connectionStateText;
        public TMP_Text activePortText;
        public TMP_Text configuredPortText;
        public TMP_Text lastErrorText;
        public TMP_Text scannerStateText;
        public TMP_Text feedbackText;

        // Port
        public TMP_Dropdown portDropdown;
        public Button refreshPortsButton;
        public Button useSelectedPortButton;
        public Button autoDetectButton;

        // Connection
        public Button connectButton;
        public Button disconnectButton;
        public Button scannerOnButton;
        public Button scannerOffButton;
        public Button autoScannerModeButton;

        // Tuning
        public TMP_InputField baudRateInput;
        public TMP_InputField reconnectDelayInput;
        public TMP_InputField scannerTimeoutInput;
        public Button applySettingsButton;
        public Button resetSettingsButton;

        // Close
        public Button closeButton;
    }

    // ─── Utilitaire couleur ───────────────────────────────────────────────────
    private static Color HexColor(string hex)
    {
        if (ColorUtility.TryParseHtmlString("#" + hex, out Color c))
            return c;
        return Color.magenta; // couleur d'erreur visible
    }
}
#endif
