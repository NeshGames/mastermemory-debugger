using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Nesh.MasterMemoryDebugger.Tests.Generated;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class RebuildTests : DebuggerTestBase
    {
        [SetUp]
        public void SetUp()
        {
            MasterMemoryDebuggerMessages.Clear();
        }

        [Test]
        public void Apply_ShouldReturnTheOriginalWithoutOverrides()
        {
            Assert.AreSame(Database, MasterMemoryDebugRebuild.Apply(Database));
        }

        [Test]
        public void Apply_ShouldRebuildWithEveryOverride()
        {
            MasterMemoryDebugRuntime.SetOverride(1002, Database.TestSkillTable.FindById(1002) with { Category = 2, Damage = 7 });
            MasterMemoryDebugRuntime.SetOverride((2, 1), Database.TestEnemyLevelTable.FindByEnemyIdAndLevel((2, 1)) with { Hp = 1 });

            var rebuilt = MasterMemoryDebugRebuild.Apply(Database);

            Assert.AreNotSame(Database, rebuilt);
            Assert.AreEqual(7, rebuilt.TestSkillTable.FindById(1002).Damage);
            Assert.AreEqual(2, rebuilt.TestSkillTable.FindByCategory(2).Count, "secondary indexes see the override");
            Assert.AreEqual(1, rebuilt.TestEnemyLevelTable.FindByEnemyIdAndLevel((2, 1)).Hp);
            Assert.AreEqual(100, Database.TestSkillTable.FindById(1002).Damage, "the original is untouched");
        }

        [Test]
        public void Apply_ShouldSkipTypesOutsideTheDatabase()
        {
            MasterMemoryDebugRuntime.Store.Set(typeof(ManualItem), 1, new ManualItem(1, "x", 1));
            Assert.AreSame(Database, MasterMemoryDebugRebuild.Apply(Database));
        }

        [Test]
        public void AutoRebuild_ShouldFollowOverridesAndRestoreOnDispose()
        {
            MemoryDatabase current = null;
            using (MasterMemoryDebugRebuild.AutoRebuild(Database, db => current = db))
            {
                Assert.AreSame(Database, current);

                MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 999 });
                Assert.AreEqual(999, current.TestSkillTable.FindById(1001).Damage);

                MasterMemoryDebugRuntime.ClearAllOverrides();
                Assert.AreSame(Database, current, "no overrides: back to the original");

                MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 5 });
            }
            Assert.AreSame(Database, current);

            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 6 });
            Assert.AreSame(Database, current, "disposed");
        }

        [Test]
        public void AutoRebuild_ShouldReportNewValidationFailures()
        {
            using (MasterMemoryDebugRebuild.AutoRebuild(Database, _ => { }))
            {
                MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { SummonEnemyId = 99 });
            }

            var warnings = MasterMemoryDebuggerMessages.Messages.Where(x => x.Type == MasterMemoryDebuggerMessageType.Warning).Select(x => x.Text).ToList();
            Assert.AreEqual(1, warnings.Count, string.Join("\n", warnings));
            StringAssert.Contains("TestSkill.SummonEnemyId -> TestEnemyLevel.EnemyId", warnings[0]);
            StringAssert.Contains("99", warnings[0]);
        }

        [Test]
        public void Validate_ShouldOnlyReportFailuresMissingFromTheBaseline()
        {
            var baseline = MasterMemoryDebugRebuild.Validate(Database);
            Assert.IsFalse(baseline.IsValidationFailed, baseline.FormatFailedResults());

            MasterMemoryDebugRuntime.SetOverride(1003, Database.TestSkillTable.FindById(1003) with { Damage = -1 });
            var result = MasterMemoryDebugRebuild.Validate(MasterMemoryDebugRebuild.Apply(Database));

            var failures = MasterMemoryDebugRebuild.GetNewFailures(result, baseline);
            Assert.AreEqual(1, failures.Count);
            Assert.IsEmpty(MasterMemoryDebugRebuild.GetNewFailures(result, result));
        }

        [Test]
        public void Validation_ShouldListTheFailuresOfTheRebuiltDatabase()
        {
            RegisterTestDatabase();
            Assert.IsFalse(MasterMemoryDebugValidation.IsAvailable);
            var changed = 0;
            void OnChanged() => changed++;
            MasterMemoryDebugValidation.Changed += OnChanged;
            try
            {
                using (MasterMemoryDebugRebuild.AutoRebuild(Database, _ => { }))
                {
                    Assert.IsTrue(MasterMemoryDebugValidation.IsAvailable);
                    Assert.IsEmpty(MasterMemoryDebugValidation.Run());
                    Assert.AreEqual(0, MasterMemoryDebugValidation.NewFailureCount);

                    changed = 0;
                    MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { SummonEnemyId = 99 });
                    Assert.AreEqual(1, changed);
                    Assert.AreEqual(1, MasterMemoryDebugValidation.NewFailureCount);

                    var failures = MasterMemoryDebugValidation.Run();
                    Assert.AreEqual(1, failures.Count);
                    Assert.IsTrue(failures[0].IsNew);
                    Assert.AreEqual(typeof(TestSkill), failures[0].RecordType);
                    Assert.AreEqual(1001, Table<TestSkill>().GetPrimaryKey(failures[0].Record), "the failing record can be opened");
                    StringAssert.Contains("SummonEnemyId", failures[0].Message);

                    MasterMemoryDebugRuntime.ClearAllOverrides();
                    Assert.AreEqual(0, MasterMemoryDebugValidation.NewFailureCount);
                    Assert.IsEmpty(MasterMemoryDebugValidation.Run());
                }
                Assert.IsFalse(MasterMemoryDebugValidation.IsAvailable, "disposed");
            }
            finally
            {
                MasterMemoryDebugValidation.Changed -= OnChanged;
            }
        }

        [Test]
        public void Validation_ShouldNotListDatabasesRebuiltWithoutValidation()
        {
            using (MasterMemoryDebugRebuild.AutoRebuild(Database, _ => { }, validate: false))
            {
                Assert.IsFalse(MasterMemoryDebugValidation.IsAvailable);
            }
        }

        [Test]
        public void Validation_ShouldNotMarkFailuresOfTheOriginalAsNew()
        {
            MasterMemoryDebugRuntime.SetOverride(1003, Database.TestSkillTable.FindById(1003) with { Damage = -1 });
            var rebuilt = MasterMemoryDebugRebuild.Apply(Database);
            var result = MasterMemoryDebugRebuild.Validate(rebuilt);

            var failures = new List<MasterMemoryValidationFailure>();
            MasterMemoryDebugValidation.Convert(result, MasterMemoryDebugRebuild.Validate(rebuilt), failures);
            Assert.AreEqual(1, failures.Count);
            Assert.IsFalse(failures[0].IsNew, "the same failure in the baseline");

            failures.Clear();
            MasterMemoryDebugValidation.Convert(result, MasterMemoryDebugRebuild.Validate(Database), failures);
            Assert.IsTrue(failures[0].IsNew);
        }

        [Test]
        public void SlowValidation_ShouldStopValidatingAfterEveryChange()
        {
            var previous = MasterMemoryDebugRebuild.SlowValidateSeconds;
            MasterMemoryDebugRebuild.SlowValidateSeconds = -1;
            try
            {
                using (MasterMemoryDebugRebuild.AutoRebuild(Database, _ => { }))
                {
                    MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 1 });
                    MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { SummonEnemyId = 99 });

                    var warnings = MasterMemoryDebuggerMessages.Messages.Where(x => x.Type == MasterMemoryDebuggerMessageType.Warning).Select(x => x.Text).ToList();
                    Assert.AreEqual(1, warnings.Count, string.Join("\n", warnings));
                    StringAssert.Contains("no longer validated after every change", warnings[0]);
                    Assert.AreEqual(0, MasterMemoryDebugValidation.NewFailureCount, "not validated after the second change");

                    // the Validation tab still validates on demand
                    var failures = MasterMemoryDebugValidation.Run();
                    Assert.AreEqual(1, failures.Count);
                    Assert.IsTrue(failures[0].IsNew);
                }
            }
            finally
            {
                MasterMemoryDebugRebuild.SlowValidateSeconds = previous;
            }
        }
    }
}
