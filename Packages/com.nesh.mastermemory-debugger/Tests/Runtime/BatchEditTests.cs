using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class BatchEditTests : DebuggerTestBase
    {
        [SetUp]
        public void SetUp()
        {
            RegisterTestDatabase();
        }

        MasterMemoryFieldDescriptor Field(string name) => Table<TestSkill>().TypeDescriptor.Fields.Single(x => x.Name == name);

        List<MasterMemoryRecordDescriptor> Records() => Table<TestSkill>().CreateRecordSnapshot();

        static TestSkill Current(int id) => MasterMemoryDebugRuntime.Store.TryGet(typeof(TestSkill), id, out var value) ? (TestSkill)value : null;

        [Test]
        public void Multiply_ShouldRoundIntegersAndOverrideChangedRecords()
        {
            var result = MasterMemoryBatchEdit.Apply(Records(), Field("Damage"), MasterMemoryBatchOperation.Multiply, "1.15");

            Assert.IsNull(result.InvalidValue);
            Assert.AreEqual(2, result.Changed);
            Assert.AreEqual(1, result.Unchanged, "0 × 1.15 stays 0");
            Assert.AreEqual(138, Current(1001).Damage);
            Assert.AreEqual(115, Current(1002).Damage);
            Assert.IsNull(Current(1003));
            Assert.AreEqual("Fireball", Current(1001).Name, "other members are kept");
            Assert.AreEqual(120, Database.TestSkillTable.FindById(1001).Damage, "the original is untouched");
        }

        [Test]
        public void AddAndSet_ShouldWorkOnEveryNumberType()
        {
            MasterMemoryBatchEdit.Apply(Records(), Field("Cooldown"), MasterMemoryBatchOperation.Add, "-0.5");
            Assert.AreEqual(2f, Current(1001).Cooldown);
            Assert.AreEqual(4.5f, Current(1003).Cooldown);

            MasterMemoryBatchEdit.Apply(Records().Take(1), Field("Damage"), MasterMemoryBatchOperation.Set, "7");
            Assert.AreEqual(7, Current(1001).Damage);
            Assert.AreEqual(100, Current(1002).Damage, "only the given records");
        }

        [Test]
        public void Set_ShouldParseEnumsBooleansAndNull()
        {
            MasterMemoryBatchEdit.Apply(Records(), Field("Element"), MasterMemoryBatchOperation.Set, "ice");
            MasterMemoryBatchEdit.Apply(Records(), Field("IsPassive"), MasterMemoryBatchOperation.Set, "true");
            MasterMemoryBatchEdit.Apply(Records(), Field("UnlockLevel"), MasterMemoryBatchOperation.Set, "null");

            Assert.AreEqual(TestElement.Ice, Current(1001).Element);
            Assert.IsTrue(Current(1002).IsPassive);
            Assert.IsNull(Current(1001).UnlockLevel);
            Assert.IsTrue(Current(1003).IsPassive, "unchanged members of an overridden record keep their values");
        }

        [Test]
        public void BackToTheOriginal_ShouldRemoveTheOverride()
        {
            MasterMemoryBatchEdit.Apply(Records(), Field("Damage"), MasterMemoryBatchOperation.Add, "10");
            Assert.AreEqual(3, MasterMemoryDebugRuntime.OverrideCount);

            MasterMemoryBatchEdit.Apply(Records(), Field("Damage"), MasterMemoryBatchOperation.Add, "-10");
            Assert.AreEqual(0, MasterMemoryDebugRuntime.OverrideCount);
        }

        [Test]
        public void Failures_ShouldBeReportedPerRecord()
        {
            // null UnlockLevel of 1002 / 1003 can not be multiplied; ulong overflow
            var nulls = MasterMemoryBatchEdit.Apply(Records(), Field("UnlockLevel"), MasterMemoryBatchOperation.Multiply, "2");
            Assert.AreEqual(1, nulls.Changed);
            Assert.AreEqual(2, nulls.Failed);
            Assert.AreEqual(2, nulls.Errors.Count);
            Assert.AreEqual(6, Current(1001).UnlockLevel);

            var overflow = MasterMemoryBatchEdit.Apply(Records().Take(1), Field("BigValue"), MasterMemoryBatchOperation.Add, "5");
            Assert.AreEqual(1, overflow.Failed);
            StringAssert.Contains("out of range", overflow.Errors[0]);
        }

        [Test]
        public void InvalidValues_ShouldChangeNothing()
        {
            Assert.IsNotNull(MasterMemoryBatchEdit.Apply(Records(), Field("Damage"), MasterMemoryBatchOperation.Multiply, "abc").InvalidValue);
            Assert.IsNotNull(MasterMemoryBatchEdit.Apply(Records(), Field("Damage"), MasterMemoryBatchOperation.Set, "1.5").InvalidValue, "not an integer");
            Assert.IsNotNull(MasterMemoryBatchEdit.Apply(Records(), Field("Name"), MasterMemoryBatchOperation.Add, "1").InvalidValue, "Add needs a number");
            Assert.IsNotNull(MasterMemoryBatchEdit.Apply(Records(), Field("Element"), MasterMemoryBatchOperation.Set, "Wind").InvalidValue);
            Assert.IsNotNull(MasterMemoryBatchEdit.Apply(Records(), Field("Id"), MasterMemoryBatchOperation.Set, "1").InvalidValue, "keys can not be batch edited");
            Assert.AreEqual(0, MasterMemoryDebugRuntime.OverrideCount);
        }

        [Test]
        public void CanEdit_ShouldExcludeKeysListsAndComplexMembers()
        {
            Assert.IsFalse(MasterMemoryBatchEdit.CanEdit(Field("Id")));
            Assert.IsFalse(MasterMemoryBatchEdit.CanEdit(Field("Category")), "secondary key");
            Assert.IsFalse(MasterMemoryBatchEdit.CanEdit(Field("Tags")));
            Assert.IsTrue(MasterMemoryBatchEdit.CanEdit(Field("Name")));
            Assert.IsTrue(MasterMemoryBatchEdit.CanEdit(Field("UnlockLevel")));
        }

        [Test]
        public void BatchEdit_ShouldBeOneUndoStep()
        {
            using (MasterMemoryDebugHistory.Record("batch"))
            {
                MasterMemoryBatchEdit.Apply(Records(), Field("Damage"), MasterMemoryBatchOperation.Add, "1");
            }
            Assert.AreEqual(3, MasterMemoryDebugRuntime.OverrideCount);
            MasterMemoryDebugHistory.Undo();
            Assert.AreEqual(0, MasterMemoryDebugRuntime.OverrideCount);
        }
    }
}
