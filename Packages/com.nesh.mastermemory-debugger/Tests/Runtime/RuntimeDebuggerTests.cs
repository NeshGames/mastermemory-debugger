using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger.Tests
{
    /// <summary>Play Mode tests of the UI Toolkit debugger. Ignored in Edit Mode.</summary>
    public class RuntimeDebuggerTests : DebuggerTestBase
    {
        [SetUp]
        public void SetUp()
        {
            if (!Application.isPlaying) Assert.Ignore("Run these tests in Play Mode.");
            RegisterTestDatabase();
        }

        [TearDown]
        public void TearDown()
        {
            RuntimeMasterMemoryDebugger.Close();
        }

        [UnityTest]
        public IEnumerator RuntimeDebugger_ShouldOpen()
        {
            Assert.IsTrue(RuntimeMasterMemoryDebugger.Open());
            yield return null;
            yield return null;

            Assert.IsTrue(RuntimeMasterMemoryDebugger.IsOpen);
            var host = GameObject.Find(MasterMemoryDebuggerDocument.GameObjectName);
            Assert.IsNotNull(host);
            var root = host.GetComponent<UIDocument>().rootVisualElement;
            Assert.IsNotNull(root.Q("mm-window"));
            Assert.IsNotNull(root.Q<ListView>("mm-table-list"));
            Assert.IsNotNull(root.Q<ListView>("mm-record-list"));
        }

        [UnityTest]
        public IEnumerator RuntimeDebugger_ShouldClose()
        {
            RuntimeMasterMemoryDebugger.Open();
            yield return null;

            RuntimeMasterMemoryDebugger.Close();
            Assert.IsFalse(RuntimeMasterMemoryDebugger.IsOpen);
            yield return null;

            Assert.IsNull(GameObject.Find(MasterMemoryDebuggerDocument.GameObjectName));
        }

        [UnityTest]
        public IEnumerator RuntimeDebugger_Toggle_ShouldSwitchState()
        {
            RuntimeMasterMemoryDebugger.Toggle();
            yield return null;
            Assert.IsTrue(RuntimeMasterMemoryDebugger.IsOpen);

            RuntimeMasterMemoryDebugger.Toggle();
            yield return null;
            Assert.IsFalse(RuntimeMasterMemoryDebugger.IsOpen);
        }

        [UnityTest]
        public IEnumerator RuntimeDebugger_Disabled_ShouldNotOpen()
        {
            Settings.Enabled = false;
            Assert.IsFalse(RuntimeMasterMemoryDebugger.Open());
            yield return null;
            Assert.IsFalse(RuntimeMasterMemoryDebugger.IsOpen);
        }
    }
}
