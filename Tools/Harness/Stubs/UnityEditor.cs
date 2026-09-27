using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
namespace UnityEngine
{
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
}
namespace UnityEngine.UIElements
{
    public enum HelpBoxMessageType { None, Info, Warning, Error }
    public class HelpBox : VisualElement { public HelpBox(string text, HelpBoxMessageType messageType) { } }
    public enum FlexDirection { Column, ColumnReverse, Row, RowReverse }
    public struct StyleLength { public static implicit operator StyleLength(float v) => default; }
    public struct StyleFloat { public static implicit operator StyleFloat(float v) => default; }
    public partial interface IStyleExt { }
}
namespace UnityEditor
{
    public enum SettingsScope { User, Project }
    [AttributeUsage(AttributeTargets.Method)] public sealed class SettingsProviderAttribute : Attribute { }
    public class SettingsProvider
    {
        public SettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null) { settingsPath = path; }
        public string label { get; set; }
        public string settingsPath { get; }
        public Action<string, VisualElement> activateHandler { get; set; }
        public IEnumerable<string> keywords { get; set; }
    }
    public static class SettingsService { public static object OpenProjectSettings(string settingsPath = null) => null; }
    public class SerializedObject { public SerializedObject(UnityEngine.Object obj) { } public SerializedProperty FindProperty(string name) => null; public bool ApplyModifiedPropertiesWithoutUndo() => true; }
    public static class AssetDatabase
    {
        public static string[] FindAssets(string filter) => new string[0];
        public static string GUIDToAssetPath(string guid) => guid;
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object => null;
        public static void CreateAsset(UnityEngine.Object asset, string path) { }
        public static void SaveAssets() { }
        public static bool IsValidFolder(string path) => false;
        public static string CreateFolder(string parent, string newFolderName) => "";
        public static string GetAssetPath(UnityEngine.Object o) => "";
        public static void Refresh() { }
        public static bool CopyAsset(string path, string newPath) => true;
        public static void ImportAsset(string path) { }
        public static void ImportAsset(string path, ImportAssetOptions options) { }
        public static bool DeleteAsset(string path) => true;
        public static string MoveAsset(string oldPath, string newPath) => "";
        public static string GenerateUniqueAssetPath(string path) => path;
    }
    [Flags]
    public enum ImportAssetOptions
    {
        Default = 0,
        ForceUpdate = 1,
        ForceSynchronousImport = 8,
        ImportRecursive = 256,
        DontDownloadFromCacheServer = 8192,
        ForceUncompressedImport = 16384,
    }

    public static class EditorGUIUtility { public static void PingObject(UnityEngine.Object o) { } }
    public static class EditorUtility
    {
        public static bool DisplayDialog(string title, string message, string ok, string cancel = "") => true;
        public static string SaveFilePanel(string title, string directory, string defaultName, string extension) => "";
        public static string OpenFilePanel(string title, string directory, string extension) => "";
        public static void RevealInFinder(string path) { }
        public static string SaveFolderPanel(string title, string folder, string defaultName) => "";
        public static void SetDirty(UnityEngine.Object target) { }
    }
    public enum PlayModeStateChange { EnteredEditMode, ExitingEditMode, EnteredPlayMode, ExitingPlayMode }
    public static class EditorApplication { public static bool isPlaying => false; public static event Action<PlayModeStateChange> playModeStateChanged; public delegate void CallbackFunction(); public static CallbackFunction delayCall; }
    public static class AssemblyReloadEvents { public delegate void AssemblyReloadCallback(); public static event AssemblyReloadCallback beforeAssemblyReload; }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)] public sealed class MenuItem : Attribute
    {
        public MenuItem(string itemName) { }
        public MenuItem(string itemName, bool isValidateFunction) { }
        public MenuItem(string itemName, bool isValidateFunction, int priority) { }
    }
}
namespace UnityEditor.UIElements
{
    public class InspectorElement : VisualElement { public InspectorElement(SerializedObject obj) { } }
}
namespace UnityEditor
{
    [System.Flags] public enum BuildOptions { None = 0, Development = 1 }
    public static class TypeCache
    {
        public static System.Collections.Generic.List<System.Type> GetTypesWithAttribute<T>() where T : System.Attribute => new System.Collections.Generic.List<System.Type>();
        public static System.Collections.Generic.List<System.Type> GetTypesDerivedFrom<T>() => new System.Collections.Generic.List<System.Type>();
    }
}
namespace UnityEditor.Build
{
    public interface IOrderedCallback { int callbackOrder { get; } }
    public interface IPreprocessBuildWithReport : IOrderedCallback { void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report); }
    public interface IPostprocessBuildWithReport : IOrderedCallback { void OnPostprocessBuild(UnityEditor.Build.Reporting.BuildReport report); }
    public interface IUnityLinkerProcessor : IOrderedCallback { string GenerateAdditionalLinkXmlFile(UnityEditor.Build.Reporting.BuildReport report, UnityEditor.UnityLinker.UnityLinkerBuildPipelineData data); }
}
namespace UnityEditor.Build.Reporting
{
    public enum BuildResult { Unknown, Succeeded, Failed, Cancelled }
    public class BuildSummary { public UnityEditor.BuildOptions options => default; public BuildResult result => default; }
    public class BuildReport { public BuildSummary summary => null; }
}
namespace UnityEditor.UnityLinker
{
    public class UnityLinkerBuildPipelineData { }
}
namespace UnityEngine
{
    public enum FullScreenMode { ExclusiveFullScreen, FullScreenWindow, MaximizedWindow, Windowed }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { }
}
namespace UnityEditor
{
    public enum BuildTarget { StandaloneOSX = 2, StandaloneWindows64 = 19, StandaloneLinux64 = 24 }
    public enum ScriptingImplementation { Mono2x = 0, IL2CPP = 1 }
    public struct BuildPlayerOptions
    {
        public string[] scenes { get; set; }
        public string locationPathName { get; set; }
        public BuildTarget target { get; set; }
        public BuildOptions options { get; set; }
    }
    public static class BuildPipeline { public static UnityEditor.Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions options) => null; public static bool isBuildingPlayer => false; }
    [AttributeUsage(AttributeTargets.Method)] public sealed class InitializeOnLoadMethodAttribute : Attribute { }
    public static class PlayerSettings
    {
        public static string productName { get; set; }
        public static UnityEngine.FullScreenMode fullScreenMode { get; set; }
        public static int defaultScreenWidth { get; set; }
        public static int defaultScreenHeight { get; set; }
        public static bool resizableWindow { get; set; }
        public static ScriptingImplementation GetScriptingBackend(UnityEditor.Build.NamedBuildTarget target) => default;
        public static void SetScriptingBackend(UnityEditor.Build.NamedBuildTarget target, ScriptingImplementation backend) { }
    }
    public class SerializedProperty { public bool boolValue { get; set; } }
}
namespace UnityEditor.Build
{
    public struct NamedBuildTarget { public static readonly NamedBuildTarget Standalone = default; }
}
namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene, DefaultGameObjects }
    public enum NewSceneMode { Single, Additive }
    public static class EditorSceneManager
    {
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup setup, NewSceneMode mode) => default;
        public static bool SaveScene(UnityEngine.SceneManagement.Scene scene, string path) => true;
        public static bool SaveCurrentModifiedScenesIfUserWantsTo() => true;
    }
}
