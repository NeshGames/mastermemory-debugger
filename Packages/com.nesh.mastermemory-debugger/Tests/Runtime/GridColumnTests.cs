using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class GridColumnTests : DebuggerTestBase
    {
        [SetUp]
        public void SetUp()
        {
            RegisterTestDatabase();
        }

        [Test]
        public void Columns_ShouldStartWithStateAndPrimaryKeys()
        {
            var columns = MasterRecordListController.CreateColumns(Table<TestSkill>());
            var keys = columns.Select(x => x.Key).ToList();

            Assert.AreEqual(MasterRecordListController.ModifiedColumn, keys[0]);
            Assert.AreEqual("Id", keys[1]);
            Assert.AreEqual("Id (PK)", columns[1].Title);
            Assert.IsFalse(keys.Contains(MasterRecordListController.NameColumn), "the default display name only repeats Name");
            Assert.AreEqual(1, keys.Count(x => x == "Name"));
            Assert.IsTrue(columns[0].Frozen && columns[1].Frozen, "state and primary key are frozen by default");
            Assert.IsFalse(columns.Skip(2).Any(x => x.Frozen));
            StringAssert.Contains("mm-debugger__cell--number", columns.Single(x => x.Key == "Damage").CellClass);

            var composite = MasterRecordListController.CreateColumns(Table<TestEnemyLevel>());
            CollectionAssert.AreEqual(new[] { "EnemyId", "Level" }, composite.Skip(1).Take(2).Select(x => x.Key));
        }

        [Test]
        public void DisplayColumn_ShouldOnlyExistForProjectDisplayNames()
        {
            MasterMemoryDebugRegistry.SetDisplayName<TestSkill>(x => "Skill " + x.Id);
            try
            {
                var keys = MasterRecordListController.CreateColumns(Table<TestSkill>()).Select(x => x.Key).ToList();
                Assert.AreEqual(2, keys.IndexOf(MasterRecordListController.NameColumn));
            }
            finally
            {
                MasterMemoryDebugRegistry.SetDisplayName<TestSkill>(null);
            }
        }

        [Test]
        public void Split_ShouldPutVisibleFrozenColumnsFirst()
        {
            var columns = new List<MasterGridColumn>
            {
                new MasterGridColumn("a", "A", 10, null),
                new MasterGridColumn("b", "B", 20, null) { Frozen = true },
                new MasterGridColumn("c", "C", 30, null) { Visible = false },
                new MasterGridColumn("d", "D", 40, null) { Frozen = true, Visible = false },
            };
            var frozen = new List<MasterGridColumn>();
            var scrolled = new List<MasterGridColumn>();

            MasterGridLayout.Split(columns, frozen, scrolled);

            CollectionAssert.AreEqual(new[] { "b" }, frozen.Select(x => x.Key));
            CollectionAssert.AreEqual(new[] { "a" }, scrolled.Select(x => x.Key));
            Assert.AreEqual(30f, MasterGridLayout.TotalWidth(columns.Take(2).ToList()));
            Assert.AreEqual(50f, MasterGridLayout.MaxScroll(150f, 100f));
            Assert.AreEqual(0f, MasterGridLayout.MaxScroll(80f, 100f));
        }

        [Test]
        public void Settings_ShouldBeRememberedPerTable()
        {
            var columns = MasterRecordListController.CreateColumns(Table<TestSkill>());
            var damage = columns.Single(x => x.Key == "Damage");
            damage.Visible = false;
            damage.Frozen = true;
            damage.Width = 222f;
            MasterGridLayout.Save("TestSkill", columns);

            var restored = MasterRecordListController.CreateColumns(Table<TestSkill>());
            MasterGridLayout.Restore("TestSkill", restored);
            var again = restored.Single(x => x.Key == "Damage");
            Assert.IsFalse(again.Visible);
            Assert.IsTrue(again.Frozen);
            Assert.AreEqual(222f, again.Width);

            var other = MasterRecordListController.CreateColumns(Table<TestEnemyLevel>());
            MasterGridLayout.Restore("TestEnemyLevel", other);
            Assert.IsTrue(other.All(x => x.Visible), "other tables keep their defaults");

            MasterGridLayout.ResetToDefaults(restored);
            Assert.IsTrue(again.Visible);
            Assert.IsFalse(again.Frozen);
            Assert.AreEqual(again.DefaultWidth, again.Width);
            Assert.IsTrue(restored[1].Frozen, "the primary key is frozen again");
        }
    }
}
