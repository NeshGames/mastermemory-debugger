using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class ReferenceTests : DebuggerTestBase
    {
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
        public void TypesWithoutValidate_ShouldHaveNoReferences()
        {
            RegisterTestDatabase();
            Assert.AreEqual(0, MasterMemoryReferences.Get(Table<TestEnemyLevel>()).Count);
            Assert.AreEqual(0, MasterMemoryReferences.Get(null).Count);
        }
    }
}
