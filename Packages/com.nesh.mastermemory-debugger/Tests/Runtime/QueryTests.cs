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
        public void BooleanOperators_ShouldRespectPrecedenceAndGrouping()
        {
            CollectionAssert.AreEqual(new[] { 1001, 1002 }, Ids("Damage>=120 || Element=Ice"));
            CollectionAssert.AreEqual(new[] { 1001 }, Ids("Damage>=120 || Element=Ice && IsPassive=true"), "AND binds tighter than OR");
            CollectionAssert.AreEqual(new[] { 1002 }, Ids("(Damage>=120 || Element=Ice) && Name~blast"), "parentheses override precedence");
            CollectionAssert.AreEqual(new[] { 1001 }, Ids("Damage>=120&&(Element=Fire||Element=Ice)"), "spaces around operators are optional");
            CollectionAssert.AreEqual(new[] { 1002 }, Ids("(Damage>=120 || Element=Ice) Name~blast"), "adjacent terms remain AND");
            CollectionAssert.AreEqual(new[] { 1001, 1002 }, Ids("Name=\"Fireball\" || Name=\"Ice Blast\""));
        }

        [Test]
        public void BooleanSyntax_ShouldPreserveQuotedTextAndRejectMalformedQueries()
        {
            Ids("Name=\"Ice || Blast\"", out var quoted);
            Assert.AreEqual(0, quoted.Errors.Count, "an operator inside quotes is part of the value");
            Assert.AreEqual(1, quoted.ConditionCount);

            foreach (var text in new[] { "Damage>100 ||", "Damage>100 && (Element=Fire", "()", "Damage>100 || Dmg>1" })
            {
                CollectionAssert.IsEmpty(Ids(text, out var query), text);
                Assert.IsNotEmpty(query.Errors, text);
            }
        }

        [Test]
        public void FlagsValue_ShouldKeepSinglePipeSeparateFromBooleanOr()
        {
            MasterMemoryDebugRuntime.SetOverride((1, 2), Database.TestEnemyLevelTable.FindByEnemyIdAndLevel((1, 2))
                with { Flags = TestFlags.Boss | TestFlags.Flying });
            var table = Table<TestEnemyLevel>();
            var query = MasterRecordQuery.Parse("Flags=Boss|Flying || Hp>300", table.TypeDescriptor);
            Assert.IsEmpty(query.Errors);
            var matches = new List<MasterMemoryRecordDescriptor>();
            MasterRecordListController.Filter(table.CreateRecordSnapshot(), query, false, int.MaxValue, matches);
            CollectionAssert.AreEqual(new[] { (1, 2), (2, 1) }, matches.Select(x => ((int, int))x.PrimaryKey).ToArray());
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

        sealed class NotComparable : MasterDataValueConverter<TestFixed>
        {
            readonly TestFixedConverter inner = new TestFixedConverter();
            public override string Format(TestFixed value) => inner.Format(value);
            public override bool TryParse(string text, out TestFixed value, out string error) => inner.TryParse(text, out value, out error);
        }

        [Test]
        public void CustomValues_ShouldBeReadAndComparedWithTheirConverter()
        {
            RegisterTunings();
            var tunings = Table<TestTuning>().CreateRecordSnapshot();
            int[] Tunings(string text, out MasterRecordQuery query)
            {
                query = MasterRecordQuery.Parse(text, MasterDataReflectionCache.Get<TestTuning>());
                var result = new List<MasterMemoryRecordDescriptor>();
                MasterRecordListController.Filter(tunings, query, false, int.MaxValue, result);
                return result.Select(x => (int)x.PrimaryKey).ToArray();
            }

            CollectionAssert.AreEqual(new[] { 2 }, Tunings("Speed>2.5", out _), "10 > 2.5 (as text it is not)");
            CollectionAssert.AreEqual(new[] { 1, 3 }, Tunings("Speed<=2.5", out _));
            CollectionAssert.AreEqual(new[] { 1 }, Tunings("Speed=2.50", out _), "the value is read, not compared as typed");
            CollectionAssert.AreEqual(new[] { 1, 3 }, Tunings("Limit=null", out _));
            CollectionAssert.AreEqual(new[] { 2 }, Tunings("Limit>12", out _));
            Tunings("Speed>fast", out var invalid);
            Assert.AreEqual(1, invalid.Errors.Count);

            // without a comparer, values can only be equal or not
            MasterMemoryDebugRegistry.ClearTables();
            FixedConverter.Dispose();
            using (MasterDataValueConverters.Register(new NotComparable()))
            {
                var ordering = MasterRecordQuery.Parse("Speed>1", MasterDataReflectionCache.Get<TestTuning>());
                Assert.AreEqual(1, ordering.Errors.Count);
                StringAssert.Contains("= or !=", ordering.Errors[0]);
                CollectionAssert.IsEmpty(MasterRecordQuery.Parse("Speed!=2.5", MasterDataReflectionCache.Get<TestTuning>()).Errors);
            }
        }

        [Test]
        public void Sort_ShouldOrderCustomValuesWithTheirComparer()
        {
            RegisterTunings();
            var table = Table<TestTuning>();
            var records = table.CreateRecordSnapshot();
            MasterRecordListController.SortBy(records, "Speed", table, false);
            CollectionAssert.AreEqual(new[] { 3, 1, 2 }, records.Select(x => (int)x.PrimaryKey), "-1, 2.5, 10 (as text: -1, 10, 2.5)");
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
