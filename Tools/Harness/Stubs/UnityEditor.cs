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
    public class SerializedObject { public SerializedObject(UnityEngine.Object obj) { } }
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
    }
    public static class EditorGUIUtility { public static void PingObject(UnityEngine.Object o) { } }
    public static class EditorUtility
    {
        public static bool DisplayDialog(string title, string message, string ok, string cancel = "") => true;
        public static string SaveFilePanel(string title, string directory, string defaultName, string extension) => "";
        public static string OpenFilePanel(string title, string directory, string extension) => "";
        public static void RevealInFinder(string path) { }
    }
    public static class EditorApplication { public static bool isPlaying => false; }
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
    public interface IUnityLinkerProcessor : IOrderedCallback { string GenerateAdditionalLinkXmlFile(UnityEditor.Build.Reporting.BuildReport report, UnityEditor.UnityLinker.UnityLinkerBuildPipelineData data); }
}
namespace UnityEditor.Build.Reporting
{
    public class BuildSummary { public UnityEditor.BuildOptions options => default; }
    public class BuildReport { public BuildSummary summary => null; }
}
namespace UnityEditor.UnityLinker
{
    public class UnityLinkerBuildPipelineData { }
}
