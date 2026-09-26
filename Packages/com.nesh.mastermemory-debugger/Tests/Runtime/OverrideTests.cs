using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class OverrideTests : DebuggerTestBase
    {
        TestSkill GetSkill(int id)
        {
            // the integration pattern of a project MasterDataService
            return MasterMemoryDebugRuntime.Resolve<TestSkill, int>(id, key => Database.TestSkillTable.FindById(key));
        }

        [Test]
        public void NoOverride_ShouldReturnFallback()
        {
            Assert.IsFalse(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1001, out _));
            Assert.AreSame(Database.TestSkillTable.FindById(1001), GetSkill(1001));
        }

        [Test]
        public void OverrideRecord_ShouldReturnOverride()
        {
            var original = Database.TestSkillTable.FindById(1001);
            var copy = original with { Damage = 185 };
            MasterMemoryDebugRuntime.SetOverride(1001, copy);

            Assert.IsTrue(MasterMemoryDebugRuntime.IsOverridden<TestSkill, int>(1001));
            Assert.AreEqual(185, GetSkill(1001).Damage);
            Assert.AreEqual(120, Database.TestSkillTable.FindById(1001).Damage, "MasterMemory data must stay untouched");
            Assert.AreSame(Database.TestSkillTable.FindById(1002), GetSkill(1002), "other records fall back");
        }

        [Test]
        public void ResetOverride_ShouldReturnOriginal()
        {
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 185 });
            Assert.IsTrue(MasterMemoryDebugRuntime.RemoveOverride<TestSkill, int>(1001));

            Assert.IsFalse(MasterMemoryDebugRuntime.IsOverridden<TestSkill, int>(1001));
            Assert.AreEqual(120, GetSkill(1001).Damage);
        }

        [Test]
        public void ClearAll_ShouldRemoveOverrides()
        {
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 1 });
            MasterMemoryDebugRuntime.SetOverride(1002, Database.TestSkillTable.FindById(1002) with { Damage = 2 });
            Assert.AreEqual(2, MasterMemoryDebugRuntime.OverrideCount);

            MasterMemoryDebugRuntime.ClearAllOverrides();

            Assert.AreEqual(0, MasterMemoryDebugRuntime.OverrideCount);
            Assert.AreEqual(120, GetSkill(1001).Damage);
            Assert.AreEqual(100, GetSkill(1002).Damage);
        }

        [Test]
        public void CompositeKey_ShouldOverrideByTuple()
        {
            var original = Database.TestEnemyLevelTable.FindByEnemyIdAndLevel((1, 2));
            MasterMemoryDebugRuntime.SetOverride((1, 2), original with { Hp = 999 });

            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestEnemyLevel, (int, int)>((1, 2), out var value));
            Assert.AreEqual(999, value.Hp);
            Assert.IsFalse(MasterMemoryDebugRuntime.TryGetOverride<TestEnemyLevel, (int, int)>((1, 1), out _));
        }

        [Test]
        public void GetOverrides_ShouldReturnRecordsForImmutableBuilderDiff()
        {
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 1 });
            MasterMemoryDebugRuntime.SetOverride((1, 1), Database.TestEnemyLevelTable.FindByEnemyIdAndLevel((1, 1)) with { Hp = 1 });

            var skills = MasterMemoryDebugRuntime.GetOverrides<TestSkill>();
            Assert.AreEqual(1, skills.Length);
            Assert.AreEqual(1, skills[0].Damage);

            // official rebuild path (done by the project, never by the package)
            var builder = Database.ToImmutableBuilder();
            builder.Diff(skills);
            var rebuilt = builder.Build();
            Assert.AreEqual(1, rebuilt.TestSkillTable.FindById(1001).Damage);
            Assert.AreEqual(2, rebuilt.TestSkillTable.FindByCategory(1).Count);
        }

        [Test]
        public void OverridesChanged_ShouldBeRaisedOncePerBatch()
        {
            var count = 0;
            void Handler() => count++;
            MasterMemoryDebugRuntime.OverridesChanged += Handler;
            try
            {
                MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 1 });
                Assert.AreEqual(1, count);

                using (MasterMemoryDebugRuntime.BeginBatch())
                {
                    MasterMemoryDebugRuntime.SetOverride(1002, Database.TestSkillTable.FindById(1002) with { Damage = 2 });
                    MasterMemoryDebugRuntime.SetOverride(1003, Database.TestSkillTable.FindById(1003) with { Damage = 3 });
                    Assert.AreEqual(1, count);
                }
                Assert.AreEqual(2, count);

                MasterMemoryDebugRuntime.RemoveOverride<TestSkill, int>(9999);
                Assert.AreEqual(2, count, "no event when nothing changed");
            }
            finally
            {
                MasterMemoryDebugRuntime.OverridesChanged -= Handler;
            }
        }

        [Test]
        public void SetOverride_WrongRecordType_ShouldThrow()
        {
            var store = MasterMemoryDebugRuntime.Store;
            Assert.Throws<System.ArgumentException>(() => store.Set(typeof(TestSkill), 1001, new ManualItem(1, "x", 1)));
            Assert.Throws<System.ArgumentNullException>(() => store.Set(typeof(TestSkill), 1001, null));
        }
    }
}
