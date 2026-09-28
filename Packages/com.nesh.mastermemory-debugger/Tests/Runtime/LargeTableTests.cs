using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using Nesh.MasterMemoryDebugger.Tests.Generated;
using Nesh.MasterMemoryDebugger.Tests.Generated.Tables;

namespace Nesh.MasterMemoryDebugger.Tests
{
    /// <summary>
    /// 50,000 records: every operation of the debugger that walks a whole table stays well below a second (catches
    /// accidental O(n²) work). The limits are generous for slow machines; the timings are printed.
    /// </summary>
    public class LargeTableTests : DebuggerTestBase
    {
        const int RecordCount = 50000;
        const double LimitMs = 3000;

        MemoryDatabase database;

        [SetUp]
        public void SetUp()
        {
            var skills = new TestSkill[RecordCount];
            for (var i = 0; i < RecordCount; i++)
            {
                skills[i] = new TestSkill
                {
                    Id = 100000 + i,
                    Category = i % 10,
                    Name = "Skill " + i,
                    Damage = (i * 7) % 1000,
                    Cooldown = (i % 20) * 0.5f,
                    Element = (TestElement)(i % 3),
                    SummonEnemyId = i % 5 == 0 ? 2 : 0,
                };
            }
            var enemyLevels = new[]
            {
                new TestEnemyLevel { EnemyId = 1, Level = 1, Hp = 100 },
                new TestEnemyLevel { EnemyId = 2, Level = 1, Hp = 400 },
            };
            database = new MemoryDatabase(TestEnemyLevelTable: new TestEnemyLevelTable(enemyLevels), TestSkillTable: new TestSkillTable(skills));
            var db = database;
            MasterMemoryDebugRegistry.RegisterDatabase(MemoryDatabase.GetMetaDatabase(), name => MemoryDatabase.GetTable(db, name));
        }

        static T Measure<T>(string name, Func<T> action)
        {
            var watch = Stopwatch.StartNew();
            var result = action();
            watch.Stop();
            TestContext.Progress.WriteLine($"{name,-40} {watch.Elapsed.TotalMilliseconds,8:0.0} ms");
            Assert.Less(watch.Elapsed.TotalMilliseconds, LimitMs, name);
            return result;
        }

        static void Measure(string name, Action action) => Measure(name, () =>
        {
            action();
            return 0;
        });

        [Test]
        public void WholeTableOperations_ShouldStayFast()
        {
            var table = Table<TestSkill>();
            var snapshot = Measure("snapshot", () => table.CreateRecordSnapshot());
            Assert.AreEqual(RecordCount, snapshot.Count);

            var query = MasterRecordQuery.Parse("Damage>500 Element=Fire", table.TypeDescriptor);
            var filtered = new List<MasterMemoryRecordDescriptor>();
            Measure("filter", () => MasterRecordListController.Filter(snapshot, query, false, int.MaxValue, filtered));
            Measure("text search", () => MasterRecordListController.Filter(snapshot, MasterRecordQuery.Parse("skill 4999", table.TypeDescriptor), false, int.MaxValue, new List<MasterMemoryRecordDescriptor>()));
            var sorted = snapshot.ToList();
            Measure("sort", () => MasterRecordListController.SortBy(sorted, "Damage", table, true));
            var columns = MasterRecordListController.CreateColumns(table);
            Measure("auto fit columns", () => MasterGridLayout.AutoFit(columns, snapshot));
            Measure("copy rows", () => MasterRecordListController.BuildTsv(columns, snapshot));
            var found = Measure("find in every table", () => MasterMemoryGlobalSearch.Find("Skill 4999"));
            Assert.AreEqual(11, found.TotalHits, "Skill 4999 and Skill 49990 … 49999");
            Measure("find whole value", () => MasterMemoryGlobalSearch.Find("104999", wholeValue: true));

            var damage = table.TypeDescriptor.Fields.Single(x => x.Name == "Damage");
            var edit = Measure("batch edit (all records)", () =>
            {
                using (MasterMemoryDebugHistory.Record("batch")) return MasterMemoryBatchEdit.Apply(snapshot, damage, MasterMemoryBatchOperation.Add, "1");
            });
            Assert.AreEqual(RecordCount, edit.Changed);

            var changes = Measure("changes summary", () => MasterMemoryChangeSummary.Build());
            var tsv = Measure("changes TSV", () => MasterMemoryChangeSummary.ToTsv(changes));
            Measure("rebuild database", () => MasterMemoryDebugRebuild.Apply(database));
            Measure("patch", () => MasterDataPatchService.CreatePatchJson());

            Measure("undo", () => MasterMemoryDebugHistory.Undo());
            Assert.AreEqual(0, MasterMemoryDebugRuntime.OverrideCount);

            var plan = Measure("read TSV", () => MasterMemoryTsvImport.Read(tsv));
            Assert.AreEqual(RecordCount, plan.Changes.Count);
            Measure("apply TSV", () => MasterMemoryTsvImport.Apply(plan));

            var enemies = Table<TestEnemyLevel>();
            var incoming = Measure("incoming references", () => MasterMemoryReferences.GetIncoming(enemies));
            var boss = enemies.CreateRecordSnapshot().Single(x => Equals(x.PrimaryKey, (2, 1)));
            var referencing = Measure("referenced by", () => MasterMemoryReferences.FindReferencing(incoming[0], MasterMemoryReferences.GetReferencedValue(incoming[0], boss)));
            Assert.AreEqual(RecordCount / 5, referencing.Count);

            Measure("validate (MasterMemory)", () => MasterMemoryDebugRebuild.Validate(database));

            // remote editing: what the game sends when the tool connects, and what the tool does with it
            MasterMemoryDebugRemote.SerializerOptions = MessagePack.MessagePackSerializerOptions.Standard.WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);
            try
            {
                var welcome = Measure("remote: collect tables", () => MasterMemoryRemoteServer.CreateWelcome());
                var bytes = Measure("remote: encode", () => MasterMemoryRemoteProtocol.Encode(welcome));
                TestContext.Progress.WriteLine($"remote: welcome size {bytes.Length / 1024} KB");
                var decoded = Measure("remote: decode", () => MasterMemoryRemoteProtocol.DecodeWelcome(bytes));
                var skillTable = decoded.Tables.Single(x => x.TableName == nameof(TestSkill));
                Assert.AreEqual(RecordCount, skillTable.RecordCount);
                Assert.AreEqual(0, skillTable.Records.Count, "Welcome v6 is metadata-only");
                Assert.Less(bytes.Length, 128 * 1024, "the initial remote handshake must not scale with table record count");
            }
            finally
            {
                MasterMemoryDebugRemote.SerializerOptions = null;
            }
        }
    }
}
