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

        public static string Status => s_peer?.Status ?? (IsToolMode ? "Not connected." : "Remote editing is off.");

        public static bool IsServerRunning => s_peer is MasterMemoryRemoteServer;

        /// <summary>Game: the port listened on (0 when off).</summary>
        public static int ServerPort => (s_peer as MasterMemoryRemoteServer)?.Port ?? 0;

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

        /// <summary>Stops the server or closes the connection. The tool keeps the tables it received.</summary>
        public static void Stop()
        {
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
        internal static void Pump() => s_peer?.Pump();

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
