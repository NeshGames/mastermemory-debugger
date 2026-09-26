using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class LocalizationTests : DebuggerTestBase
    {
        [SetUp]
        public void SetUp()
        {
            RegisterTestDatabase();
        }

        static MasterMemoryFieldDescriptor Field(string name)
        {
            MasterDataReflectionCache.Get<TestSkill>().TryGetField(name, out var field);
            return field;
        }

        [Test]
        public void Labels_ShouldFollowTheSelectedLanguage()
        {
            var table = Table<TestSkill>();
            MasterMemoryDebugLocalization.SetTableLabel<TestSkill>("zh-TW", "技能", "技能資料");
            MasterMemoryDebugLocalization.SetFieldLabel<TestSkill>("Damage", "zh-TW", "傷害", "基礎傷害");
            MasterMemoryDebugLocalization.SetFieldLabel<TestSkill>("Damage", "ja", "ダメージ");

            CollectionAssert.AreEqual(new[] { "zh-TW", "ja" }, MasterMemoryDebugLocalization.Languages);
            Assert.AreEqual("TestSkill", MasterMemoryDebugLocalization.GetTableLabel(table), "code names by default");
            Assert.AreEqual("Damage", MasterMemoryDebugLocalization.GetFieldLabel(table, Field("Damage")));

            MasterMemoryDebugLocalization.Language = "zh-TW";
            Assert.AreEqual("技能", MasterMemoryDebugLocalization.GetTableLabel(table));
            Assert.AreEqual("TestSkill\n技能資料", MasterMemoryDebugLocalization.GetTableTooltip(table));
            Assert.AreEqual("傷害", MasterMemoryDebugLocalization.GetFieldLabel(table, Field("Damage")));
            Assert.AreEqual("Damage (Int32)\n基礎傷害", MasterMemoryDebugLocalization.GetFieldTooltip(table, Field("Damage")));
            Assert.AreEqual("Name", MasterMemoryDebugLocalization.GetFieldLabel(table, Field("Name")), "missing labels fall back to the code name");

            MasterMemoryDebugLocalization.Language = "ja";
            Assert.AreEqual("ダメージ", MasterMemoryDebugLocalization.GetFieldLabel(table, Field("Damage")));
            Assert.AreEqual("TestSkill", MasterMemoryDebugLocalization.GetTableLabel(table));
            Assert.AreEqual("ダメージ", MasterMemoryDebugLocalization.FindFieldLabel(table, "Damage"));
        }

        [Test]
        public void LanguageIndependentTips_ShouldApplyToEveryLanguage()
        {
            var table = Table<TestSkill>();
            MasterMemoryDebugLocalization.SetFieldLabel<TestSkill>("Cooldown", null, null, "Seconds");
            MasterMemoryDebugLocalization.SetFieldLabel<TestSkill>("Cooldown", "zh-TW", "冷卻");

            Assert.AreEqual("Cooldown (Single)\nSeconds", MasterMemoryDebugLocalization.GetFieldTooltip(table, Field("Cooldown")));
            MasterMemoryDebugLocalization.Language = "zh-TW";
            Assert.AreEqual("冷卻", MasterMemoryDebugLocalization.GetFieldLabel(table, Field("Cooldown")));
            Assert.AreEqual("Cooldown (Single)\nSeconds", MasterMemoryDebugLocalization.GetFieldTooltip(table, Field("Cooldown")));
        }

        [Test]
        public void MemoryTableNames_ShouldAlsoMatch()
        {
            MasterMemoryDebugLocalization.SetTableLabel("test_skill", "zh-TW", "技能");
            MasterMemoryDebugLocalization.Language = "zh-TW";
            Assert.AreEqual("技能", MasterMemoryDebugLocalization.GetTableLabel(Table<TestSkill>()));
        }

        [Test]
        public void Tsv_ShouldLoadTablesAndFields()
        {
            var count = MasterMemoryDebugLocalization.LoadTsv(
                "table\tfield\tlanguage\tlabel\ttip\n" +
                "# comment\n" +
                "TestSkill\t\tzh-TW\t技能\t所有技能\n" +
                "TestSkill\tDamage\tzh-TW\t傷害\t第一行\\n第二行\r\n" +
                "\n" +
                "broken line\n");

            Assert.AreEqual(2, count);
            MasterMemoryDebugLocalization.Language = "zh-TW";
            var table = Table<TestSkill>();
            Assert.AreEqual("技能", MasterMemoryDebugLocalization.GetTableLabel(table));
            Assert.AreEqual("Damage (Int32)\n第一行\n第二行", MasterMemoryDebugLocalization.GetFieldTooltip(table, Field("Damage")));
        }

        [Test]
        public void Changed_ShouldBeRaisedForLanguageAndLabels()
        {
            var changed = 0;
            void Handler() => changed++;
            MasterMemoryDebugLocalization.Changed += Handler;
            try
            {
                MasterMemoryDebugLocalization.SetTableLabel<TestSkill>("zh-TW", "技能");
                MasterMemoryDebugLocalization.Language = "zh-TW";
                MasterMemoryDebugLocalization.Language = "zh-TW";
                Assert.AreEqual(2, changed);
            }
            finally
            {
                MasterMemoryDebugLocalization.Changed -= Handler;
            }
        }

        [Test]
        public void Grid_ShouldUseLabelsAndKeepCodeNamesAsKeys()
        {
            MasterMemoryDebugLocalization.SetFieldLabel<TestSkill>("Id", "zh-TW", "編號");
            MasterMemoryDebugLocalization.SetFieldLabel<TestSkill>("Category", "zh-TW", "分類");
            MasterMemoryDebugLocalization.Language = "zh-TW";

            var columns = MasterRecordListController.CreateColumns(Table<TestSkill>());
            Assert.AreEqual("Id", columns[1].Key);
            Assert.AreEqual("編號 (PK)", columns[1].Title);
            Assert.AreEqual("分類 (SK)", columns[2].Title);
        }

        [Test]
        public void Pins_ShouldBeKeptInOrder()
        {
            MasterTablePins.ResetForTests();

            MasterTablePins.Pin("TestSkill");
            MasterTablePins.Pin("TestEnemyLevel");
            MasterTablePins.Pin("TestSkill");
            CollectionAssert.AreEqual(new[] { "TestSkill", "TestEnemyLevel" }, MasterTablePins.Names);

            MasterTablePins.Unpin("TestSkill");
            Assert.IsFalse(MasterTablePins.IsPinned("TestSkill"));
            Assert.IsTrue(MasterTablePins.IsPinned("TestEnemyLevel"));
        }

        [Test]
        public void TsvTemplate_ShouldListEveryTableAndFieldAndLoadBack()
        {
            RegisterTestDatabase();
            MasterMemoryDebugLocalization.SetFieldLabel<TestSkill>("Damage", "zh-TW", "傷害", "line 1\nline 2");

            var template = MasterMemoryDebugLocalization.CreateTsvTemplate("zh-TW");
            var lines = template.TrimEnd('\n').Split('\n');
            Assert.AreEqual("table\tfield\tlanguage\tlabel\ttip", lines[0]);
            CollectionAssert.Contains(lines, "TestSkill\t\tzh-TW\t\t");
            CollectionAssert.Contains(lines, "TestSkill\tDamage\tzh-TW\t傷害\tline 1\\nline 2");
            var fieldCount = Table<TestSkill>().TypeDescriptor.Fields.Count + Table<TestEnemyLevel>().TypeDescriptor.Fields.Count;
            Assert.AreEqual(1 + 2 + fieldCount, lines.Length);

            MasterMemoryDebugLocalization.Clear();
            Assert.AreEqual(1, MasterMemoryDebugLocalization.LoadTsv(template), "unfilled lines are skipped");
            MasterMemoryDebugLocalization.Language = "zh-TW";
            var damage = Table<TestSkill>().TypeDescriptor.Fields.Single(x => x.Name == "Damage");
            Assert.AreEqual("傷害", MasterMemoryDebugLocalization.GetFieldLabel(Table<TestSkill>(), damage));
            StringAssert.EndsWith("line 1\nline 2", MasterMemoryDebugLocalization.GetFieldTooltip(Table<TestSkill>(), damage));
        }
    }
}
