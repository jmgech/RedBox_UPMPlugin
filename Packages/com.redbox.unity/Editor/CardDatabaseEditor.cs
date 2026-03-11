using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools → REDbox → Card Database
///
/// Single-window editor for browsing, creating, and editing all Card
/// ScriptableObjects in the project. Shows a searchable list on the left
/// and a card preview + editable fields on the right.
/// </summary>
public class CardDatabaseEditor : EditorWindow
{
    // ── Menu ──────────────────────────────────────────────────────────────────
    [MenuItem("Tools/REDbox/Card Database", priority = 0)]
    public static void Open()
    {
        var win = GetWindow<CardDatabaseEditor>("REDbox Cards");
        win.minSize = new Vector2(700f, 480f);
        win.Show();
    }

    // ── State ─────────────────────────────────────────────────────────────────
    private List<Card>   _cards        = new List<Card>();
    private Card         _selected;
    private SerializedObject _so;
    private string       _search       = string.Empty;
    private Vector2      _listScroll;
    private Vector2      _detailScroll;
    private bool         _dirty;
    private double       _lastRefresh;
    // UID capture (Play Mode only)
    private bool   _capturingUid;
    private string _captureBaseUid = string.Empty;
    // ── Styles (lazy) ─────────────────────────────────────────────────────────
    private GUIStyle _styleListItem;
    private GUIStyle _styleListSelected;
    private GUIStyle _styleHeader;
    private GUIStyle _stylePill;
    private bool     _stylesInit;

    // ── Palette ───────────────────────────────────────────────────────────────
    private static readonly Color ColPanel   = new Color(0.13f, 0.15f, 0.20f);
    private static readonly Color ColSelected= new Color(0.87f, 0.24f, 0.21f, 0.25f);
    private static readonly Color ColAccent  = new Color(0.87f, 0.24f, 0.21f);
    private static readonly Color ColCard    = new Color(0.10f, 0.12f, 0.17f);
    private static readonly Color ColBorder  = new Color(0.22f, 0.27f, 0.36f);

    private Texture2D _txPanel, _txSelected, _txAccent, _txCard, _txBorder;

    // ═════════════════════════════════════════════════════════════════════════
    // UNITY CALLBACKS
    // ═════════════════════════════════════════════════════════════════════════

    private void OnEnable()
    {
        Refresh();
        Undo.undoRedoPerformed += OnUndoRedo;
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        Undo.undoRedoPerformed -= OnUndoRedo;
        StopCapture();
        SaveIfDirty();
    }

    // Polls ArduinoBridge.LastScannedCardId every editor frame when capture mode is active.
    // LastScannedCardId = the logical ID emitted in UID= (e.g. "001") — the registry key.
    // Also auto-populates cardName / cardType from LastCardTagData when those fields are blank.
    private void OnEditorUpdate()
    {
        if (!_capturingUid || !EditorApplication.isPlaying) return;

        string uid = null;
        ArduinoBridge bridge = null;
        try
        {
            // Reflection-free: ArduinoBridge is in the Runtime assembly, accessible in Editor.
            bridge = UnityEngine.Object.FindAnyObjectByType<ArduinoBridge>();
            uid = bridge != null ? bridge.LastScannedCardId : null;
        }
        catch { }

        if (string.IsNullOrEmpty(uid) || uid == "—" || uid == _captureBaseUid) return;

        // New ID detected — fill the Card ID field and optionally Name / Type.
        if (_so != null && _so.targetObject != null)
        {
            _so.Update();

            SerializedProperty pId = _so.FindProperty("cardId");
            if (pId != null) pId.stringValue = uid;

            // Auto-populate cardName / cardType from the NDEF tag data when fields are empty.
            // This saves a manual step for new cards: scan → ID, Name, and Type all filled in.
            try
            {
                if (bridge != null)
                {
                    CardTagData tagData = bridge.LastCardTagData;
                    if (tagData.IsValid)
                    {
                        SerializedProperty pName = _so.FindProperty("cardName");
                        if (pName != null && string.IsNullOrWhiteSpace(pName.stringValue)
                            && !string.IsNullOrEmpty(tagData.Name))
                        {
                            // Convert ALLCAPS tag name to Title Case for readability.
                            pName.stringValue = System.Globalization.CultureInfo.CurrentCulture
                                .TextInfo.ToTitleCase(tagData.Name.ToLower());
                        }

                        SerializedProperty pType = _so.FindProperty("cardType");
                        if (pType != null && string.IsNullOrWhiteSpace(pType.stringValue)
                            && !string.IsNullOrEmpty(tagData.Type))
                        {
                            pType.stringValue = System.Globalization.CultureInfo.CurrentCulture
                                .TextInfo.ToTitleCase(tagData.Type.ToLower());
                        }
                    }
                }
            }
            catch { /* tag data population is best-effort */ }

            _so.ApplyModifiedProperties();
            EditorUtility.SetDirty(_selected);
            _dirty = true;
        }

        StopCapture();
        Repaint();
    }

    private void StartCapture()
    {
        try
        {
            var bridge = UnityEngine.Object.FindAnyObjectByType<ArduinoBridge>();
            _captureBaseUid = bridge != null ? bridge.LastScannedCardId : string.Empty;
        }
        catch { _captureBaseUid = string.Empty; }
        _capturingUid = true;
    }

    private void StopCapture()
    {
        _capturingUid   = false;
        _captureBaseUid = string.Empty;
    }

    private void OnUndoRedo()
    {
        if (_so != null) _so.Update();
        Repaint();
    }

    private void OnGUI()
    {
        InitStyles();

        // Auto-refresh every 4 s in case assets change on disk.
        // Defer via delayCall so Refresh() never mutates _selected mid Layout/Repaint cycle.
        if (EditorApplication.timeSinceStartup - _lastRefresh > 4.0)
        {
            _lastRefresh = EditorApplication.timeSinceStartup; // prevent re-entry
            EditorApplication.delayCall += Refresh;
        }

        DrawToolbar();

        float listW  = Mathf.Min(position.width * 0.32f, 240f);
        float bodyH  = position.height - 42f;

        GUILayout.BeginHorizontal();

        // Left: card list
        GUILayout.BeginVertical(GUILayout.Width(listW), GUILayout.Height(bodyH));
        DrawList(listW, bodyH);
        GUILayout.EndVertical();

        // Divider
        EditorGUILayout.LabelField(string.Empty, GUILayout.Width(1f), GUILayout.ExpandHeight(true));

        // Right: detail / create
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.Height(bodyH));
        if (_selected != null)
            DrawDetail();
        else
            DrawEmptyState();
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TOOLBAR
    // ═════════════════════════════════════════════════════════════════════════

    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUILayout.Label("REDbox  ·  Card Database", EditorStyles.boldLabel, GUILayout.Width(200f));

        _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);

        if (GUILayout.Button("↺  Refresh", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            Refresh();

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("＋  Character", EditorStyles.toolbarButton))
            CreateCard<CharacterCard>("NewCharacterCard");

        GUILayout.EndHorizontal();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LIST PANEL
    // ═════════════════════════════════════════════════════════════════════════

    private void DrawList(float listW, float bodyH)
    {
        GUILayout.Space(4f);
        _listScroll = GUILayout.BeginScrollView(_listScroll,
            GUILayout.Width(listW), GUILayout.Height(bodyH - 8f));

        string lo = _search.ToLowerInvariant();
        foreach (Card c in _cards)
        {
            if (c == null) continue;
            if (!string.IsNullOrEmpty(_search) &&
                !c.cardName.ToLowerInvariant().Contains(lo) &&
                !c.cardId.ToLowerInvariant().Contains(lo) &&
                !c.cardType.ToLowerInvariant().Contains(lo))
                continue;

            bool isSel = c == _selected;

            Rect r = GUILayoutUtility.GetRect(GUIContent.none,
                isSel ? _styleListSelected : _styleListItem,
                GUILayout.Height(52f), GUILayout.Width(listW - 4f));

            if (Event.current.type == EventType.Repaint)
            {
                if (isSel) _styleListSelected.Draw(r, false, false, true, false);
                else       _styleListItem.Draw(r, false, r.Contains(Event.current.mousePosition), false, false);
            }

            // Card info inside each row — thumbnail on the left when cardArt is set
            const float ThumbSz = 38f;
            float thumbRight = 0f;
            if (c.cardArt != null)
            {
                thumbRight = ThumbSz + 6f;
                Rect thumb = new Rect(r.x + 6f, r.y + 7f, ThumbSz, ThumbSz);
                GUI.DrawTexture(thumb, c.cardArt, ScaleMode.ScaleToFit, true);
            }

            Rect inner = new Rect(r.x + 8f + thumbRight, r.y + 6f,
                r.width - 16f - thumbRight, r.height - 12f);
            GUI.Label(new Rect(inner.x, inner.y,     inner.width, 18f), c.cardName ?? "(unnamed)",
                EditorStyles.boldLabel);
            GUI.Label(new Rect(inner.x, inner.y + 20f, inner.width, 14f),
                $"{c.cardType ?? "—"}  ·  {c.cardId ?? "—"}",
                EditorStyles.miniLabel);

            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                SaveIfDirty();
                Select(c);
                Event.current.Use();
            }
        }

        GUILayout.EndScrollView();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DETAIL PANEL
    // ═════════════════════════════════════════════════════════════════════════

    private void DrawDetail()
    {
        if (_so == null || _so.targetObject == null)
        {
            // Defer selection clear so we don't mutate _selected between Layout and Repaint.
            EditorApplication.delayCall += () => { if (_so == null || _so.targetObject == null) Select(null); };
            return;
        }

        _so.Update();

        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();
        GUILayout.Label(_selected.cardName ?? "(unnamed)", _styleHeader);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Select in Project", GUILayout.Width(130f)))
        {
            EditorGUIUtility.PingObject(_selected);
            Selection.activeObject = _selected;
        }
        if (GUILayout.Button("Delete", GUILayout.Width(70f)))
        {
            if (EditorUtility.DisplayDialog("Delete Card",
                $"Delete '{_selected.cardName}'? This cannot be undone.", "Delete", "Cancel"))
            {
                string path = AssetDatabase.GetAssetPath(_selected);
                Select(null);
                AssetDatabase.DeleteAsset(path);
                Refresh();
                return;
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(4f);
        DrawHRule();
        GUILayout.Space(8f);

        _detailScroll = GUILayout.BeginScrollView(_detailScroll);

        // ── Core fields ───────────────────────────────────────────────────────
        SectionLabel("Identity");

        // Card ID field (full-width property row)
        SerializedProperty idProp = _so.FindProperty("cardId");
        if (idProp != null)
        {
            var idLabel = new GUIContent("Card ID",
                "Short ID token from the NDEF payload — e.g. \"001\", \"T001\", \"DB\", \"P02\".\n" +
                "Must match the first colon-delimited token written on the physical tag.\n" +
                "Use ⬤ Capture in Play Mode to auto-fill from a live scan.");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(idProp, idLabel);
            if (EditorGUI.EndChangeCheck()) _dirty = true;
        }

        // Capture / Cancel button on its own indented row below Card ID
        GUILayout.BeginHorizontal();
        GUILayout.Space(EditorGUIUtility.labelWidth);
        if (_capturingUid)
        {
            Color prev = GUI.color;
            GUI.color = new Color(0.95f, 0.75f, 0.10f); // amber
            if (GUILayout.Button("⏹  Cancel", GUILayout.Width(84f)))
                StopCapture();
            GUI.color = prev;
        }
        else
        {
            bool canCapture = EditorApplication.isPlaying;
            EditorGUI.BeginDisabledGroup(!canCapture);
            GUIContent captureLabel = new GUIContent(
                "⬤  Capture",
                canCapture ? "Tap a card on the scanner to auto-fill this ID."
                           : "Enter Play Mode to use live UID capture.");
            if (GUILayout.Button(captureLabel, GUILayout.Width(84f)))
                StartCapture();
            EditorGUI.EndDisabledGroup();
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (_capturingUid)
        {
            EditorGUILayout.HelpBox("Waiting — tap a card on the REDbox scanner…", MessageType.Info);
        }

        PropField("cardName", "Display Name");
        PropField("cardType", "Type");

        GUILayout.Space(8f);
        SectionLabel("Description");
        SerializedProperty descProp = _so.FindProperty("description");
        if (descProp != null)
        {
            EditorGUI.BeginChangeCheck();
            descProp.stringValue = EditorGUILayout.TextArea(descProp.stringValue,
                GUILayout.MinHeight(54f));
            if (EditorGUI.EndChangeCheck()) _dirty = true;
        }

        // ── Stats ─────────────────────────────────────────────────────────────
        GUILayout.Space(8f);
        SectionLabel("Stats");
        GUILayout.BeginHorizontal();
        StatField("hp", "HP");
        GUILayout.Space(8f);
        StatField("mp", "MP");
        GUILayout.Space(8f);
        StatField("at", "AT");
        GUILayout.EndHorizontal();

        // ── Card Art ─────────────────────────────────────────────────────────
        GUILayout.Space(8f);
        SectionLabel("Card Art");
        GUILayout.BeginHorizontal();
        SerializedProperty artProp = _so.FindProperty("cardArt");
        if (artProp != null)
        {
            // Show a drag-and-drop Texture2D field
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(artProp, new GUIContent("Artwork"));
            if (EditorGUI.EndChangeCheck()) _dirty = true;

            // Live preview thumbnail
            if (_selected.cardArt != null)
            {
                GUILayout.Space(6f);
                Rect artRect = GUILayoutUtility.GetRect(72f, 72f,
                    GUILayout.Width(72f), GUILayout.Height(72f));
                GUI.DrawTexture(artRect, _selected.cardArt, ScaleMode.ScaleToFit, true);
            }
        }
        GUILayout.EndHorizontal();

        // ── Type-specific fields (CharacterCard) ──────────────────────────────
        if (_selected is CharacterCard)
        {
            GUILayout.Space(8f);
            SectionLabel("Character Spawn");
            PropField("characterPrefab", "Character Prefab");
            PropField("spawnOffset",     "Spawn Offset");
            PropField("autoDestroyAfter","Auto Destroy (s)");

            GUILayout.Space(8f);
            SectionLabel("Effects");
            PropField("summonVFX",   "Summon VFX");
            PropField("summonSound", "Summon Sound");
        }

        // ── Card preview box ──────────────────────────────────────────────────
        GUILayout.Space(12f);
        DrawPreview();

        GUILayout.Space(8f);
        GUILayout.EndScrollView();

        if (_so.ApplyModifiedProperties())
        {
            _dirty = true;
            EditorUtility.SetDirty(_selected);
        }
    }

    private void DrawPreview()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("Preview", EditorStyles.boldLabel);
        GUILayout.Space(4f);

        GUILayout.BeginHorizontal();

        // Art thumbnail on the left (when available)
        if (_selected.cardArt != null)
        {
            Rect artR = GUILayoutUtility.GetRect(64f, 64f,
                GUILayout.Width(64f), GUILayout.Height(64f));
            GUI.DrawTexture(artR, _selected.cardArt, ScaleMode.ScaleToFit, true);
            GUILayout.Space(8f);
        }

        GUILayout.BeginVertical();
        GUILayout.Label($"[{_selected.cardType ?? "—"}]  {_selected.cardName ?? "—"}", EditorStyles.boldLabel);
        if (!string.IsNullOrEmpty(_selected.cardId))
            GUILayout.Label($"ID: {_selected.cardId}", EditorStyles.miniLabel);
        if (_selected.hp > 0 || _selected.mp > 0 || _selected.at > 0)
            GUILayout.Label($"HP {_selected.hp}   MP {_selected.mp}   AT {_selected.at}");
        if (!string.IsNullOrEmpty(_selected.description))
        {
            GUILayout.Space(4f);
            GUILayout.Label(_selected.description, EditorStyles.wordWrappedMiniLabel);
        }
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    // ── Empty state ───────────────────────────────────────────────────────────

    private void DrawEmptyState()
    {
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical();
        GUILayout.Label("Select a card from the list", EditorStyles.centeredGreyMiniLabel);
        GUILayout.Space(8f);
        if (GUILayout.Button("＋  Create Character Card", GUILayout.Width(200f), GUILayout.Height(32f)))
            CreateCard<CharacterCard>("NewCharacterCard");
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    private void Refresh()
    {
        _lastRefresh = EditorApplication.timeSinceStartup;
        _cards.Clear();

        string[] guids = AssetDatabase.FindAssets("t:Card");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Card c = AssetDatabase.LoadAssetAtPath<Card>(path);
            if (c != null) _cards.Add(c);
        }

        _cards.Sort((a, b) => string.Compare(a.cardName, b.cardName,
            System.StringComparison.OrdinalIgnoreCase));

        // Keep selection valid
        if (_selected != null && !_cards.Contains(_selected))
            Select(null);

        Repaint();
    }

    private void Select(Card c)
    {
        SaveIfDirty();
        _selected = c;
        _so       = c != null ? new SerializedObject(c) : null;
        _dirty    = false;
    }

    private void SaveIfDirty()
    {
        if (!_dirty || _selected == null) return;
        EditorUtility.SetDirty(_selected);
        AssetDatabase.SaveAssetIfDirty(_selected);
        _dirty = false;
    }

    private void CreateCard<T>(string defaultName) where T : Card
    {
        string dir = "Assets/REDbox";
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets", "REDbox");

        string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{defaultName}.asset");
        T card = ScriptableObject.CreateInstance<T>();
        card.cardType = typeof(T).Name.Replace("Card", string.Empty);

        AssetDatabase.CreateAsset(card, path);
        AssetDatabase.SaveAssets();
        Refresh();
        Select(card);
        EditorGUIUtility.PingObject(card);
    }

    private void PropField(string propName, string label)
    {
        SerializedProperty p = _so.FindProperty(propName);
        if (p == null) return;
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(p, new GUIContent(label));
        if (EditorGUI.EndChangeCheck()) _dirty = true;
    }

    private void StatField(string propName, string label)
    {
        SerializedProperty p = _so.FindProperty(propName);
        if (p == null) return;
        EditorGUI.BeginChangeCheck();
        GUILayout.BeginVertical(GUILayout.Width(80f));
        GUILayout.Label(label, EditorStyles.centeredGreyMiniLabel);
        p.intValue = EditorGUILayout.IntField(p.intValue, GUILayout.Width(80f));
        GUILayout.EndVertical();
        if (EditorGUI.EndChangeCheck()) _dirty = true;
    }

    private void SectionLabel(string text)
    {
        GUILayout.Label(text.ToUpperInvariant(), EditorStyles.boldLabel);
    }

    private void DrawHRule()
    {
        Rect r = GUILayoutUtility.GetRect(position.width, 1f);
        EditorGUI.DrawRect(r, ColBorder);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // STYLES
    // ═════════════════════════════════════════════════════════════════════════

    private void InitStyles()
    {
        if (_stylesInit) return;
        _stylesInit = true;

        _txPanel    = MakeTex(ColPanel);
        _txSelected = MakeTex(ColSelected);
        _txCard     = MakeTex(ColCard);
        _txAccent   = MakeTex(ColAccent);
        _txBorder   = MakeTex(ColBorder);

        _styleListItem = new GUIStyle(GUI.skin.label)
        {
            padding   = new RectOffset(8, 8, 4, 4),
            normal    = { background = null },
            hover     = { background = _txCard },
            fixedHeight = 52f,
        };
        _styleListSelected = new GUIStyle(_styleListItem)
        {
            normal    = { background = _txSelected },
            hover     = { background = _txSelected },
        };
        _styleHeader = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 18,
        };
        _stylePill = new GUIStyle(EditorStyles.miniLabel)
        {
            padding  = new RectOffset(6, 6, 2, 2),
            normal   = { background = _txCard },
        };
    }

    private static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}
