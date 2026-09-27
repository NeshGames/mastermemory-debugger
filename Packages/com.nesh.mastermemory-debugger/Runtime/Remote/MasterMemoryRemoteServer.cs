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
        const int MaxOperationResults = 4096;
        readonly TcpListener listener;
        readonly Thread acceptThread;
        readonly ConcurrentQueue<TcpClient> accepted = new ConcurrentQueue<TcpClient>();
        readonly List<MasterMemoryRemoteConnection> closing = new List<MasterMemoryRemoteConnection>();
        MasterMemoryRemoteConnection waitingForHello;
        volatile bool stopped;
        bool validationChanged;
        bool operationsChanged;
        readonly string serverEpoch = Guid.NewGuid().ToString("N");
        readonly Dictionary<string, MasterMemoryRemoteProtocol.OperationResult> operationResults =
            new Dictionary<string, MasterMemoryRemoteProtocol.OperationResult>(StringComparer.Ordinal);
        readonly HashSet<Type> checkedTypes = new HashSet<Type>();
        readonly MasterMemoryRemoteDiscovery.Responder discovery;
        string lastError;

        public MasterMemoryRemoteServer(int port, string pairingCode)
        {
            PairingCode = pairingCode;
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "MasterMemoryRemote accept" };
            acceptThread.Start();
            // tables are often registered after the server starts (Settings > Remote Server starts it after the first scene loads)
            MasterMemoryDebugRegistry.TablesChanged += OnTablesChanged;
            MasterMemoryDebugRemote.OperationsChanged += OnOperationsChanged;
            CheckSerialization();

            try
            {
                discovery = new MasterMemoryRemoteDiscovery.Responder(Port, UnityEngine.Application.productName, UnityEngine.SystemInfo.deviceName, MasterMemoryDebugRegistry.GetMasterVersion());
            }
            catch (SocketException e)
            {
                MasterMemoryDebugLog.Warning($"Remote: LAN discovery is not available (UDP port {MasterMemoryRemoteDiscovery.Port}: {e.Message}); enter the address in the tool.");
            }
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
                    MasterMemoryDebugValidation.Changed -= OnValidationChanged;
                    StopSyncing();
                    Connection.Dispose();
                    Connection = null;
                    RaiseChanged();
                }
                else
                {
                    Flush();
                    if (validationChanged) SendValidationState();
                    if (operationsChanged) SendOperations();
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
            MasterMemoryDebugValidation.Changed -= OnValidationChanged;
            MasterMemoryDebugRegistry.TablesChanged -= OnTablesChanged;
            MasterMemoryDebugRemote.OperationsChanged -= OnOperationsChanged;
            discovery?.Dispose();
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
                var message = CreateWelcome();
                message.ServerEpoch = serverEpoch;
                welcome = MasterMemoryRemoteProtocol.Encode(message);
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
            MasterMemoryDebugValidation.Changed += OnValidationChanged;
            SendValidationState();
            MasterMemoryDebugLog.Warning($"Remote: the tool at {connection.RemoteAddress} connected.");
            RaiseChanged();
        }

        void Handle(byte[] payload)
        {
            try
            {
                switch (MasterMemoryRemoteProtocol.GetType(payload))
                {
                    case MasterMemoryRemoteProtocol.MessageType.Changes:
                        ApplyRemote(MasterMemoryRemoteProtocol.DecodeChanges(payload));
                        break;
                    case MasterMemoryRemoteProtocol.MessageType.ValidateRequest:
                        Connection.Send(MasterMemoryRemoteProtocol.Encode(Validate()));
                        break;
                    case MasterMemoryRemoteProtocol.MessageType.OperationRequest:
                        HandleOperation(MasterMemoryRemoteProtocol.DecodeOperationRequest(payload));
                        break;
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

        void OnOperationsChanged() => operationsChanged = true;

        void SendOperations()
        {
            operationsChanged = false;
            Connection?.Send(MasterMemoryRemoteProtocol.EncodeOperations(MasterMemoryDebugRemote.SnapshotOperations()));
        }

        void HandleOperation(MasterMemoryRemoteProtocol.OperationRequest request)
        {
            if (string.IsNullOrEmpty(request.RequestId) || request.RequestId.Length > 64) return;
            if (!operationResults.TryGetValue(request.RequestId, out var result))
            {
                if (operationResults.Count >= MaxOperationResults)
                    result = new MasterMemoryRemoteProtocol.OperationResult
                    {
                        RequestId = request.RequestId,
                        Status = (byte)MasterMemoryRemoteOperationStatus.Busy,
                        Message = "Remote operation limit reached; restart the Remote server to continue.",
                    };
                else
                {
                    result = MasterMemoryDebugRemote.ExecuteOperation(request);
                    operationResults.Add(request.RequestId, result);
                }
            }
            Connection?.Send(MasterMemoryRemoteProtocol.Encode(result));
        }

        void OnTablesChanged()
        {
            CheckSerialization();
            discovery?.SetInfo(Port, UnityEngine.Application.productName, UnityEngine.SystemInfo.deviceName, MasterMemoryDebugRegistry.GetMasterVersion());
        }

        /// <summary>
        /// Serializes one record of every newly registered table, so that a missing MessagePack resolver (IL2CPP) is reported
        /// when the game starts rather than when the tool connects.
        /// </summary>
        void CheckSerialization()
        {
            var problems = new List<string>();
            foreach (var table in MasterMemoryDebugRegistry.Tables)
            {
                if (!checkedTypes.Add(table.RecordType)) continue;
                var problem = CheckSerialization(table);
                if (problem != null) problems.Add(problem);
            }
            if (problems.Count == 0) return;
            MasterMemoryDebugLog.Error(
                "Remote: records can not be serialized with MessagePack, so the remote editor tool can not connect:\n  " + string.Join("\n  ", problems) +
                "\nSet MasterMemoryDebugRemote.SerializerOptions to the MessagePack options the game uses to load its MemoryDatabase " +
                "(with IL2CPP: the options with the generated resolvers, e.g. MessagePackSerializerOptions.Standard.WithResolver(StaticCompositeResolver.Instance)), " +
                "before StartServer or before the tables are registered. The remote editor tool must use the same options.");
        }

        /// <summary>Null when a record of <paramref name="table"/> survives a MessagePack round trip, otherwise the problem.</summary>
        internal static string CheckSerialization(MasterMemoryTableDescriptor table)
        {
            object record = null;
            try
            {
                foreach (var item in table.GetAllRecords())
                {
                    if (item == null) continue;
                    record = item;
                    break;
                }
                if (record == null) return null;
                Deserialize(table.RecordType, Serialize(table.RecordType, record));
                return null;
            }
            catch (Exception e)
            {
                return $"{table.TableName} ({table.RecordType.FullName}): {(e.InnerException ?? e).Message}";
            }
        }

        // raised by rebuilds; sent once per Pump
        void OnValidationChanged() => validationChanged = true;

        void SendValidationState()
        {
            validationChanged = false;
            Connection?.Send(MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.ValidationState
            {
                IsAvailable = MasterMemoryDebugValidation.IsAvailable,
                NewFailureCount = MasterMemoryDebugValidation.NewFailureCount,
            }));
        }

        /// <summary>Runs the game's validation for the tool; records are identified by table name and key text.</summary>
        internal static List<MasterMemoryRemoteProtocol.Failure> Validate()
        {
            var result = new List<MasterMemoryRemoteProtocol.Failure>();
            foreach (var failure in MasterMemoryDebugValidation.Run())
            {
                var item = new MasterMemoryRemoteProtocol.Failure { TableName = failure.RecordType?.Name ?? "?", Key = string.Empty, Message = failure.Message, IsNew = failure.IsNew };
                if (failure.RecordType != null && MasterMemoryDebugRegistry.TryGetTable(failure.RecordType, out var table))
                {
                    item.TableName = table.TableName;
                    if (failure.Record != null && table.RecordType.IsInstanceOfType(failure.Record))
                    {
                        try
                        {
                            item.Key = MasterDataValueUtility.FormatKey(table.GetPrimaryKey(failure.Record));
                        }
                        catch (Exception)
                        {
                            // no key: shown without Open
                        }
                    }
                }
                result.Add(item);
            }
            return result;
        }

        /// <summary>Every registered table with its original records, and the current overrides.</summary>
        internal static MasterMemoryRemoteProtocol.Welcome CreateWelcome()
        {
            var welcome = new MasterMemoryRemoteProtocol.Welcome
            {
                Version = MasterMemoryRemoteProtocol.Version,
                Operations = MasterMemoryDebugRemote.SnapshotOperations(),
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
                object record;
                try
                {
                    record = MasterMemoryRemotePeer.RecordWithKey(table, entry.Key, entry.Value);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
                welcome.Overrides.Add(new MasterMemoryRemoteProtocol.Change
                {
                    Kind = entry.IsDeleted ? MasterMemoryRemoteProtocol.ChangeKind.Delete : MasterMemoryRemoteProtocol.ChangeKind.Set,
                    TableName = table.TableName,
                    Record = Serialize(table.RecordType, record),
                });
            }
            return welcome;
        }

        /// <summary>Full name and assembly, without version: the tool is built from the same project.</summary>
        internal static string TypeName(Type type) => type.FullName + ", " + type.Assembly.GetName().Name;
    }
}
