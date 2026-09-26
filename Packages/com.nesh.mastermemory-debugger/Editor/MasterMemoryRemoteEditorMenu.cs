using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger.Editor
{
    /// <summary>
    /// Tools &gt; MasterMemory Debugger &gt; Remote Editing: creates the scenes of the remote editing setup and builds the
    /// remote editor tool (a desktop Development Build of this project that only contains the tool scene).
    /// </summary>
    static class MasterMemoryRemoteEditorMenu
    {
        const string Root = "Tools/MasterMemory Debugger/Remote Editing/";
        const string SceneFolder = "Assets/MasterMemoryDebugger";
        internal const string ToolScenePath = SceneFolder + "/MasterMemoryRemoteEditor.unity";
        internal const string ExampleScenePath = SceneFolder + "/MasterMemoryExampleGame.unity";
        const string ExampleLauncherType = "Nesh.MasterMemoryDebugger.Samples.BasicExample.ExampleDebuggerLauncher, Nesh.MasterMemoryDebugger.Samples.BasicExample";
        const string ToolName = "MasterMemoryRemoteEditor";

        [MenuItem(Root + "Create Example Game Scene", false, 100)]
        static void CreateExampleScene()
        {
            var launcher = Type.GetType(ExampleLauncherType);
            if (launcher == null)
            {
                EditorUtility.DisplayDialog("MasterMemory Debugger",
                    "Import the Basic Example sample first (Window > Package Manager > MasterMemory Runtime Debugger > Samples).", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var component = new GameObject("Example Debugger").AddComponent(launcher);
            // the game side of remote editing: listen for the tool at start
            var serialized = new SerializedObject(component);
            var startRemote = serialized.FindProperty("startRemoteServer");
            if (startRemote != null) startRemote.boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Save(scene, ExampleScenePath);
            Debug.Log($"[MasterMemoryDebugger] Created {ExampleScenePath}: press Play, F8 opens the debugger; the pairing code for the remote editor tool is in the Console and the Remote dialog.");
        }

        [MenuItem(Root + "Create Remote Editor Tool Scene", false, 101)]
        static void CreateToolSceneMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateToolScene();
            Debug.Log($"[MasterMemoryDebugger] Created {ToolScenePath}: press Play to use the Editor as the remote editor tool, or build it with Build Remote Editor Tool.");
        }

        [MenuItem(Root + "Build Remote Editor Tool…", false, 102)]
        static void BuildTool()
        {
            if (!File.Exists(ToolScenePath))
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                CreateToolScene();
            }

            var target = DesktopTarget();
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var folder = EditorUtility.SaveFolderPanel("Remote editor tool output folder", Path.Combine(projectRoot, "Builds"), ToolName);
            if (string.IsNullOrEmpty(folder)) return;
            var location = Path.Combine(folder, target == BuildTarget.StandaloneWindows64 ? ToolName + ".exe" : target == BuildTarget.StandaloneOSX ? ToolName + ".app" : ToolName);

            // a window, and its own data folder / PlayerPrefs (the product name), without changing the game's settings
            var productName = PlayerSettings.productName;
            var fullScreenMode = PlayerSettings.fullScreenMode;
            var width = PlayerSettings.defaultScreenWidth;
            var height = PlayerSettings.defaultScreenHeight;
            var resizable = PlayerSettings.resizableWindow;
            var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            BuildReport report;
            try
            {
                PlayerSettings.productName = productName + " Remote Editor";
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.defaultScreenWidth = 1600;
                PlayerSettings.defaultScreenHeight = 900;
                PlayerSettings.resizableWindow = true;
                // Mono: MessagePack can serialize the records without generated resolvers
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ToolScenePath },
                    locationPathName = location,
                    target = target,
                    // the debugger only runs in Development Builds
                    options = BuildOptions.Development,
                });
            }
            finally
            {
                PlayerSettings.productName = productName;
                PlayerSettings.fullScreenMode = fullScreenMode;
                PlayerSettings.defaultScreenWidth = width;
                PlayerSettings.defaultScreenHeight = height;
                PlayerSettings.resizableWindow = resizable;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, backend);
            }

            if (report.summary.result != BuildResult.Succeeded)
            {
                EditorUtility.DisplayDialog("MasterMemory Debugger", $"The remote editor tool was not built ({report.summary.result}). See the Console.", "OK");
                return;
            }
            Debug.Log($"[MasterMemoryDebugger] Remote editor tool built: {location}");
            EditorUtility.RevealInFinder(location);
        }

        static void CreateToolScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("Remote Editor").AddComponent<MasterMemoryRemoteEditor>();
            Save(scene, ToolScenePath);
        }

        static void Save(UnityEngine.SceneManagement.Scene scene, string path)
        {
            if (!AssetDatabase.IsValidFolder(SceneFolder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(SceneFolder));
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.Refresh();
        }

        /// <summary>The desktop platform of this Editor (its build support is installed with the Editor).</summary>
        internal static BuildTarget DesktopTarget()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                    return BuildTarget.StandaloneOSX;
                case RuntimePlatform.LinuxEditor:
                    return BuildTarget.StandaloneLinux64;
                default:
                    return BuildTarget.StandaloneWindows64;
            }
        }
    }
}
