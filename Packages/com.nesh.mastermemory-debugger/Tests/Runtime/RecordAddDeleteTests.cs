using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    public class RecordAddDeleteTests : DebuggerTestBase
    {
        [SetUp]
        public void SetUp()
        {
            RegisterTestDatabase();
        }

        [TearDown]
        public void TearDown()
        {
            MasterDataPatchStorage.Delete();
        }

        TestSkill AddCopy(int sourceId, int newId)
        {
            Assert.IsTrue(MasterMemoryRecordFactory.TryCreate(Table<TestSkill>(), Database.TestSkillTable.FindById(sourceId), new[] { newId.ToString() }, out var record, out var key, out var error), error);
            MasterMemoryDebugRuntime.Store.Set(typeof(TestSkill), key, record);
            return (TestSkill)record;
        }

        [Test]
        public void Delete_ShouldHideTheOverrideButKeepTheOriginalForGameplay()
        {
            MasterMemoryDebugRuntime.SetOverride(1002, Database.TestSkillTable.FindById(1002) with { Damage = 5 });
            MasterMemoryDebugRuntime.Store.Delete(typeof(TestSkill), 1002);

            Assert.IsTrue(MasterMemoryDebugRuntime.IsDeleted<TestSkill, int>(1002));
            Assert.IsTrue(MasterMemoryDebugRuntime.IsOverridden<TestSkill, int>(1002));
            Assert.IsFalse(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1002, out _), "TryGetOverride never returns a deleted record");
            Assert.AreEqual(100, MasterMemoryDebugRuntime.Resolve<TestSkill, int>(1002, Database.TestSkillTable.FindById).Damage);
            Assert.IsFalse(MasterMemoryDebugRuntime.Store.TryGet(typeof(TestSkill), 1002, out _));
            CollectionAssert.IsEmpty(MasterMemoryDebugRuntime.GetOverrides<TestSkill>());
            CollectionAssert.AreEqual(new[] { 1002 }, MasterMemoryDebugRuntime.GetDeletedKeys<TestSkill, int>());
            Assert.IsTrue(MasterMemoryDebugRuntime.GetAllOverrides().Single().IsDeleted);

            var record = Table<TestSkill>().CreateRecordSnapshot().Single(x => Equals(x.PrimaryKey, 1002));
            Assert.IsTrue(record.IsDeleted);
            Assert.IsTrue(record.IsModified);
            Assert.AreSame(Database.TestSkillTable.FindById(1002), record.Current, "a deleted record shows its original");

            Assert.IsTrue(MasterMemoryDebugRuntime.RemoveOverride<TestSkill, int>(1002), "Remove restores it");
            Assert.IsFalse(MasterMemoryDebugRuntime.IsDeleted<TestSkill, int>(1002));
        }

        [Test]
        public void AddedRecords_ShouldBeInTheSnapshotAndReadable()
        {
            var added = AddCopy(1001, 1004);
            Assert.AreEqual(1004, added.Id);
            Assert.AreEqual("Fireball", added.Name);
            Assert.AreEqual(1001, Database.TestSkillTable.FindById(1001).Id, "the source is not modified");

            var snapshot = Table<TestSkill>().CreateRecordSnapshot();
            Assert.AreEqual(4, snapshot.Count);
            var record = snapshot.Last();
            Assert.IsTrue(record.IsAdded);
            Assert.IsNull(record.Original);
            Assert.AreSame(added, record.Current);
            Assert.IsTrue(record.Matches("fireball"));

            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1004, out var read));
            Assert.AreSame(added, read);

            MasterMemoryDebugRuntime.RemoveOverride<TestSkill, int>(1004);
            Assert.AreEqual(3, Table<TestSkill>().CreateRecordSnapshot().Count);
            Assert.IsNull(record.Current, "the stale descriptor has no record any more");
        }

        [Test]
        public void TryCreate_ShouldRejectUsedAndInvalidKeys()
        {
            var table = Table<TestSkill>();
            Assert.IsFalse(MasterMemoryRecordFactory.TryCreate(table, null, new[] { "1001" }, out _, out _, out var error));
            StringAssert.Contains("already has a record 1001", error);
            Assert.IsFalse(MasterMemoryRecordFactory.TryCreate(table, null, new[] { "x" }, out _, out _, out error));
            StringAssert.Contains("Id", error);
            Assert.IsFalse(MasterMemoryRecordFactory.TryCreate(table, null, new[] { "1", "2" }, out _, out _, out _));

            MasterMemoryDebugRuntime.Store.Delete(typeof(TestSkill), 1003);
            Assert.IsFalse(MasterMemoryRecordFactory.TryCreate(table, null, new[] { "1003" }, out _, out _, out error));
            StringAssert.Contains("deleted record", error);

            AddCopy(1001, 2000);
            Assert.IsFalse(MasterMemoryRecordFactory.TryCreate(table, null, new[] { "2000" }, out _, out _, out error));
            StringAssert.Contains("added record", error);

            // manual tables without [PrimaryKey] can not add records
            MasterMemoryDebugRegistry.RegisterTable<ManualItem, int>("ItemMaster", () => new[] { new ManualItem(1, "a", 1) }, x => x.Code);
            Assert.IsFalse(MasterMemoryRecordFactory.CanAdd(Table<ManualItem>(), out var reason));
            StringAssert.Contains("[PrimaryKey]", reason);
        }

        [Test]
        public void NewRecords_ShouldGetDefaultsAndTheNextKey()
        {
            var table = Table<TestSkill>();
            CollectionAssert.AreEqual(new[] { "1004" }, MasterMemoryRecordFactory.SuggestKey(table));

            var record = (TestSkill)MasterMemoryRecordFactory.CreateDefault(table);
            Assert.AreEqual(string.Empty, record.Name);
            CollectionAssert.IsEmpty(record.Tags);
            Assert.IsNull(record.UnlockLevel);

            // composite key: the other members of the source, the last one + 1
            var enemies = Table<TestEnemyLevel>();
            CollectionAssert.AreEqual(new[] { "1", "3" }, MasterMemoryRecordFactory.SuggestKey(enemies, Database.TestEnemyLevelTable.FindByEnemyIdAndLevel((1, 1))));
            Assert.IsTrue(MasterMemoryRecordFactory.TryCreate(enemies, Database.TestEnemyLevelTable.FindByEnemyIdAndLevel((1, 2)), new[] { "1", "3" }, out var enemy, out var key, out var error), error);
            Assert.AreEqual((1, 3), key);
            Assert.AreEqual(150, ((TestEnemyLevel)enemy).Hp);
        }

        [Test]
        public void Rebuild_ShouldAddAndRemoveRecords()
        {
            AddCopy(1002, 1500);
            MasterMemoryDebugRuntime.Store.Delete(typeof(TestSkill), 1003);
            MasterMemoryDebugRuntime.Store.Delete(typeof(TestEnemyLevel), (1, 2));

            var rebuilt = MasterMemoryDebugRebuild.Apply(Database);
            Assert.IsTrue(rebuilt.TestSkillTable.TryFindById(1500, out var added));
            Assert.AreEqual("Ice Blast", added.Name);
            Assert.IsFalse(rebuilt.TestSkillTable.TryFindById(1003, out _));
            Assert.AreEqual(3, rebuilt.TestSkillTable.All.Count);
            Assert.AreEqual(3, rebuilt.TestSkillTable.FindByCategory(1).Count, "1001, 1002 and the added copy of 1002");
            Assert.AreEqual(0, rebuilt.TestSkillTable.FindByCategory(2).Count, "the deleted 1003 is not indexed");
            Assert.IsFalse(rebuilt.TestEnemyLevelTable.TryFindByEnemyIdAndLevel((1, 2), out _));
            Assert.IsTrue(Database.TestSkillTable.TryFindById(1003, out _), "the original is untouched");
        }

        [Test]
        public void Patches_ShouldSaveAndLoadAddedAndDeletedRecords()
        {
            var added = AddCopy(1001, 1600);
            MasterMemoryDebugRuntime.Store.Set(typeof(TestSkill), 1600, added with { Name = "Firestorm", Category = 3 });
            MasterMemoryDebugRuntime.Store.Delete(typeof(TestSkill), 1002);
            MasterMemoryDebugRuntime.SetOverride(1003, Database.TestSkillTable.FindById(1003) with { Damage = 9 });

            var warnings = new List<string>();
            var patch = MasterDataPatchService.CreatePatch(warnings);
            CollectionAssert.IsEmpty(warnings);
            Assert.AreEqual(MasterDataPatch.CurrentFormatVersion, patch.FormatVersion);
            var records = patch.Tables.Single().Records;
            var addedRecord = records.Single(x => x.Added);
            Assert.AreEqual("{\"Id\":1600}", addedRecord.PrimaryKey.ToCanonicalString());
            Assert.IsTrue(addedRecord.Changes.All(x => !x.HasOriginal));
            Assert.IsTrue(addedRecord.Changes.Any(x => x.Field == "Category"), "an added record keeps its secondary keys");
            Assert.IsFalse(addedRecord.Changes.Any(x => x.Field == "Id"));
            Assert.AreEqual("{\"Id\":1002}", records.Single(x => x.Deleted).PrimaryKey.ToCanonicalString());

            var json = MasterDataPatchSerializer.ToJson(patch);
            StringAssert.Contains("\"added\": true", json);
            StringAssert.Contains("\"deleted\": true", json);
            MasterDataPatchStorage.Save(MasterDataPatchSerializer.FromJson(json));

            MasterMemoryDebugRuntime.ClearAllOverrides();
            var result = MasterDataPatchService.Apply(MasterDataPatchStorage.Load());
            Assert.AreEqual(MasterDataPatchApplyStatus.Applied, result.Status);
            CollectionAssert.IsEmpty(result.Warnings);
            Assert.AreEqual(3, result.AppliedRecords);
            Assert.AreEqual(1, result.AddedRecords);
            Assert.AreEqual(1, result.DeletedRecords);

            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1600, out var loaded));
            Assert.AreEqual("Firestorm", loaded.Name);
            Assert.AreEqual(3, loaded.Category);
            Assert.AreEqual(ulong.MaxValue - 1, loaded.BigValue);
            CollectionAssert.AreEqual(new[] { 1, 2 }, loaded.Tags);
            Assert.IsTrue(MasterMemoryDebugRuntime.IsDeleted<TestSkill, int>(1002));
            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1003, out var changed));
            Assert.AreEqual(9, changed.Damage);
        }

        [Test]
        public void PatchesWithoutAddedOrDeletedRecords_ShouldStayVersion1()
        {
            MasterMemoryDebugRuntime.SetOverride(1003, Database.TestSkillTable.FindById(1003) with { Damage = 9 });
            Assert.AreEqual(MasterDataPatch.BasicFormatVersion, MasterDataPatchService.CreatePatch().FormatVersion);
        }

        [Test]
        public void PatchApply_ShouldWarnAboutDeletedRecordsThatDoNotExist()
        {
            var patch = MasterDataPatchSerializer.FromJson(@"{
  ""formatVersion"": 2,
  ""tables"": [{ ""tableName"": ""TestSkill"", ""records"": [
    { ""primaryKey"": { ""Id"": 4242 }, ""deleted"": true, ""changes"": [] },
    { ""primaryKey"": { ""Id"": 1001 }, ""added"": true, ""changes"": [{ ""field"": ""Damage"", ""value"": 3 }] }
  ] }]
}");
            var result = MasterDataPatchService.Apply(patch, force: true);
            Assert.IsTrue(result.Warnings.Any(x => x.Contains("4242") && x.Contains("does not exist")), string.Join("\n", result.Warnings));
            Assert.IsTrue(result.Warnings.Any(x => x.Contains("1001") && x.Contains("exists in the master data now")), string.Join("\n", result.Warnings));
            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1001, out var skill), "applied as changes");
            Assert.AreEqual(3, skill.Damage);

            Assert.Throws<FormatException>(() => MasterDataPatchSerializer.FromJson(@"{ ""formatVersion"": 2, ""tables"": [{ ""records"": [{ ""primaryKey"": {}, ""added"": true, ""deleted"": true }] }] }"));
            var future = MasterDataPatchService.Apply(new MasterDataPatch { FormatVersion = 3 });
            Assert.AreEqual(MasterDataPatchApplyStatus.UnsupportedFormat, future.Status);
        }

        [Test]
        public void ChangeSummary_ShouldListAddedAndDeletedRecords()
        {
            AddCopy(1001, 1700);
            MasterMemoryDebugRuntime.Store.Delete(typeof(TestSkill), 1002);

            var entries = MasterMemoryChangeSummary.Build();
            var added = entries.Single(x => Equals(x.PrimaryKey, 1700));
            Assert.AreEqual(MasterMemoryChangeStatus.Added, added.Status);
            Assert.IsTrue(added.Changes.Any(x => x.Name == "Name" && (string)x.NewValue == "Fireball"));
            var deleted = entries.Single(x => Equals(x.PrimaryKey, 1002));
            Assert.AreEqual(MasterMemoryChangeStatus.Deleted, deleted.Status);
            Assert.AreEqual("Ice Blast", deleted.DisplayName);
            CollectionAssert.IsEmpty(deleted.Changes);

            var tsv = MasterMemoryChangeSummary.ToTsv(entries);
            StringAssert.Contains("TestSkill\t1002\tIce Blast\t(deleted)", tsv);
            StringAssert.Contains("TestSkill\t1700\tFireball\tName\t\tFireball", tsv);

            // Paste TSV of the same text: the deleted line is ignored, the added record's values are unchanged
            var plan = MasterMemoryTsvImport.Read(tsv);
            // only the secondary key and the list of the added record can not be imported
            Assert.IsTrue(plan.Problems.All(x => x.Contains("Category") || x.Contains("Tags")), string.Join("\n", plan.Problems));
            CollectionAssert.IsEmpty(plan.Changes);
        }

        [Test]
        public void BatchEditAndTsv_ShouldKeepAddedRecordsAndSkipDeletedOnes()
        {
            AddCopy(1003, 1800);
            MasterMemoryDebugRuntime.Store.Delete(typeof(TestSkill), 1002);
            var damage = Table<TestSkill>().TypeDescriptor.Fields.Single(x => x.Name == "Damage");

            // the added copy of 1003 has Damage 0: setting 0 again keeps it (an added record has no original to equal)
            var result = MasterMemoryBatchEdit.Apply(Table<TestSkill>().CreateRecordSnapshot(), damage, MasterMemoryBatchOperation.Set, "0");
            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1800, out _), "the added record still exists");
            Assert.IsTrue(MasterMemoryDebugRuntime.IsDeleted<TestSkill, int>(1002), "the deleted record is not edited");
            Assert.AreEqual(1, result.Changed, "only 1001 changes (120 → 0)");

            var plan = MasterMemoryTsvImport.Read("table\tkey\tfield\tcurrent\nTestSkill\t1800\tDamage\t40\nTestSkill\t1002\tDamage\t40\n");
            Assert.AreEqual(1, plan.Changes.Count);
            Assert.IsTrue(plan.Problems.Any(x => x.Contains("deleted")));
            MasterMemoryTsvImport.Apply(plan);
            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1800, out var added));
            Assert.AreEqual(40, added.Damage);
        }

        [Test]
        public void Find_ShouldSkipDeletedRecordsAndFindAddedOnes()
        {
            AddCopy(1001, 1900);
            MasterMemoryDebugRuntime.Store.Delete(typeof(TestSkill), 1001);
            CollectionAssert.AreEqual(new object[] { 1900 }, MasterMemoryGlobalSearch.Find("Fireball", wholeValue: true).Hits.Select(x => x.Record.PrimaryKey).ToArray());
        }

        [Test]
        public void Undo_ShouldRevertAddsAndDeletes()
        {
            using (MasterMemoryDebugHistory.Record("delete")) MasterMemoryDebugRuntime.Store.Delete(typeof(TestSkill), 1002);
            using (MasterMemoryDebugHistory.Record("add")) AddCopy(1001, 1950);

            MasterMemoryDebugHistory.Undo();
            Assert.IsFalse(MasterMemoryDebugRuntime.IsOverridden<TestSkill, int>(1950));
            MasterMemoryDebugHistory.Undo();
            Assert.IsFalse(MasterMemoryDebugRuntime.IsDeleted<TestSkill, int>(1002));
            MasterMemoryDebugHistory.Redo();
            Assert.IsTrue(MasterMemoryDebugRuntime.IsDeleted<TestSkill, int>(1002));
            MasterMemoryDebugHistory.Redo();
            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1950, out _));
        }
    }
}
