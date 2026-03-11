#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools → REDbox → Validate Scene
///
/// Checks the active scene for common REDbox setup mistakes and prints a
/// structured report in a modal dialog. Run this before entering Play Mode
/// to catch wiring issues early.
///
/// Checks performed:
///   ● ArduinoBridge present and HardwareSettings assigned
///   ● EventManager present
///   ● MainThreadDispatcher present
///   ● ArduinoBridge.cardDataArray not empty
///   ● No duplicate Card IDs in cardDataArray
///   ● Each Card asset has a non-empty cardId and cardName
///   ● HardwareSettings: port is set (or auto-detect is on)
///   ● No multiple ArduinoBridge / EventManager instances
/// </summary>
public static class REDboxSceneValidator
{
    // ── Menu ──────────────────────────────────────────────────────────────────
    [MenuItem("Tools/REDbox/Validate Scene", priority = 10)]
    public static void Validate()
    {
        var results = RunChecks();

        int errors   = results.Count(r => r.level == Level.Error);
        int warnings = results.Count(r => r.level == Level.Warning);
        int passed   = results.Count(r => r.level == Level.OK);

        string body = FormatReport(results, errors, warnings, passed);

        string title = errors > 0
            ? $"REDbox Scene Validation — {errors} Error(s)"
            : warnings > 0
                ? $"REDbox Scene Validation — {warnings} Warning(s)"
                : "REDbox Scene Validation — All checks passed";

        if (errors > 0 || warnings > 0)
            EditorUtility.DisplayDialog(title, body, "OK");
        else
            EditorUtility.DisplayDialog(title, body, "Great!");

        foreach (var r in results)
        {
            string prefix = $"[REDbox Validator]  {r.icon} {r.message}";
            if      (r.level == Level.Error)   Debug.LogError(prefix);
            else if (r.level == Level.Warning) Debug.LogWarning(prefix);
            else                               Debug.Log(prefix);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CHECK LOGIC
    // ═════════════════════════════════════════════════════════════════════════

    private static List<Result> RunChecks()
    {
        var results = new List<Result>();

        // ── ArduinoBridge ─────────────────────────────────────────────────────
        var bridges = FindAll<ArduinoBridge>();
        if (bridges.Count == 0)
        {
            results.Add(Error("ArduinoBridge not found in scene. " +
                              "Add via Tools → REDbox → Welcome Scene, or create the GameObject manually."));
        }
        else
        {
            if (bridges.Count > 1)
                results.Add(Warning($"{bridges.Count} ArduinoBridge instances found. Only one should exist."));

            var bridge = bridges[0];

            if (bridge.settings == null)
                results.Add(Error("ArduinoBridge.settings is null. " +
                                  "Create a HardwareSettings asset (Create → RK/Settings → Hardware Settings) and assign it."));
            else
            {
                var hw = bridge.settings;
                if (!hw.autoDetectPort && (string.IsNullOrWhiteSpace(hw.serialPort) || hw.serialPort == "COM3"))
                    results.Add(Warning("HardwareSettings.serialPort is still the default 'COM3'. " +
                                        "Set the correct port or enable Auto Detect Port."));
                else
                    results.Add(OK("HardwareSettings is assigned and port is configured."));
            }

            if (bridge.cardDataArray == null || bridge.cardDataArray.Length == 0)
            {
                results.Add(Warning("ArduinoBridge.cardDataArray is empty. " +
                                    "Drag Card assets into the array so scans can be resolved."));
            }
            else
            {
                // Duplicate ID check
                var idCounts = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
                var badCards = new List<string>();

                foreach (var card in bridge.cardDataArray)
                {
                    if (card == null) { badCards.Add("(null entry)"); continue; }

                    if (string.IsNullOrWhiteSpace(card.cardId))
                        results.Add(Warning($"Card '{card.cardName}' has an empty Card ID."));
                    else
                    {
                        string id = card.cardId.Trim().ToUpperInvariant();
                        idCounts.TryGetValue(id, out int cnt);
                        idCounts[id] = cnt + 1;
                    }

                    if (string.IsNullOrWhiteSpace(card.cardName))
                        results.Add(Warning($"A card with ID '{card.cardId}' has no Display Name."));
                }

                foreach (var kvp in idCounts.Where(kv => kv.Value > 1))
                    results.Add(Error($"Duplicate Card ID '{kvp.Key}' appears {kvp.Value} times in cardDataArray."));

                if (badCards.Count > 0)
                    results.Add(Warning($"{badCards.Count} null slot(s) in cardDataArray."));

                int goodCards = bridge.cardDataArray.Length - badCards.Count;
                results.Add(OK($"{goodCards} card(s) registered in ArduinoBridge."));
            }
        }

        // ── EventManager ──────────────────────────────────────────────────────
        var eventManagers = FindAll<EventManager>();
        if (eventManagers.Count == 0)
            results.Add(Error("EventManager not found in scene. Card events will not fire."));
        else if (eventManagers.Count > 1)
            results.Add(Warning($"{eventManagers.Count} EventManager instances found. Only one should exist."));
        else
            results.Add(OK("EventManager present."));

        // ── MainThreadDispatcher ──────────────────────────────────────────────
        var dispatchers = FindAll<MainThreadDispatcher>();
        if (dispatchers.Count == 0)
            results.Add(Warning("MainThreadDispatcher not found in scene. " +
                                "ArduinoBridge will auto-create one at runtime, but it's cleaner to add it explicitly."));
        else
            results.Add(OK("MainThreadDispatcher present."));

        return results;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // REPORT
    // ═════════════════════════════════════════════════════════════════════════

    private static string FormatReport(List<Result> results, int errors, int warnings, int passed)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Scene: {SceneManager.GetActiveScene().name}");
        sb.AppendLine($"Errors: {errors}   Warnings: {warnings}   Passed: {passed}");
        sb.AppendLine();

        foreach (var r in results)
            sb.AppendLine($"{r.icon} {r.message}");

        return sb.ToString();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    private static List<T> FindAll<T>() where T : Component =>
        Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToList();

    private enum Level { OK, Warning, Error }

    private struct Result
    {
        public Level  level;
        public string message;
        public string icon => level == Level.Error ? "✖" : level == Level.Warning ? "⚠" : "✔";
    }

    private static Result OK(string msg)      => new Result { level = Level.OK,      message = msg };
    private static Result Warning(string msg) => new Result { level = Level.Warning, message = msg };
    private static Result Error(string msg)   => new Result { level = Level.Error,   message = msg };
}
#endif
