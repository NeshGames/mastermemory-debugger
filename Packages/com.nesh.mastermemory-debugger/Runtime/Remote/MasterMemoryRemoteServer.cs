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
        const int MaxReplayResults = 4096;
        readonly TcpListener listener;
        readonly Thread acceptThread;
        readonly ConcurrentQueue<TcpClient> accepted = new ConcurrentQueue<TcpClient>();
        readonly List<MasterMemoryRemoteConnection> closing = new List<MasterMemoryRemoteConnection>();
        readonly Queue<MasterMemoryRemoteProtocol.TableRequest> tableRequests =
            new Queue<MasterMemoryRemoteProtocol.TableRequest>();
        MasterMemoryRemoteConnection waitingForHello;
        TableTransfer tableTransfer;
        volatile bool stopped;
        bool validationChanged;
        bool operationsChanged;
        readonly string serverEpoch = Guid.NewGuid().ToString("N");
        readonly MasterMemoryReplayCache<MasterMemoryRemoteProtocol.OperationResult> operationResults =
            new MasterMemoryReplayCache<MasterMemoryRemoteProtocol.OperationResult>(MaxReplayResults);
        readonly MasterMemoryReplayCache<Tuple<string, MasterMemoryRemoteProtocol.PatchResponse>> patchResults =
            new MasterMemoryReplayCache<Tuple<string, MasterMemoryRemoteProtocol.PatchResponse>>(MaxReplayResults);
        readonly HashSet<Type> checkedTypes = new HashSet<Type>();
        readonly MasterMemoryRemoteDiscovery.Responder discovery;
        string lastError;

        sealed class TableTransfer : IDisposable
        {
            public MasterMemoryRemoteProtocol.TableRequest Request;
            public MasterMemoryTableDescriptor Table;
            public IEnumerator<object> Records;
            public int ChunkIndex;
            public byte[] PendingFrame;
            public bool PendingIsLast;

            public void Dispose() => Records?.Dispose();
        }

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
                    tableRequests.Clear();
                    tableTransfer?.Dispose();
                    tableTransfer = null;
                    RaiseChanged();
                }
                else
                {
                    Flush();
                    if (validationChanged) SendValidationState();
                    if (operationsChanged) SendOperations();
                    PumpTableTransfer();
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
            tableRequests.Clear();
            tableTransfer?.Dispose();
            tableTransfer = null;
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
                    case MasterMemoryRemoteProtocol.MessageType.PatchExportRequest:
                    case MasterMemoryRemoteProtocol.MessageType.PatchPlanRequest:
                    case MasterMemoryRemoteProtocol.MessageType.PatchApplyRequest:
                        HandlePatch(MasterMemoryRemoteProtocol.GetType(payload), payload);
                        break;
                    case MasterMemoryRemoteProtocol.MessageType.OperationRequest:
                        HandleOperation(MasterMemoryRemoteProtocol.DecodeOperationRequest(payload));
                        break;
                    case MasterMemoryRemoteProtocol.MessageType.TableRequest:
                        HandleTableRequest(MasterMemoryRemoteProtocol.DecodeTableRequest(payload));
                        break;
                }
            }
            catch (Exception e)
            {
                MasterMemoryDebugLog.Warning("Remote: invalid message from the tool: " + e.Message);
            }
        }

        void HandleTableRequest(MasterMemoryRemoteProtocol.TableRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.RequestId) || request.RequestId.Length > 64
                || string.IsNullOrEmpty(request.TableName) || request.TableName.Length > 256)
                return;
            if (tableRequests.Count >= 32)
            {
                Connection?.Send(MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.TableChunk
                {
                    RequestId = request.RequestId,
                    TableName = request.TableName,
                    IsLast = true,
                    Error = "Too many table requests are pending.",
                }));
                return;
            }
            tableRequests.Enqueue(request);
        }

        void PumpTableTransfer()
        {
            if (Connection == null || Connection.IsClosed) return;

            if (tableTransfer == null)
            {
                if (tableRequests.Count == 0) return;
                var request = tableRequests.Dequeue();
                if (!MasterMemoryDebugRegistry.TryGetTable(request.TableName, out var table))
                {
                    Connection.TrySend(MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.TableChunk
                    {
                        RequestId = request.RequestId,
                        TableName = request.TableName,
                        IsLast = true,
                        Error = "Table is not registered.",
                    }));
                    return;
                }
                tableTransfer = new TableTransfer
                {
                    Request = request,
                    Table = table,
                    Records = table.GetAllRecords().GetEnumerator(),
                };
            }

            var transfer = tableTransfer;
            if (transfer.PendingFrame != null)
            {
                if (!Connection.TrySend(transfer.PendingFrame)) return;
                transfer.PendingFrame = null;
                if (transfer.PendingIsLast)
                {
                    transfer.Dispose();
                    tableTransfer = null;
                }
                return;
            }

            var chunk = new MasterMemoryRemoteProtocol.TableChunk
            {
                RequestId = transfer.Request.RequestId,
                TableName = transfer.Table.TableName,
                ChunkIndex = transfer.ChunkIndex,
                DisplayNames = transfer.Table.HasCustomDisplayName ? new List<string>() : null,
            };

            var bytes = 0;
            var exhausted = false;
            try
            {
                while (chunk.Records.Count < 512 && bytes < MasterMemoryRemoteProtocol.TargetTableChunkBytes)
                {
                    if (!transfer.Records.MoveNext())
                    {
                        exhausted = true;
                        break;
                    }
                    var record = transfer.Records.Current;
                    if (record == null) continue;
                    var serialized = Serialize(transfer.Table.RecordType, record);
                    if (serialized.Length > MasterMemoryRemoteProtocol.MaxFrameBytes - 4096)
                        throw new InvalidOperationException("One record is too large for the remote protocol frame limit.");
                    chunk.Records.Add(serialized);
                    bytes += serialized.Length + 8;
                    if (chunk.DisplayNames != null)
                    {
                        var name = transfer.Table.GetDisplayName(record) ?? string.Empty;
                        chunk.DisplayNames.Add(name);
                        bytes += name.Length * 2;
                    }
                }
                chunk.IsLast = exhausted;
            }
            catch (Exception e)
            {
                chunk.Records.Clear();
                chunk.DisplayNames?.Clear();
                chunk.IsLast = true;
                chunk.Error = (e.InnerException ?? e).Message;
            }

            transfer.PendingFrame = MasterMemoryRemoteProtocol.Encode(chunk);
            transfer.PendingIsLast = chunk.IsLast;
            transfer.ChunkIndex++;
            if (Connection.TrySend(transfer.PendingFrame))
            {
                transfer.PendingFrame = null;
                if (transfer.PendingIsLast)
                {
                    transfer.Dispose();
                    tableTransfer = null;
                }
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
                result = MasterMemoryDebugRemote.ExecuteOperation(request);
                operationResults.Add(request.RequestId, result);
            }
            Connection?.Send(MasterMemoryRemoteProtocol.Encode(result));
        }

        void HandlePatch(MasterMemoryRemoteProtocol.MessageType type, byte[] payload)
        {
            var request = MasterMemoryRemoteProtocol.DecodePatchRequest(payload, type);
            if (string.IsNullOrEmpty(request.RequestId) || request.RequestId.Length > 64) return;
            MasterMemoryRemoteProtocol.PatchResponse response;
            if (type == MasterMemoryRemoteProtocol.MessageType.PatchExportRequest)
                response = MasterMemoryRemotePatch.Export(request.RequestId, serverEpoch);
            else if (type == MasterMemoryRemoteProtocol.MessageType.PatchPlanRequest)
                response = MasterMemoryRemotePatch.Build(request, serverEpoch).Response;
            else
            {
                var fingerprint = MasterMemoryRemotePatch.Fingerprint(request);
                if (patchResults.TryGetValue(request.RequestId, out var cached))
                {
                    if (cached.Item1 == fingerprint) response = cached.Item2;
                    else response = new MasterMemoryRemoteProtocol.PatchResponse
                    {
                        RequestId = request.RequestId,
                        Status = MasterMemoryRemoteProtocol.PatchStatus.Conflict,
                        Message = "Request ID was already used with different Patch content.",
                        ServerEpoch = serverEpoch,
                        MasterVersion = MasterMemoryDebugRegistry.GetMasterVersion(),
                    };
                }
                else
                {
                    var plan = MasterMemoryRemotePatch.Build(request, serverEpoch);
                    response = MasterMemoryRemotePatch.Apply(plan, request.PlanSha);
                    patchResults.Add(request.RequestId, Tuple.Create(fingerprint, response));
                }
            }
            Connection?.Send(MasterMemoryRemoteProtocol.Encode(response));
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
        /// <summary>Metadata manifest plus current overrides. Table records are requested lazily in protocol v6.</summary>
        internal static MasterMemoryRemoteProtocol.Welcome CreateWelcome()
        {
            var welcome = new MasterMemoryRemoteProtocol.Welcome
            {
                Version = MasterMemoryRemoteProtocol.Version,
                Operations = MasterMemoryDebugRemote.SnapshotOperations(),
                MasterVersion = MasterMemoryDebugRegistry.GetMasterVersion(),
                SchemaHash = MasterMemoryDebugRegistry.GetSchemaHash(),
                LabelsTsv = MasterMemoryDebugLocalization.ExportTsv(),
            };
            foreach (var group in MasterMemoryDebugRegistry.GetGroupedTables())
            {
                foreach (var table in group.Tables)
                {
                    var count = 0;
                    foreach (var record in table.GetAllRecords())
                    {
                        if (record != null) count++;
                    }
                    welcome.Tables.Add(new MasterMemoryRemoteProtocol.Table
                    {
                        TableName = table.TableName,
                        MemoryTableName = table.MemoryTableName,
                        RecordType = TypeName(table.RecordType),
                        KeyType = TypeName(table.KeyType),
                        Group = MasterMemoryDebugRegistry.GetTableGroup(table),
                        RecordCount = count,
                        HasCustomDisplayName = table.HasCustomDisplayName,
                    });
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
