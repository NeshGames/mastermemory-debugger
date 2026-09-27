using UnityEngine;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// The debugger's UI assets (layout, style sheet, theme). They are not in a Resources folder, so no build contains them
    /// by default: the Editor loads them from the package, and the build processor of the package copies them into a
    /// temporary Resources folder only for Development Builds that include the debugger UI.
    /// </summary>
    internal static class MasterMemoryDebuggerAssets
    {
        /// <summary>Resources path of the copies made for a Development Build.</summary>
        public const string ResourcesFolder = "MasterMemoryDebugger/";

        // Resources names of the copies. They must differ: the UXML also holds a (nearly empty) inline StyleSheet, which
        // Resources.Load<StyleSheet> returned instead of the USS when both had the same name, so builds lost the styles.
        public const string LayoutName = "MasterMemoryDebuggerLayout";
        public const string StyleName = "MasterMemoryDebuggerStyle";
        public const string ThemeName = "MasterMemoryDebuggerTheme";

        // .meta GUIDs of Runtime/UI/Layout: found wherever the package is (Packages/, a local path, Assets/)
        public const string LayoutGuid = "77a202a7071242eb8de1e30667caeb6f";
        public const string StyleGuid = "109368658aa34c9f97d4dfa867261d58";
        public const string ThemeGuid = "72598048e1c94e68b0b91d0185b438f0";

        public static VisualTreeAsset Layout => Load<VisualTreeAsset>(LayoutGuid, LayoutName);

        public static StyleSheet Style => Load<StyleSheet>(StyleGuid, StyleName);

        public static ThemeStyleSheet Theme => Load<ThemeStyleSheet>(ThemeGuid, ThemeName);

        /// <summary>False in a build made with Include Debugger UI turned off (the debugger can not be opened).</summary>
        public static bool IsUIIncluded => Layout != null;

        static T Load<T>(string guid, string name) where T : Object
        {
#if UNITY_EDITOR
            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path))
            {
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) return asset;
            }
#endif
            return Resources.Load<T>(ResourcesFolder + name);
        }
    }
}
