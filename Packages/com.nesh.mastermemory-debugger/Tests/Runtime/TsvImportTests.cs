using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class TsvImportTests : DebuggerTestBase
    {
        [SetUp]
        public void SetUp()
        {
            RegisterTestDatabase();
        }

        static T Current<T>(object key) where T : class => MasterMemoryDebugRuntime.Store.TryGet(typeof(T), key, out var value) ? (T)value : null;

        [Test]
        public void CopiedChanges_ShouldImportBackAfterEditing()
        {
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 185, Name = "line 1\nline 2" });
            MasterMemoryDebugRuntime.SetOverride((2, 1), Database.TestEnemyLevelTable.FindByEnemyIdAndLevel((2, 1)) with { Hp = 1 });
            var copied = MasterMemoryChangeSummary.ToTsv(MasterMemoryChangeSummary.Build());
            MasterMemoryDebugRuntime.ClearAllOverrides();

            // edited in a spreadsheet: Damage 185 -> 200; the other lines are left as copied
            var edited = copied.Replace("\tDamage\t120\t185", "\tDamage\t120\t200");
            var plan = MasterMemoryTsvImport.Read(edited);

            Assert.IsNull(plan.InvalidFormat);
            Assert.AreEqual(0, plan.Failed, string.Join("\n", plan.Problems));
            Assert.AreEqual(3, plan.Changes.Count);
            Assert.AreEqual(2, plan.RecordCount);

            Assert.AreEqual(3, MasterMemoryTsvImport.Apply(plan));
            Assert.AreEqual(200, Current<TestSkill>(1001).Damage);
            Assert.AreEqual("line 1 line 2", Current<TestSkill>(1001).Name, "Copy TSV flattened the line break");
            Assert.AreEqual(1, Current<TestEnemyLevel>((2, 1)).Hp, "composite keys are matched as shown");
        }

        [Test]
        public void LinesLeftAsCopied_ShouldNotChangeAnything()
        {
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Name = "line 1\nline 2" });
            var plan = MasterMemoryTsvImport.Read(MasterMemoryChangeSummary.ToTsv(MasterMemoryChangeSummary.Build()));

            Assert.AreEqual(0, plan.Changes.Count);
            Assert.AreEqual(1, plan.Unchanged);
        }

        [Test]
        public void Columns_ShouldBeFoundByName()
        {
            var plan = MasterMemoryTsvImport.Read("note\tvalue\tfield\tkey\ttable\nfirst\t9\tDamage\t1002\tTestSkill\nsecond\tFire\tElement\t1003\tTestSkill\n");

            Assert.AreEqual(2, plan.Changes.Count, string.Join("\n", plan.Problems));
            MasterMemoryTsvImport.Apply(plan);
            Assert.AreEqual(9, Current<TestSkill>(1002).Damage);
            Assert.AreEqual(TestElement.Fire, Current<TestSkill>(1003).Element);
        }

        [Test]
        public void TheLastLineOfAField_ShouldWin()
        {
            var plan = MasterMemoryTsvImport.Read("table\tkey\tfield\tcurrent\nTestSkill\t1002\tDamage\t1\nTestSkill\t1002\tDamage\t2\n");
            Assert.AreEqual(1, plan.Changes.Count);
            Assert.AreEqual(2, plan.Changes[0].Value);
        }

        [Test]
        public void Problems_ShouldBeReportedPerLine()
        {
            var text = "table\tkey\tfield\toriginal\tcurrent\n" +
                       "Unknown\t1\tDamage\t1\t2\n" +
                       "TestSkill\t9999\tDamage\t1\t2\n" +
                       "TestSkill\t1001\tId\t1001\t5\n" +
                       "TestSkill\t1001\tDamage\t120\tabc\n" +
                       "TestSkill\t1001\tCooldown\t9\t1.5\n";
            var plan = MasterMemoryTsvImport.Read(text);

            Assert.AreEqual(4, plan.Failed);
            Assert.AreEqual(1, plan.Outdated, "Cooldown was 2.5, not 9");
            Assert.AreEqual(1, plan.Changes.Count, "outdated lines are still imported");
            Assert.IsTrue(plan.Problems.Any(x => x.StartsWith("line 3:")));
        }

        [Test]
        public void TextWithoutTitleLine_ShouldBeRejected()
        {
            Assert.IsNotNull(MasterMemoryTsvImport.Read("TestSkill\t1001\tDamage\t5").InvalidFormat);
            Assert.IsNotNull(MasterMemoryTsvImport.Read("").InvalidFormat);
        }

        [Test]
        public void Import_ShouldBeOneUndoStep()
        {
            var plan = MasterMemoryTsvImport.Read("table\tkey\tfield\tcurrent\nTestSkill\t1001\tDamage\t1\nTestSkill\t1002\tDamage\t2\n");
            using (MasterMemoryDebugHistory.Record("import")) MasterMemoryTsvImport.Apply(plan);
            Assert.AreEqual(2, MasterMemoryDebugRuntime.OverrideCount);
            MasterMemoryDebugHistory.Undo();
            Assert.AreEqual(0, MasterMemoryDebugRuntime.OverrideCount);
        }
    }
}
