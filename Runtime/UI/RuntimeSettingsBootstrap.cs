using UnityEngine;

/// <summary>
/// Guarantees a runtime settings entry-point in Play Mode.
/// If no menu instance exists in the loaded scene, spawns the lightweight IMGUI
/// version (RuntimeSettingsMenu) which always works without a Canvas hierarchy.
/// The Canvas version (RuntimeSettingsCanvasMenu) must be placed explicitly in a
/// scene with a properly built Canvas — use Tools > REDbox > UI > Create Runtime
/// Settings Canvas Menu to generate one.
/// </summary>
public static class RuntimeSettingsBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeSettingsMenuExists()
    {
        // Canvas menu takes priority if already present in the scene.
        if (Object.FindAnyObjectByType<RuntimeSettingsCanvasMenu>(FindObjectsInactive.Include) != null)
            return;

        // IMGUI fallback already present — nothing to do.
        if (Object.FindAnyObjectByType<RuntimeSettingsMenu>(FindObjectsInactive.Include) != null)
            return;

        // Spawn the lightweight IMGUI menu; it requires no Canvas and always works.
        GameObject host = new GameObject("[RuntimeSettingsMenu AutoBootstrap]");
        var menu = host.AddComponent<RuntimeSettingsMenu>();
        menu.showOnStart           = false;
        menu.enableLegacyImGuiMenu = true;
    }
}
