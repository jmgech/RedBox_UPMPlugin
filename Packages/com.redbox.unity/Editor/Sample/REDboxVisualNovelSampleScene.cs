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
        overlay.developerMode = false;
        overlay.toggleDeveloperModeKey = KeyCode.F3;
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
        story.storyTitle = "Sakura Protocol: Academy Trial";
        story.startNodeId = "intro";
        story.nodes = new[]
        {
            new VNNode
            {
                id = "intro",
                chapter = "Prologue - First Day",
                speaker = "Aiko",
                text = "You made it. I am Aiko, your guide for the Academy Trial. Relax, this is half training and half story.",
                learningHint = "A VN starts with character voice and scene context.",
                nextNodeId = "intro_ren",
            },
            new VNNode
            {
                id = "intro_ren",
                chapter = "Prologue - First Day",
                speaker = "Ren",
                text = "I will monitor your card decisions. Every card you use changes the story state in real time.",
                learningHint = "REDbox card scans are story inputs, not just UI buttons.",
                nextNodeId = "memory_brief",
            },
            new VNNode
            {
                id = "memory_brief",
                chapter = "Chapter 1 - Why Lore Cards Matter",
                speaker = "Ren",
                text = "Our city map is encrypted. We need one memory shard from the archive before we can move.",
                learningHint = "Lore cards unlock narrative knowledge.",
                nextNodeId = "lore_gate",
            },
            new VNNode
            {
                id = "lore_gate",
                chapter = "Chapter 1 - Memory Card",
                speaker = "Aiko",
                text = "Show me a Lore card tagged as memory. If you are testing without hardware, use the recommended card button.",
                learningHint = "Required card: Lore/memory.",
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
                text = "Archive opened. Incident identified: unstable reactor at Moonlit District.",
                learningHint = "Correct scan instantly transitions to the next narrative beat.",
                nextNodeId = "world_brief",
            },
            new VNNode
            {
                id = "world_brief",
                chapter = "Chapter 2 - Route Planning",
                speaker = "Ren",
                text = "Now we pick the destination card. World cards decide where this scene takes place.",
                learningHint = "World cards control location context.",
                nextNodeId = "world_gate",
            },
            new VNNode
            {
                id = "world_gate",
                chapter = "Chapter 2 - World Control",
                speaker = "System",
                text = "Select a World/location card to travel from Academy HQ to Moonlit District.",
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
                text = "Neon rain, warning sirens, crowds in panic. The power core is close to collapse.",
                learningHint = "A world/location scan can be mapped to loading visuals, audio, and NPC sets.",
                nextNodeId = "ally_brief",
            },
            new VNNode
            {
                id = "ally_brief",
                chapter = "Chapter 3 - Party Formation",
                speaker = "Aiko",
                text = "You need a partner for core access. Actor cards bring characters into the active scene.",
                learningHint = "Actor cards represent people joining gameplay state.",
                nextNodeId = "actor_gate",
            },
            new VNNode
            {
                id = "actor_gate",
                chapter = "Chapter 3 - Team Building",
                speaker = "Aiko",
                text = "Recruit Hikari with an Actor/ally card.",
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
                text = "Core stability is falling. Give one final instruction. Your command defines this ending.",
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
                text = "Hikari channels your attack command. The reactor shell shatters, then stabilizes. Loud, risky, successful.",
                learningHint = "Validated taxonomy match: Instruction + attack -> unique narrative outcome.",
                isEnding = true,
            },
            new VNNode
            {
                id = "ending_echo",
                chapter = "Ending B - Azure Route",
                speaker = "Narrator",
                text = "You choose an effect command and tune the harmonics. The reactor calms with zero collateral.",
                learningHint = "Validated alternate subtype branch: Instruction + effect -> different ending.",
                isEnding = true,
            },
            new VNNode
            {
                id = "ending_manual",
                chapter = "Ending C - Training Route",
                speaker = "Narrator",
                text = "Fallback protocol succeeds. The mission still completes in UI-only mode.",
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
