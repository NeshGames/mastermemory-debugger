using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>A game build found on the local network, waiting for the remote editor tool.</summary>
    public sealed class MasterMemoryRemoteGame
    {
        public string Address;
        public int Port;
        public string ProductName;
        public string DeviceName;
        public string MasterVersion;

        public override string ToString() => $"{ProductName} — {DeviceName}  ({Address}:{Port}, master {MasterVersion})";
    }

    /// <summary>
    /// LAN discovery: a game with a running remote server answers UDP broadcasts on <see cref="Port"/>; the tool broadcasts
    /// a request and lists the answers, so the address does not have to be typed. Best effort: some networks (guest Wi-Fi,
    /// VPN) drop broadcasts, then the address is entered by hand.
    /// </summary>
    internal static class MasterMemoryRemoteDiscovery
    {
        public const int DefaultPort = 7787;
        const string Request = "MMDBG-DISCOVER";
        const string Answer = "MMDBG-HERE";

        /// <summary>UDP port of the requests (tests use another one).</summary>
        internal static int Port = DefaultPort;

        // ------------------------------------------------------------------ game

        /// <summary>Game side: answers discovery requests on a background thread.</summary>
        public sealed class Responder : IDisposable
        {
            readonly UdpClient udp;
            readonly Thread thread;
            volatile string answer;
            volatile bool stopped;

            public Responder(int serverPort, string productName, string deviceName, string masterVersion)
            {
                udp = new UdpClient { ExclusiveAddressUse = false };
                // several games on one PC (the Editor and a build) can all answer
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
                SetInfo(serverPort, productName, deviceName, masterVersion);
                thread = new Thread(Loop) { IsBackground = true, Name = "MasterMemoryRemote discovery" };
                thread.Start();
            }

            public void SetInfo(int serverPort, string productName, string deviceName, string masterVersion)
            {
                answer = string.Join("\n", Answer + " " + MasterMemoryRemoteProtocol.Version, serverPort.ToString(), Clean(productName), Clean(deviceName), Clean(masterVersion));
            }

            public void Dispose()
            {
                stopped = true;
                udp.Close();
            }

            void Loop()
            {
                while (!stopped)
                {
                    try
                    {
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        var data = udp.Receive(ref from);
                        if (!Encoding.UTF8.GetString(data).StartsWith(Request, StringComparison.Ordinal)) continue;
                        var reply = Encoding.UTF8.GetBytes(answer);
                        udp.Send(reply, reply.Length, from);
                    }
                    catch (Exception)
                    {
                        // Dispose interrupts Receive; a failed reply is not fatal
                        if (stopped) return;
                    }
                }
            }

            static string Clean(string text) => (text ?? string.Empty).Replace('\n', ' ');
        }

        // ------------------------------------------------------------------ tool

        /// <summary>Tool side: one search, running on a background thread for <paramref name="milliseconds"/>.</summary>
        public sealed class Search
        {
            readonly List<MasterMemoryRemoteGame> games = new List<MasterMemoryRemoteGame>();
            volatile bool done;

            public Search(int milliseconds)
            {
                new Thread(() => Run(milliseconds)) { IsBackground = true, Name = "MasterMemoryRemote search" }.Start();
            }

            public bool IsDone => done;

            public List<MasterMemoryRemoteGame> Games
            {
                get
                {
                    lock (games) return new List<MasterMemoryRemoteGame>(games);
                }
            }

            void Run(int milliseconds)
            {
                try
                {
                    using (var udp = new UdpClient(0) { EnableBroadcast = true })
                    {
                        var request = Encoding.UTF8.GetBytes(Request + " " + MasterMemoryRemoteProtocol.Version);
                        foreach (var target in Targets())
                        {
                            try
                            {
                                udp.Send(request, request.Length, new IPEndPoint(target, Port));
                            }
                            catch (Exception)
                            {
                                // e.g. no route for this broadcast address
                            }
                        }

                        var end = DateTime.UtcNow.AddMilliseconds(milliseconds);
                        while (true)
                        {
                            var left = (int)(end - DateTime.UtcNow).TotalMilliseconds;
                            if (left <= 0) break;
                            udp.Client.ReceiveTimeout = left;
                            try
                            {
                                var from = new IPEndPoint(IPAddress.Any, 0);
                                var game = Parse(udp.Receive(ref from), from.Address);
                                if (game == null) continue;
                                lock (games)
                                {
                                    if (!games.Exists(x => x.Address == game.Address && x.Port == game.Port)) games.Add(game);
                                }
                            }
                            catch (SocketException)
                            {
                                // timeout
                                break;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // no network: nothing found
                }
                finally
                {
                    done = true;
                }
            }

            static MasterMemoryRemoteGame Parse(byte[] data, IPAddress from)
            {
                var lines = Encoding.UTF8.GetString(data).Split('\n');
                if (lines.Length < 5 || !lines[0].StartsWith(Answer, StringComparison.Ordinal) || !int.TryParse(lines[1], out var port)) return null;
                return new MasterMemoryRemoteGame
                {
                    // a game on this PC answers from any of its addresses; 127.0.0.1 always works for it
                    Address = IsLocal(from) ? "127.0.0.1" : from.ToString(),
                    Port = port,
                    ProductName = lines[2],
                    DeviceName = lines[3],
                    MasterVersion = lines[4],
                };
            }
        }

        /// <summary>The broadcast address of every network, the limited broadcast and this PC.</summary>
        static List<IPAddress> Targets()
        {
            var targets = new List<IPAddress> { IPAddress.Loopback, IPAddress.Broadcast };
            try
            {
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null) continue;
                        var address = unicast.Address.GetAddressBytes();
                        var mask = unicast.IPv4Mask.GetAddressBytes();
                        for (var i = 0; i < 4; i++) address[i] = (byte)(address[i] | ~mask[i]);
                        var broadcast = new IPAddress(address);
                        if (!targets.Contains(broadcast)) targets.Add(broadcast);
                    }
                }
            }
            catch (Exception)
            {
                // interfaces not available: the limited broadcast is still sent
            }
            return targets;
        }

        static bool IsLocal(IPAddress address)
        {
            if (IPAddress.IsLoopback(address)) return true;
            try
            {
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.Equals(address)) return true;
                    }
                }
            }
            catch (Exception)
            {
                // unknown
            }
            return false;
        }
    }
}
