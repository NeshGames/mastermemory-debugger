using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class PatchTests : DebuggerTestBase
    {
        [SetUp]
        public void SetUp()
        {
            MasterMemoryDebugRegistry.SetMasterVersionProvider(() => "v1");
            RegisterTestDatabase();
        }

        [TearDown]
        public void TearDown()
        {
            MasterDataPatchStorage.Delete();
            MasterDataPatchStorage.Delete("unit-test-balance A");
            MasterDataPatchStorage.Delete("unit-test-renamed");
        }

        [Test]
        public void PatchesTab_ShouldSummarizePatches()
        {
            ApplySampleOverrides();
            var patch = MasterDataPatchService.CreatePatch();

            Assert.AreEqual(5, MasterPatchesController.CountFields(patch));
            Assert.AreEqual(0, MasterPatchesController.CountFields(null));
            Assert.AreEqual("185", MasterPatchesController.FormatValue(185));
            Assert.AreEqual("Fire", MasterPatchesController.FormatValue("Fire"));
            Assert.AreEqual("null", MasterPatchesController.FormatValue(null));
            Assert.AreEqual("[1,2]", MasterPatchesController.FormatValue(new System.Collections.Generic.List<object> { 1, 2 }).Replace(" ", ""));
        }

        [Test]
        public void Rename_ShouldMoveThePatchAndRespectExistingNames()
        {
            ApplySampleOverrides();
            MasterDataPatchStorage.Save(MasterDataPatchService.CreatePatch(), "unit-test-balance A");
            MasterDataPatchStorage.Save(MasterDataPatchService.CreatePatch());
            Assert.IsNotNull(MasterDataPatchStorage.GetSavedTime("unit-test-balance A"));

            Assert.IsFalse(MasterDataPatchStorage.Rename("unit-test-balance A", MasterDataPatchStorage.DefaultPatchName), "name taken");
            Assert.IsTrue(MasterDataPatchStorage.Rename("unit-test-balance A", "unit-test-renamed"));
            Assert.IsFalse(MasterDataPatchStorage.Exists("unit-test-balance A"));
            Assert.AreEqual(2, MasterDataPatchStorage.Load("unit-test-renamed").RecordCount);
            Assert.IsNull(MasterDataPatchStorage.GetSavedTime("unit-test-balance A"));

            Assert.IsTrue(MasterDataPatchStorage.Rename("unit-test-renamed", MasterDataPatchStorage.DefaultPatchName, overwrite: true));
            Assert.IsFalse(MasterDataPatchStorage.Rename("missing", "unit-test-renamed"));
        }

        void ApplySampleOverrides()
        {
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 185, Name = "Big \"Fire\"\nball", UnlockLevel = null });
            MasterMemoryDebugRuntime.SetOverride((1, 2), Database.TestEnemyLevelTable.FindByEnemyIdAndLevel((1, 2)) with { Hp = 999, Flags = TestFlags.Boss | TestFlags.Flying });
        }

        [Test]
        public void CreatePatch_ShouldContainOnlyChangedFields()
        {
            ApplySampleOverrides();
            var patch = MasterDataPatchService.CreatePatch();

            Assert.AreEqual("v1", patch.MasterVersion);
            Assert.AreEqual(2, patch.RecordCount);

            var skill = patch.Tables.Single(x => x.TableName == "TestSkill");
            Assert.AreEqual("test_skill", skill.MemoryTableName);
            Assert.AreEqual(typeof(TestSkill).FullName, skill.RecordType);
            var record = skill.Records.Single();
            Assert.AreEqual("{\"Id\":1001}", record.PrimaryKey.ToCanonicalString());
            CollectionAssert.AreEquivalent(new[] { "Name", "Damage", "UnlockLevel" }, record.Changes.Select(x => x.Field));

            var damage = record.Changes.Single(x => x.Field == "Damage");
            Assert.AreEqual(120, damage.Original);
            Assert.AreEqual(185, damage.Value);

            var level = patch.Tables.Single(x => x.TableName == "TestEnemyLevel").Records.Single();
            Assert.AreEqual("{\"EnemyId\":1,\"Level\":2}", level.PrimaryKey.ToCanonicalString());
        }

        [Test]
        public void SavePatch_LoadPatch_ShouldRestoreOverrides()
        {
            ApplySampleOverrides();
            var json = MasterDataPatchSerializer.ToJson(MasterDataPatchService.CreatePatch());
            MasterMemoryDebugRuntime.ClearAllOverrides();

            var result = MasterDataPatchService.Apply(MasterDataPatchSerializer.FromJson(json));

            Assert.AreEqual(MasterDataPatchApplyStatus.Applied, result.Status, string.Join("\n", result.Warnings));
            Assert.AreEqual(2, result.AppliedRecords);
            CollectionAssert.IsEmpty(result.Warnings);

            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1001, out var skill));
            Assert.AreEqual(185, skill.Damage);
            Assert.AreEqual("Big \"Fire\"\nball", skill.Name);
            Assert.IsNull(skill.UnlockLevel);
            Assert.AreEqual(ulong.MaxValue - 1, skill.BigValue, "unchanged fields come from the original");
            Assert.AreSame(Database.TestSkillTable.FindById(1001).Tags, skill.Tags);

            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestEnemyLevel, (int, int)>((1, 2), out var level));
            Assert.AreEqual(999, level.Hp);
            Assert.AreEqual(TestFlags.Boss | TestFlags.Flying, level.Flags);
        }

        [Test]
        public void SavePatch_LoadPatch_ShouldRoundTripThroughStorage()
        {
            ApplySampleOverrides();
            MasterDataPatchStorage.Save(MasterDataPatchService.CreatePatch());
            Assert.IsTrue(MasterDataPatchStorage.Exists());

            MasterMemoryDebugRuntime.ClearAllOverrides();
            var result = MasterDataPatchService.Apply(MasterDataPatchStorage.Load());

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(2, MasterMemoryDebugRuntime.OverrideCount);
        }

        [Test]
        public void NamedPatches_ShouldBeListedAndLoadedByName()
        {
            ApplySampleOverrides();
            var path = MasterDataPatchStorage.Save(MasterDataPatchService.CreatePatch(), "unit-test-balance A");
            StringAssert.EndsWith("unit-test-balance A.patch.json", path);
            MasterDataPatchStorage.Save(new MasterDataPatch { MasterVersion = "v1" });

            var names = MasterDataPatchStorage.ListPatchNames();
            CollectionAssert.Contains(names, "unit-test-balance A");
            CollectionAssert.Contains(names, "unit-test");

            MasterMemoryDebugRuntime.ClearAllOverrides();
            Assert.AreEqual(2, MasterDataPatchService.Apply(MasterDataPatchStorage.Load("unit-test-balance A")).AppliedRecords);
            Assert.AreEqual(0, MasterDataPatchService.Apply(MasterDataPatchStorage.Load()).AppliedRecords, "default patch is empty");

            Assert.IsTrue(MasterDataPatchStorage.Delete("unit-test-balance A"));
            CollectionAssert.DoesNotContain(MasterDataPatchStorage.ListPatchNames(), "unit-test-balance A");
        }

        [TestCase("  balance ", "balance")]
        [TestCase("balance.patch.json", "balance")]
        [TestCase("balance.json", "balance")]
        [TestCase("a/b:c*?", "a_b_c__")]
        [TestCase("   ", null)]
        [TestCase(null, null)]
        public void PatchName_ShouldBeNormalized(string input, string expected)
        {
            Assert.AreEqual(expected, MasterDataPatchStorage.NormalizeName(input));
        }

        [Test]
        public void DifferentMasterVersion_ShouldWarn()
        {
            ApplySampleOverrides();
            var patch = MasterDataPatchService.CreatePatch();
            MasterMemoryDebugRuntime.ClearAllOverrides();
            MasterMemoryDebugRegistry.SetMasterVersionProvider(() => "v2");

            var result = MasterDataPatchService.Apply(patch);
            Assert.AreEqual(MasterDataPatchApplyStatus.VersionMismatch, result.Status);
            Assert.AreEqual("v1", result.PatchMasterVersion);
            Assert.AreEqual("v2", result.CurrentMasterVersion);
            Assert.AreEqual(0, MasterMemoryDebugRuntime.OverrideCount, "nothing is applied without force");

            var forced = MasterDataPatchService.Apply(patch, force: true);
            Assert.IsTrue(forced.Succeeded);
            Assert.AreEqual(2, forced.AppliedRecords);
            Assert.IsTrue(forced.Warnings.Any(x => x.Contains("Master version differs")));
        }

        [Test]
        public void Apply_UnknownRecordWithoutAdded_ShouldNotAddRecords()
        {
            const string json = @"{
  ""formatVersion"": 1,
  ""masterVersion"": ""v1"",
  ""tables"": [{
    ""tableName"": ""TestSkill"",
    ""records"": [
      { ""primaryKey"": { ""Id"": 9999 }, ""changes"": [{ ""field"": ""Damage"", ""original"": 1, ""value"": 2 }] },
      { ""primaryKey"": { ""Id"": 1002.0 }, ""changes"": [
          { ""field"": ""Damage"", ""value"": 55 },
          { ""field"": ""Id"", ""value"": 5 },
          { ""field"": ""Category"", ""value"": 5 },
          { ""field"": ""Missing"", ""value"": 5 },
          { ""field"": ""Cooldown"", ""original"": 9.5, ""value"": 1.25 }
      ] }
    ]
  }]
}";
            var result = MasterDataPatchService.Apply(MasterDataPatchSerializer.FromJson(json));

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(1, result.AppliedRecords);
            Assert.AreEqual(2, result.AppliedFields);
            Assert.IsFalse(MasterMemoryDebugRuntime.IsOverridden<TestSkill, int>(9999));

            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1002, out var skill));
            Assert.AreEqual(55, skill.Damage);
            Assert.AreEqual(1.25f, skill.Cooldown);
            Assert.AreEqual(1002, skill.Id, "primary key can not be patched");
            Assert.AreEqual(1, skill.Category, "secondary key can not be patched");

            Assert.IsTrue(result.Warnings.Any(x => x.Contains("9999") && x.Contains("does not exist")));
            Assert.IsTrue(result.Warnings.Any(x => x.Contains("'Id' is read-only")));
            Assert.IsTrue(result.Warnings.Any(x => x.Contains("'Category' is read-only")));
            Assert.IsTrue(result.Warnings.Any(x => x.Contains("'Missing' does not exist")));
            Assert.IsTrue(result.Warnings.Any(x => x.Contains("original value changed")), "original 9.5 differs from 3");
        }

        [Test]
        public void Apply_ReplaceExisting_ShouldClearOtherOverrides()
        {
            MasterMemoryDebugRuntime.SetOverride(1003, Database.TestSkillTable.FindById(1003) with { Damage = 1 });
            var empty = new MasterDataPatch { MasterVersion = "v1" };

            MasterDataPatchService.Apply(empty, replaceExisting: false);
            Assert.AreEqual(1, MasterMemoryDebugRuntime.OverrideCount);

            MasterDataPatchService.Apply(empty);
            Assert.AreEqual(0, MasterMemoryDebugRuntime.OverrideCount);
        }

        [Test]
        public void Preview_ShouldMatchApplyImpactWithoutMutatingTheStore()
        {
            RegisterTestDatabase();
            MasterMemoryDebugRegistry.SetMasterVersionProvider(() => "v1");
            MasterMemoryDebugRuntime.SetOverride(
                1001, Database.TestSkillTable.FindById(1001) with { Damage = 185 });
            var patch = MasterDataPatchService.CreatePatch();
            MasterMemoryDebugRuntime.ClearAllOverrides();
            MasterMemoryDebugRuntime.SetOverride(
                1002, Database.TestSkillTable.FindById(1002) with { Damage = 77 });

            var preview = MasterDataPatchService.Preview(patch, replaceExisting: true);

            Assert.AreEqual(MasterDataPatchPreviewStatus.Ready, preview.Status);
            Assert.AreEqual(1, preview.TargetRecords);
            Assert.AreEqual(1, preview.ChangedRecords);
            Assert.AreEqual(1, preview.Fields);
            Assert.AreEqual(1, preview.RemovedExistingOverrides);
            Assert.AreEqual(1, MasterMemoryDebugRuntime.OverrideCount,
                "preview must not mutate current overrides");
            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1002, out var existing));
            Assert.AreEqual(77, existing.Damage);

            var applied = MasterDataPatchService.Apply(patch, replaceExisting: true);

            Assert.AreEqual(MasterDataPatchApplyStatus.Applied, applied.Status);
            Assert.AreEqual(preview.TargetRecords, applied.AppliedRecords);
            Assert.AreEqual(preview.Fields, applied.AppliedFields);
            Assert.IsFalse(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1002, out _));
            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1001, out var skill));
            Assert.AreEqual(185, skill.Damage);
        }

        [Test]
        public void Preview_InvalidPatch_ShouldExposeErrorsWithoutMutation()
        {
            RegisterTestDatabase();
            MasterMemoryDebugRegistry.SetMasterVersionProvider(() => "v1");
            MasterMemoryDebugRuntime.SetOverride(
                1002, Database.TestSkillTable.FindById(1002) with { Damage = 77 });
            var patch = new MasterDataPatch
            {
                MasterVersion = "v1",
                SchemaHash = MasterMemoryDebugRegistry.GetSchemaHash(),
                Tables =
                {
                    new MasterDataPatchTable
                    {
                        TableName = nameof(TestSkill),
                        RecordType = typeof(TestSkill).FullName,
                        Records =
                        {
                            new MasterDataPatchRecord
                            {
                                PrimaryKey = new MasterDataJsonObject { { "Id", 1001 } },
                                Changes =
                                {
                                    new MasterDataPatchChange
                                    {
                                        Field = "MissingField",
                                        Original = 120,
                                        Value = 999,
                                    },
                                },
                            },
                        },
                    },
                },
            };

            var preview = MasterDataPatchService.Preview(patch, replaceExisting: true);

            Assert.AreEqual(MasterDataPatchPreviewStatus.Invalid, preview.Status);
            Assert.IsNotEmpty(preview.Errors);
            Assert.AreEqual(1, MasterMemoryDebugRuntime.OverrideCount);
            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1002, out _));
        }

        [Test]
        public void Apply_InvalidPatch_ShouldNotReplaceExistingOverrides()
        {
            RegisterTestDatabase();
            MasterMemoryDebugRegistry.SetMasterVersionProvider(() => "v1");
            MasterMemoryDebugRuntime.SetOverride(
                1002, Database.TestSkillTable.FindById(1002) with { Damage = 77 });

            var patch = new MasterDataPatch
            {
                MasterVersion = "v1",
                SchemaHash = MasterMemoryDebugRegistry.GetSchemaHash(),
            };
            patch.Tables.Add(new MasterDataPatchTable
            {
                TableName = nameof(TestSkill),
                RecordType = typeof(TestSkill).FullName,
                Records =
                {
                    new MasterDataPatchRecord
                    {
                        PrimaryKey = new MasterDataJsonObject { { "Id", 1001 } },
                        Changes =
                        {
                            new MasterDataPatchChange
                            {
                                Field = "MissingField",
                                Original = 120,
                                Value = 999,
                            },
                        },
                    },
                },
            });

            var result = MasterDataPatchService.Apply(patch, replaceExisting: true);

            Assert.AreEqual(MasterDataPatchApplyStatus.Invalid, result.Status);
            Assert.AreEqual(1, MasterMemoryDebugRuntime.OverrideCount,
                "preflight failure must not clear the existing override layer");
            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1002, out var existing));
            Assert.AreEqual(77, existing.Damage);
        }

        [Test]
        public void Apply_SchemaMismatch_ShouldRequireForce()
        {
            RegisterTestDatabase();
            MasterMemoryDebugRegistry.SetMasterVersionProvider(() => "v1");
            MasterMemoryDebugRuntime.SetOverride(
                1001, Database.TestSkillTable.FindById(1001) with { Damage = 185 });
            var patch = MasterDataPatchService.CreatePatch();
            MasterMemoryDebugRuntime.ClearAllOverrides();
            patch.SchemaHash = "different-schema";

            var rejected = MasterDataPatchService.Apply(patch);

            Assert.AreEqual(MasterDataPatchApplyStatus.SchemaMismatch, rejected.Status);
            Assert.AreEqual(0, MasterMemoryDebugRuntime.OverrideCount);

            var forced = MasterDataPatchService.Apply(patch, force: true);
            Assert.AreEqual(MasterDataPatchApplyStatus.Applied, forced.Status);
            Assert.IsTrue(forced.Warnings.Any(x => x.Contains("schema differs")));
            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1001, out var skill));
            Assert.AreEqual(185, skill.Damage);
        }

        [Test]
        public void Apply_UnsupportedFormat_ShouldFail()
        {
            var result = MasterDataPatchService.Apply(new MasterDataPatch { FormatVersion = 99, MasterVersion = "v1" });
            Assert.AreEqual(MasterDataPatchApplyStatus.UnsupportedFormat, result.Status);
        }
    }
}
