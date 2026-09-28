using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MessagePack;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    public enum MasterMemoryRemoteState
    {
        /// <summary>No server and no connection.</summary>
        Off,
        /// <summary>Game: waiting for the tool.</summary>
        Listening,
        /// <summary>Tool: connecting / waiting for the tables.</summary>
        Connecting,
        /// <summary>Game and tool are in sync.</summary>
        Connected,
        /// <summary>Tool: the connection failed or was lost (<see cref="MasterMemoryDebugRemote.Status"/> says why).</summary>
        Failed,
    }

    public enum MasterMemoryRemoteOperationStatus : byte
    {
        Success, NoChange, Busy, Stale, Incompatible, ValidationError, Failed,
    }

    public sealed class MasterMemoryRemoteOperationResult
    {
        public string RequestId { get; internal set; }
        public MasterMemoryRemoteOperationStatus Status { get; set; }
        public string Message { get; set; }
        public string OldSha { get; set; }
        public string NewSha { get; set; }
    }

    public sealed class MasterMemoryRemoteOperationView
    {
        public string Id { get; internal set; }
        public string Label { get; internal set; }
        public string Context { get; internal set; }
        public int Revision { get; internal set; }
    }

    /// <summary>
    /// Remote editing: a desktop build of the project (the tool, see <see cref="MasterMemoryRemoteEditor"/>) connects to a
    /// game build over TCP and edits its master data with the full debugger UI; overrides stay in sync both ways.
    /// <code>
    /// // game (Development Build): or turn on Remote Server in the settings
    /// MasterMemoryDebugRemote.StartServer();          // port 7788, random pairing code shown in the Remote dialog / log
    /// // tool: the MasterMemoryRemoteEditor component shows a connect dialog, or
    /// MasterMemoryDebugRemote.Connect("192.168.1.20", 7788, "123456");
    /// </code>
    /// Editor and Development Builds only; not available on WebGL.
    /// </summary>
    public static class MasterMemoryDebugRemote
    {
        public const int DefaultPort = 7788;

        static MasterMemoryRemotePeer s_peer;
        static MasterMemoryRemoteRunner s_runner;
        static readonly System.Diagnostics.Stopwatch s_clock = System.Diagnostics.Stopwatch.StartNew();
        // tool: retrying the last game after the connection was lost
        static bool s_reconnecting;
        static double s_retryAt = -1;
        static int s_attempt;
        static readonly Dictionary<string, RegisteredOperation> s_operations = new Dictionary<string, RegisteredOperation>(StringComparer.Ordinal);
        static readonly List<MasterMemoryRemoteOperationView> s_remoteOperations = new List<MasterMemoryRemoteOperationView>();
        static readonly Dictionary<string, MasterMemoryRemoteProtocol.OperationRequest> s_pendingOperations =
            new Dictionary<string, MasterMemoryRemoteProtocol.OperationRequest>(StringComparer.Ordinal);
        static string s_serverEpoch;
        internal static event Action OperationsChanged;

        /// <summary>Seconds between reconnection attempts.</summary>
        internal static double ReconnectDelaySeconds = 3;

        /// <summary>
        /// Tool: when the connection to the game is lost (the game restarted, Wi-Fi dropped), connect again to the same address
        /// with the same code every few seconds, until it works, the game refuses or <see cref="Stop"/> is called.
        /// With a fixed pairing code (Remote Pairing Code setting) a restarted game is found again automatically.
        /// </summary>
        public static bool AutoReconnect { get; set; } = true;

        /// <summary>Tool: the connection was lost and is being retried.</summary>
        public static bool IsReconnecting => s_reconnecting;

        /// <summary>Raised on the main thread when <see cref="State"/> or <see cref="Status"/> changed.</summary>
        public static event Action Changed;

        /// <summary>
        /// MessagePack options used for records; null = <c>MessagePackSerializer.DefaultOptions</c>. With IL2CPP, give the
        /// options (resolver) the project uses to load its MemoryDatabase. Game and tool must use compatible options.
        /// </summary>
        public static MessagePackSerializerOptions SerializerOptions { get; set; }

        /// <summary>False in release builds and on WebGL (no sockets).</summary>
        public static bool IsSupported
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return false;
#else
                return MasterMemoryDebugBuild.IsEnabled;
#endif
            }
        }

        /// <summary>True in the remote editor tool (set by <see cref="MasterMemoryRemoteEditor"/>).</summary>
        public static bool IsToolMode { get; set; }

        public static MasterMemoryRemoteState State => s_peer?.State ?? MasterMemoryRemoteState.Off;

        public static string Status
        {
            get
            {
                var status = s_peer?.Status ?? (IsToolMode ? "Not connected." : "Remote editing is off.");
                if (s_reconnecting && s_retryAt >= 0)
                {
                    var seconds = Math.Max(0, (int)Math.Ceiling(s_retryAt - s_clock.Elapsed.TotalSeconds));
                    status += $"  Reconnecting in {seconds} s (attempt {s_attempt + 1})…";
                }
                return status;
            }
        }

        public static bool IsServerRunning => s_peer is MasterMemoryRemoteServer;

        /// <summary>Game: the port listened on (0 when off).</summary>
        public static int ServerPort => (s_peer as MasterMemoryRemoteServer)?.Port ?? 0;

        /// <summary>
        /// Remote tool: requests a table the first time it is opened. Local/game tables are always ready.
        /// Returns true when the table data is already available.
        /// </summary>
        internal static bool EnsureTableLoaded(MasterMemoryTableDescriptor table)
        {
            if (table == null || !(s_peer is MasterMemoryRemoteClient client)) return true;
            return client.EnsureTableLoaded(table.TableName);
        }

        internal static bool IsTableLoaded(string tableName) =>
            !(s_peer is MasterMemoryRemoteClient client) || client.IsTableLoaded(tableName);

        /// <summary>Tool: operations advertised by the connected game.</summary>
        public static IReadOnlyList<MasterMemoryRemoteOperationView> Operations => s_remoteOperations;
        /// <summary>Tool: the last operation result; null until one arrives.</summary>
        public static MasterMemoryRemoteOperationResult LastOperationResult { get; private set; }

        /// <summary>Tool: request an advertised operation using its current target and candidate revision.</summary>
        public static bool RequestOperation(string id)
        {
            if (!(s_peer is MasterMemoryRemoteClient client) || client.State != MasterMemoryRemoteState.Connected
                || s_pendingOperations.Count >= 32) return false;
            foreach (var operation in s_remoteOperations)
            {
                if (operation.Id != id) continue;
                var request = new MasterMemoryRemoteProtocol.OperationRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"), OperationId = id,
                    Context = operation.Context, Revision = operation.Revision,
                };
                s_pendingOperations.Add(request.RequestId, request);
                client.SendOperation(request);
                return true;
            }
            return false;
        }

        internal static void ReceiveOperations(List<MasterMemoryRemoteProtocol.Operation> operations, string serverEpoch = null)
        {
            if (serverEpoch != null)
            {
                if (s_serverEpoch != null && s_serverEpoch != serverEpoch)
                {
                    bool hadPendingOperations = s_pendingOperations.Count != 0;
                    s_pendingOperations.Clear();
                    if (hadPendingOperations) LastOperationResult = new MasterMemoryRemoteOperationResult
                    {
                        Status = MasterMemoryRemoteOperationStatus.Stale,
                        Message = "The game restarted; an outstanding operation has an unknown result.",
                    };
                    else LastOperationResult = null;
                }
                s_serverEpoch = serverEpoch;
            }
            s_remoteOperations.Clear();
            foreach (var operation in operations) s_remoteOperations.Add(new MasterMemoryRemoteOperationView
            {
                Id = operation.Id, Label = operation.Label,
                Context = operation.Context, Revision = operation.Revision,
            });
            RaiseChanged();
        }

        internal static void ResendPending(MasterMemoryRemoteClient client)
        {
            foreach (var request in s_pendingOperations.Values) client.SendOperation(request);
        }

        internal static void ReceiveOperationResult(MasterMemoryRemoteProtocol.OperationResult result)
        {
            if (!s_pendingOperations.Remove(result.RequestId)) return;
            LastOperationResult = new MasterMemoryRemoteOperationResult
            {
                RequestId = result.RequestId, Status = (MasterMemoryRemoteOperationStatus)result.Status,
                Message = result.Message, OldSha = result.OldSha, NewSha = result.NewSha,
            };
            RaiseChanged();
        }

        /// <summary>
        /// Game: registers a main-thread operation. The token must be disposed when its owner leaves.
        /// Context identifies the current target (for example a battle and generation); revision identifies its data candidate.
        /// The server rejects stale requests before invoking the callback.
        /// </summary>
        public static IDisposable RegisterOperation(string id, string label, Func<string> context,
            Func<int> revision, Func<MasterMemoryRemoteOperationResult> execute)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Operation id and label are required.");
            if (context == null || revision == null || execute == null) throw new ArgumentNullException("Operation callbacks are required.");
            if (!IsSupported || IsToolMode) return EmptyRegistration.Instance;
            if (s_operations.ContainsKey(id)) throw new InvalidOperationException("Operation already registered: " + id);
            var entry = new RegisteredOperation(id, label, context, revision, execute);
            s_operations.Add(id, entry);
            OperationsChanged?.Invoke();
            return entry;
        }

        /// <summary>Game: call when a registered operation's context or candidate revision changes.</summary>
        public static void NotifyOperationsChanged()
        {
            if (IsSupported && !IsToolMode) OperationsChanged?.Invoke();
        }

        internal static List<MasterMemoryRemoteProtocol.Operation> SnapshotOperations()
        {
            var result = new List<MasterMemoryRemoteProtocol.Operation>(s_operations.Count);
            foreach (var entry in s_operations.Values) result.Add(new MasterMemoryRemoteProtocol.Operation
            {
                Id = entry.Id, Label = entry.Label, Context = entry.Context(), Revision = entry.Revision(),
            });
            result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return result;
        }

        internal static MasterMemoryRemoteProtocol.OperationResult ExecuteOperation(MasterMemoryRemoteProtocol.OperationRequest request)
        {
            var result = new MasterMemoryRemoteProtocol.OperationResult { RequestId = request.RequestId };
            if (string.IsNullOrEmpty(request.RequestId) || request.RequestId.Length > 64
                || string.IsNullOrEmpty(request.OperationId) || request.OperationId.Length > 128)
            {
                result.Status = (byte)MasterMemoryRemoteOperationStatus.Failed;
                result.Message = "Invalid operation request.";
                return result;
            }
            try
            {
                if (!s_operations.TryGetValue(request.OperationId, out var entry)
                    || !string.Equals(request.Context, entry.Context(), StringComparison.Ordinal)
                    || request.Revision != entry.Revision())
                {
                    result.Status = (byte)MasterMemoryRemoteOperationStatus.Stale;
                    result.Message = "Operation target or candidate changed; refresh the operation list.";
                    return result;
                }
                var answer = entry.Execute() ?? throw new InvalidOperationException("Operation returned no result.");
                result.Status = (byte)answer.Status;
                result.Message = answer.Message;
                result.OldSha = answer.OldSha;
                result.NewSha = answer.NewSha;
            }
            catch (Exception e)
            {
                result.Status = (byte)MasterMemoryRemoteOperationStatus.Failed;
                result.Message = e.Message;
            }
            return result;
        }

        sealed class RegisteredOperation : IDisposable
        {
            public readonly string Id;
            public readonly string Label;
            public readonly Func<string> Context;
            public readonly Func<int> Revision;
            public readonly Func<MasterMemoryRemoteOperationResult> Execute;
            public RegisteredOperation(string id, string label, Func<string> context, Func<int> revision,
                Func<MasterMemoryRemoteOperationResult> execute)
            { Id = id; Label = label; Context = context; Revision = revision; Execute = execute; }
            public void Dispose()
            {
                if (!s_operations.TryGetValue(Id, out var current) || current != this) return;
                s_operations.Remove(Id);
                OperationsChanged?.Invoke();
            }
        }

        sealed class EmptyRegistration : IDisposable
        {
            public static readonly EmptyRegistration Instance = new EmptyRegistration();
            public void Dispose() { }
        }

        /// <summary>Game: the code the tool must enter.</summary>
        public static string PairingCode => (s_peer as MasterMemoryRemoteServer)?.PairingCode;

        /// <summary>
        /// Game: starts listening for the tool. <paramref name="pairingCode"/> null or empty = a random 6 digit code.
        /// Returns false when remote editing is not supported or the port is in use.
        /// </summary>
        public static bool StartServer(int port = DefaultPort, string pairingCode = null)
        {
            if (!IsSupported) return false;
            if (IsServerRunning && ServerPort == port) return true;
            Stop();
            if (string.IsNullOrWhiteSpace(pairingCode)) pairingCode = CreatePairingCode();
            try
            {
                SetPeer(new MasterMemoryRemoteServer(port, pairingCode.Trim()));
            }
            catch (SocketException e)
            {
                MasterMemoryDebugLog.Error($"Remote: can not listen on port {port}: {e.Message}");
                return false;
            }
            Debug.Log($"[MasterMemoryDebugger] Remote editing: listening on {string.Join(", ", GetLocalAddresses())} port {ServerPort}, pairing code {PairingCode}");
            return true;
        }

        /// <summary>Tool: connects to a game build. The result arrives through <see cref="Changed"/> / <see cref="State"/>.</summary>
        public static void Connect(string host, int port, string pairingCode)
        {
            if (!IsSupported) return;
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host is required.", nameof(host));
            Stop();
            SetPeer(new MasterMemoryRemoteClient(host.Trim(), port, pairingCode?.Trim() ?? string.Empty));
        }

        /// <summary>Tool: schedules and makes the reconnection attempts (called by <see cref="Pump"/>).</summary>
        static void UpdateReconnect()
        {
            if (!(s_peer is MasterMemoryRemoteClient client)) return;
            if (client.State == MasterMemoryRemoteState.Connected)
            {
                s_reconnecting = false;
                s_retryAt = -1;
                s_attempt = 0;
                return;
            }
            if (client.State != MasterMemoryRemoteState.Failed) return;
            if (!AutoReconnect || client.WasRefused || !(client.HasConnected || s_reconnecting))
            {
                s_reconnecting = false;
                s_retryAt = -1;
                return;
            }

            var now = s_clock.Elapsed.TotalSeconds;
            if (!s_reconnecting || s_retryAt < 0)
            {
                s_reconnecting = true;
                s_retryAt = now + ReconnectDelaySeconds;
                RaiseChanged();
                return;
            }
            if (now < s_retryAt) return;

            s_attempt++;
            s_retryAt = -1;
            var host = client.Host;
            var port = client.Port;
            var code = client.PairingCode;
            client.Changed -= RaiseChanged;
            client.Dispose();
            s_peer = null;
            SetPeer(new MasterMemoryRemoteClient(host, port, code));
        }

        /// <summary>Stops the server or closes the connection. The tool keeps the tables it received.</summary>
        public static void Stop()
        {
            s_reconnecting = false;
            s_retryAt = -1;
            s_attempt = 0;
            s_pendingOperations.Clear();
            s_remoteOperations.Clear();
            s_serverEpoch = null;
            LastOperationResult = null;
            if (s_peer == null) return;
            var peer = s_peer;
            s_peer = null;
            peer.Changed -= RaiseChanged;
            peer.Dispose();
            RaiseChanged();
        }

        /// <summary>IPv4 addresses of this device, to type in the tool.</summary>
        public static List<string> GetLocalAddresses()
        {
            var result = new List<string>();
            try
            {
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork) result.Add(address.Address.ToString());
                    }
                }
            }
            catch (Exception)
            {
                // not available on this platform
            }
            if (result.Count == 0)
            {
                try
                {
                    foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()))
                    {
                        if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address)) result.Add(address.ToString());
                    }
                }
                catch (Exception)
                {
                    // no DNS
                }
            }
            if (result.Count == 0) result.Add("127.0.0.1");
            return result;
        }

        /// <summary>Called every frame (by a hidden runner object; tests call it directly).</summary>
        internal static void Pump()
        {
            s_peer?.Pump();
            UpdateReconnect();
        }

        static void SetPeer(MasterMemoryRemotePeer peer)
        {
            s_peer = peer;
            peer.Changed += RaiseChanged;
            if (Application.isPlaying && s_runner == null) s_runner = MasterMemoryRemoteRunner.Create();
#if UNITY_EDITOR
            // sockets and their threads must be closed before Play Mode ends or scripts reload, or the Editor waits for them
            if (!s_editorHooks)
            {
                s_editorHooks = true;
                UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Stop;
                UnityEditor.EditorApplication.playModeStateChanged += state =>
                {
                    if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) Stop();
                };
            }
#endif
            RaiseChanged();
        }

#if UNITY_EDITOR
        static bool s_editorHooks;
#endif

        static void RaiseChanged() => Changed?.Invoke();

        static string CreatePairingCode()
        {
            var random = new System.Random(Guid.NewGuid().GetHashCode());
            return random.Next(0, 1000000).ToString("000000");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (!IsSupported || IsToolMode) return;
            var settings = MasterMemoryDebuggerSettings.Current;
            if (settings.Enabled && settings.RemoteServer) StartServer(settings.RemotePort, settings.RemotePairingCode);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_peer?.Dispose();
            s_peer = null;
            s_runner = null;
            Changed = null;
            IsToolMode = false;
        }

        /// <summary>Pumps the connection on the main thread and stops it when the application quits.</summary>
        sealed class MasterMemoryRemoteRunner : MonoBehaviour
        {
            public static MasterMemoryRemoteRunner Create()
            {
                var gameObject = new GameObject("MasterMemoryDebuggerRemote") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
                DontDestroyOnLoad(gameObject);
                return gameObject.AddComponent<MasterMemoryRemoteRunner>();
            }

            void Update() => Pump();

            void OnApplicationQuit() => Stop();

            void OnDestroy()
            {
                if (s_runner == this) s_runner = null;
            }
        }
    }
}
