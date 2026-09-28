using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class ReferenceTests : DebuggerTestBase
    {
        [Test]
        public void ExplicitReferences_ShouldMergeWithDiscoveredReferences()
        {
            RegisterTestDatabase();
            MasterMemoryReferences.Register<TestSkill, TestEnemyLevel, int>(
                x => x.SummonEnemyId, x => x.EnemyId);

            var references = MasterMemoryReferences.Get(Table<TestSkill>());

            Assert.AreEqual(1, references.Count, "the explicit reference and Exists discovery are de-duplicated");
            Assert.AreEqual("SummonEnemyId", references[0].SourceMember);
            Assert.AreEqual("EnemyId", references[0].TargetMember);
        }

        [Test]
        public void References_ShouldComeFromValidateExists()
        {
            RegisterTestDatabase();

            var references = MasterMemoryReferences.Get(Table<TestSkill>());

            Assert.AreEqual(1, references.Count, string.Join(", ", references));
            var reference = references[0];
            Assert.AreEqual(typeof(TestSkill), reference.SourceType);
            Assert.AreEqual("SummonEnemyId", reference.SourceMember);
            Assert.AreEqual(typeof(TestEnemyLevel), reference.TargetType);
            Assert.AreEqual("EnemyId", reference.TargetMember);
            Assert.AreSame(reference, MasterMemoryReferences.Find(Table<TestSkill>(), "SummonEnemyId"));
            Assert.IsNull(MasterMemoryReferences.Find(Table<TestSkill>(), "Damage"));
        }

        [Test]
        public void References_ShouldBeFoundInConditionalBranches()
        {
            // only 1001 takes the branch that declares the reference
            var records = new[] { Database.TestSkillTable.FindById(1002), Database.TestSkillTable.FindById(1001) };
            Assert.AreEqual(1, MasterMemoryReferences.Discover(typeof(TestSkill), records).Count);
            Assert.AreEqual(0, MasterMemoryReferences.Discover(typeof(TestSkill), records.Take(1)).Count);
        }

        [Test]
        public void IncomingReferences_ShouldFindTheReferencingRecords()
        {
            RegisterTestDatabase();
            var enemies = Table<TestEnemyLevel>();
            var incoming = MasterMemoryReferences.GetIncoming(enemies);
            Assert.AreEqual(1, incoming.Count);
            Assert.AreEqual("SummonEnemyId", incoming[0].SourceMember);
            Assert.IsEmpty(MasterMemoryReferences.GetIncoming(Table<TestSkill>()));

            var boss = enemies.CreateRecordSnapshot().Single(x => Equals(x.PrimaryKey, (2, 1)));
            var value = MasterMemoryReferences.GetReferencedValue(incoming[0], boss);
            Assert.AreEqual(2, value);
            CollectionAssert.AreEqual(new object[] { 1001 }, MasterMemoryReferences.FindReferencing(incoming[0], value).Select(x => x.PrimaryKey));

            // current values: an override that points at the boss is found too
            MasterMemoryDebugRuntime.SetOverride(1002, Database.TestSkillTable.FindById(1002) with { SummonEnemyId = 2 });
            Assert.AreEqual(2, MasterMemoryReferences.FindReferencing(incoming[0], 2L).Count, "compared by value, not by type");
            Assert.IsEmpty(MasterMemoryReferences.FindReferencing(incoming[0], null));
        }

        [Test]
        public void TypesWithoutValidate_ShouldHaveNoReferences()
        {
            RegisterTestDatabase();
            Assert.AreEqual(0, MasterMemoryReferences.Get(Table<TestEnemyLevel>()).Count);
            Assert.AreEqual(0, MasterMemoryReferences.Get(null).Count);
        }
    }
}
