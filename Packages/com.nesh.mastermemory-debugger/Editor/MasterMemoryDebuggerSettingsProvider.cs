using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger.Editor
{
    /// <summary>Project Settings &gt; MasterMemory Debugger.</summary>
    static class MasterMemoryDebuggerSettingsProvider
    {
        public const string SettingsPath = "Project/MasterMemory Debugger";
        // not a Resources folder: the build processor copies the asset into Development Builds only
        const string DefaultAssetFolder = "Assets/MasterMemoryDebugger";
        const string DefaultAssetPath = DefaultAssetFolder + "/" + MasterMemoryDebuggerSettings.ResourcesPath + ".asset";

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "MasterMemory Debugger",
                activateHandler = (_, root) => Build(root),
                keywords = new HashSet<string>
                {
                    "MasterMemory", "Debugger", "Override", "Patch", "F8",
                },
            };
        }

        /// <summary>Finds the settings asset (anywhere in the project).</summary>
        public static MasterMemoryDebuggerSettings FindSettingsAsset()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(MasterMemoryDebuggerSettings));
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<MasterMemoryDebuggerSettings>(path);
                if (asset != null) return asset;
            }
            return null;
        }

        public static MasterMemoryDebuggerSettings GetOrCreateSettingsAsset()
        {
            var settings = FindSettingsAsset();
            if (settings != null) return settings;

            EnsureFolder(DefaultAssetFolder);
            settings = ScriptableObject.CreateInstance<MasterMemoryDebuggerSettings>();
            AssetDatabase.CreateAsset(settings, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            MasterMemoryDebuggerSettings.SetCurrent(settings);
            return settings;
        }

        static void Build(VisualElement root)
        {
            root.Clear();
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 4;

            var title = new Label("MasterMemory Debugger");
            title.style.fontSize = 19;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            root.Add(title);

            var settings = FindSettingsAsset();
            if (settings == null)
            {
                root.Add(new HelpBox(
                    "No settings asset. Default values are used. Create one to change the settings " +
                    "(" + DefaultAssetPath + "; Development Builds get a copy, release builds do not contain it).",
                    HelpBoxMessageType.Info));
                root.Add(new Button(() =>
                {
                    GetOrCreateSettingsAsset();
                    Build(root);
                })
                {
                    text = "Create Settings Asset",
                });
                return;
            }

            var path = AssetDatabase.GetAssetPath(settings);
            if (path.Contains("/Resources/"))
            {
                // older versions created the asset in a Resources folder: every build, release too, contained it
                root.Add(new HelpBox(
                    $"'{path}' is in a Resources folder, so release builds contain it and what it references (font, PanelSettings). " +
                    "Move it out: Development Builds get a copy automatically.",
                    HelpBoxMessageType.Warning));
                root.Add(new Button(() =>
                {
                    EnsureFolder(DefaultAssetFolder);
                    var error = AssetDatabase.MoveAsset(path, AssetDatabase.GenerateUniqueAssetPath(DefaultAssetPath));
                    if (!string.IsNullOrEmpty(error)) Debug.LogError("[MasterMemoryDebugger] " + error);
                    Build(root);
                })
                {
                    text = "Move out of Resources",
                });
            }

            root.Add(new InspectorElement(new SerializedObject(settings)));

            var pathRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8 } };
            pathRow.Add(new Label(path) { style = { flexGrow = 1, unityTextAlign = TextAnchor.MiddleLeft } });
            pathRow.Add(new Button(() => EditorGUIUtility.PingObject(settings)) { text = "Select Asset" });
            root.Add(pathRow);

            root.Add(new HelpBox(
                "The debugger only runs in the Editor and in Development Builds. " +
                "Define MMDEBUGGER_DISABLE in Player Settings > Scripting Define Symbols to disable it completely.",
                HelpBoxMessageType.None));
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parts = folder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
