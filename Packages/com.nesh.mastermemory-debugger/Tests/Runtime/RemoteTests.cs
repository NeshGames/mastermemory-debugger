using System;
using System.Diagnostics;
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

        static byte[] Pack<T>(T record) => MasterMemoryRemotePeer.Serialize(typeof(T), record);

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
            Assert.AreEqual(3, skills.Records.Count);
            var first = (TestSkill)MasterMemoryRemotePeer.Deserialize(typeof(TestSkill), skills.Records[0]);
            Assert.AreEqual("Fireball", first.Name);
            Assert.AreEqual(typeof(TestSkill), Type.GetType(skills.RecordType));
            Assert.AreEqual(1, welcome.Overrides.Count);

            // game edits → tool
            MasterMemoryDebugRuntime.SetOverride(1001, Database.TestSkillTable.FindById(1001) with { Damage = 777 });
            MasterMemoryDebugRuntime.RemoveOverride<TestSkill, int>(1002);
            var changes = MasterMemoryRemoteProtocol.DecodeChanges(Receive(stream, "the game's changes"));
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
            Assert.IsFalse(stream.DataAvailable, "changes of the tool are not echoed");
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

            var skills = Table<TestSkill>();
            Assert.AreEqual(3, skills.CreateRecordSnapshot().Count);
            Assert.AreEqual("skill 1001", skills.GetDisplayName(skills.CreateRecordSnapshot()[0].Original), "display names come from the game");
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
            var welcome = new MasterMemoryRemoteProtocol.Welcome { Version = 1, MasterVersion = "v", LabelsTsv = "x" };
            welcome.Tables.Add(new MasterMemoryRemoteProtocol.Table { TableName = "T", RecordType = "R", KeyType = "K", Group = "", Records = { new byte[] { 1, 2 } }, DisplayNames = new System.Collections.Generic.List<string> { "a" } });
            welcome.Overrides.Add(new MasterMemoryRemoteProtocol.Change { IsSet = true, TableName = "T", Record = new byte[] { 3 } });

            var read = MasterMemoryRemoteProtocol.DecodeWelcome(MasterMemoryRemoteProtocol.Encode(welcome));
            Assert.AreEqual("v", read.MasterVersion);
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, read.Tables[0].Records[0]);
            Assert.AreEqual("a", read.Tables[0].DisplayNames[0]);
            Assert.IsTrue(read.Overrides[0].IsSet);
            Assert.Throws<System.IO.InvalidDataException>(() => MasterMemoryRemoteProtocol.DecodeHello(MasterMemoryRemoteProtocol.EncodeReject("no")));
        }
    }
}
