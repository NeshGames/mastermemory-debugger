using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class RegistryTests : DebuggerTestBase
    {
        [Test]
        public void RegisterTable_ShouldAppear()
        {
            var items = new[] { new ManualItem(1, "Potion", 50), new ManualItem(2, "Ether", 120) };
            MasterMemoryDebugRegistry.RegisterTable<ManualItem, int>("ItemMaster", () => items, x => x.Code, x => x.Title);

            Assert.IsTrue(MasterMemoryDebugRegistry.TryGetTable("ItemMaster", out var table));
            Assert.AreEqual(typeof(ManualItem), table.RecordType);
            Assert.AreEqual(typeof(int), table.KeyType);
            Assert.AreEqual(2, table.GetAllRecords().Count());
            Assert.AreEqual(2, table.GetPrimaryKey(items[1]));
            Assert.AreEqual("Ether", table.GetDisplayName(items[1]));
        }

        [Test]
        public void RegisterDatabase_ShouldRegisterEveryTable()
        {
            RegisterTestDatabase();

            var skill = Table<TestSkill>();
            Assert.AreEqual("TestSkill", skill.TableName);
            Assert.AreEqual("test_skill", skill.MemoryTableName);
            Assert.AreEqual(typeof(int), skill.KeyType);
            Assert.AreEqual(3, skill.GetAllRecords().Count());
            Assert.AreEqual(1002, skill.GetPrimaryKey(Database.TestSkillTable.FindById(1002)));
            Assert.AreEqual("Ice Blast", skill.GetDisplayName(Database.TestSkillTable.FindById(1002)));

            var level = Table<TestEnemyLevel>();
            Assert.AreEqual(typeof((int, int)), level.KeyType);
            Assert.AreEqual((1, 2), level.GetPrimaryKey(Database.TestEnemyLevelTable.FindByEnemyIdAndLevel((1, 2))));
        }

        [Test]
        public void RegisterTable_SameRecordType_ShouldReplace()
        {
            var items = new[] { new ManualItem(1, "Potion", 50) };
            MasterMemoryDebugRegistry.RegisterTable<ManualItem, int>("ItemMaster", () => items, x => x.Code);
            MasterMemoryDebugRegistry.RegisterTable<ManualItem, int>("ItemMasterV2", () => items, x => x.Code);

            Assert.AreEqual(1, MasterMemoryDebugRegistry.Tables.Count);
            Assert.AreEqual("ItemMasterV2", MasterMemoryDebugRegistry.Tables[0].TableName);
        }

        [Test]
        public void DisplayName_Default_ShouldUseNameMember()
        {
            var items = new[] { new ManualItem(1, "Potion", 50) };
            MasterMemoryDebugRegistry.RegisterTable<ManualItem, int>("ItemMaster", () => items, x => x.Code);
            var table = Table<ManualItem>();
            Assert.AreEqual("Potion", table.GetDisplayName(items[0]), "falls back to the first string member");

            MasterMemoryDebugRegistry.SetDisplayName<ManualItem>(x => "#" + x.Code);
            Assert.AreEqual("#1", table.GetDisplayName(items[0]));
        }

        [Test]
        public void MasterVersion_ShouldDefaultToUnknown()
        {
            Assert.AreEqual(MasterMemoryDebugRegistry.UnknownMasterVersion, MasterMemoryDebugRegistry.GetMasterVersion());
            MasterMemoryDebugRegistry.SetMasterVersionProvider(() => "2026.09.26.001");
            Assert.AreEqual("2026.09.26.001", MasterMemoryDebugRegistry.GetMasterVersion());
        }
    }
}
