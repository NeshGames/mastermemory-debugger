using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Remote editor tool side: connects to a game build, mirrors its tables and overrides into this process (so the whole
    /// debugger UI works on them), and keeps the overrides in sync both ways.
    /// </summary>
    internal sealed class MasterMemoryRemoteClient : MasterMemoryRemotePeer
    {
        const int ConnectTimeoutMs = 5000;

        readonly string code;
        TcpClient connected;
        string connectError;
        MasterMemoryRemoteState state = MasterMemoryRemoteState.Connecting;
        string status;
        volatile bool disposed;

        public MasterMemoryRemoteClient(string host, int port, string pairingCode)
        {
            Host = host;
            Port = port;
            code = pairingCode;
            status = $"Connecting to {host}:{port}…";
            new Thread(() => ConnectInBackground(host, port)) { IsBackground = true, Name = "MasterMemoryRemote connect" }.Start();
        }

        public string Host { get; }

        public int Port { get; }

        public override MasterMemoryRemoteState State => state;

        public override string Status => status;

        public override void Pump()
        {
            if (state == MasterMemoryRemoteState.Connecting && Connection == null)
            {
                TcpClient client;
                string error;
                lock (this)
                {
                    client = connected;
                    error = connectError;
                    connected = null;
                }
                if (error != null)
                {
                    SetState(MasterMemoryRemoteState.Failed, error);
                    return;
                }
                if (client == null) return;
                Connection = new MasterMemoryRemoteConnection(client);
                Connection.Send(MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.Hello { Version = MasterMemoryRemoteProtocol.Version, Code = code }));
                SetState(MasterMemoryRemoteState.Connecting, $"Connected to {Host}:{Port}, waiting for the tables…");
            }

            if (Connection == null) return;
            while (Connection != null && Connection.TryReceive(out var payload)) Handle(payload);
            if (Connection == null) return;
            if (Connection.IsClosed)
            {
                var reason = Connection.CloseReason ?? "The game closed the connection.";
                StopSyncing();
                Connection.Dispose();
                Connection = null;
                if (state != MasterMemoryRemoteState.Failed) SetState(MasterMemoryRemoteState.Failed, reason + " The tables shown are the last received; edits are no longer sent.");
                return;
            }
            Flush();
        }

        public override void Dispose()
        {
            disposed = true;
            lock (this)
            {
                connected?.Close();
                connected = null;
            }
            base.Dispose();
        }

        void ConnectInBackground(string host, int port)
        {
            var client = new TcpClient();
            string error = null;
            try
            {
                if (!client.ConnectAsync(host, port).Wait(ConnectTimeoutMs)) error = $"No answer from {host}:{port}. Is the game running with remote editing started, on the same network?";
            }
            catch (Exception e)
            {
                var inner = e is AggregateException aggregate && aggregate.InnerException != null ? aggregate.InnerException : e;
                error = $"Could not connect to {host}:{port}: {inner.Message}";
            }
            lock (this)
            {
                if (disposed || error != null)
                {
                    client.Close();
                    if (!disposed) connectError = error;
                    return;
                }
                connected = client;
            }
        }

        void Handle(byte[] payload)
        {
            try
            {
                switch (MasterMemoryRemoteProtocol.GetType(payload))
                {
                    case MasterMemoryRemoteProtocol.MessageType.Welcome:
                        ApplyWelcome(MasterMemoryRemoteProtocol.DecodeWelcome(payload));
                        break;
                    case MasterMemoryRemoteProtocol.MessageType.Reject:
                        SetState(MasterMemoryRemoteState.Failed, "Refused by the game: " + MasterMemoryRemoteProtocol.DecodeReject(payload));
                        Connection.Dispose();
                        Connection = null;
                        break;
                    case MasterMemoryRemoteProtocol.MessageType.Changes:
                        ApplyRemote(MasterMemoryRemoteProtocol.DecodeChanges(payload));
                        break;
                }
            }
            catch (Exception e)
            {
                MasterMemoryDebugLog.Error("Remote: invalid message from the game: " + e);
            }
        }

        /// <summary>Replaces the tables, labels and overrides of this process with the game's.</summary>
        void ApplyWelcome(MasterMemoryRemoteProtocol.Welcome welcome)
        {
            if (welcome.Version != MasterMemoryRemoteProtocol.Version)
            {
                SetState(MasterMemoryRemoteState.Failed, $"The game uses protocol version {welcome.Version}, this tool {MasterMemoryRemoteProtocol.Version}.");
                return;
            }

            var skipped = new List<string>();
            var masterVersion = welcome.MasterVersion;
            MasterMemoryDebugRegistry.ClearTables();
            MasterMemoryDebugRegistry.ClearTableGroups();
            MasterMemoryDebugRegistry.SetMasterVersionProvider(() => masterVersion);
            MasterMemoryDebugLocalization.Clear();
            MasterMemoryDebugLocalization.LoadTsv(welcome.LabelsTsv);
            MasterMemoryReferences.ClearCache();

            foreach (var table in welcome.Tables)
            {
                var recordType = Type.GetType(table.RecordType);
                var keyType = Type.GetType(table.KeyType);
                if (recordType == null || keyType == null)
                {
                    skipped.Add($"{table.TableName} (type {(recordType == null ? table.RecordType : table.KeyType)} is not in this build)");
                    continue;
                }
                try
                {
                    var records = new List<object>(table.Records.Count);
                    foreach (var bytes in table.Records) records.Add(Deserialize(recordType, bytes));
                    MasterMemoryDebugRegistry.RegisterRemoteTable(table.TableName, table.MemoryTableName, recordType, keyType, records, table.DisplayNames);
                    if (!string.IsNullOrEmpty(table.Group)) MasterMemoryDebugRegistry.SetTableGroup(table.Group, table.TableName);
                }
                catch (Exception e)
                {
                    skipped.Add($"{table.TableName} ({(e.InnerException ?? e).Message})");
                }
            }

            StartSyncing();
            ApplyRemote(welcome.Overrides, replaceAll: true);
            MasterMemoryDebugHistory.Clear();

            var text = $"Connected to {Host}:{Port}: {welcome.Tables.Count - skipped.Count} tables, master {masterVersion}";
            if (skipped.Count > 0)
            {
                MasterMemoryDebugLog.Warning("Remote: tables not shown: " + string.Join(", ", skipped) + ". Build the tool from the same project version as the game.");
                text += $" ({skipped.Count} tables skipped, see Console)";
            }
            SetState(MasterMemoryRemoteState.Connected, text);
        }

        void SetState(MasterMemoryRemoteState newState, string newStatus)
        {
            state = newState;
            status = newStatus;
            RaiseChanged();
        }
    }
}
