using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// One TCP connection. Frames are read and written on background threads; received messages are queued for the main
    /// thread (<see cref="TryReceive"/>), so large messages never block a frame.
    /// </summary>
    internal sealed class MasterMemoryRemoteConnection : IDisposable
    {
        readonly TcpClient client;
        readonly NetworkStream stream;
        readonly ConcurrentQueue<byte[]> received = new ConcurrentQueue<byte[]>();
        readonly BlockingCollection<byte[]> outgoing = new BlockingCollection<byte[]>();
        readonly Thread readThread;
        readonly Thread writeThread;
        volatile bool closed;
        volatile string closeReason;

        public MasterMemoryRemoteConnection(TcpClient client)
        {
            this.client = client;
            client.NoDelay = true;
            stream = client.GetStream();
            RemoteAddress = client.Client.RemoteEndPoint?.ToString() ?? "?";
            readThread = new Thread(ReadLoop) { IsBackground = true, Name = "MasterMemoryRemote read" };
            writeThread = new Thread(WriteLoop) { IsBackground = true, Name = "MasterMemoryRemote write" };
            readThread.Start();
            writeThread.Start();
        }

        public string RemoteAddress { get; }

        public bool IsClosed => closed;

        /// <summary>Why the connection closed (null while open, or after <see cref="Dispose"/>).</summary>
        public string CloseReason => closeReason;

        public void Send(byte[] payload)
        {
            if (closed) return;
            try
            {
                outgoing.Add(payload);
            }
            catch (InvalidOperationException)
            {
                // closed meanwhile
            }
        }

        public bool TryReceive(out byte[] payload) => received.TryDequeue(out payload);

        /// <summary>Sends the queued messages, then closes (used after a Reject).</summary>
        public void CloseAfterSending()
        {
            outgoing.CompleteAdding();
        }

        public void Dispose()
        {
            Close(null);
        }

        void ReadLoop()
        {
            try
            {
                while (!closed)
                {
                    var payload = MasterMemoryRemoteProtocol.ReadFrame(stream);
                    if (payload == null)
                    {
                        Close("The other side closed the connection.");
                        return;
                    }
                    received.Enqueue(payload);
                }
            }
            catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException || e is InvalidDataException)
            {
                Close(closed ? null : "Connection lost: " + (e.InnerException ?? e).Message);
            }
        }

        void WriteLoop()
        {
            try
            {
                foreach (var payload in outgoing.GetConsumingEnumerable())
                {
                    MasterMemoryRemoteProtocol.WriteFrame(stream, payload);
                }
                // CloseAfterSending
                Close(null);
            }
            catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException || e is InvalidOperationException)
            {
                Close(closed ? null : "Connection lost: " + (e.InnerException ?? e).Message);
            }
        }

        void Close(string reason)
        {
            lock (outgoing)
            {
                if (closed) return;
                closed = true;
                closeReason = reason;
            }
            try
            {
                outgoing.CompleteAdding();
            }
            catch (ObjectDisposedException)
            {
            }
            try
            {
                stream.Close();
                client.Close();
            }
            catch (Exception)
            {
                // already closed
            }
        }
    }
}
