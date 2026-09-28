using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using MessagePack;
using MessagePack.Resolvers;
using NUnit.Framework;

namespace Nesh.MasterMemoryDebugger.Tests
{
    /// <summary>
    /// Remote editing over a real loopback TCP connection. Game and tool share the static registry / store of one process,
    /// so each test runs one side for real and plays the other side with raw protocol messages.
    /// </summary>
    public class RemoteTests : DebuggerTestBase
    {
        TcpClient rawClient;
        TcpListener rawServer;

        [SetUp]
        public void SetUp()
        {
            // the test records have no [MessagePackObject]
            MasterMemoryDebugRemote.SerializerOptions = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            MasterMemoryDebugRemote.Stop();
            MasterMemoryDebugRemote.SerializerOptions = null;
            MasterMemoryDebugRemote.AutoReconnect = true;
            MasterMemoryDebugRemote.ReconnectDelaySeconds = 3;
            MasterMemoryDebugRemote.IsToolMode = false;
            rawClient?.Close();
            rawServer?.Stop();
        }

        static void PumpUntil(Func<bool> condition, string what)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                MasterMemoryDebugRemote.Pump();
                if (watch.ElapsedMilliseconds > 5000) Assert.Fail("Timed out waiting for " + what);
                System.Threading.Thread.Sleep(5);
            }
        }

        static byte[] Receive(NetworkStream stream, string what)
        {
            PumpUntil(() => stream.DataAvailable, what);
            return MasterMemoryRemoteProtocol.ReadFrame(stream);
        }

        /// <summary>The next message of <paramref name="type"/>, skipping others.</summary>
        static byte[] ReceiveOf(NetworkStream stream, MasterMemoryRemoteProtocol.MessageType type)
        {
            while (true)
            {
                var payload = Receive(stream, type.ToString());
                if (MasterMemoryRemoteProtocol.GetType(payload) == type) return payload;
            }
        }

        static MasterMemoryRemoteProtocol.Table RequestTable(NetworkStream stream, MasterMemoryRemoteProtocol.Table manifest)
        {
            var requestId = Guid.NewGuid().ToString("N");
            MasterMemoryRemoteProtocol.WriteFrame(stream, MasterMemoryRemoteProtocol.Encode(
                new MasterMemoryRemoteProtocol.TableRequest { RequestId = requestId, TableName = manifest.TableName }));
            var result = new MasterMemoryRemoteProtocol.Table
            {
                TableName = manifest.TableName,
                MemoryTableName = manifest.MemoryTableName,
                RecordType = manifest.RecordType,
                KeyType = manifest.KeyType,
                Group = manifest.Group,
                RecordCount = manifest.RecordCount,
                HasCustomDisplayName = manifest.HasCustomDisplayName,
                DisplayNames = manifest.HasCustomDisplayName ? new System.Collections.Generic.List<string>() : null,
            };
            var next = 0;
            while (true)
            {
                var chunk = MasterMemoryRemoteProtocol.DecodeTableChunk(
                    ReceiveOf(stream, MasterMemoryRemoteProtocol.MessageType.TableChunk));
                if (chunk.RequestId != requestId) continue;
                Assert.AreEqual(next++, chunk.ChunkIndex);
                Assert.IsTrue(string.IsNullOrEmpty(chunk.Error), chunk.Error);
                result.Records.AddRange(chunk.Records);
                if (result.DisplayNames != null && chunk.DisplayNames != null) result.DisplayNames.AddRange(chunk.DisplayNames);
                if (chunk.IsLast) break;
            }
            Assert.AreEqual(manifest.RecordCount, result.Records.Count);
            return result;
        }

        static byte[] Pack<T>(T record) => MasterMemoryRemotePeer.Serialize(typeof(T), record);

        [Test]
        public void ReplayCache_ShouldEvictTheOldestRequest()
        {
            var cache = new MasterMemoryReplayCache<int>(2);
            cache.Add("a", 1);
            cache.Add("b", 2);
            Assert.IsTrue(cache.TryGetValue("a", out _));

            cache.Add("c", 3);

            Assert.IsFalse(cache.TryGetValue("a", out _));
            Assert.IsTrue(cache.TryGetValue("b", out var b));
            Assert.AreEqual(2, b);
            Assert.IsTrue(cache.TryGetValue("c", out var c));
            Assert.AreEqual(3, c);
            Assert.AreEqual(2, cache.Count);
        }

        // ------------------------------------------------------------------ game side (server)

        NetworkStream ConnectRaw(string code)
        {
            rawClient = new TcpClient();
            rawClient.Connect(IPAddress.Loopback, MasterMemoryDebugRemote.ServerPort);
            var stream = rawClient.GetStream();
            MasterMemoryRemoteProtocol.WriteFrame(stream, MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.Hello { Version = MasterMemoryRemoteProtocol.Version, Code = code }));
            return stream;
        }

        [Test]
        public void Server_ShouldRefuseAWrongCode()
        {
            RegisterTestDatabase();
            Assert.IsTrue(MasterMemoryDebugRemote.StartServer(0, "123456"));
            Assert.AreEqual(MasterMemoryRemoteState.Listening, MasterMemoryDebugRemote.State);

            var stream = ConnectRaw("000000");
            var reply = Receive(stream, "the reject");
            Assert.AreEqual(MasterMemoryRemoteProtocol.MessageType.Reject, MasterMemoryRemoteProtocol.GetType(reply));
            StringAssert.Contains("pairing code", MasterMemoryRemoteProtocol.DecodeReject(reply));
            Assert.AreEqual(MasterMemoryRemoteState.Listening, MasterMemoryDebugRemote.State);
        }

        [Test]
        public void Server_ShouldSendTheTablesAndSyncBothWays()
        {
            RegisterTestDatabase();
            MasterMemoryDebugRegistry.SetTableGroup("Battle", nameof(TestSkill));
            MasterMemoryDebugRuntime.SetOverride(1002, Database.TestSkillTable.FindById(1002) with { Damage = 5 });
            MasterMemoryDebugRemote.StartServer(0, "123456");

            var stream = ConnectRaw("123456");
            var welcome = MasterMemoryRemoteProtocol.DecodeWelcome(Receive(stream, "the welcome"));
            Assert.AreEqual(MasterMemoryRemoteState.Connected, MasterMemoryDebugRemote.State);

            var skills = welcome.Tables.Single(x => x.TableName == nameof(TestSkill));
            Assert.AreEqual("Battle", skills.Group);
            Assert.AreEqual(3, skills.RecordCount);
            Assert.AreEqual(0, skills.Records.Count, "Welcome v6 contains metadata only");
            var loadedSkills = RequestTable(stream, skills);
            var first = (TestSkill)MasterMemoryRemotePeer.Deserialize(typeof(TestSkill), loadedSkills.Records[0]);
            Assert.AreEqual("Fireball", first.Name);
            Assert.AreEqual(typeof(TestSkill), Type.GetType(skills.RecordType));
            Assert.AreEqual(1, welcome.Overrides.Count);

            // game edits → tool
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 777 });
            MasterMemoryDebugRuntime.RemoveOverride<TestSkill, int>(1002);
            var changes = MasterMemoryRemoteProtocol.DecodeChanges(ReceiveOf(stream, MasterMemoryRemoteProtocol.MessageType.Changes));
            Assert.AreEqual(2, changes.Count);
            Assert.IsTrue(changes[0].IsSet);
            Assert.AreEqual(777, ((TestSkill)MasterMemoryRemotePeer.Deserialize(typeof(TestSkill), changes[0].Record)).Damage);
            Assert.IsFalse(changes[1].IsSet);

            // tool edits → game, not sent back
            var fromTool = new[]
            {
                new MasterMemoryRemoteProtocol.Change { IsSet = true, TableName = nameof(TestEnemyLevel), Record = Pack(Database.TestEnemyLevelTable.FindByEnemyIdAndLevel((2, 1)) with { Hp = 9 }) },
            }.ToList();
            MasterMemoryRemoteProtocol.WriteFrame(stream, MasterMemoryRemoteProtocol.Encode(fromTool));
            PumpUntil(() => MasterMemoryDebugRuntime.Store.IsOverridden(typeof(TestEnemyLevel), (2, 1)), "the tool's change");
            MasterMemoryDebugRuntime.Store.TryGet(typeof(TestEnemyLevel), (2, 1), out var enemy);
            Assert.AreEqual(9, ((TestEnemyLevel)enemy).Hp);
            MasterMemoryDebugRemote.Pump();
            while (stream.DataAvailable)
            {
                // the rebuild-less test game has no validation; only validation states may follow
                Assert.AreEqual(MasterMemoryRemoteProtocol.MessageType.ValidationState, MasterMemoryRemoteProtocol.GetType(MasterMemoryRemoteProtocol.ReadFrame(stream)), "changes of the tool are not echoed");
            }
        }

        [Test]
        public void Server_ShouldListenAgainWhenTheToolLeaves()
        {
            RegisterTestDatabase();
            MasterMemoryDebugRemote.StartServer(0, "1");
            var stream = ConnectRaw("1");
            Receive(stream, "the welcome");
            rawClient.Close();
            PumpUntil(() => MasterMemoryDebugRemote.State == MasterMemoryRemoteState.Listening, "the disconnect");
        }

        [Test]
        public void Protocol_ShouldRejectOversizedFrameBeforeReadingPayload()
        {
            using var stream = new MemoryStream();
            var header = BitConverter.GetBytes(MasterMemoryRemoteProtocol.MaxFrameBytes + 1);
            if (!BitConverter.IsLittleEndian) Array.Reverse(header);
            stream.Write(header, 0, header.Length);
            stream.Position = 0;

            var error = Assert.Throws<InvalidDataException>(() => MasterMemoryRemoteProtocol.ReadFrame(stream));

            StringAssert.Contains("Invalid frame length", error.Message);
        }

        [Test]
        public void PatchPlan_ShouldScanEachOriginalTableOnce()
        {
            MasterMemoryDebugRegistry.SetMasterVersionProvider(() => "v1");
            var records = new[]
            {
                Database.TestSkillTable.FindById(1001),
                Database.TestSkillTable.FindById(1002),
                Database.TestSkillTable.FindById(1003),
            };
            var scans = 0;
            MasterMemoryDebugRegistry.RegisterTable<TestSkill, int>(
                "CountedSkill",
                () =>
                {
                    scans++;
                    return records;
                },
                x => x.Id);

            var patch = new MasterDataPatch { MasterVersion = "v1" };
            var table = new MasterDataPatchTable
            {
                TableName = "CountedSkill",
                RecordType = typeof(TestSkill).FullName,
            };
            table.Records.Add(new MasterDataPatchRecord
            {
                PrimaryKey = new MasterDataJsonObject { { "Id", 1001 } },
                Changes = new System.Collections.Generic.List<MasterDataPatchChange>
                {
                    new MasterDataPatchChange { Field = "Damage", Original = 120, Value = 121 },
                },
            });
            table.Records.Add(new MasterDataPatchRecord
            {
                PrimaryKey = new MasterDataJsonObject { { "Id", 1002 } },
                Changes = new System.Collections.Generic.List<MasterDataPatchChange>
                {
                    new MasterDataPatchChange { Field = "Damage", Original = 100, Value = 101 },
                },
            });
            table.Records.Add(new MasterDataPatchRecord
            {
                PrimaryKey = new MasterDataJsonObject { { "Id", 1003 } },
                Changes = new System.Collections.Generic.List<MasterDataPatchChange>
                {
                    new MasterDataPatchChange { Field = "Damage", Original = 0, Value = 1 },
                },
            });
            patch.Tables.Add(table);

            var plan = MasterMemoryRemotePatch.Build(new MasterMemoryRemoteProtocol.PatchRequest
            {
                RequestId = "single-scan",
                ServerEpoch = "epoch",
                MasterVersion = "v1",
                PatchJson = MasterDataPatchSerializer.ToJson(patch),
            }, "epoch");

            Assert.AreEqual(MasterMemoryRemoteProtocol.PatchStatus.Success, plan.Response.Status,
                string.Join("; ", plan.Response.Errors.ConvertAll(x => x.Message)));
            Assert.AreEqual(1, scans, "Remote patch planning should build one original lookup per table.");
        }

        [Test]
        public void PatchPlan_ShouldRejectAllTargetsWhenOneFieldIsInvalid()
        {
            MasterMemoryDebugRegistry.SetMasterVersionProvider(() => "v1");
            RegisterTestDatabase();
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 185 });
            var patch = MasterDataPatchService.CreatePatch();
            patch.Tables[0].Records[0].Changes.Add(new MasterDataPatchChange { Field = "MissingField", Value = 1 });
            var json = MasterDataPatchSerializer.ToJson(patch);
            MasterMemoryDebugRuntime.ClearAllOverrides();

            var plan = MasterMemoryRemotePatch.Build(new MasterMemoryRemoteProtocol.PatchRequest
            { RequestId = "invalid", ServerEpoch = "epoch", MasterVersion = "v1", PatchJson = json }, "epoch");

            Assert.AreEqual(MasterMemoryRemoteProtocol.PatchStatus.Invalid, plan.Response.Status);
            Assert.IsTrue(plan.Response.Errors.Exists(x => x.Code == "FIELD_NOT_EDITABLE"));
            Assert.AreEqual(0, plan.Changes.Count);
            Assert.AreEqual(0, MasterMemoryDebugRuntime.OverrideCount);
        }

        [Test]
        public void PatchPlan_ShouldApplyAtomicallyAndRejectWrongOriginal()
        {
            MasterMemoryDebugRegistry.SetMasterVersionProvider(() => "v1");
            RegisterTestDatabase();
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 185 });
            var json = MasterDataPatchService.CreatePatchJson();
            MasterMemoryDebugRuntime.ClearAllOverrides();
            var request = new MasterMemoryRemoteProtocol.PatchRequest
            { RequestId = "apply-1", ServerEpoch = "epoch", MasterVersion = "v1", PatchJson = json };

            var plan = MasterMemoryRemotePatch.Build(request, "epoch");
            Assert.AreEqual(MasterMemoryRemoteProtocol.PatchStatus.Success, plan.Response.Status);
            Assert.AreEqual(1, plan.Response.Targets.Count);
            var applied = MasterMemoryRemotePatch.Apply(plan, plan.Response.PlanSha);
            Assert.AreEqual(MasterMemoryRemoteProtocol.PatchStatus.Success, applied.Status);
            Assert.AreEqual(1, applied.AppliedRecords);
            Assert.AreEqual(1, applied.AppliedFields);
            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1001, out var skill));
            Assert.AreEqual(185, skill.Damage);

            var wrong = MasterDataPatchSerializer.FromJson(json);
            wrong.Tables[0].Records[0].Changes[0].Original = 999;
            request.PatchJson = MasterDataPatchSerializer.ToJson(wrong);
            var rejected = MasterMemoryRemotePatch.Build(request, "epoch");
            Assert.AreEqual(MasterMemoryRemoteProtocol.PatchStatus.Invalid, rejected.Response.Status);
            Assert.IsTrue(rejected.Response.Errors.Exists(x => x.Code == "ORIGINAL_MISMATCH"));
        }

        [Test]
        public void PatchApply_ShouldCommitAddedAndDeletedRecordsTogether()
        {
            MasterMemoryDebugRegistry.SetMasterVersionProvider(() => "v1");
            RegisterTestDatabase();
            var patch = new MasterDataPatch { MasterVersion = "v1" };
            patch.Tables.Add(new MasterDataPatchTable
            {
                TableName = nameof(TestSkill), RecordType = typeof(TestSkill).FullName,
                Records = new System.Collections.Generic.List<MasterDataPatchRecord>
                {
                    new MasterDataPatchRecord
                    {
                        PrimaryKey = new MasterDataJsonObject { { "Id", 1003 } }, Deleted = true,
                    },
                    new MasterDataPatchRecord
                    {
                        PrimaryKey = new MasterDataJsonObject { { "Id", 9001 } }, Added = true,
                        Changes = new System.Collections.Generic.List<MasterDataPatchChange>
                        {
                            new MasterDataPatchChange { Field = "Name", Value = "New" },
                            new MasterDataPatchChange { Field = "Damage", Value = 50 },
                        },
                    },
                },
            });
            var plan = MasterMemoryRemotePatch.Build(new MasterMemoryRemoteProtocol.PatchRequest
            { RequestId = "add-delete", ServerEpoch = "epoch", MasterVersion = "v1",
                PatchJson = MasterDataPatchSerializer.ToJson(patch) }, "epoch");
            Assert.AreEqual(MasterMemoryRemoteProtocol.PatchStatus.Success, plan.Response.Status,
                string.Join("; ", plan.Response.Errors.ConvertAll(x => x.Message)));

            var result = MasterMemoryRemotePatch.Apply(plan, plan.Response.PlanSha);

            Assert.AreEqual(MasterMemoryRemoteProtocol.PatchStatus.Success, result.Status);
            Assert.AreEqual(2, result.AppliedRecords);
            Assert.IsTrue(MasterMemoryDebugRuntime.IsDeleted<TestSkill, int>(1003));
            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(9001, out var added));
            Assert.AreEqual("New", added.Name);
            Assert.AreEqual(50, added.Damage);
        }
        [Test]
        public void PatchApply_ShouldRejectChangedTargetAfterPlanning()
        {
            MasterMemoryDebugRegistry.SetMasterVersionProvider(() => "v1");
            RegisterTestDatabase();
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 185 });
            var json = MasterDataPatchService.CreatePatchJson();
            MasterMemoryDebugRuntime.ClearAllOverrides();
            var request = new MasterMemoryRemoteProtocol.PatchRequest
            { RequestId = "conflict", ServerEpoch = "epoch", MasterVersion = "v1", PatchJson = json };
            var first = MasterMemoryRemotePatch.Build(request, "epoch");
            Assert.AreEqual(MasterMemoryRemoteProtocol.PatchStatus.Success, first.Response.Status);

            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Name = "renamed" });
            var current = MasterMemoryRemotePatch.Build(request, "epoch");
            Assert.AreEqual(MasterMemoryRemoteProtocol.PatchStatus.Success, current.Response.Status);
            Assert.AreNotEqual(first.Response.Targets[0].BeforeSha, current.Response.Targets[0].BeforeSha);
            var result = MasterMemoryRemotePatch.Apply(current, first.Response.PlanSha);
            Assert.AreEqual(MasterMemoryRemoteProtocol.PatchStatus.Conflict, result.Status);
            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1001, out var skill));
            Assert.AreEqual(120, skill.Damage);
            Assert.AreEqual("renamed", skill.Name);
        }
        [Test]
        public void PatchApply_ShouldRejectConcurrentWriteAfterPreflight()
        {
            MasterMemoryDebugRegistry.SetMasterVersionProvider(() => "v1");
            RegisterTestDatabase();
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 185 });
            var json = MasterDataPatchService.CreatePatchJson();
            MasterMemoryDebugRuntime.ClearAllOverrides();
            var request = new MasterMemoryRemoteProtocol.PatchRequest
            { RequestId = "race", ServerEpoch = "epoch", MasterVersion = "v1", PatchJson = json };
            var plan = MasterMemoryRemotePatch.Build(request, "epoch");
            Assert.AreEqual(MasterMemoryRemoteProtocol.PatchStatus.Success, plan.Response.Status);
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 999 });

            var result = MasterMemoryRemotePatch.Apply(plan, plan.Response.PlanSha);

            Assert.AreEqual(MasterMemoryRemoteProtocol.PatchStatus.Conflict, result.Status);
            Assert.IsTrue(MasterMemoryDebugRuntime.TryGetOverride<TestSkill, int>(1001, out var skill));
            Assert.AreEqual(999, skill.Damage);
        }
        [Test]
        public void PatchApply_ShouldReplayResultForTheSameRequestId()
        {
            MasterMemoryDebugRegistry.SetMasterVersionProvider(() => "v1");
            RegisterTestDatabase();
            MasterMemoryDebugRemote.StartServer(0, "123456");
            var stream = ConnectRaw("123456");
            var welcome = MasterMemoryRemoteProtocol.DecodeWelcome(Receive(stream, "welcome"));
            var patch = new MasterDataPatch { MasterVersion = "v1" };
            patch.Tables.Add(new MasterDataPatchTable
            {
                TableName = nameof(TestSkill), RecordType = typeof(TestSkill).FullName,
                Records = new System.Collections.Generic.List<MasterDataPatchRecord>
                {
                    new MasterDataPatchRecord
                    {
                        PrimaryKey = new MasterDataJsonObject { { "Id", 1001 } },
                        Changes = new System.Collections.Generic.List<MasterDataPatchChange>
                        { new MasterDataPatchChange { Field = "Damage", Original = 120, Value = 185 } },
                    },
                },
            });
            var request = new MasterMemoryRemoteProtocol.PatchRequest
            {
                RequestId = "patch-1", ServerEpoch = welcome.ServerEpoch, MasterVersion = "v1",
                PatchJson = MasterDataPatchSerializer.ToJson(patch),
            };
            MasterMemoryRemoteProtocol.WriteFrame(stream, MasterMemoryRemoteProtocol.EncodePatchRequest(
                MasterMemoryRemoteProtocol.MessageType.PatchPlanRequest, request));
            var planned = MasterMemoryRemoteProtocol.DecodePatchResponse(ReceiveOf(stream, MasterMemoryRemoteProtocol.MessageType.PatchResponse));
            Assert.AreEqual(MasterMemoryRemoteProtocol.PatchStatus.Success, planned.Status);
            request.PlanSha = planned.PlanSha;
            MasterMemoryRemoteProtocol.WriteFrame(stream, MasterMemoryRemoteProtocol.EncodePatchRequest(
                MasterMemoryRemoteProtocol.MessageType.PatchApplyRequest, request));
            var first = MasterMemoryRemoteProtocol.DecodePatchResponse(ReceiveOf(stream, MasterMemoryRemoteProtocol.MessageType.PatchResponse));
            Assert.AreEqual(MasterMemoryRemoteProtocol.PatchStatus.Success, first.Status);
            Assert.AreEqual(1, MasterMemoryDebugRuntime.OverrideCount);
            MasterMemoryRemoteProtocol.WriteFrame(stream, MasterMemoryRemoteProtocol.EncodePatchRequest(
                MasterMemoryRemoteProtocol.MessageType.PatchApplyRequest, request));
            var replay = MasterMemoryRemoteProtocol.DecodePatchResponse(ReceiveOf(stream, MasterMemoryRemoteProtocol.MessageType.PatchResponse));
            Assert.AreEqual(first.StateSha, replay.StateSha);
            Assert.AreEqual(1, MasterMemoryDebugRuntime.OverrideCount);
            request.PatchJson += " ";
            MasterMemoryRemoteProtocol.WriteFrame(stream, MasterMemoryRemoteProtocol.EncodePatchRequest(
                MasterMemoryRemoteProtocol.MessageType.PatchApplyRequest, request));
            var collision = MasterMemoryRemoteProtocol.DecodePatchResponse(ReceiveOf(stream, MasterMemoryRemoteProtocol.MessageType.PatchResponse));
            Assert.AreEqual(MasterMemoryRemoteProtocol.PatchStatus.Conflict, collision.Status);
        }
        // ------------------------------------------------------------------ tool side (client)

        [Test]
        public void Client_ShouldMirrorTheGameAndSyncBothWays()
        {
            // what the game would send
            RegisterTestDatabase();
            MasterMemoryDebugRegistry.SetDisplayName<TestSkill>(x => "skill " + x.Id);
            MasterMemoryDebugLocalization.SetTableLabel<TestSkill>("zh-TW", "技能");
            MasterMemoryDebugRuntime.SetOverride(1003, Database.TestSkillTable.FindById(1003) with { Damage = 50 });
            var welcome = MasterMemoryRemoteServer.CreateWelcome();

            // the tool starts empty
            MasterMemoryDebugRuntime.ClearAllOverrides();
            MasterMemoryDebugRegistry.ClearTables();
            MasterMemoryDebugRegistry.SetDisplayName<TestSkill>(null);
            MasterMemoryDebugLocalization.Clear();
            MasterMemoryDebugRemote.IsToolMode = true;

            rawServer = new TcpListener(IPAddress.Loopback, 0);
            rawServer.Start();
            MasterMemoryDebugRemote.Connect("127.0.0.1", ((IPEndPoint)rawServer.LocalEndpoint).Port, "42");
            PumpUntil(() => rawServer.Pending(), "the connection");
            var game = rawServer.AcceptTcpClient().GetStream();
            var hello = MasterMemoryRemoteProtocol.DecodeHello(Receive(game, "the hello"));
            Assert.AreEqual("42", hello.Code);

            MasterMemoryRemoteProtocol.WriteFrame(game, MasterMemoryRemoteProtocol.Encode(welcome));
            PumpUntil(() => MasterMemoryDebugRemote.State == MasterMemoryRemoteState.Connected, "the tables");

            var skillsManifest = Table<TestSkill>();
            Assert.AreEqual(0, skillsManifest.CreateRecordSnapshot().Count, "records are lazy");
            Assert.IsFalse(MasterMemoryDebugRemote.EnsureTableLoaded(skillsManifest));
            var skillRequest = MasterMemoryRemoteProtocol.DecodeTableRequest(ReceiveOf(game, MasterMemoryRemoteProtocol.MessageType.TableRequest));
            var skillChunk = new MasterMemoryRemoteProtocol.TableChunk
            {
                RequestId = skillRequest.RequestId,
                TableName = skillRequest.TableName,
                ChunkIndex = 0,
                IsLast = true,
                DisplayNames = new System.Collections.Generic.List<string> { "skill 1001", "skill 1002", "skill 1003" },
            };
            foreach (var record in Database.TestSkillTable.All) skillChunk.Records.Add(Pack(record));
            MasterMemoryRemoteProtocol.WriteFrame(game, MasterMemoryRemoteProtocol.Encode(skillChunk));
            PumpUntil(() => MasterMemoryDebugRemote.IsTableLoaded(nameof(TestSkill)), "the skill table");
            var skills = Table<TestSkill>();
            Assert.AreEqual(3, skills.CreateRecordSnapshot().Count);
            Assert.AreEqual("skill 1001", skills.GetDisplayName(skills.CreateRecordSnapshot()[0].Original), "display names come from the game");

            var enemiesManifest = Table<TestEnemyLevel>();
            Assert.IsFalse(MasterMemoryDebugRemote.EnsureTableLoaded(enemiesManifest));
            var enemyRequest = MasterMemoryRemoteProtocol.DecodeTableRequest(ReceiveOf(game, MasterMemoryRemoteProtocol.MessageType.TableRequest));
            var enemyChunk = new MasterMemoryRemoteProtocol.TableChunk
            {
                RequestId = enemyRequest.RequestId,
                TableName = enemyRequest.TableName,
                ChunkIndex = 0,
                IsLast = true,
            };
            foreach (var record in Database.TestEnemyLevelTable.All) enemyChunk.Records.Add(Pack(record));
            MasterMemoryRemoteProtocol.WriteFrame(game, MasterMemoryRemoteProtocol.Encode(enemyChunk));
            PumpUntil(() => MasterMemoryDebugRemote.IsTableLoaded(nameof(TestEnemyLevel)), "the enemy table");
            var enemies = Table<TestEnemyLevel>();
            Assert.AreEqual(typeof((int, int)), enemies.KeyType);
            Assert.IsTrue(enemies.TryFindOriginal((2, 1), out _), "composite keys are built from the [PrimaryKey] members");
            Assert.IsTrue(MasterMemoryDebugRuntime.Store.IsOverridden(typeof(TestSkill), 1003), "the game's overrides");
            CollectionAssert.Contains(MasterMemoryDebugLocalization.Languages.ToList(), "zh-TW");

            // tool edits → game (one message per pump)
            var original = (TestSkill)skills.CreateRecordSnapshot()[0].Original;
            using (MasterMemoryDebugHistory.Record("edit")) MasterMemoryDebugRuntime.Store.Set(typeof(TestSkill), 1001, original with { Damage = 1 });
            var changes = MasterMemoryRemoteProtocol.DecodeChanges(Receive(game, "the tool's change"));
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(nameof(TestSkill), changes[0].TableName);

            // undo on the tool is sent as well
            MasterMemoryDebugHistory.Undo();
            Assert.IsFalse(MasterMemoryDebugRemote.State != MasterMemoryRemoteState.Connected);
            var undo = MasterMemoryRemoteProtocol.DecodeChanges(Receive(game, "the undo"));
            Assert.IsFalse(undo[0].IsSet);

            // game edits → tool
            var fromGame = new[] { new MasterMemoryRemoteProtocol.Change { IsSet = false, TableName = nameof(TestSkill), Record = Pack(Database.TestSkillTable.FindById(1003) with { Damage = 50 }) } }.ToList();
            MasterMemoryRemoteProtocol.WriteFrame(game, MasterMemoryRemoteProtocol.Encode(fromGame));
            PumpUntil(() => !MasterMemoryDebugRuntime.Store.IsOverridden(typeof(TestSkill), 1003), "the game's change");

            // the game goes away: the tool keeps the tables
            game.Close();
            PumpUntil(() => MasterMemoryDebugRemote.State == MasterMemoryRemoteState.Failed, "the disconnect");
            Assert.AreEqual(3, Table<TestSkill>().CreateRecordSnapshot().Count);
        }

        [Test]
        public void Client_ShouldReportWhenNobodyListens()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            MasterMemoryDebugRemote.Connect("127.0.0.1", port, "1");
            PumpUntil(() => MasterMemoryDebugRemote.State == MasterMemoryRemoteState.Failed, "the failure");
            StringAssert.Contains(port.ToString(), MasterMemoryDebugRemote.Status);
        }

        [Test]
        public void Protocol_ShouldRoundTrip()
        {
            var welcome = new MasterMemoryRemoteProtocol.Welcome { Version = MasterMemoryRemoteProtocol.Version, ServerEpoch = "epoch", MasterVersion = "v", SchemaHash = "schema", LabelsTsv = "x" };
            welcome.Tables.Add(new MasterMemoryRemoteProtocol.Table { TableName = "T", RecordType = "R", KeyType = "K", Group = "", RecordCount = 1, HasCustomDisplayName = true });
            welcome.Overrides.Add(new MasterMemoryRemoteProtocol.Change { IsSet = true, TableName = "T", Record = new byte[] { 3 } });
            welcome.Operations.Add(new MasterMemoryRemoteProtocol.Operation { Id = "refresh", Label = "Refresh", Context = "battle:1", Revision = 7 });

            var read = MasterMemoryRemoteProtocol.DecodeWelcome(MasterMemoryRemoteProtocol.Encode(welcome));
            Assert.AreEqual("v", read.MasterVersion);
            Assert.AreEqual("schema", read.SchemaHash);
            Assert.AreEqual(1, read.Tables[0].RecordCount);
            Assert.IsTrue(read.Tables[0].HasCustomDisplayName);
            Assert.AreEqual(0, read.Tables[0].Records.Count);
            Assert.IsTrue(read.Overrides[0].IsSet);
            Assert.AreEqual("epoch", read.ServerEpoch);
            Assert.AreEqual("battle:1", read.Operations[0].Context);
            var request = new MasterMemoryRemoteProtocol.OperationRequest
            { RequestId = "req", OperationId = "refresh", Context = "battle:1", Revision = 7 };
            Assert.AreEqual(7, MasterMemoryRemoteProtocol.DecodeOperationRequest(MasterMemoryRemoteProtocol.Encode(request)).Revision);
            var result = new MasterMemoryRemoteProtocol.OperationResult
            { RequestId = "req", Status = (byte)MasterMemoryRemoteOperationStatus.Success, OldSha = "old", NewSha = "new" };
            Assert.AreEqual("new", MasterMemoryRemoteProtocol.DecodeOperationResult(MasterMemoryRemoteProtocol.Encode(result)).NewSha);
            Assert.AreEqual("refresh", MasterMemoryRemoteProtocol.DecodeOperations(
                MasterMemoryRemoteProtocol.EncodeOperations(welcome.Operations))[0].Id);
            var tableRequest = new MasterMemoryRemoteProtocol.TableRequest { RequestId = "table-1", TableName = "T" };
            Assert.AreEqual("T", MasterMemoryRemoteProtocol.DecodeTableRequest(
                MasterMemoryRemoteProtocol.Encode(tableRequest)).TableName);
            var tableChunk = new MasterMemoryRemoteProtocol.TableChunk
            {
                RequestId = "table-1", TableName = "T", ChunkIndex = 0, IsLast = true,
                Records = { new byte[] { 1, 2 } },
                DisplayNames = new System.Collections.Generic.List<string> { "a" },
            };
            var readChunk = MasterMemoryRemoteProtocol.DecodeTableChunk(MasterMemoryRemoteProtocol.Encode(tableChunk));
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, readChunk.Records[0]);
            Assert.AreEqual("a", readChunk.DisplayNames[0]);
            Assert.Throws<System.IO.InvalidDataException>(() => MasterMemoryRemoteProtocol.DecodeHello(MasterMemoryRemoteProtocol.EncodeReject("no")));
        }

        [Test]
        public void Server_ShouldInvokeAnOperationOnceAndRejectStaleContext()
        {
            RegisterTestDatabase();
            var revision = 1;
            var calls = 0;
            using var registration = MasterMemoryDebugRemote.RegisterOperation("refresh", "Refresh battle",
                () => "battle:1:g1", () => revision, () =>
                {
                    calls++;
                    return new MasterMemoryRemoteOperationResult
                    { Status = MasterMemoryRemoteOperationStatus.Success, OldSha = "old", NewSha = "new" };
                });
            Assert.IsTrue(MasterMemoryDebugRemote.StartServer(0, "123456"));
            var stream = ConnectRaw("123456");
            var welcome = MasterMemoryRemoteProtocol.DecodeWelcome(ReceiveOf(stream, MasterMemoryRemoteProtocol.MessageType.Welcome));
            Assert.AreEqual("refresh", welcome.Operations[0].Id);
            Assert.IsNotEmpty(welcome.ServerEpoch);
            var request = new MasterMemoryRemoteProtocol.OperationRequest
            { RequestId = "once", OperationId = "refresh", Context = "battle:1:g1", Revision = 1 };
            MasterMemoryRemoteProtocol.WriteFrame(stream, MasterMemoryRemoteProtocol.Encode(request));
            var first = MasterMemoryRemoteProtocol.DecodeOperationResult(
                ReceiveOf(stream, MasterMemoryRemoteProtocol.MessageType.OperationResult));
            Assert.AreEqual((byte)MasterMemoryRemoteOperationStatus.Success, first.Status);
            Assert.AreEqual("old", first.OldSha);
            Assert.AreEqual(1, calls);

            MasterMemoryRemoteProtocol.WriteFrame(stream, MasterMemoryRemoteProtocol.Encode(request));
            var duplicate = MasterMemoryRemoteProtocol.DecodeOperationResult(
                ReceiveOf(stream, MasterMemoryRemoteProtocol.MessageType.OperationResult));
            Assert.AreEqual(first.NewSha, duplicate.NewSha);
            Assert.AreEqual(1, calls);

            revision++;
            MasterMemoryDebugRemote.NotifyOperationsChanged();
            var operations = MasterMemoryRemoteProtocol.DecodeOperations(
                ReceiveOf(stream, MasterMemoryRemoteProtocol.MessageType.Operations));
            Assert.AreEqual(2, operations[0].Revision);
            request.RequestId = "stale";
            MasterMemoryRemoteProtocol.WriteFrame(stream, MasterMemoryRemoteProtocol.Encode(request));
            var stale = MasterMemoryRemoteProtocol.DecodeOperationResult(
                ReceiveOf(stream, MasterMemoryRemoteProtocol.MessageType.OperationResult));
            Assert.AreEqual((byte)MasterMemoryRemoteOperationStatus.Stale, stale.Status);
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void Client_ShouldSendTheAdvertisedContextAndReceiveAnOperationResult()
        {
            RegisterTestDatabase();
            var welcome = MasterMemoryRemoteServer.CreateWelcome();
            welcome.ServerEpoch = "game-epoch";
            welcome.Operations.Add(new MasterMemoryRemoteProtocol.Operation
            { Id = "refresh", Label = "Refresh battle", Context = "battle:2:g4", Revision = 9 });
            MasterMemoryDebugRegistry.ClearTables();
            MasterMemoryDebugRemote.IsToolMode = true;

            rawServer = new TcpListener(IPAddress.Loopback, 0);
            rawServer.Start();
            MasterMemoryDebugRemote.Connect("127.0.0.1", ((IPEndPoint)rawServer.LocalEndpoint).Port, "42");
            PumpUntil(() => rawServer.Pending(), "the connection");
            var game = rawServer.AcceptTcpClient().GetStream();
            ReceiveOf(game, MasterMemoryRemoteProtocol.MessageType.Hello);
            MasterMemoryRemoteProtocol.WriteFrame(game, MasterMemoryRemoteProtocol.Encode(welcome));
            PumpUntil(() => MasterMemoryDebugRemote.State == MasterMemoryRemoteState.Connected, "the tables");
            Assert.AreEqual("refresh", MasterMemoryDebugRemote.Operations[0].Id);
            Assert.IsTrue(MasterMemoryDebugRemote.RequestOperation("refresh"));
            var request = MasterMemoryRemoteProtocol.DecodeOperationRequest(
                ReceiveOf(game, MasterMemoryRemoteProtocol.MessageType.OperationRequest));
            Assert.AreEqual("battle:2:g4", request.Context);
            Assert.AreEqual(9, request.Revision);
            MasterMemoryRemoteProtocol.WriteFrame(game, MasterMemoryRemoteProtocol.Encode(
                new MasterMemoryRemoteProtocol.OperationResult
                { RequestId = request.RequestId, Status = (byte)MasterMemoryRemoteOperationStatus.Success,
                    Message = "applied", OldSha = "old", NewSha = "new" }));
            PumpUntil(() => MasterMemoryDebugRemote.LastOperationResult != null, "the operation result");
            Assert.AreEqual(MasterMemoryRemoteOperationStatus.Success, MasterMemoryDebugRemote.LastOperationResult.Status);
            Assert.AreEqual("new", MasterMemoryDebugRemote.LastOperationResult.NewSha);
            MasterMemoryDebugRemote.ReceiveOperations(welcome.Operations, "another-game");
            Assert.IsNull(MasterMemoryDebugRemote.LastOperationResult,
                "a new game must not display the prior game's completed result");
        }

        [Test]
        public void Client_ShouldResendPendingOperationOnlyToTheSameServerEpoch()
        {
            MasterMemoryDebugRemote.ReconnectDelaySeconds = 0.01;
            RegisterTestDatabase();
            var welcome = MasterMemoryRemoteServer.CreateWelcome();
            welcome.ServerEpoch = "original-game";
            welcome.Operations.Add(new MasterMemoryRemoteProtocol.Operation
            { Id = "refresh", Label = "Refresh", Context = "battle:1", Revision = 2 });
            MasterMemoryDebugRegistry.ClearTables();
            MasterMemoryDebugRemote.IsToolMode = true;
            rawServer = new TcpListener(IPAddress.Loopback, 0);
            rawServer.Start();
            MasterMemoryDebugRemote.Connect("127.0.0.1", ((IPEndPoint)rawServer.LocalEndpoint).Port, "42");
            PumpUntil(() => rawServer.Pending(), "the first connection");
            var first = rawServer.AcceptTcpClient().GetStream();
            ReceiveOf(first, MasterMemoryRemoteProtocol.MessageType.Hello);
            MasterMemoryRemoteProtocol.WriteFrame(first, MasterMemoryRemoteProtocol.Encode(welcome));
            PumpUntil(() => MasterMemoryDebugRemote.State == MasterMemoryRemoteState.Connected, "the first welcome");
            Assert.IsTrue(MasterMemoryDebugRemote.RequestOperation("refresh"));
            var request = MasterMemoryRemoteProtocol.DecodeOperationRequest(
                ReceiveOf(first, MasterMemoryRemoteProtocol.MessageType.OperationRequest));

            first.Close();
            PumpUntil(() => rawServer.Pending(), "the same game reconnection");
            var second = rawServer.AcceptTcpClient().GetStream();
            ReceiveOf(second, MasterMemoryRemoteProtocol.MessageType.Hello);
            MasterMemoryRemoteProtocol.WriteFrame(second, MasterMemoryRemoteProtocol.Encode(welcome));
            var retried = MasterMemoryRemoteProtocol.DecodeOperationRequest(
                ReceiveOf(second, MasterMemoryRemoteProtocol.MessageType.OperationRequest));
            Assert.AreEqual(request.RequestId, retried.RequestId);
            MasterMemoryRemoteProtocol.WriteFrame(second, MasterMemoryRemoteProtocol.Encode(
                new MasterMemoryRemoteProtocol.OperationResult
                { RequestId = request.RequestId, Status = (byte)MasterMemoryRemoteOperationStatus.Success }));
            PumpUntil(() => MasterMemoryDebugRemote.LastOperationResult != null, "the retried result");
            Assert.AreEqual(MasterMemoryRemoteOperationStatus.Success, MasterMemoryDebugRemote.LastOperationResult.Status);

            Assert.IsTrue(MasterMemoryDebugRemote.RequestOperation("refresh"));
            ReceiveOf(second, MasterMemoryRemoteProtocol.MessageType.OperationRequest);
            second.Close();
            PumpUntil(() => rawServer.Pending(), "the restarted game connection");
            var restarted = rawServer.AcceptTcpClient().GetStream();
            ReceiveOf(restarted, MasterMemoryRemoteProtocol.MessageType.Hello);
            welcome.ServerEpoch = "new-game";
            MasterMemoryRemoteProtocol.WriteFrame(restarted, MasterMemoryRemoteProtocol.Encode(welcome));
            PumpUntil(() => MasterMemoryDebugRemote.State == MasterMemoryRemoteState.Connected, "the restarted welcome");
            Assert.AreEqual(MasterMemoryRemoteOperationStatus.Stale, MasterMemoryDebugRemote.LastOperationResult.Status);
            Assert.IsFalse(restarted.DataAvailable, "an old request must not be sent to a new game process");
        }

        [Test]
        public void Server_ShouldValidateForTheTool()
        {
            RegisterTestDatabase();
            using (MasterMemoryDebugRebuild.AutoRebuild(Database, _ => { }))
            {
                MasterMemoryDebugRemote.StartServer(0, "1");
                var stream = ConnectRaw("1");
                ReceiveOf(stream, MasterMemoryRemoteProtocol.MessageType.Welcome);
                var state = MasterMemoryRemoteProtocol.DecodeValidationState(ReceiveOf(stream, MasterMemoryRemoteProtocol.MessageType.ValidationState));
                Assert.IsTrue(state.IsAvailable);
                Assert.AreEqual(0, state.NewFailureCount);

                MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { SummonEnemyId = 99 });
                state = MasterMemoryRemoteProtocol.DecodeValidationState(ReceiveOf(stream, MasterMemoryRemoteProtocol.MessageType.ValidationState));
                Assert.AreEqual(1, state.NewFailureCount, "pushed after the rebuild");

                MasterMemoryRemoteProtocol.WriteFrame(stream, MasterMemoryRemoteProtocol.EncodeValidateRequest());
                var failures = MasterMemoryRemoteProtocol.DecodeValidateResult(ReceiveOf(stream, MasterMemoryRemoteProtocol.MessageType.ValidateResult));
                Assert.AreEqual(1, failures.Count);
                Assert.AreEqual(nameof(TestSkill), failures[0].TableName);
                Assert.AreEqual("1001", failures[0].Key);
                Assert.IsTrue(failures[0].IsNew);
                StringAssert.Contains("SummonEnemyId", failures[0].Message);
            }
        }

        [Test]
        public void Client_ShouldShowTheGamesValidation()
        {
            RegisterTestDatabase();
            var welcome = MasterMemoryRemoteServer.CreateWelcome();
            MasterMemoryDebugRegistry.ClearTables();
            MasterMemoryDebugRemote.IsToolMode = true;

            rawServer = new TcpListener(IPAddress.Loopback, 0);
            rawServer.Start();
            MasterMemoryDebugRemote.Connect("127.0.0.1", ((IPEndPoint)rawServer.LocalEndpoint).Port, "1");
            PumpUntil(() => rawServer.Pending(), "the connection");
            var game = rawServer.AcceptTcpClient().GetStream();
            ReceiveOf(game, MasterMemoryRemoteProtocol.MessageType.Hello);
            MasterMemoryRemoteProtocol.WriteFrame(game, MasterMemoryRemoteProtocol.Encode(welcome));
            PumpUntil(() => MasterMemoryDebugRemote.State == MasterMemoryRemoteState.Connected, "the tables");
            Assert.IsFalse(MasterMemoryDebugValidation.IsAvailable, "until the game says it validates");

            MasterMemoryRemoteProtocol.WriteFrame(game, MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.ValidationState { IsAvailable = true, NewFailureCount = 1 }));
            PumpUntil(() => MasterMemoryDebugValidation.IsAvailable, "the validation state");
            Assert.AreEqual(1, MasterMemoryDebugValidation.NewFailureCount);

            // the tab asks the game, then shows its results
            Assert.IsEmpty(MasterMemoryDebugValidation.Run());
            Assert.IsTrue(MasterMemoryDebugValidation.IsPending);
            ReceiveOf(game, MasterMemoryRemoteProtocol.MessageType.ValidateRequest);
            MasterMemoryRemoteProtocol.WriteFrame(game, MasterMemoryRemoteProtocol.Encode(new System.Collections.Generic.List<MasterMemoryRemoteProtocol.Failure>
            {
                new MasterMemoryRemoteProtocol.Failure { TableName = nameof(TestSkill), Key = "1001", Message = "Exists failed", IsNew = true },
            }));
            PumpUntil(() => !MasterMemoryDebugValidation.IsPending, "the results");
            var failures = MasterMemoryDebugValidation.Run();
            Assert.AreEqual(1, failures.Count);
            Assert.AreEqual(typeof(TestSkill), failures[0].RecordType);
            Assert.AreEqual(1001, Table<TestSkill>().GetPrimaryKey(failures[0].Record), "Open jumps to the tool's copy of the record");
            MasterMemoryDebugRemote.Pump();
            Assert.IsFalse(game.DataAvailable, "no new request until the results are stale");

            MasterMemoryDebugRemote.Stop();
            Assert.IsFalse(MasterMemoryDebugValidation.IsAvailable);
        }

        [Test]
        public void SerializationCheck_ShouldReportTablesMessagePackCanNotWrite()
        {
            RegisterTestDatabase();
            Assert.IsNull(MasterMemoryRemoteServer.CheckSerialization(Table<TestSkill>()), "contractless options can write the test records");

            // the standard resolver needs [MessagePackObject], which the test records do not have
            MasterMemoryDebugRemote.SerializerOptions = MessagePackSerializerOptions.Standard;
            var problem = MasterMemoryRemoteServer.CheckSerialization(Table<TestSkill>());
            Assert.IsNotNull(problem);
            StringAssert.StartsWith(nameof(TestSkill), problem);
        }

        [Test]
        public void Client_ShouldReconnectWhenTheGameComesBack()
        {
            MasterMemoryDebugRemote.ReconnectDelaySeconds = 0.05;
            RegisterTestDatabase();
            var welcome = MasterMemoryRemoteServer.CreateWelcome();
            MasterMemoryDebugRegistry.ClearTables();
            MasterMemoryDebugRemote.IsToolMode = true;

            rawServer = new TcpListener(IPAddress.Loopback, 0);
            rawServer.Start();
            MasterMemoryDebugRemote.Connect("127.0.0.1", ((IPEndPoint)rawServer.LocalEndpoint).Port, "7");

            // first game
            PumpUntil(() => rawServer.Pending(), "the connection");
            var game = rawServer.AcceptTcpClient().GetStream();
            ReceiveOf(game, MasterMemoryRemoteProtocol.MessageType.Hello);
            MasterMemoryRemoteProtocol.WriteFrame(game, MasterMemoryRemoteProtocol.Encode(welcome));
            PumpUntil(() => MasterMemoryDebugRemote.State == MasterMemoryRemoteState.Connected, "the tables");

            // the game restarts: the tool comes back with the same code
            game.Close();
            PumpUntil(() => MasterMemoryDebugRemote.State == MasterMemoryRemoteState.Failed, "the disconnect");
            PumpUntil(() => rawServer.Pending(), "the reconnection");
            var restarted = rawServer.AcceptTcpClient().GetStream();
            Assert.AreEqual("7", MasterMemoryRemoteProtocol.DecodeHello(ReceiveOf(restarted, MasterMemoryRemoteProtocol.MessageType.Hello)).Code);
            MasterMemoryRemoteProtocol.WriteFrame(restarted, MasterMemoryRemoteProtocol.Encode(welcome));
            PumpUntil(() => MasterMemoryDebugRemote.State == MasterMemoryRemoteState.Connected, "the tables again");
        }

        [Test]
        public void Client_ShouldNotRetryARefusal()
        {
            MasterMemoryDebugRemote.ReconnectDelaySeconds = 0.01;
            rawServer = new TcpListener(IPAddress.Loopback, 0);
            rawServer.Start();
            MasterMemoryDebugRemote.IsToolMode = true;
            MasterMemoryDebugRemote.Connect("127.0.0.1", ((IPEndPoint)rawServer.LocalEndpoint).Port, "wrong");
            PumpUntil(() => rawServer.Pending(), "the connection");
            var game = rawServer.AcceptTcpClient().GetStream();
            ReceiveOf(game, MasterMemoryRemoteProtocol.MessageType.Hello);
            MasterMemoryRemoteProtocol.WriteFrame(game, MasterMemoryRemoteProtocol.EncodeReject("Wrong pairing code."));
            PumpUntil(() => MasterMemoryDebugRemote.State == MasterMemoryRemoteState.Failed, "the refusal");

            for (var i = 0; i < 20; i++)
            {
                MasterMemoryDebugRemote.Pump();
                System.Threading.Thread.Sleep(5);
            }
            Assert.IsFalse(rawServer.Pending(), "no retry after a refusal");
            StringAssert.DoesNotContain("Reconnecting", MasterMemoryDebugRemote.Status);
        }

        [Test]
        public void Discovery_ShouldFindTheGameOnThisPC()
        {
            var udp = new UdpClient(0);
            var port = ((IPEndPoint)udp.Client.LocalEndPoint).Port;
            udp.Close();
            var previous = MasterMemoryRemoteDiscovery.Port;
            MasterMemoryRemoteDiscovery.Port = port;
            try
            {
                RegisterTestDatabase();
                MasterMemoryDebugRemote.StartServer(0, "1");
                var search = new MasterMemoryRemoteDiscovery.Search(1500);
                PumpUntil(() => search.IsDone || search.Games.Count > 0, "the answer");
                while (!search.IsDone) System.Threading.Thread.Sleep(10);

                var game = search.Games.Single();
                Assert.AreEqual("127.0.0.1", game.Address);
                Assert.AreEqual(MasterMemoryDebugRemote.ServerPort, game.Port);
                Assert.AreEqual("Harness", game.ProductName);

                MasterMemoryDebugRemote.Stop();
                var none = new MasterMemoryRemoteDiscovery.Search(200);
                while (!none.IsDone) System.Threading.Thread.Sleep(10);
                Assert.IsEmpty(none.Games, "a stopped server does not answer");
            }
            finally
            {
                MasterMemoryRemoteDiscovery.Port = previous;
            }
        }
    }
}
