using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger.Editor
{
    /// <summary>
    /// The debugger's UI assets and the settings asset are kept outside Resources, so release builds contain none of them
    /// (nor the font / PanelSettings the settings reference). For a Development Build they are copied into a temporary
    /// Resources folder (<see cref="GeneratedFolder"/>), which is deleted when the build ends.
    /// </summary>
    sealed class MasterMemoryDebuggerBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        internal const string GeneratedFolder = "Assets/MasterMemoryDebuggerBuild";
        const string ResourcesFolder = GeneratedFolder + "/Resources";

        /// <summary>Set while the remote editor tool is built: it always needs the UI.</summary>
        internal static bool ForceIncludeUI;

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            Clean();
            // a failed build does not call OnPostprocessBuild
            EditorApplication.delayCall += Clean;
            if ((report.summary.options & BuildOptions.Development) == 0) return;

            var settings = MasterMemoryDebuggerSettingsProvider.FindSettingsAsset();
            if (settings != null && !settings.Enabled) return;
            var includeUI = ForceIncludeUI || settings == null || settings.IncludeDebuggerUI;

            CreateFolder(ResourcesFolder);
            if (includeUI)
            {
                CreateFolder(ResourcesFolder + "/" + MasterMemoryDebuggerAssets.ResourcesFolder.TrimEnd('/'));
                Copy(MasterMemoryDebuggerAssets.LayoutGuid, MasterMemoryDebuggerAssets.LayoutName, ".uxml");
                Copy(MasterMemoryDebuggerAssets.StyleGuid, MasterMemoryDebuggerAssets.StyleName, ".uss");
                Copy(MasterMemoryDebuggerAssets.ThemeGuid, MasterMemoryDebuggerAssets.ThemeName, ".tss");
            }
            if (settings != null) CopySettings(settings, includeUI);
            AssetDatabase.SaveAssets();
        }

        public void OnPostprocessBuild(BuildReport report) => Clean();

        [InitializeOnLoadMethod]
        static void CleanLeftovers()
        {
            // e.g. the Editor was closed during a build
            if (!BuildPipeline.isBuildingPlayer) EditorApplication.delayCall += Clean;
        }

        internal static void Clean()
        {
            if (BuildPipeline.isBuildingPlayer || !AssetDatabase.IsValidFolder(GeneratedFolder)) return;
            AssetDatabase.DeleteAsset(GeneratedFolder);
        }

        static void Copy(string guid, string name, string extension)
        {
            var source = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(source))
            {
                Debug.LogError($"[MasterMemoryDebugger] The debugger UI asset {name}{extension} was not found; the debugger can not be opened in this build.");
                return;
            }
            AssetDatabase.CopyAsset(source, ResourcesFolder + "/" + MasterMemoryDebuggerAssets.ResourcesFolder + name + extension);
        }

        static void CopySettings(MasterMemoryDebuggerSettings settings, bool includeUI)
        {
            var source = AssetDatabase.GetAssetPath(settings);
            // an asset still in a Resources folder is in every build anyway
            if (source.Contains("/Resources/")) return;
            var destination = ResourcesFolder + "/" + MasterMemoryDebuggerSettings.ResourcesPath + ".asset";
            if (!AssetDatabase.CopyAsset(source, destination)) return;
            if (includeUI) return;
            // no UI: do not pull the font / PanelSettings into the build
            var copy = AssetDatabase.LoadAssetAtPath<MasterMemoryDebuggerSettings>(destination);
            if (copy == null) return;
            copy.Font = null;
            copy.PanelSettings = null;
            EditorUtility.SetDirty(copy);
        }

        static void CreateFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var slash = folder.LastIndexOf('/');
            CreateFolder(folder.Substring(0, slash));
            AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
        }
    }
}
