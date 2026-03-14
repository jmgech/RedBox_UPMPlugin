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

    [MenuItem("Tools/REDbox/Samples/Reset Visual Novel Story Data", priority = -4)]
    public static void ResetStoryDataAsset()
    {
        EnsureFolders();
        CreateOrReplaceStoryAsset();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog(
            "REDbox Visual Novel Sample",
            "Story data has been reset to the latest pedagogical sample flow.",
            "OK");
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

        return CreateOrReplaceStoryAsset();
    }

    private static REDboxVNStoryData CreateOrReplaceStoryAsset()
    {
        var existing = AssetDatabase.LoadAssetAtPath<REDboxVNStoryData>(StoryPath);
        if (existing != null)
        {
            AssetDatabase.DeleteAsset(StoryPath);
            AssetDatabase.Refresh();
        }

        var story = ScriptableObject.CreateInstance<REDboxVNStoryData>();
        story.storyTitle = "Sakura Protocol: First Day at REDbox Academy";
        story.startNodeId = "intro";
        story.nodes = new[]
        {
            new VNNode
            {
                id = "intro",
                chapter = "Prologue - First Day",
                speaker = "Aiko",
                text = "Welcome, Cadet. Today you will run one complete REDbox mission: recover a memory shard, travel to the right district, recruit an ally, and solve a crisis.",
                learningHint = "This sample teaches scan -> event -> game state changes in a full loop.",
                nextNodeId = "memory_brief",
            },
            new VNNode
            {
                id = "memory_brief",
                chapter = "Chapter 1 - Why Lore Cards Matter",
                speaker = "Ren",
                text = "The city map is encrypted. Only a Lore/Memory card can unlock the briefing archive.",
                learningHint = "Lore cards represent narrative knowledge and quest progression.",
                nextNodeId = "lore_gate",
            },
            new VNNode
            {
                id = "lore_gate",
                chapter = "Chapter 1 - Memory Card",
                speaker = "Aiko",
                text = "Scan a Lore card with subtype memory to reveal the mission dossier.",
                learningHint = "Required card: taxonomy Lore + subtype memory.",
                requiresCard = true,
                requiredCard = new VNCardRequirement
                {
                    expectedTaxonomyType = RedboxCardType.Lore,
                    expectedSubtype = "memory",
                    allowUnknownCard = true,
                },
                nextNodeId = "memory_success",
            },
            new VNNode
            {
                id = "memory_success",
                chapter = "Chapter 1 - Dossier Open",
                speaker = "System",
                text = "Dossier unlocked: 'Incident at Moonlit District'. Destination identified.",
                learningHint = "Successful scan immediately advances narrative state.",
                nextNodeId = "world_gate",
            },
            new VNNode
            {
                id = "world_gate",
                chapter = "Chapter 2 - World Control",
                speaker = "System",
                text = "Scan a World card with subtype location to move the mission from Academy HQ to Moonlit District.",
                learningHint = "World cards drive environment and scene context.",
                requiresCard = true,
                requiredCard = new VNCardRequirement
                {
                    expectedTaxonomyType = RedboxCardType.World,
                    expectedSubtype = "location",
                    allowUnknownCard = true,
                },
                nextNodeId = "world_success",
            },
            new VNNode
            {
                id = "world_success",
                chapter = "Chapter 2 - Arrival",
                speaker = "Narrator",
                text = "Neon rain falls over Moonlit District. Civilians are panicking near the power core.",
                learningHint = "A world/location scan can be mapped to loading visuals, audio, and NPC sets.",
                nextNodeId = "actor_gate",
            },
            new VNNode
            {
                id = "actor_gate",
                chapter = "Chapter 3 - Team Building",
                speaker = "Aiko",
                text = "You cannot enter the core alone. Scan an Actor card with subtype ally to recruit Hikari.",
                learningHint = "Actor cards represent playable allies or enemies entering the scene.",
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
                chapter = "Chapter 4 - Final Decision",
                speaker = "Ren",
                text = "Core stability at 12%. Choose a resolution path. Instruction cards branch outcomes based on subtype.",
                learningHint = "Instruction cards are command inputs that can create branching outcomes.",
                choices = new[]
                {
                    new VNChoice
                    {
                        id = "scan_attack",
                        label = "Overload core shields (attack instruction)",
                        learningHint = "Scan Instruction/attack to take a high-risk decisive route.",
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
                        label = "Stabilize core harmonics (effect instruction)",
                        learningHint = "Scan Instruction/effect for a safer support-oriented route.",
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
                        label = "Fallback protocol (UI-only path)",
                        learningHint = "Demonstrates accessibility: progress is still possible without hardware.",
                        requiresCard = false,
                        nextNodeId = "ending_manual",
                    },
                },
            },
            new VNNode
            {
                id = "ending_blaze",
                chapter = "Ending A - Crimson Route",
                speaker = "Narrator",
                text = "Hikari channels your command and blasts the corrupted shell. District saved in dramatic style.",
                learningHint = "Validated taxonomy match: Instruction + attack -> unique narrative outcome.",
                isEnding = true,
            },
            new VNNode
            {
                id = "ending_echo",
                chapter = "Ending B - Azure Route",
                speaker = "Narrator",
                text = "You reroute harmonics and calm the core. District saved with zero collateral damage.",
                learningHint = "Validated alternate subtype branch: Instruction + effect -> different ending.",
                isEnding = true,
            },
            new VNNode
            {
                id = "ending_manual",
                chapter = "Ending C - Training Route",
                speaker = "Narrator",
                text = "Fallback protocol succeeds. Mission complete even without physical cards.",
                learningHint = "Confirmed no-hardware compatibility for onboarding and CI-friendly demos.",
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
