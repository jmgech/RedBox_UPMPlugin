using UnityEngine;

/// <summary>
/// Guarantees a runtime settings entry-point in Play Mode.
/// If no Canvas menu instance exists in the loaded scene, we spawn one and let
/// RuntimeSettingsCanvasMenu recover to IMGUI mode when layout refs are missing.
/// </summary>
public static class RuntimeSettingsBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeSettingsMenuExists()
    {
        RuntimeSettingsCanvasMenu existing = Object.FindAnyObjectByType<RuntimeSettingsCanvasMenu>(FindObjectsInactive.Include);
        if (existing != null)
            return;

        GameObject host = new GameObject("[RuntimeSettingsCanvasMenu AutoBootstrap]");
        host.AddComponent<RuntimeSettingsCanvasMenu>();
    }
}
