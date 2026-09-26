using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Game build side: listens for the remote editor tool, sends the tables and overrides after a valid pairing code,
    /// then keeps the overrides in sync both ways. One tool at a time.
    /// </summary>
    internal sealed class MasterMemoryRemoteServer : MasterMemoryRemotePeer
    {
        readonly TcpListener listener;
        readonly Thread acceptThread;
        readonly ConcurrentQueue<TcpClient> accepted = new ConcurrentQueue<TcpClient>();
        readonly List<MasterMemoryRemoteConnection> closing = new List<MasterMemoryRemoteConnection>();
        MasterMemoryRemoteConnection waitingForHello;
        volatile bool stopped;
        string lastError;

        public MasterMemoryRemoteServer(int port, string pairingCode)
        {
            PairingCode = pairingCode;
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "MasterMemoryRemote accept" };
            acceptThread.Start();
        }

        public int Port { get; }

        public string PairingCode { get; }

        public string ClientAddress => Connection?.RemoteAddress;

        public override MasterMemoryRemoteState State => Connection != null ? MasterMemoryRemoteState.Connected : MasterMemoryRemoteState.Listening;

        public override string Status
        {
            get
            {
                if (Connection != null) return $"Connected to the tool at {Connection.RemoteAddress}";
                var text = $"Waiting for the tool on port {Port}, pairing code {PairingCode}";
                return lastError != null ? text + "  (" + lastError + ")" : text;
            }
        }

        public override void Pump()
        {
            while (accepted.TryDequeue(out var client)) OnAccepted(client);

            if (waitingForHello != null) ReceiveHello();

            if (Connection != null)
            {
                while (Connection.TryReceive(out var payload)) Handle(payload);
                if (Connection.IsClosed)
                {
                    MasterMemoryDebugLog.Warning("Remote: " + (Connection.CloseReason ?? "the tool disconnected."));
                    lastError = null;
                    StopSyncing();
                    Connection.Dispose();
                    Connection = null;
                    RaiseChanged();
                }
                else
                {
                    Flush();
                }
            }
            closing.RemoveAll(x => x.IsClosed);
        }

        public override void Dispose()
        {
            stopped = true;
            try
            {
                listener.Stop();
            }
            catch (Exception)
            {
                // already stopped
            }
            waitingForHello?.Dispose();
            foreach (var connection in closing) connection.Dispose();
            while (accepted.TryDequeue(out var client)) client.Close();
            base.Dispose();
        }

        void AcceptLoop()
        {
            while (!stopped)
            {
                try
                {
                    accepted.Enqueue(listener.AcceptTcpClient());
                }
                catch (Exception)
                {
                    // Stop() interrupts the wait
                    if (stopped) return;
                }
            }
        }

        void OnAccepted(TcpClient client)
        {
            var connection = new MasterMemoryRemoteConnection(client);
            if (Connection != null)
            {
                Reject(connection, "Another tool is already connected.");
                return;
            }
            // a new attempt replaces one that never sent its code
            if (waitingForHello != null) Reject(waitingForHello, "Replaced by a newer connection.");
            waitingForHello = connection;
        }

        void ReceiveHello()
        {
            var connection = waitingForHello;
            if (connection.IsClosed)
            {
                waitingForHello = null;
                connection.Dispose();
                return;
            }
            if (!connection.TryReceive(out var payload)) return;
            waitingForHello = null;

            MasterMemoryRemoteProtocol.Hello hello;
            try
            {
                hello = MasterMemoryRemoteProtocol.DecodeHello(payload);
            }
            catch (Exception)
            {
                Reject(connection, "Not a MasterMemory Debugger remote tool.");
                return;
            }
            if (hello.Version != MasterMemoryRemoteProtocol.Version)
            {
                Reject(connection, $"Protocol version {hello.Version} is not supported (the game uses {MasterMemoryRemoteProtocol.Version}); update the package of the tool or the game.");
                return;
            }
            if (!string.Equals(hello.Code?.Trim(), PairingCode, StringComparison.Ordinal))
            {
                lastError = $"wrong pairing code from {connection.RemoteAddress}";
                Reject(connection, "Wrong pairing code.");
                RaiseChanged();
                return;
            }

            byte[] welcome;
            try
            {
                welcome = MasterMemoryRemoteProtocol.Encode(CreateWelcome());
            }
            catch (Exception e)
            {
                MasterMemoryDebugLog.Error("Remote: the tables could not be sent: " + e);
                Reject(connection, "The game could not serialize its tables: " + (e.InnerException ?? e).Message);
                return;
            }
            Connection = connection;
            lastError = null;
            connection.Send(welcome);
            StartSyncing();
            MasterMemoryDebugLog.Warning($"Remote: the tool at {connection.RemoteAddress} connected.");
            RaiseChanged();
        }

        void Handle(byte[] payload)
        {
            try
            {
                if (MasterMemoryRemoteProtocol.GetType(payload) == MasterMemoryRemoteProtocol.MessageType.Changes)
                {
                    ApplyRemote(MasterMemoryRemoteProtocol.DecodeChanges(payload));
                }
            }
            catch (Exception e)
            {
                MasterMemoryDebugLog.Warning("Remote: invalid message from the tool: " + e.Message);
            }
        }

        void Reject(MasterMemoryRemoteConnection connection, string reason)
        {
            connection.Send(MasterMemoryRemoteProtocol.EncodeReject(reason));
            connection.CloseAfterSending();
            closing.Add(connection);
        }

        /// <summary>Every registered table with its original records, and the current overrides.</summary>
        internal static MasterMemoryRemoteProtocol.Welcome CreateWelcome()
        {
            var welcome = new MasterMemoryRemoteProtocol.Welcome
            {
                Version = MasterMemoryRemoteProtocol.Version,
                MasterVersion = MasterMemoryDebugRegistry.GetMasterVersion(),
                LabelsTsv = MasterMemoryDebugLocalization.ExportTsv(),
            };
            foreach (var group in MasterMemoryDebugRegistry.GetGroupedTables())
            {
                foreach (var table in group.Tables)
                {
                    var message = new MasterMemoryRemoteProtocol.Table
                    {
                        TableName = table.TableName,
                        MemoryTableName = table.MemoryTableName,
                        RecordType = TypeName(table.RecordType),
                        KeyType = TypeName(table.KeyType),
                        Group = MasterMemoryDebugRegistry.GetTableGroup(table),
                        DisplayNames = table.HasCustomDisplayName ? new List<string>() : null,
                    };
                    foreach (var record in table.GetAllRecords())
                    {
                        if (record == null) continue;
                        message.Records.Add(Serialize(table.RecordType, record));
                        message.DisplayNames?.Add(table.GetDisplayName(record));
                    }
                    welcome.Tables.Add(message);
                }
            }
            foreach (var entry in MasterMemoryDebugRuntime.GetAllOverrides())
            {
                if (!MasterMemoryDebugRegistry.TryGetTable(entry.Key.RecordType, out var table)) continue;
                welcome.Overrides.Add(new MasterMemoryRemoteProtocol.Change { IsSet = true, TableName = table.TableName, Record = Serialize(table.RecordType, entry.Value) });
            }
            return welcome;
        }

        /// <summary>Full name and assembly, without version: the tool is built from the same project.</summary>
        internal static string TypeName(Type type) => type.FullName + ", " + type.Assembly.GetName().Name;
    }
}
