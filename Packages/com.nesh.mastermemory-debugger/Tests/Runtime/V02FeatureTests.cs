using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    /// <summary>Changes summary, record JSON, value tree, patch import and message log.</summary>
    public class V02FeatureTests : DebuggerTestBase
    {
        sealed class Nested
        {
            public int Level { get; set; }
            public List<int> Values { get; set; }
            public Nested Child { get; set; }
            public Dictionary<string, int> Map { get; set; }
        }

        [SetUp]
        public void SetUp()
        {
            MasterMemoryDebuggerMessages.Clear();
        }

        [Test]
        public void ChangeSummary_ShouldListChangedFieldsInTableOrder()
        {
            RegisterTestDatabase();
            MasterMemoryDebugRuntime.SetOverride(1002, Database.TestSkillTable.FindById(1002) with { Damage = 150 });
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 185, Cooldown = 1f });
            MasterMemoryDebugRuntime.SetOverride((1, 1), Database.TestEnemyLevelTable.FindByEnemyIdAndLevel((1, 1)) with { Hp = 1 });

            var entries = MasterMemoryChangeSummary.Build();

            CollectionAssert.AreEqual(new object[] { (1, 1), 1001, 1002 }, entries.Select(x => x.PrimaryKey).ToArray(), "tables by name, then primary key");
            var fireball = entries[1];
            Assert.AreEqual(MasterMemoryChangeStatus.Changed, fireball.Status);
            Assert.AreEqual("TestSkill", fireball.TableName);
            Assert.AreEqual("Fireball", fireball.DisplayName);
            CollectionAssert.AreEquivalent(new[] { "Damage", "Cooldown" }, fireball.Changes.Select(x => x.Name));
        }

        [Test]
        public void ChangeSummary_ShouldFlagMissingOriginalsAndUnregisteredTables()
        {
            RegisterTestDatabase();
            MasterMemoryDebugRuntime.SetOverride(9999, new TestSkill { Id = 9999, Name = "Ghost" });
            MasterMemoryDebugRuntime.Store.Set(typeof(ManualItem), 1, new ManualItem(1, "x", 1));

            var entries = MasterMemoryChangeSummary.Build();

            Assert.AreEqual(MasterMemoryChangeStatus.OriginalMissing, entries.Single(x => Equals(x.PrimaryKey, 9999)).Status);
            var unregistered = entries.Last();
            Assert.AreEqual(MasterMemoryChangeStatus.TableNotRegistered, unregistered.Status);
            Assert.AreEqual("ManualItem", unregistered.TableName);
        }

        [Test]
        public void RecordJson_ShouldContainEveryMember()
        {
            var json = (MasterDataJsonObject)MasterDataJson.Parse(MasterDataRecordJson.ToJson(Database.TestSkillTable.FindById(1001)));

            Assert.AreEqual("1001", json["Id"].ToString());
            Assert.AreEqual("Fireball", json["Name"]);
            Assert.AreEqual("Fire", json["Element"]);
            Assert.AreEqual(2, ((List<object>)json["Tags"]).Count);
            Assert.AreEqual((ulong.MaxValue - 1).ToString(), json["BigValue"].ToString());
        }

        [Test]
        public void RecordJson_ShouldHandleNestingAndCycles()
        {
            var root = new Nested { Level = 1, Values = new List<int> { 1, 2 }, Map = new Dictionary<string, int> { { "a", 1 } } };
            root.Child = root; // cycle

            var json = (MasterDataJsonObject)MasterDataJson.Parse(MasterDataRecordJson.ToJson(root));

            Assert.AreEqual("<cycle>", json["Child"]);
            Assert.AreEqual("1", ((MasterDataJsonObject)json["Map"])["a"].ToString());
        }

        [Test]
        public void ValueTree_ShouldExposeChildren()
        {
            Assert.IsFalse(MasterValueTreeFactory.IsExpandable(1));
            Assert.IsFalse(MasterValueTreeFactory.IsExpandable("text"));
            Assert.IsFalse(MasterValueTreeFactory.IsExpandable(null));
            Assert.IsTrue(MasterValueTreeFactory.IsExpandable(new[] { 1 }));

            var children = MasterValueTreeFactory.GetChildren(Enumerable.Range(0, 250).ToArray(), 100, out var total);
            Assert.AreEqual(100, children.Count);
            Assert.AreEqual(250, total);
            Assert.AreEqual("[0]", children[0].Key);

            var nested = MasterValueTreeFactory.GetChildren(new Nested { Level = 3 }, 100, out _);
            Assert.AreEqual(3, nested.Single(x => x.Key == "Level").Value);

            var map = MasterValueTreeFactory.GetChildren(new Dictionary<string, int> { { "k", 7 } }, 100, out _);
            Assert.AreEqual("k", map[0].Key);
            Assert.AreEqual(7, map[0].Value);
        }

        [Test]
        public void Import_ShouldValidateAndSuggestNames()
        {
            Assert.AreEqual("balance-A", MasterDataPatchImporter.SuggestName("balance-A.patch.json"));
            Assert.AreEqual("export-20260926", MasterDataPatchImporter.SuggestName("C:/tmp/export-20260926.json"));
            Assert.AreEqual("imported", MasterDataPatchImporter.SuggestName(""));

            Assert.Throws<FormatException>(() => MasterDataPatchImporter.Parse("  "));
            Assert.Throws<FormatException>(() => MasterDataPatchImporter.Parse("{ \"formatVersion\": 99 }"));
            Assert.Throws<FormatException>(() => MasterDataPatchImporter.Parse("not json"));

            var patch = MasterDataPatchImporter.Parse(MasterDataPatchSerializer.ToJson(new MasterDataPatch { MasterVersion = "v9" }));
            Assert.AreEqual("v9", patch.MasterVersion);
        }

        [Test]
        public void Messages_ShouldKeepTheLatestEntries()
        {
            var changed = 0;
            void Handler() => changed++;
            MasterMemoryDebuggerMessages.Changed += Handler;
            try
            {
                for (var i = 0; i < MasterMemoryDebuggerMessages.Capacity + 5; i++)
                {
                    MasterMemoryDebuggerMessages.Add(MasterMemoryDebuggerMessageType.Info, "m" + i);
                }
                Assert.AreEqual(MasterMemoryDebuggerMessages.Capacity, MasterMemoryDebuggerMessages.Messages.Count);
                Assert.AreEqual("m5", MasterMemoryDebuggerMessages.Messages[0].Text);
                Assert.AreEqual(MasterMemoryDebuggerMessages.Capacity + 5, changed);
            }
            finally
            {
                MasterMemoryDebuggerMessages.Changed -= Handler;
            }
        }

        [Test]
        public void ChangeLog_ShouldReachTheMessagePanel()
        {
            RegisterTestDatabase();
            var table = Table<TestSkill>();
            var original = Database.TestSkillTable.FindById(1001);
            MasterMemoryChangeLog.Applied(table, 1001, original, original with { Damage = 185 });

            var message = MasterMemoryDebuggerMessages.Messages.Last();
            Assert.AreEqual(MasterMemoryDebuggerMessageType.Change, message.Type);
            StringAssert.Contains("Damage: 120 → 185", message.Text);
            StringAssert.DoesNotContain("<color", message.Text);
        }
    }
}
