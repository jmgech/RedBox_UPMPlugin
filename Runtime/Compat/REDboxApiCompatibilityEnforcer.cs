#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace REDbox.Compat
{
    [InitializeOnLoad]
    internal static class REDboxApiCompatibilityEnforcer
    {
        private const string SessionPromptKey = "REDbox.Compat.ApiLevelPromptShown";

        static REDboxApiCompatibilityEnforcer()
        {
            EditorApplication.delayCall += ValidateApiCompatibility;
        }

        private static void ValidateApiCompatibility()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            BuildTargetGroup targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            ApiCompatibilityLevel currentLevel = PlayerSettings.GetApiCompatibilityLevel(targetGroup);
            if (IsSupported(currentLevel))
            {
                SessionState.EraseBool(SessionPromptKey);
                return;
            }

            if (SessionState.GetBool(SessionPromptKey, false))
            {
                return;
            }

            SessionState.SetBool(SessionPromptKey, true);

            const string title = "REDbox API Compatibility Required";
            string message =
                "REDbox requires .NET Framework API compatibility for this project.\n\n" +
                "Current: " + currentLevel + "\n\n" +
                "Select 'Fix Automatically' to set Api Compatibility Level to .NET Framework and recompile.";

            bool autoFix = EditorUtility.DisplayDialog(title, message, "Fix Automatically", "Later");
            if (!autoFix)
            {
                Debug.LogError("[REDbox] API Compatibility Level is not set to .NET Framework 4.x. Runtime assemblies will not compile until updated in Player Settings.");
                return;
            }

            PlayerSettings.SetApiCompatibilityLevel(targetGroup, ApiCompatibilityLevel.NET_Unity_4_8);
            AssetDatabase.SaveAssets();

            Debug.Log("[REDbox] Updated Api Compatibility Level to .NET Framework 4.x (.NET_Unity_4_8). Unity will recompile scripts.");
            EditorUtility.DisplayDialog("REDbox Updated Settings", "Api Compatibility Level was set to .NET Framework 4.x. Unity will now recompile.", "OK");
        }

        private static bool IsSupported(ApiCompatibilityLevel level)
        {
            return level == ApiCompatibilityLevel.NET_4_6 ||
                   level == ApiCompatibilityLevel.NET_Unity_4_8;
        }
    }
}
#endif
