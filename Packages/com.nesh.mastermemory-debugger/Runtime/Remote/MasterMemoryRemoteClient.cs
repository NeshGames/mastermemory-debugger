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
        readonly ValidationSource validation;
        readonly Dictionary<string, RemoteTableState> remoteTables =
            new Dictionary<string, RemoteTableState>(StringComparer.Ordinal);

        sealed class RemoteTableState
        {
            public MasterMemoryRemoteProtocol.Table Manifest;
            public Type RecordType;
            public Type KeyType;
            public readonly List<object> Records = new List<object>();
            public List<string> DisplayNames;
            public bool Loading;
            public bool Loaded;
            public string RequestId;
            public int NextChunkIndex;
        }

        public MasterMemoryRemoteClient(string host, int port, string pairingCode)
        {
            Host = host;
            Port = port;
            code = pairingCode;
            status = $"Connecting to {host}:{port}…";
            validation = new ValidationSource(this);
            new Thread(() => ConnectInBackground(host, port)) { IsBackground = true, Name = "MasterMemoryRemote connect" }.Start();
        }

        public string Host { get; }

        /// <summary>The tables were received: the connection worked (a reconnect is worth trying when it is lost).</summary>
        public bool HasConnected { get; private set; }

        /// <summary>The game refused the connection (wrong code, another tool, other version): retrying does not help.</summary>
        public bool WasRefused { get; private set; }

        public string PairingCode => code;

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
                MasterMemoryDebugValidation.Remove(validation);
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
            MasterMemoryDebugValidation.Remove(validation);
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
                        WasRefused = true;
                        SetState(MasterMemoryRemoteState.Failed, "Refused by the game: " + MasterMemoryRemoteProtocol.DecodeReject(payload));
                        Connection.Dispose();
                        Connection = null;
                        break;
                    case MasterMemoryRemoteProtocol.MessageType.Changes:
                        ApplyRemote(MasterMemoryRemoteProtocol.DecodeChanges(payload));
                        break;
                    case MasterMemoryRemoteProtocol.MessageType.ValidationState:
                        validation.OnState(MasterMemoryRemoteProtocol.DecodeValidationState(payload));
                        break;
                    case MasterMemoryRemoteProtocol.MessageType.ValidateResult:
                        validation.OnResult(MasterMemoryRemoteProtocol.DecodeValidateResult(payload));
                        break;
                    case MasterMemoryRemoteProtocol.MessageType.Operations:
                        MasterMemoryDebugRemote.ReceiveOperations(MasterMemoryRemoteProtocol.DecodeOperations(payload));
                        break;
                    case MasterMemoryRemoteProtocol.MessageType.OperationResult:
                        MasterMemoryDebugRemote.ReceiveOperationResult(MasterMemoryRemoteProtocol.DecodeOperationResult(payload));
                        break;
                    case MasterMemoryRemoteProtocol.MessageType.TableChunk:
                        ReceiveTableChunk(MasterMemoryRemoteProtocol.DecodeTableChunk(payload));
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
                SetState(MasterMemoryRemoteState.Failed,
                    $"The game uses protocol version {welcome.Version}, this tool {MasterMemoryRemoteProtocol.Version}.");
                return;
            }

            var skipped = new List<string>();
            var masterVersion = welcome.MasterVersion;
            remoteTables.Clear();
            MasterMemoryDebugRegistry.ClearTables();
            MasterMemoryDebugRegistry.ClearTableGroups();
            MasterMemoryDebugRegistry.SetMasterVersionProvider(() => masterVersion);
            MasterMemoryDebugLocalization.Clear();
            MasterMemoryDebugLocalization.LoadTsv(welcome.LabelsTsv);
            MasterMemoryReferences.ClearCache();

            foreach (var manifest in welcome.Tables)
            {
                var recordType = Type.GetType(manifest.RecordType);
                var keyType = Type.GetType(manifest.KeyType);
                if (recordType == null || keyType == null)
                {
                    skipped.Add($"{manifest.TableName} (type {(recordType == null ? manifest.RecordType : manifest.KeyType)} is not in this build)");
                    continue;
                }

                try
                {
                    var state = new RemoteTableState
                    {
                        Manifest = manifest,
                        RecordType = recordType,
                        KeyType = keyType,
                        DisplayNames = manifest.HasCustomDisplayName ? new List<string>() : null,
                    };
                    remoteTables.Add(manifest.TableName, state);
                    MasterMemoryDebugRegistry.RegisterRemoteTable(
                        manifest.TableName, manifest.MemoryTableName, recordType, keyType,
                        state.Records, state.DisplayNames);
                    if (!string.IsNullOrEmpty(manifest.Group))
                        MasterMemoryDebugRegistry.SetTableGroup(manifest.Group, manifest.TableName);
                }
                catch (Exception e)
                {
                    skipped.Add($"{manifest.TableName} ({(e.InnerException ?? e).Message})");
                }
            }

            if (!string.IsNullOrEmpty(welcome.SchemaHash)
                && !string.Equals(welcome.SchemaHash, MasterMemoryDebugRegistry.GetSchemaHash(), StringComparison.Ordinal))
            {
                SetState(MasterMemoryRemoteState.Failed,
                    "The remote editor schema differs from the game. Build the tool from the same project revision.");
                return;
            }

            StartSyncing();
            ApplyRemote(welcome.Overrides, replaceAll: true);
            MasterMemoryDebugRemote.ReceiveOperations(welcome.Operations, welcome.ServerEpoch);
            MasterMemoryDebugRemote.ResendPending(this);
            MasterMemoryDebugHistory.Clear();
            MasterMemoryDebugValidation.Add(validation);

            HasConnected = true;
            var text = $"Connected to {Host}:{Port}: {welcome.Tables.Count - skipped.Count} table manifests, master {masterVersion}";
            if (skipped.Count > 0)
            {
                MasterMemoryDebugLog.Warning(
                    "Remote: tables not shown: " + string.Join(", ", skipped)
                    + ". Build the tool from the same project version as the game.");
                text += $" ({skipped.Count} tables skipped, see Console)";
            }
            SetState(MasterMemoryRemoteState.Connected, text);
        }

        internal bool EnsureTableLoaded(string tableName)
        {
            if (!remoteTables.TryGetValue(tableName, out var state)) return true;
            if (state.Loaded) return true;
            if (state.Loading) return false;
            if (Connection == null || state != null && State != MasterMemoryRemoteState.Connected) return false;

            state.Loading = true;
            state.RequestId = Guid.NewGuid().ToString("N");
            state.NextChunkIndex = 0;
            state.Records.Clear();
            state.DisplayNames?.Clear();
            Connection.Send(MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.TableRequest
            {
                RequestId = state.RequestId,
                TableName = tableName,
            }));
            SetState(MasterMemoryRemoteState.Connected,
                $"Loading {tableName} ({state.Manifest.RecordCount} records) from {Host}:{Port}…");
            return false;
        }

        internal bool IsTableLoaded(string tableName) =>
            !remoteTables.TryGetValue(tableName, out var state) || state.Loaded;

        void ReceiveTableChunk(MasterMemoryRemoteProtocol.TableChunk chunk)
        {
            if (!remoteTables.TryGetValue(chunk.TableName, out var state)
                || !state.Loading || chunk.RequestId != state.RequestId)
                return;

            if (chunk.ChunkIndex != state.NextChunkIndex)
            {
                state.Loading = false;
                MasterMemoryDebugLog.Warning(
                    $"Remote: {chunk.TableName} chunk {chunk.ChunkIndex} arrived; expected {state.NextChunkIndex}.");
                SetState(MasterMemoryRemoteState.Connected, $"Remote table {chunk.TableName} transfer failed; select it to retry.");
                return;
            }
            state.NextChunkIndex++;

            if (!string.IsNullOrEmpty(chunk.Error))
            {
                state.Loading = false;
                MasterMemoryDebugLog.Warning($"Remote: {chunk.TableName} could not be loaded: {chunk.Error}");
                SetState(MasterMemoryRemoteState.Connected, $"Remote table {chunk.TableName} transfer failed; select it to retry.");
                return;
            }

            try
            {
                for (var i = 0; i < chunk.Records.Count; i++)
                {
                    state.Records.Add(Deserialize(state.RecordType, chunk.Records[i]));
                    if (state.DisplayNames != null)
                        state.DisplayNames.Add(chunk.DisplayNames != null ? chunk.DisplayNames[i] : string.Empty);
                }
            }
            catch (Exception e)
            {
                state.Loading = false;
                MasterMemoryDebugLog.Warning($"Remote: {chunk.TableName} could not be deserialized: {(e.InnerException ?? e).Message}");
                SetState(MasterMemoryRemoteState.Connected, $"Remote table {chunk.TableName} transfer failed; select it to retry.");
                return;
            }

            if (!chunk.IsLast) return;

            state.Loading = false;
            state.Loaded = true;
            MasterMemoryDebugRegistry.RegisterRemoteTable(
                state.Manifest.TableName, state.Manifest.MemoryTableName, state.RecordType, state.KeyType,
                state.Records, state.DisplayNames);
            if (!string.IsNullOrEmpty(state.Manifest.Group))
                MasterMemoryDebugRegistry.SetTableGroup(state.Manifest.Group, state.Manifest.TableName);
            SetState(MasterMemoryRemoteState.Connected,
                $"Connected to {Host}:{Port}: {chunk.TableName} loaded ({state.Records.Count} records).");
        }

        void RequestValidation() => Connection?.Send(MasterMemoryRemoteProtocol.EncodeValidateRequest());

        internal void SendOperation(MasterMemoryRemoteProtocol.OperationRequest request) =>
            Connection?.Send(MasterMemoryRemoteProtocol.Encode(request));

        /// <summary>
        /// The Validation tab of the tool shows the game's Validate() results: the game pushes whether it validates and the
        /// failures caused by the overrides; the full list is requested when the tab needs it.
        /// </summary>
        sealed class ValidationSource : MasterMemoryDebugValidation.ISource
        {
            readonly MasterMemoryRemoteClient client;
            List<MasterMemoryRemoteProtocol.Failure> results = new List<MasterMemoryRemoteProtocol.Failure>();
            bool available;
            int newFailureCount;
            bool stale = true;
            bool pending;

            public ValidationSource(MasterMemoryRemoteClient client)
            {
                this.client = client;
            }

            public int NewFailureCount => newFailureCount;

            public bool IsAvailable => available;

            public bool IsPending => pending;

            public void Invalidate() => stale = true;

            public void Collect(List<MasterMemoryValidationFailure> failures)
            {
                if (!available) return;
                if (stale && !pending)
                {
                    pending = true;
                    client.RequestValidation();
                }
                var records = new Dictionary<string, Dictionary<string, object>>();
                foreach (var failure in results)
                {
                    Type recordType = null;
                    object record = null;
                    if (MasterMemoryDebugRegistry.TryGetTable(failure.TableName, out var table))
                    {
                        recordType = table.RecordType;
                        if (!records.TryGetValue(table.TableName, out var byKey))
                        {
                            byKey = new Dictionary<string, object>();
                            foreach (var descriptor in table.CreateRecordSnapshot()) byKey[descriptor.KeyText] = descriptor.Original ?? descriptor.Current;
                            records.Add(table.TableName, byKey);
                        }
                        // the tool's copy of the failing record: Open jumps to it
                        if (failure.Key.Length > 0) byKey.TryGetValue(failure.Key, out record);
                    }
                    failures.Add(new MasterMemoryValidationFailure(recordType, failure.Message, record, failure.IsNew));
                }
            }

            public void OnState(MasterMemoryRemoteProtocol.ValidationState state)
            {
                available = state.IsAvailable;
                newFailureCount = state.NewFailureCount;
                stale = true;
                MasterMemoryDebugValidation.NotifyChanged();
            }

            public void OnResult(List<MasterMemoryRemoteProtocol.Failure> failures)
            {
                results = failures;
                pending = false;
                stale = false;
                MasterMemoryDebugValidation.NotifyChanged();
            }
        }

        void SetState(MasterMemoryRemoteState newState, string newStatus)
        {
            state = newState;
            status = newStatus;
            RaiseChanged();
        }
    }
}
