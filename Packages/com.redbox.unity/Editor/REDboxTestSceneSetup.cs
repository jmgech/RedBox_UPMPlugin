using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Menu: Tools > REDbox > Setup > Create Test Scene
///
/// Creates a fully-wired REDbox test scene in Assets/REDbox_TestScene/ with:
///   - EventManager, MainThreadDispatcher, ArduinoBridge (debug mode on)
///   - Two demo CharacterCard ScriptableObjects pre-assigned to ArduinoBridge
///   - HardwareSettings configured for debug mode (no real Arduino needed)
///   - Runtime Settings Canvas Menu auto-generated via the existing helper
///   - A REDboxTestListener that prints every card event to the Console
/// </summary>
public static class REDboxTestSceneSetup
{
    private const string kOutputDir   = "Assets/REDbox_TestScene";
    private const string kScenePath   = kOutputDir + "/REDbox_TestScene.unity";
    private const string kSettingsPath = kOutputDir + "/REDbox_HardwareSettings.asset";
    private const string kCard1Path   = kOutputDir + "/DemoCard_Alpha.asset";
    private const string kCard2Path   = kOutputDir + "/DemoCard_Beta.asset";
    private const string kListenerPath = kOutputDir + "/REDboxTestListener.cs";

    // ──────────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/REDbox/Setup/Create Test Scene", priority = 0)]
    public static void CreateTestScene()
    {
        // 1 ── Output folder
        if (!AssetDatabase.IsValidFolder(kOutputDir))
        {
            AssetDatabase.CreateFolder("Assets", "REDbox_TestScene");
            AssetDatabase.Refresh();
        }

        // 2 ── HardwareSettings (debugMode = true → no Arduino required)
        HardwareSettings hw = LoadOrCreate<HardwareSettings>(kSettingsPath);
        hw.debugMode        = true;
        hw.autoDetectPort   = true;
        hw.baudRate         = 9600;
        EditorUtility.SetDirty(hw);

        // 3 ── Demo cards  (CharacterCard)
        CharacterCard card1 = LoadOrCreate<CharacterCard>(kCard1Path);
        card1.cardId   = "DEMO_ALPHA";
        card1.cardName = "Alpha (demo)";
        card1.cardType = "Character";
        card1.hp = 100; card1.mp = 50; card1.at = 30;
        EditorUtility.SetDirty(card1);

        CharacterCard card2 = LoadOrCreate<CharacterCard>(kCard2Path);
        card2.cardId   = "DEMO_BETA";
        card2.cardName = "Beta (demo)";
        card2.cardType = "Character";
        card2.hp = 80; card2.mp = 80; card2.at = 50;
        EditorUtility.SetDirty(card2);

        AssetDatabase.SaveAssets();

        // 4 ── Write test listener script (only if it doesn't exist yet)
        WriteListenerScript();

        AssetDatabase.Refresh();

        // 5 ── Scene
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 6 ── Required singletons
        GameObject goEM  = new GameObject("EventManager");
        goEM.AddComponent<EventManager>();

        GameObject goMTD = new GameObject("MainThreadDispatcher");
        goMTD.AddComponent<MainThreadDispatcher>();

        // 7 ── ArduinoBridge
        GameObject goBridge = new GameObject("ArduinoBridge");
        ArduinoBridge bridge = goBridge.AddComponent<ArduinoBridge>();

        // Assign serialized fields via SerializedObject so Unity tracks undo/dirty
        SerializedObject soBridge = new SerializedObject(bridge);
        soBridge.FindProperty("settings").objectReferenceValue = hw;

        SerializedProperty cardArray = soBridge.FindProperty("cardDataArray");
        cardArray.arraySize = 2;
        cardArray.GetArrayElementAtIndex(0).objectReferenceValue = card1;
        cardArray.GetArrayElementAtIndex(1).objectReferenceValue = card2;
        soBridge.ApplyModifiedPropertiesWithoutUndo();

        // 8 ── Camera (so the scene isn't totally black)
        GameObject cam = new GameObject("Main Camera");
        cam.AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.transform.position = new Vector3(0, 1, -10);

        // 9 ── Save scene
        EditorSceneManager.SaveScene(scene, kScenePath);
        AssetDatabase.Refresh();

        // 10 ── Summary
        Debug.Log(
            "[REDbox Setup] ✅ Test scene created at " + kScenePath + "\n\n" +
            "NEXT STEPS:\n" +
            "  1. Open " + kScenePath + " if not already open.\n" +
            "  2. (Optional) Tools > REDbox > UI > Create Runtime Settings Canvas Menu\n" +
            "  3. Press ▶ Play.\n" +
            "  4. Select 'ArduinoBridge' in the Hierarchy.\n" +
            "  5. In the Inspector → 'Simulate Scan' panel → type DEMO_ALPHA → click 'Simulate'.\n" +
            "     OR click one of the quick-simulate buttons under 'Registered Cards'.\n" +
            "  6. Check Console for:\n" +
            "        [REDboxTestListener] 🃏 Card scanned: Alpha (demo) (DEMO_ALPHA)  status=True\n" +
            "  7. Hardware test: disable debugMode in HardwareSettings, plug Arduino,\n" +
            "     press Play → Inspector → Connect → Activate Scanner → scan NFC card."
        );

        EditorUtility.FocusProjectWindow();
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(kScenePath));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing;
        T asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static void WriteListenerScript()
    {
        string fullPath = Path.Combine(Application.dataPath.Replace("/Assets", ""),
                                       kListenerPath);
        if (File.Exists(fullPath)) return;  // never overwrite user edits

        string src = @"// REDboxTestListener.cs — auto-generated by REDboxTestSceneSetup
// Place this in your scene to validate that EventManager events fire correctly.
// Safe to delete once you're satisfied with the integration.
using UnityEngine;

public class REDboxTestListener : MonoBehaviour
{
    private void OnEnable()
    {
        if (EventManager.Instance == null)
        {
            Debug.LogWarning(""[REDboxTestListener] EventManager not found in scene!"");
            return;
        }
        EventManager.Instance.OnCardScanned.AddListener(OnCardScanned);
        EventManager.Instance.OnScannerMissing.AddListener(OnScannerMissing);
        Debug.Log(""[REDboxTestListener] Subscribed to EventManager events."");
    }

    private void OnDisable()
    {
        if (EventManager.Instance == null) return;
        EventManager.Instance.OnCardScanned.RemoveListener(OnCardScanned);
        EventManager.Instance.OnScannerMissing.RemoveListener(OnScannerMissing);
    }

    private void OnCardScanned(Card card, bool status)
    {
        Debug.Log($""[REDboxTestListener] \ud83c\udccf Card scanned: {card.cardName} ({card.cardId})  status={status}"");
    }

    private void OnScannerMissing(bool missing)
    {
        Debug.LogWarning($""[REDboxTestListener] \u26a0 ScannerMissing event: {missing}"");
    }
}
";
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllText(fullPath, src);
    }
}
