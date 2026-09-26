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
            damage.UserSized = true;
            columns.Single(x => x.Key == "Cooldown").Width = 333f;
            MasterGridLayout.Save("TestSkill", columns);

            var restored = MasterRecordListController.CreateColumns(Table<TestSkill>());
            MasterGridLayout.Restore("TestSkill", restored);
            var again = restored.Single(x => x.Key == "Damage");
            Assert.IsFalse(again.Visible);
            Assert.IsTrue(again.Frozen);
            Assert.AreEqual(222f, again.Width);
            Assert.AreNotEqual(333f, restored.Single(x => x.Key == "Cooldown").Width, "automatic widths are computed again");

            var other = MasterRecordListController.CreateColumns(Table<TestEnemyLevel>());
            MasterGridLayout.Restore("TestEnemyLevel", other);
            Assert.IsTrue(other.All(x => x.Visible), "other tables keep their defaults");

            MasterGridLayout.ResetToDefaults(restored);
            Assert.IsTrue(again.Visible);
            Assert.IsFalse(again.Frozen);
            Assert.IsFalse(again.UserSized);
            Assert.AreEqual(again.DefaultWidth, again.Width);
            Assert.IsTrue(restored[1].Frozen, "the primary key is frozen again");
        }

        [Test]
        public void Settings_ShouldRoundTripThroughText()
        {
            var settings = new Dictionary<string, MasterGridLayout.ColumnSettings>
            {
                ["Damage"] = new MasterGridLayout.ColumnSettings { Visible = false, Frozen = true, Width = 222.5f, UserSized = true },
                ["Name"] = new MasterGridLayout.ColumnSettings { Visible = true, Frozen = false, Width = 90f, UserSized = false },
            };
            var text = MasterGridLayout.Serialize(settings);
            var read = MasterGridLayout.Deserialize(text + "broken line\n\tx\n");

            Assert.AreEqual(2, read.Count);
            Assert.IsFalse(read["Damage"].Visible);
            Assert.IsTrue(read["Damage"].Frozen);
            Assert.AreEqual(222.5f, read["Damage"].Width);
            Assert.IsTrue(read["Damage"].UserSized);
            Assert.IsTrue(read["Name"].Visible);
            Assert.IsFalse(read["Name"].UserSized);
            Assert.AreEqual(0, MasterGridLayout.Deserialize(null).Count);
        }

        [Test]
        public void Tsv_ShouldContainTheShownColumnsInGridOrder()
        {
            var table = Table<TestSkill>();
            var columns = MasterRecordListController.CreateColumns(table);
            columns.Single(x => x.Key == "Damage").Frozen = true;
            columns.Single(x => x.Key == "Cooldown").Visible = false;
            var rows = table.CreateRecordSnapshot().Take(2).ToList();

            var lines = MasterRecordListController.BuildTsv(columns, rows).TrimEnd().Split('\n').Select(x => x.TrimEnd('\r').Split('\t')).ToList();

            Assert.AreEqual(3, lines.Count);
            Assert.AreEqual("Id (PK)", lines[0][0], "the state column is left out");
            Assert.AreEqual("Damage", lines[0][1], "frozen columns first");
            CollectionAssert.DoesNotContain(lines[0], "Cooldown");
            Assert.AreEqual("1001", lines[1][0]);
            Assert.AreEqual("120", lines[1][1]);
            Assert.AreEqual(lines[0].Length, lines[2].Length);
        }

        [Test]
        public void StateAndPrimaryKeyColumns_ShouldAlwaysBeShownAndFrozen()
        {
            var columns = MasterRecordListController.CreateColumns(Table<TestEnemyLevel>());
            var locked = columns.Take(3).ToList();
            Assert.IsTrue(locked.All(x => x.Locked));
            Assert.IsFalse(columns.Skip(3).Any(x => x.Locked));

            foreach (var column in locked)
            {
                column.Visible = false;
                column.Frozen = false;
                Assert.IsTrue(column.Visible && column.Frozen, column.Key);
            }

            MasterGridLayout.Save("TestEnemyLevel", columns);
            var restored = MasterRecordListController.CreateColumns(Table<TestEnemyLevel>());
            MasterGridLayout.Restore("TestEnemyLevel", restored);
            Assert.IsTrue(restored.Take(3).All(x => x.Visible && x.Frozen));
        }

        [Test]
        public void AutoFit_ShouldFollowTitlesAndValues()
        {
            var records = Table<TestSkill>().CreateRecordSnapshot();
            var columns = MasterRecordListController.CreateColumns(Table<TestSkill>());
            MasterGridLayout.AutoFit(columns, records);

            var state = columns[0];
            Assert.AreEqual(state.DefaultWidth, state.Width, "the state column keeps its fixed width");
            var bigValue = columns.Single(x => x.Key == "BigValue");
            var isPassive = columns.Single(x => x.Key == "IsPassive");
            Assert.Greater(bigValue.Width, isPassive.Width, "18446744073709551614 is wider than the title IsPassive");
            Assert.IsTrue(columns.Where(x => x.AutoWidth).All(x => x.Width >= MasterGridLayout.MinAutoWidth && x.Width <= MasterGridLayout.MaxAutoWidth));

            var resized = columns.Single(x => x.Key == "Name");
            resized.Width = 500f;
            resized.UserSized = true;
            MasterGridLayout.AutoFit(columns, records);
            Assert.AreEqual(500f, resized.Width, "a dragged width is kept");
        }

        [Test]
        public void TextWidth_ShouldCountFullWidthCharactersWider()
        {
            Assert.AreEqual(0f, MasterGridLayout.EstimateTextWidth(null, 12f));
            Assert.Greater(MasterGridLayout.EstimateTextWidth("傷害", 12f), MasterGridLayout.EstimateTextWidth("ab", 12f));
            Assert.Greater(MasterGridLayout.EstimateTextWidth("MMMM", 12f), MasterGridLayout.EstimateTextWidth("iiii", 12f));
            Assert.AreEqual(24f, MasterGridLayout.EstimateTextWidth("技能", 12f));
        }
    }
}
