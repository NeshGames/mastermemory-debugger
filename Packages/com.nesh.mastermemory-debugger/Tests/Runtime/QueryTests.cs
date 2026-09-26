using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class QueryTests : DebuggerTestBase
    {
        List<MasterMemoryRecordDescriptor> snapshot;

        [SetUp]
        public void SetUp()
        {
            RegisterTestDatabase();
            snapshot = Table<TestSkill>().CreateRecordSnapshot();
        }

        int[] Ids(string text, out MasterRecordQuery query)
        {
            query = MasterRecordQuery.Parse(text, MasterDataReflectionCache.Get<TestSkill>());
            var result = new List<MasterMemoryRecordDescriptor>();
            MasterRecordListController.Filter(snapshot, query, false, int.MaxValue, result);
            return result.Select(x => (int)x.PrimaryKey).ToArray();
        }

        int[] Ids(string text) => Ids(text, out _);

        [Test]
        public void Conditions_ShouldFilterByFieldValues()
        {
            CollectionAssert.AreEqual(new[] { 1001, 1002 }, Ids("Damage>0"));
            CollectionAssert.AreEqual(new[] { 1001 }, Ids("Damage>=120"));
            CollectionAssert.AreEqual(new[] { 1003 }, Ids("Damage<=0"));
            CollectionAssert.AreEqual(new[] { 1002, 1003 }, Ids("Damage!=120"));
            CollectionAssert.AreEqual(new[] { 1002 }, Ids("Element=ice"), "enum names are case-insensitive");
            CollectionAssert.AreEqual(new[] { 1003 }, Ids("IsPassive=true"));
            CollectionAssert.AreEqual(new[] { 1001, 1002 }, Ids("Cooldown<4.5"));
            CollectionAssert.AreEqual(new[] { 1002 }, Ids("Name~blast"));
            CollectionAssert.AreEqual(new[] { 1002 }, Ids("name=\"Ice Blast\""), "field names are case-insensitive, quoted values");
            CollectionAssert.AreEqual(new[] { 1002, 1003 }, Ids("UnlockLevel=null"));
            CollectionAssert.AreEqual(new[] { 1001 }, Ids("BigValue>18446744073709551613"), "ulong without precision loss");
        }

        [Test]
        public void Terms_ShouldBeCombinedWithAnd()
        {
            CollectionAssert.AreEqual(new[] { 1001 }, Ids("Category=1 Damage > 110"), "spaces around operators are allowed");
            CollectionAssert.AreEqual(new[] { 1002 }, Ids("ice Category=1"), "text and condition");
            CollectionAssert.IsEmpty(Ids("Category=2 Damage>0"));
        }

        [Test]
        public void Conditions_ShouldUseOverriddenValues()
        {
            MasterMemoryDebugRuntime.SetOverride(1003, Database.TestSkillTable.FindById(1003) with { Damage = 999 });
            CollectionAssert.AreEqual(new[] { 1003 }, Ids("Damage>500"));
        }

        [Test]
        public void InvalidTerms_ShouldBeReportedAndIgnored()
        {
            CollectionAssert.AreEqual(new[] { 1001, 1002, 1003 }, Ids("Dmg>1", out var query));
            Assert.AreEqual(1, query.Errors.Count);
            StringAssert.Contains("Dmg", query.Errors[0]);

            Ids("Damage>abc Element=Wind IsPassive>1", out query);
            Assert.AreEqual(3, query.Errors.Count);
        }

        [Test]
        public void Sort_ShouldOrderByColumn()
        {
            var records = new List<MasterMemoryRecordDescriptor>(snapshot);
            var table = Table<TestSkill>();

            MasterRecordListController.SortBy(records, "Damage", table, true);
            CollectionAssert.AreEqual(new[] { 1001, 1002, 1003 }, records.Select(x => (int)x.PrimaryKey));

            MasterRecordListController.SortBy(records, "Name", table, false);
            CollectionAssert.AreEqual(new[] { 1001, 1003, 1002 }, records.Select(x => (int)x.PrimaryKey), "Fireball, Heal, Ice Blast");

            MasterRecordListController.SortBy(records, "UnlockLevel", table, false);
            Assert.AreEqual(1001, records[2].PrimaryKey, "nulls first");
        }
    }
}
