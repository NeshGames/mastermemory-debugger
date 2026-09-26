using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger.Editor.Tests
{
    public class DebuggerAssetTests
    {
        [Test]
        public void Layout_ShouldContainEveryElementUsedByTheController()
        {
            var layout = Resources.Load<VisualTreeAsset>(MasterMemoryDebuggerDocument.LayoutResourcePath);
            Assert.IsNotNull(layout, "MasterMemoryDebugger.uxml");
            var root = layout.CloneTree();

            foreach (var name in MasterMemoryDebuggerController.RequiredElementNames)
            {
                Assert.IsNotNull(root.Q(name), name);
            }
            Assert.IsNotNull(root.Q<ListView>("mm-table-list"));
            Assert.IsNotNull(root.Q<ListView>("mm-record-list"));
            Assert.IsNotNull(root.Q<ScrollView>("mm-inspector"));
        }

        [Test]
        public void StyleAndTheme_ShouldLoad()
        {
            Assert.IsNotNull(Resources.Load<StyleSheet>(MasterMemoryDebuggerDocument.StyleResourcePath), "MasterMemoryDebugger.uss");
            Assert.IsNotNull(Resources.Load<ThemeStyleSheet>(MasterMemoryDebuggerDocument.ThemeResourcePath), "MasterMemoryDebuggerTheme.tss");
        }

        [Test]
        public void SettingsProvider_ShouldBeRegistered()
        {
            var provider = MasterMemoryDebuggerSettingsProvider.Create();
            Assert.AreEqual(MasterMemoryDebuggerSettingsProvider.SettingsPath, provider.settingsPath);
        }

        [Test]
        public void DefaultSettings_ShouldMatchThePlan()
        {
            var settings = ScriptableObject.CreateInstance<MasterMemoryDebuggerSettings>();
            try
            {
                Assert.IsTrue(settings.Enabled);
                Assert.IsTrue(settings.AllowEditing);
                Assert.AreEqual(KeyCode.F8, settings.ToggleKey);
                Assert.AreEqual(500, settings.MaxSearchResults);
                Assert.AreEqual("debug.patch.json", settings.PatchFileName);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
