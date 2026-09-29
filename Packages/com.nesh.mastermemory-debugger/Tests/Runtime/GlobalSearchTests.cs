using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class GlobalSearchTests : DebuggerTestBase
    {
        public sealed class Reward
        {
            public int ItemId { get; set; }
            public string Note { get; set; }
        }

        public sealed class Chest
        {
            public int Code { get; set; }
            public Reward Reward { get; set; }
            public Dictionary<string, int> Weights { get; set; }
        }

        [SetUp]
        public void SetUp()
        {
            RegisterTestDatabase();
            var chests = new[]
            {
                new Chest { Code = 7, Reward = new Reward { ItemId = 1001, Note = "Fire gem" }, Weights = new Dictionary<string, int> { { "gold", 2 } } },
                new Chest { Code = 8 },
            };
            MasterMemoryDebugRegistry.RegisterTable<Chest, int>("ChestMaster", () => chests, x => x.Code);
        }

        static string[] Describe(MasterMemorySearchResult result) => result.Hits.Select(x => $"{x.Table.TableName} {x.Record.KeyText} {x.Field.Name}{x.Path}").ToArray();

        [Test]
        public void Find_ShouldSearchEveryTableAndMember()
        {
            var result = MasterMemoryGlobalSearch.Find("fire");
            CollectionAssert.AreEquivalent(new[]
            {
                "TestSkill 1001 Name",
                "TestSkill 1001 Element",
                "ChestMaster 7 Reward.Note",
            }, Describe(result));
            Assert.AreEqual(3, result.TotalHits);
            Assert.AreEqual(2, result.RecordCount);
            Assert.AreEqual(2, result.TableCount);
            Assert.IsFalse(result.IsTruncated);
        }

        [Test]
        public void Find_ShouldLookIntoListsNestedObjectsAndDictionaries()
        {
            var two = Describe(MasterMemoryGlobalSearch.Find("2", wholeValue: true));
            CollectionAssert.IsSubsetOf(new[] { "TestSkill 1001 Tags[1]", "TestSkill 1001 SummonEnemyId", "ChestMaster 7 Weights[gold]" }, two);
            CollectionAssert.DoesNotContain(two, "TestSkill 1001 Tags[0]");
            CollectionAssert.Contains(Describe(MasterMemoryGlobalSearch.Find("gold")), "ChestMaster 7 Weights[gold]");
            CollectionAssert.AreEquivalent(new[] { "TestSkill 1001 Id", "ChestMaster 7 Reward.ItemId" }, Describe(MasterMemoryGlobalSearch.Find("1001")));
        }

        [Test]
        public void Find_ShouldMatchCustomValuesAsTheirText()
        {
            RegisterTunings();
            CollectionAssert.AreEqual(new[] { "TestTuning 2 Limit" }, Describe(MasterMemoryGlobalSearch.Find("12.25", wholeValue: true)));
            CollectionAssert.AreEqual(new[] { "TestTuning 1 Curve[0]" }, Describe(MasterMemoryGlobalSearch.Find("0.5", wholeValue: true)));
        }

        [Test]
        public void WholeValue_ShouldOnlyFindEqualValues()
        {
            CollectionAssert.IsNotEmpty(MasterMemoryGlobalSearch.Find("ball").Hits);
            CollectionAssert.IsEmpty(MasterMemoryGlobalSearch.Find("ball", wholeValue: true).Hits);
            CollectionAssert.AreEqual(new[] { "TestSkill 1001 Name" }, Describe(MasterMemoryGlobalSearch.Find("FIREBALL", wholeValue: true)));
        }

        [Test]
        public void Find_ShouldUseTheCurrentValues()
        {
            var original = Database.TestSkillTable.FindById(1002);
            MasterMemoryDebugRuntime.SetOverride(1002, original with { Name = "Blizzard" });

            var hit = MasterMemoryGlobalSearch.Find("blizzard").Hits.Single();
            Assert.AreEqual(1002, hit.Record.PrimaryKey);
            Assert.IsTrue(hit.Record.IsModified);
            Assert.AreEqual("Blizzard", hit.Value);
            CollectionAssert.IsEmpty(MasterMemoryGlobalSearch.Find("Ice Blast").Hits);
        }

        [Test]
        public void Find_ShouldLimitTheHitsButCountThemAll()
        {
            var result = MasterMemoryGlobalSearch.Find("1", maxHits: 2);
            Assert.AreEqual(2, result.Hits.Count);
            Assert.Greater(result.TotalHits, 2);
            Assert.IsTrue(result.IsTruncated);

            CollectionAssert.IsEmpty(MasterMemoryGlobalSearch.Find("  ").Hits);
            CollectionAssert.IsEmpty(MasterMemoryGlobalSearch.Find(null).Hits);
        }
    }
}
