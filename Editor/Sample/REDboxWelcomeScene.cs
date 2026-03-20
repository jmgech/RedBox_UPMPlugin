using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;

/// <summary>
/// Tools → REDbox → Welcome Scene
///
/// Creates (or opens) Assets/REDbox/REDbox_Welcome.unity — a fully wired
/// sample scene that ships with the onboarding wizard and live monitor.
///
/// Scene contents
///   Main Camera              — dark background, tagged MainCamera
///   EventManager             — event bus
///   MainThreadDispatcher     — serial-to-main-thread bridge
///   ArduinoBridge            — wired with HardwareSettings + two demo cards
///   REDboxOnboardingWizard   — four-screen onboarding overlay
///   CardScanToast            — fallback scan notification
/// </summary>
public static class REDboxWelcomeScene
{
    private const string kSceneDir  = "Assets/REDbox";
    private const string kScenePath = kSceneDir + "/REDbox_Welcome.unity";
    private const string kHwPath    = kSceneDir + "/REDbox_HardwareSettings.asset";
    private const string kCard1     = kSceneDir + "/Demo_Alpha.asset";
    private const string kCard2     = kSceneDir + "/Demo_Beta.asset";

    // ── Menu item ─────────────────────────────────────────────────────────────

    [MenuItem("Tools/REDbox/Welcome Scene", priority = -10)]
    public static void OpenOrCreate()
    {
        // Already exists → open it.
        if (File.Exists(Path.Combine(Application.dataPath,
                                     kScenePath.Substring("Assets/".Length))))
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(kScenePath);
            return;
        }

        BuildScene();
    }

    // ── Scene builder ─────────────────────────────────────────────────────────

    private static void BuildScene()
    {
        // Folder
        if (!AssetDatabase.IsValidFolder(kSceneDir))
            AssetDatabase.CreateFolder("Assets", "REDbox");

        // ── HardwareSettings ──────────────────────────────────────────────────
        HardwareSettings hw = ScriptableObject.CreateInstance<HardwareSettings>();
        hw.debugMode      = false;
        hw.autoDetectPort = true;
        hw.baudRate       = 9600;
        AssetDatabase.CreateAsset(hw, kHwPath);

        // ── Demo cards ────────────────────────────────────────────────────────
        CharacterCard alpha = ScriptableObject.CreateInstance<CharacterCard>();
        alpha.cardId      = "DEMO_ALPHA";
        alpha.cardName    = "Alpha";
        alpha.cardType    = "Character";
        alpha.hp          = 100;
        alpha.mp          = 50;
        alpha.at          = 30;
        alpha.description = "Demo card — created by the REDbox Welcome Scene.";
        AssetDatabase.CreateAsset(alpha, kCard1);

        CharacterCard beta = ScriptableObject.CreateInstance<CharacterCard>();
        beta.cardId      = "DEMO_BETA";
        beta.cardName    = "Beta";
        beta.cardType    = "Character";
        beta.hp          = 80;
        beta.mp          = 80;
        beta.at          = 50;
        beta.description = "Demo card — created by the REDbox Welcome Scene.";
        AssetDatabase.CreateAsset(beta, kCard2);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Reload from disk so Unity tracks the references properly after scene creation
        hw    = AssetDatabase.LoadAssetAtPath<HardwareSettings>(kHwPath);
        alpha = AssetDatabase.LoadAssetAtPath<CharacterCard>(kCard1);
        beta  = AssetDatabase.LoadAssetAtPath<CharacterCard>(kCard2);

        // ── Scene ─────────────────────────────────────────────────────────────
        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Camera
        var camGo  = new GameObject("Main Camera");
        var cam    = camGo.AddComponent<Camera>();
        cam.clearFlags       = CameraClearFlags.SolidColor;
        cam.backgroundColor  = new Color(0.07f, 0.09f, 0.13f);
        cam.tag              = "MainCamera";
        camGo.transform.position = new Vector3(0f, 0f, -10f);

        // EventManager
        new GameObject("EventManager").AddComponent<EventManager>();

        // MainThreadDispatcher
        new GameObject("MainThreadDispatcher").AddComponent<MainThreadDispatcher>();

        // ArduinoBridge — assign directly to public fields so Unity serializes refs
        var bridgeGo = new GameObject("ArduinoBridge");
        var bridge   = bridgeGo.AddComponent<ArduinoBridge>();
        bridge.settings      = hw;
        bridge.cardDataArray = new Card[] { alpha, beta };
        EditorUtility.SetDirty(bridge);

        // Onboarding wizard
        new GameObject("REDboxOnboardingWizard").AddComponent<REDboxOnboardingWizard>();

        // CardScanToast (OnGUI fallback overlay)
        new GameObject("CardScanToast").AddComponent<CardScanToast>();

        // ── Save ──────────────────────────────────────────────────────────────
        EditorSceneManager.SaveScene(scene, kScenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[REDbox] Welcome scene created: {kScenePath}");

        if (EditorUtility.DisplayDialog(
                "REDbox — Welcome Scene",
                $"Scene created at {kScenePath}\n\n" +
                "Press Play to launch the wizard.\n" +
                "Use the Simulate field on screen 4 to test without hardware.",
                "Open Scene", "Later"))
        {
            EditorSceneManager.OpenScene(kScenePath);
        }
    }

    // ── First-run hint (non-intrusive console log on package install) ─────────

    [InitializeOnLoad]
    private static class FirstRunHint
    {
        static FirstRunHint()
        {
            const string key = "REDbox.FirstRun.v031";
            if (EditorPrefs.GetBool(key, false)) return;
            EditorPrefs.SetBool(key, true);
            EditorApplication.delayCall += () =>
                Debug.Log("[REDbox] Package ready. " +
                          "Go to  Tools → REDbox → Welcome Scene  to get started.");
        }
    }
}
