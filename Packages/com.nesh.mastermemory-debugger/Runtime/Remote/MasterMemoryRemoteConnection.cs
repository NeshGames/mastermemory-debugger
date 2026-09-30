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
        // Frames can be large (table snapshots), so cap queued frame counts on both directions.
        // If either side stops consuming, fail the connection instead of retaining unbounded byte arrays.
        internal const int MaxQueuedFrames = 32;

        readonly TcpClient client;
        readonly NetworkStream stream;
        readonly BlockingCollection<byte[]> received =
            new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>(), MaxQueuedFrames);
        readonly BlockingCollection<byte[]> outgoing =
            new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>(), MaxQueuedFrames);
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
            if (!TrySend(payload) && !closed)
                Close("Connection closed because the outgoing message queue is full.");
        }

        /// <summary>
        /// Queues a frame without blocking. Used by chunked transfers to apply backpressure instead of closing when the
        /// writer has not drained the bounded queue yet.
        /// </summary>
        public bool TrySend(byte[] payload)
        {
            if (closed) return false;
            try
            {
                return outgoing.TryAdd(payload);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public bool TryReceive(out byte[] payload) => received.TryTake(out payload);

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
                    if (!received.TryAdd(payload))
                    {
                        Close("Connection closed because the incoming message queue is full.");
                        return;
                    }
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
