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
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        SaveIfDirty();
    }

    private void OnUndoRedo()
    {
        if (_so != null) _so.Update();
        Repaint();
    }

    private void OnGUI()
    {
        InitStyles();

        // Auto-refresh every 4 s in case assets change on disk
        if (EditorApplication.timeSinceStartup - _lastRefresh > 4.0)
            Refresh();

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

            // Card info inside each row
            Rect inner = new Rect(r.x + 8f, r.y + 6f, r.width - 16f, r.height - 12f);
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
            Select(null);
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
        PropField("cardId",   "Card ID");
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
