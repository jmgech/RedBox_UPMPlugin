using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class REDboxVisualNovelSampleScene
{
    private const string SceneDir = "Assets/REDbox/VNSample";
    private const string ScenePath = SceneDir + "/REDbox_VisualNovel_Sample.unity";
    private const string StoryPath = SceneDir + "/REDbox_VN_Story.asset";
    private const string HardwarePath = SceneDir + "/REDbox_VN_HardwareSettings.asset";

    [MenuItem("Tools/REDbox/Samples/Create Visual Novel Sample", priority = -5)]
    public static void OpenOrCreate()
    {
        if (File.Exists(ToAbsolutePath(ScenePath)))
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
            return;
        }

        BuildScene();
    }

    private static void BuildScene()
    {
        EnsureFolders();

        var hw = CreateHardwareSettings();
        var story = CreateStoryAsset();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraGo = new GameObject("Main Camera");
        var camera = cameraGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.04f, 0.03f, 0.07f);
        cameraGo.tag = "MainCamera";
        cameraGo.transform.position = new Vector3(0f, 0f, -10f);

        new GameObject("EventManager").AddComponent<EventManager>();
        new GameObject("MainThreadDispatcher").AddComponent<MainThreadDispatcher>();

        var bridgeGo = new GameObject("ArduinoBridge");
        var bridge = bridgeGo.AddComponent<ArduinoBridge>();
        bridge.settings = hw;
        bridge.cardDataArray = Array.Empty<Card>();
        EditorUtility.SetDirty(bridge);

        var controllerGo = new GameObject("REDboxVNSampleController");
        var controller = controllerGo.AddComponent<REDboxVNSampleController>();
        controller.storyData = story;
        controller.autoStart = true;
        controller.autoAdvanceLinearNodes = false;
        controller.verboseLogs = true;
        EditorUtility.SetDirty(controller);

        var overlayGo = new GameObject("REDboxVNSampleOverlay");
        var overlay = overlayGo.AddComponent<REDboxVNSampleOverlay>();
        overlay.controller = controller;
        overlay.showOverlay = true;
        overlay.showDebugPanel = true;
        EditorUtility.SetDirty(overlay);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (EditorUtility.DisplayDialog(
            "REDbox Visual Novel Sample",
            "Sample scene created.\n\nPlay Mode starts in no-hardware mode by default. Use the overlay simulation controls to progress story beats and test card routing.",
            "Open Scene",
            "Later"))
        {
            EditorSceneManager.OpenScene(ScenePath);
        }
    }

    private static HardwareSettings CreateHardwareSettings()
    {
        var existing = AssetDatabase.LoadAssetAtPath<HardwareSettings>(HardwarePath);
        if (existing != null) return existing;

        var hw = ScriptableObject.CreateInstance<HardwareSettings>();
        hw.debugMode = true;
        hw.autoDetectPort = true;
        hw.baudRate = 9600;

        AssetDatabase.CreateAsset(hw, HardwarePath);
        AssetDatabase.SaveAssets();
        return AssetDatabase.LoadAssetAtPath<HardwareSettings>(HardwarePath);
    }

    private static REDboxVNStoryData CreateStoryAsset()
    {
        var existing = AssetDatabase.LoadAssetAtPath<REDboxVNStoryData>(StoryPath);
        if (existing != null) return existing;

        var story = ScriptableObject.CreateInstance<REDboxVNStoryData>();
        story.storyTitle = "Sakura Protocol: REDbox Chronicles";
        story.startNodeId = "intro";
        story.nodes = new[]
        {
            new VNNode
            {
                id = "intro",
                chapter = "Chapter 1 - Arrival",
                speaker = "Aiko",
                text = "Welcome to the REDbox lab. This story teaches how card scans become gameplay events.",
                nextNodeId = "lore_gate",
            },
            new VNNode
            {
                id = "lore_gate",
                chapter = "Chapter 1 - Memory Card",
                speaker = "Aiko",
                text = "Present a Lore/Memory card to unlock the dossier archive.",
                requiresCard = true,
                requiredCard = new VNCardRequirement
                {
                    expectedTaxonomyType = RedboxCardType.Lore,
                    expectedSubtype = "memory",
                    allowUnknownCard = true,
                },
                nextNodeId = "world_gate",
            },
            new VNNode
            {
                id = "world_gate",
                chapter = "Chapter 2 - Scene Shift",
                speaker = "System",
                text = "Now scan a World/Location card to change context from lab to city district.",
                requiresCard = true,
                requiredCard = new VNCardRequirement
                {
                    expectedTaxonomyType = RedboxCardType.World,
                    expectedSubtype = "location",
                    allowUnknownCard = true,
                },
                nextNodeId = "actor_gate",
            },
            new VNNode
            {
                id = "actor_gate",
                chapter = "Chapter 2 - Companion",
                speaker = "Aiko",
                text = "Scan an Actor/Ally card to recruit your first companion.",
                requiresCard = true,
                requiredCard = new VNCardRequirement
                {
                    expectedTaxonomyType = RedboxCardType.Actor,
                    expectedSubtype = "ally",
                    allowUnknownCard = true,
                },
                nextNodeId = "branch_intro",
            },
            new VNNode
            {
                id = "branch_intro",
                chapter = "Chapter 3 - Tactical Decision",
                speaker = "Ren",
                text = "Choose your next move. You can click a choice or scan an Instruction card for card-driven branching.",
                choices = new[]
                {
                    new VNChoice
                    {
                        id = "scan_attack",
                        label = "Scan an attack instruction",
                        requiresCard = true,
                        requiredCard = new VNCardRequirement
                        {
                            expectedTaxonomyType = RedboxCardType.Instruction,
                            expectedSubtype = "attack",
                            allowUnknownCard = true,
                        },
                        nextNodeId = "ending_blaze",
                    },
                    new VNChoice
                    {
                        id = "scan_effect",
                        label = "Scan an effect instruction",
                        requiresCard = true,
                        requiredCard = new VNCardRequirement
                        {
                            expectedTaxonomyType = RedboxCardType.Instruction,
                            expectedSubtype = "effect",
                            allowUnknownCard = true,
                        },
                        nextNodeId = "ending_echo",
                    },
                    new VNChoice
                    {
                        id = "manual_route",
                        label = "Proceed without scan (UI choice)",
                        requiresCard = false,
                        nextNodeId = "ending_manual",
                    },
                },
            },
            new VNNode
            {
                id = "ending_blaze",
                chapter = "Ending A",
                speaker = "Narrator",
                text = "Attack route complete. You just validated instruction.attack card handling end-to-end.",
                isEnding = true,
            },
            new VNNode
            {
                id = "ending_echo",
                chapter = "Ending B",
                speaker = "Narrator",
                text = "Effect route complete. This branch demonstrates alternate card subtype outcomes.",
                isEnding = true,
            },
            new VNNode
            {
                id = "ending_manual",
                chapter = "Ending C",
                speaker = "Narrator",
                text = "Manual branch complete. The sample supports UI-only progression when no hardware is available.",
                isEnding = true,
            },
        };

        AssetDatabase.CreateAsset(story, StoryPath);
        AssetDatabase.SaveAssets();
        return AssetDatabase.LoadAssetAtPath<REDboxVNStoryData>(StoryPath);
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/REDbox"))
            AssetDatabase.CreateFolder("Assets", "REDbox");

        if (!AssetDatabase.IsValidFolder(SceneDir))
            AssetDatabase.CreateFolder("Assets/REDbox", "VNSample");
    }

    private static string ToAbsolutePath(string assetPath)
    {
        if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            return assetPath;

        string rel = assetPath.Substring("Assets/".Length);
        return Path.Combine(Application.dataPath, rel);
    }
}
