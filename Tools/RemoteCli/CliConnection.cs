using System.Net.Sockets;
using Nesh.MasterMemoryDebugger;

internal sealed class CliConnection : IDisposable
{
    readonly TcpClient client;

    CliConnection(TcpClient client, NetworkStream stream, MasterMemoryRemoteProtocol.Welcome welcome)
    {
        this.client = client;
        Stream = stream;
        Welcome = welcome;
    }

    public NetworkStream Stream { get; }
    public MasterMemoryRemoteProtocol.Welcome Welcome { get; }

    public static async Task<CliConnection> Connect(CliOptions options)
    {
        var client = new TcpClient();
        try
        {
            using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(options.Host, options.Port, connectTimeout.Token);
            client.ReceiveTimeout = options.Timeout * 1000;
            client.SendTimeout = options.Timeout * 1000;
            var stream = client.GetStream();

            MasterMemoryRemoteProtocol.WriteFrame(stream,
                MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.Hello
                {
                    Version = MasterMemoryRemoteProtocol.Version,
                    Code = options.Code,
                }));

            var first = CliProtocol.Read(stream);
            if (MasterMemoryRemoteProtocol.GetType(first) == MasterMemoryRemoteProtocol.MessageType.Reject)
                throw new CliError("REJECTED", MasterMemoryRemoteProtocol.DecodeReject(first), 4);
            if (MasterMemoryRemoteProtocol.GetType(first) != MasterMemoryRemoteProtocol.MessageType.Welcome)
                throw new CliError("PROTOCOL_ERROR", "Expected Welcome.", 5);

            var welcome = MasterMemoryRemoteProtocol.DecodeWelcome(first);
            if (welcome.Version != MasterMemoryRemoteProtocol.Version)
                throw new CliError("VERSION_MISMATCH",
                    $"Server protocol {welcome.Version}; CLI protocol {MasterMemoryRemoteProtocol.Version}.", 4);
            return new CliConnection(client, stream, welcome);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Stream.Dispose();
        client.Dispose();
    }
}
