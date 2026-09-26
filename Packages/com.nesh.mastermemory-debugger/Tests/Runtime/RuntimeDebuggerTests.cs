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
            Assert.IsNotNull(root.Q<TreeView>("mm-table-list"));
            Assert.IsNotNull(root.Q("mm-record-grid"));
        }

        /// <summary>Regression: rows of the previous table were bound to the columns of the new one (InvalidCastException).</summary>
        [UnityTest]
        public IEnumerator RecordList_SwitchingTables_ShouldNotBindRecordsOfThePreviousTable()
        {
            RuntimeMasterMemoryDebugger.Open();
            yield return null;
            var root = GameObject.Find(MasterMemoryDebuggerDocument.GameObjectName).GetComponent<UIDocument>().rootVisualElement;

            var list = new VisualElement();
            list.style.width = 600;
            list.style.height = 300;
            root.Add(list);
            var controller = new MasterRecordListController(new TextField(), new Toggle(), list, new Label());
            try
            {
                // any exception logged while the rows are bound fails the test
                controller.SetTable(Table<TestSkill>());
                yield return null;
                yield return null;
                controller.SetTable(Table<TestEnemyLevel>());
                yield return null;
                yield return null;
                controller.SetTable(Table<TestSkill>());
                yield return null;
                yield return null;
                Assert.AreEqual(3, controller.Rows.Count);
                Assert.AreEqual("Id", controller.Columns[1].Key);
            }
            finally
            {
                controller.Dispose();
                list.RemoveFromHierarchy();
            }
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
